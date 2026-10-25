### RecordDigest

![Core Structure](CoreView.svg)

#### Purpose

`RecordDigest` calculates the digest of a review record: the single value that a signature binds itself to. A
signature is current only while the digest of the record equals the digest that was signed, so the digest defines
exactly what a signature approves. The unit is a stateless static class and therefore thread-safe. Its input,
`ReviewRecord`, is an immutable snapshot of everything the digest covers, loaded by one call of the store so that the
digest and the signing blockers describe the same data.

#### Data Model

The digest is the SHA-256 hash, written as 64 lowercase hexadecimal characters, of the UTF-8 bytes of a canonical
text. Hashing is the only use of the text; it is never stored.

**What the digest covers** (and therefore what a signature approves):

1. The content fingerprint of the review (the hash of the file contents, see `Fingerprint`).
2. Every stored checklist answer: the check id, the outcome (`Pass`, `Fail`, or `NotApplicable`), and the
   justification.
3. Every checklist section that is marked as not applicable: the section id and the reason.
4. Every comment thread: the file path, the start and end line (or none for a whole file), the state (`Open` or
   `Resolved`), and the creation time of the thread; and every comment of the thread in posting order: the display
   name of the author, the user name of the author, the text, and the time.

**What the digest does not cover**:

- Who gave an answer and when (`AnsweredBy` and `AnsweredAt`). Answering a check again with the same outcome and
  justification therefore does not invalidate a signature. This is a deliberate choice that can be reversed by adding
  the two values to the text and raising the version.
- Sections that apply. Applying is the default and carries no reason, so only the exception is recorded.
- The identities (row numbers) of answers, threads, and comments, and the order in which the store happens to return
  them.
- The reviewer slots and the signatures. Signing must not change what was signed.
- The review identifier and title, the list of files, and the checklist definition. The file list and the checklist
  are fixed when the review is created, and the file contents are covered through the fingerprint; the file paths
  that matter enter the digest through the anchors of threads.

**Canonical text, version 1**: the lines below are joined with a line feed (`\n`, no trailing line feed).

```text
ReviewCenter record v1
fingerprint {S}
answers
answer {S:itemId} {S:outcome} {S:justification}            one line per answer, sorted by item id
sections
not-applicable {S:sectionId} {S:reason}                    one line per section that does not apply, sorted by id
threads
thread {S:path} {start} {end} {S:state} {createdTicks}     one block per thread: this line,
comment {S:author} {S:username} {S:text} {ticks}           then one line per comment in posting order
```

Notation and rules:

- `{S:x}` is a string written length-prefixed as `{length}:{text}`, where the length is the number of UTF-16 code
  units, so no text can imitate a delimiter or the start of another line. An absent string (for example a missing
  justification) is written as `-`.
- `{start}` and `{end}` are the line numbers in the invariant culture, or `-` when the thread is on the whole file.
- `{createdTicks}` and `{ticks}` are the UTC ticks of the time, in the invariant culture.
- The outcome and the thread state are written by their enumeration names.
- Answers are sorted by item id and sections by section id, both with the ordinal comparison. Each thread is first
  rendered to its block (the thread line and its comment lines joined by line feeds); the blocks are sorted with the
  ordinal comparison, so the order of threads, which the store does not guarantee, does not matter. The order of the
  comments inside a thread does matter and is kept as stored, because it is part of what was said.
- The header line holds the format version. Any change to this format, to what is covered, or to the way a value is
  written must change the version, because it changes the digest of every record and makes all earlier signatures
  out of date.

**Known-answer vectors**: for a record with the fingerprint `abc`, no answers, no sections that do not apply, and no
threads, the canonical text is the five lines

```text
ReviewCenter record v1
fingerprint 3:abc
answers
sections
threads
```

and the digest is `9755f10e118733d548ae1037dd68b000d6d50ca8b62f53d4411693d85a3fe35a`. For a record with the
fingerprint `abc`, one answer (check `REQ-01`, outcome `Fail`, justification `bad`), the sections `DES` (does not
apply, reason `none`) and `DOC` (applies), and one thread on `a.txt` (lines 1 to 2, `Open`, created at time T) with the
comment by `Ann` (`ann`) with the text `Please fix` at time T, the canonical text is

```text
ReviewCenter record v1
fingerprint 3:abc
answers
answer 6:REQ-01 4:Fail 3:bad
sections
not-applicable 3:DES 4:none
threads
thread 5:a.txt 1 2 4:Open {T ticks}
comment 3:Ann 3:ann 10:Please fix {T ticks}
```

The first vector was confirmed with an independent computation of SHA-256 over the five lines; the tests check the
digest of the first vector, and the exact canonical text of the second.

#### Key Methods

**Canonicalize**: Builds the canonical text of a record.

- *Parameters*: `ReviewRecord record` — the record; the checklist definition it carries is not used.
- *Returns*: `string` — the canonical text described above.
- *Preconditions*: The record is not null.
- *Postconditions*: The text does not depend on the order of the lists in the record or on any identity.

**Compute**: Calculates the digest of a record.

- *Parameters*: `ReviewRecord record` — the record.
- *Returns*: `string` — the SHA-256 of the UTF-8 bytes of `Canonicalize(record)`, as 64 lowercase hexadecimal
  characters.
- *Preconditions*: The record is not null.

#### Error Handling

A null record raises `ArgumentNullException`. No other failure is possible: every value can be written, and the hash
cannot fail. Whether a record is signable is not decided here (see `SigningService`).

#### Dependencies

- **ReviewRecord** and the thread, comment, answer, and section types of the `Core` subsystem, which it reads.
- **.NET runtime library** — `System.Security.Cryptography.SHA256`, `System.Text.Encoding` for UTF-8, and
  `System.Globalization.CultureInfo` for the invariant number format.

#### Callers

- **SigningService** — computes the digest to show, to compare with the digest the signer saw, and to find the
  current signatures.
- **ReviewStore** (`Storage` subsystem) — computes the digest again inside the transaction that stores a signature.
- **Tests** — check the format and the known-answer vector.
