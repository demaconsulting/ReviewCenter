### Fingerprint

![Core Structure](CoreView.svg)

#### Purpose

`Fingerprint` calculates the content fingerprint of a set of files and the hash of one file. Its single
responsibility is to implement the fingerprint algorithm of the ReviewMark tool exactly, so that the
fingerprint ReviewCenter shows for a review equals the one ReviewMark computes for the same file contents.
It is a stateless static class and therefore thread-safe.

#### Data Model

The unit has no state. A file hash is a 64-character lowercase hexadecimal string (SHA-256). A fingerprint
is a 64-character lowercase hexadecimal string.

**Algorithm source**: the algorithm is the one specified in the "Fingerprinting" section of the ReviewMark
User Guide (`docs/user_guide/introduction.md` in the ReviewMark repository,
<https://github.com/demaconsulting/ReviewMark>). The fingerprint of a set of files is calculated as follows:

1. Calculate the SHA-256 hash of the raw bytes of each file and write it as lowercase hexadecimal.
2. Sort the hash strings with an ordinal (culture-independent) comparison.
3. Concatenate the sorted hash strings with no separator.
4. Encode the concatenation as UTF-8, calculate its SHA-256 hash, and write it as lowercase hexadecimal.

Consequences that the design relies on: the fingerprint does not depend on the order or the paths of the
files, so a renamed or moved file keeps it; only a change of content changes it. The bytes are hashed as
they are, with no line-ending normalization, and identical contents are counted once per file, not collapsed.

**Known-answer vectors**: The unit tests check the implementation against the following vectors. They were
obtained by running the `--elaborate` output of the ReviewMark tool on files with exactly these contents, and
were confirmed with an independent implementation of the steps above written in PowerShell, so the vectors
come from the reference tool and do not merely repeat this implementation.

- File A: `hello` and a line feed (`68 65 6c 6c 6f 0a`), SHA-256
  `5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03`
- File B: `world` and a line feed (`77 6f 72 6c 64 0a`), SHA-256
  `e258d248fda94c63753607f7c4494ee0fcbe92f1a76bfdac795c9d84101eb317`
- File C: the bytes `00 01 02 ff 0d 0a 0a`, SHA-256
  `57361c6e2882a06baeed989a104e01c40e5846ba7c12e044588c1deb866836b5`

| File set | Fingerprint |
| ---------- | ------------- |
| A | `7f39224e335994886c26ba8c241fcbe1d474aadaa2bd0a8e842983b098cea894` |
| A, B, C | `14aff6b60cf429a3eaeb2d20b71aae5b4fba2976af21766472747e130d9b73e9` |
| A, A | `71454b11b09343ed4b9ad176cc726f9cf9684439335436d874fdb70a026d99f5` |

The content of C contains a carriage return, a line feed, a zero byte, and a byte that is not valid UTF-8, to
show that the content is hashed as raw bytes.

#### Key Methods

**ComputeFileHash**: Calculates the hash of one file.

- *Parameters*: `ReadOnlySpan<byte> content` — the raw bytes of the file.
- *Returns*: `string` — the lowercase hexadecimal SHA-256 of the bytes.
- *Preconditions*: None; empty content is valid.
- *Postconditions*: The result has 64 characters.

**Compute (hashes)**: Calculates the fingerprint of a set of file hashes.

- *Parameters*: `IEnumerable<string> fileHashes` — the per-file hashes from `ComputeFileHash`.
- *Returns*: `string` — the fingerprint, 64 lowercase hexadecimal characters.
- *Preconditions*: The collection is not null.
- *Postconditions*: The result does not depend on the order of the input.

Sorts the strings with the ordinal comparer, concatenates them, and hashes the UTF-8 bytes of the result.

**Compute (contents)**: Calculates the fingerprint of a set of file contents.

- *Parameters*: `IEnumerable<byte[]> contents` — the raw bytes of each file.
- *Returns*: `string` — the fingerprint.

Calculates the hash of each file with `ComputeFileHash` and passes them to the hashes overload.

#### Error Handling

A null collection raises `ArgumentNullException`. No other failure is possible: hashing cannot fail for any
byte content, and no content is rejected here. The hash strings passed to the first overload are used as
given and are not validated. An empty set has a defined fingerprint (the hash of the empty string);
rejecting empty reviews is the business rule of `ReviewService`.

#### Dependencies

- **.NET runtime library** — `System.Security.Cryptography.SHA256` for the hashes and `System.Text.Encoding` for
  UTF-8.

#### Callers

- **ReviewService** — calculates the hash of each uploaded file and the fingerprint of the review.
- **Tests** — check the known-answer vectors above.
