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
2. The source of the files (new in version 2): for an uploaded review only the word `upload`; for a review from a
   git repository the identity of the repository, the baseline commit (or none), the head commit, the selection
   text, and every change as its path, its kind (`Added`, `Modified`, or `Deleted`), the SHA-256 of its baseline
   content (none for an addition), and the SHA-256 of its head content (none for a deletion). A signature therefore
   binds to the commits, the selection, and each changed file, not only to the fingerprint, which covers the head
   content alone and would not change if a deleted file, the baseline, or the selection changed.
3. Every stored checklist answer: the check id, the outcome (`Pass`, `Fail`, or `NotApplicable`), and the
   justification.
4. Every checklist section that is marked as not applicable: the section id and the reason.
5. Every comment thread: the file path, the start and end line (or none for a whole file), the state (`Open` or
   `Resolved`), and the creation time of the thread; and every comment of the thread in posting order: the display
   name of the author, the user name of the author, the text, and the time.

**What the digest does not cover**:

- Who gave an answer and when (`AnsweredBy` and `AnsweredAt`). Answering a check again with the same outcome and
  justification therefore does not invalidate a signature. This is a deliberate choice that can be reversed by adding
  the two values to the text and raising the version.
- Sections that apply. Applying is the default and carries no reason, so only the exception is recorded.
- The identities (row numbers) of answers, threads, and comments, and the order in which the store happens to return
  them or the changes.
- The name of the repository. It is a display copy made when the review was created; the identity of the repository
  is covered, so renaming a repository does not make a signature out of date.
- The reviewer slots and the signatures. Signing must not change what was signed.
- The review identifier and title, the list of files, and the checklist definition. The file list and the checklist
  are fixed when the review is created, and the file contents are covered through the fingerprint (and, for a review
  from a repository, through the changes); the file paths that matter enter the digest through the anchors of
  threads and the changes.

**Canonical text, version 2**: the lines below are joined with a line feed (`\n`, no trailing line feed). Version 1
had the same text without the source lines and with the header `ReviewCenter record v1`; every digest changed with
the move to version 2, which is intended: the database of the product is recreated in this phase, so no signature
of version 1 exists.

```text
ReviewCenter record v2
fingerprint {S}
source upload                                              for an uploaded review, or
source git {repositoryId} {S:baseline} {S:head} {S:selection}   for a review from a repository, then
changes
change {S:path} {S:kind} {S:baselineHash} {S:headHash}     one line per change, sorted by path
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
  justification, the baseline of a review without one, or the head hash of a deleted file) is written as `-`.
- `{repositoryId}`, `{start}` and `{end}` are the numbers in the invariant culture; `{start}` and `{end}` are `-`
  when the thread is on the whole file.
- `{createdTicks}` and `{ticks}` are the UTC ticks of the time, in the invariant culture.
- The outcome, the thread state, and the change kind are written by their enumeration names.
- The selection text is the text kept with the review: the patterns in the order given, one per line, with `!` in
  front of an exclusion.
- Changes are sorted by path, answers by item id, and sections by section id, all with the ordinal comparison. Each
  thread is first rendered to its block (the thread line and its comment lines joined by line feeds); the blocks are
  sorted with the ordinal comparison, so the order of threads, which the store does not guarantee, does not matter.
  The order of the comments inside a thread does matter and is kept as stored, because it is part of what was said.
- The header line holds the format version. Any change to this format, to what is covered, or to the way a value is
  written must change the version, because it changes the digest of every record and makes all earlier signatures
  out of date.

**Known-answer vectors**: for an uploaded review with the fingerprint `abc`, no answers, no sections that do not
apply, and no threads, the canonical text is the six lines

```text
ReviewCenter record v2
fingerprint 3:abc
source upload
answers
sections
threads
```

and the digest is `fd4f16444ef233068c76aad7bed87b1bafd550c20a0019f576e5cdaaadb9384c`. For a review from the
repository 7 with the fingerprint `abc`, the baseline `aaaa...` and the head `bbbb...` (forty characters each), the
selection `**/*`, a modified file `a.txt` (baseline hash `bs1`, head hash `hs1`) and a deleted file `b.txt` (baseline
hash `bs2`), and nothing else, the canonical text is

```text
ReviewCenter record v2
fingerprint 3:abc
source git 7 40:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 40:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 4:**/*
changes
change 5:a.txt 8:Modified 3:bs1 3:hs1
change 5:b.txt 7:Deleted 3:bs2 -
answers
sections
threads
```

and the digest is `7aed146fbc739a9314d432b2b45a843d61da01171ffb767b61671e6a66c2480d`. For a record with the
fingerprint `abc`, one answer (check `REQ-01`, outcome `Fail`, justification `bad`), the sections `DES` (does not
apply, reason `none`) and `DOC` (applies), and one thread on `a.txt` (lines 1 to 2, `Open`, created at time T) with the
comment by `Ann` (`ann`) with the text `Please fix` at time T, the upload canonical text is

```text
ReviewCenter record v2
fingerprint 3:abc
source upload
answers
answer 6:REQ-01 4:Fail 3:bad
sections
not-applicable 3:DES 4:none
threads
thread 5:a.txt 1 2 4:Open {T ticks}
comment 3:Ann 3:ann 10:Please fix {T ticks}
```

The tests check the digest of the first two vectors, and the exact canonical text of the second and the third.

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
