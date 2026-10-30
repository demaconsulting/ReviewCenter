### RecordDigest

![Core Structure](CoreView.svg)

#### Purpose

`RecordDigest` calculates the digest of a review record: the single value that a signature binds itself to. A
signature is current only while the digest of the record equals the digest that was signed, so the digest defines
exactly what a signature approves. The unit is a stateless static class and therefore thread-safe. Its input,
`ReviewRecord`, is an immutable snapshot of everything the digest covers, loaded by one call of the store so that the
digest and the signing blockers describe the same data.

The digest format is private to ReviewCenter: no other tool is expected to reproduce it, and it is versioned from
`v1` at the first release. There is no earlier history to describe; the version only grows when a later release
changes the format.

#### Data Model

The digest is the SHA-256 hash, written as 64 lowercase hexadecimal characters, of the UTF-8 bytes of a canonical
text. Hashing is the only use of the text; it is never stored.

**What the digest covers** (and therefore what a signature approves):

1. The content fingerprint of the review (the hash of the file contents, see `Fingerprint`).
2. The source of the files: for an uploaded review only the word `upload`; for a review from a git repository the
   identity of the repository, the baseline commit (or none), the head commit, the selection text, the identifier of
   the review-set of `.reviewmark.yaml` the review was planned from (none for a review that was not planned from a
   set; the empty text is a different value from none), and every change as its path, its kind (`Added`, `Modified`,
   or `Deleted`), the SHA-256 of its baseline content (none for an addition), and the SHA-256 of its head content
   (none for a deletion). A signature therefore binds to the commits, the selection, and each changed file, not only
   to the fingerprint, which covers the head content alone and would not change if a deleted file, the baseline, or
   the selection changed.
3. The checklist the review was created with (frozen at creation): its version label and, for every section, its
   identifier and title and, for every check, its identifier and question wording. A signature therefore binds to the
   questions that were asked, not only to the replies; a review without a checklist is written as none.
4. Every stored checklist answer: the check id, the outcome (`Pass`, `Fail`, or `NotApplicable`), the justification,
   and who answered (the display name and the user name of the person).
5. Every checklist section that is marked as not applicable: the section id and the reason.
6. Every comment thread: the file path, the side (`Head` or `Baseline`), the start and end line (or none for a whole
   file), the state (`Open` or `Resolved`), and the creation time of the thread; and every comment of the thread in
   posting order: the display name of the author, the user name of the author, the text, and the time. A line of a
   thread means a line of the text of its side, so a head thread and a baseline thread on the same numbers are
   different discussions and give different digests.
7. The number of times an administrator reopened the review, but only when it is above zero (see below). A reopen is
   therefore part of what a signature approves: an earlier approval can never be current after a reopen.

**What the digest does not cover**:

- When an answer was given (`AnsweredAt`). Answering a check again with the same outcome and justification, as the
  same person, therefore does not invalidate a signature. Because who answered is covered, a different person
  answering the same value again does invalidate the signatures: the record then says something different about who
  vouches for the answer.
- The hint of a checklist section that says when it can be skipped. It is display help, not wording of a check.
- Sections that apply. Applying is the default and carries no reason, so only the exception is recorded.
- Who marked a section as not applicable, and when (`SetBy`, `SetByUsername`, `SetAt`). Unlike an answer, whose
  author is a statement the record makes, the section and its reason are the statement; leaving the setter out also
  keeps the digest of every existing review as it was. The decision is pinned by a test.
- The identities (row numbers) of answers, threads, and comments, and the order in which the store happens to return
  them or the changes, or the order the checklist definition lists its sections and checks in.
- The name of the repository. It is a display copy made when the review was created; the identity of the repository
  is covered, so renaming a repository does not make a signature out of date.
- The reviewer slots and the signatures. Signing must not change what was signed.
- The review identifier and title, and the list of files. The file list is fixed when the review is created, and the
  file contents are covered through the fingerprint (and, for a review from a repository, through the changes); the
  file paths that matter enter the digest through the anchors of threads and the changes.

The digest always covers the complete record, whatever the evidence settings of the project omit from the PDF
(see `AttestationRenderer`).

**Canonical text, version 1**: the lines below are joined with a line feed (`\n`, no trailing line feed).

```text
ReviewCenter record v1
fingerprint {S}
source upload                                              for an uploaded review, or
source git {repositoryId} {S:baseline} {S:head} {S:selection} {S:reviewSetId}   for a review from a repository, then
changes
change {S:path} {S:kind} {S:baselineHash} {S:headHash}     one line per change, sorted by path
checklist {S:version}                                      or "checklist -" for a review without a checklist
section {S:id} {S:title}                                   one line per section, sorted by id, each followed by
item {S:id} {S:text}                                       one line per check of the section, sorted by id
answers
answer {S:itemId} {S:outcome} {S:justification} {S:answeredBy} {S:answeredByUsername}   sorted by item id
sections
not-applicable {S:sectionId} {S:reason}                    one line per section that does not apply, sorted by id
threads
thread {S:path} {S:side} {start} {end} {S:state} {createdTicks}   one block per thread: this line,
comment {S:author} {S:username} {S:text} {ticks}           then one line per comment in posting order
reopens {count}                                            only when the review was reopened (count above zero)
```

Notation and rules:

- `{S:x}` is a string written length-prefixed as `{length}:{text}`, where the length is the number of UTF-16 code
  units, so no text can imitate a delimiter or the start of another line. An absent string (for example a missing
  justification, the baseline of a review without one, the review-set identifier of a review that was not planned
  from a set, or the head hash of a deleted file) is written as `-`; the empty text is written as `0:`.
- `{repositoryId}`, `{start}`, `{end}`, and `{count}` are the numbers in the invariant culture; `{start}` and `{end}`
  are `-`
  when the thread is on the whole file.
- `{createdTicks}` and `{ticks}` are the UTC ticks of the time, in the invariant culture.
- The outcome, the thread state, the thread side, and the change kind are written by their enumeration names (the
  side as a length-prefixed string, for example `4:Head` or `8:Baseline`).
- The selection text is the text kept with the review: the patterns in the order given, one per line, with `!` in
  front of an exclusion.
- Changes are sorted by path, checklist sections and checks by id, answers by item id, and not-applicable sections
  by section id, all with the ordinal comparison. Each thread is first rendered to its block (the thread line and
  its comment lines joined by line feeds); the blocks are sorted with the ordinal comparison, so the order of
  threads, which the store does not guarantee, does not matter. The order of the comments inside a thread does matter
  and is kept as stored, because it is part of what was said.
- The header line holds the format version. Any change to this format, to what is covered, or to the way a value is
  written that alters the digest of an existing record must change the version, because it makes all earlier
  signatures out of date.
- **Preconditions of the claims above**: (1) The order independence holds for lists whose sort keys are unique; this is
  an assumption about the input, not something the digest enforces. The keys are unique in a stored record, because the
  store keys answers by check id, section states by section id, and files (and so changes) by path within a review, and
  a checklist definition is expected to use unique section and check identifiers (`ChecklistDefinition.FromJson` does
  not reject duplicates, so a definition with duplicates is outside the claim). The sort is stable, so input that broke
  this would be written in the order it was given. (2) The text is hashed as UTF-8.
  A lone (unpaired) UTF-16 surrogate, which is malformed text and not valid user input, is encoded as U+FFFD, so two
  strings that differ only in such characters (for example `"\uD800"` and `"\uD801"`, which also have the same length)
  give the same digest. A strict encoding would instead make the digest throw for such a record, and any other
  scheme changes the digest of existing records; the limitation is therefore accepted and documented rather than
  removed.
- **The reopens line**: `ReviewRecord.Reopens` is the number of times an administrator reopened the review. The line
  `reopens {count}` is appended after the last thread block only when the count is above zero. A review that was never
  reopened has no such line, so its canonical text and digest are exactly what they were before reopening existed:
  the format version is therefore **not** bumped (a bump would make every existing signature out of date for no
  reason), and the vectors below are unchanged. After a reopen the line (or a higher count) makes the digest differ from
  every earlier one, whatever the content, so a signature given before the reopen stays in the record as history but
  can never be current again, not even when the content is changed back to what was signed.

**Known-answer vectors**: for an uploaded review with the fingerprint `abc`, no checklist, no answers, no sections
that do not apply, and no threads, the canonical text is the seven lines

```text
ReviewCenter record v1
fingerprint 3:abc
source upload
checklist -
answers
sections
threads
```

and the digest is `9fee8bc5c8ddc1c908ed605ccb301bfa1be3e0499cc824d787275c7a783543f0`. For a review from the
repository 7 with the fingerprint `abc`, the baseline `aaaa...` and the head `bbbb...` (forty characters each), the
selection `**/*`, no review-set, no checklist, a modified file `a.txt` (baseline hash `bs1`, head hash `hs1`) and a
deleted file `b.txt` (baseline hash `bs2`), and nothing else, the canonical text is

```text
ReviewCenter record v1
fingerprint 3:abc
source git 7 40:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 40:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 4:**/* -
changes
change 5:a.txt 8:Modified 3:bs1 3:hs1
change 5:b.txt 7:Deleted 3:bs2 -
checklist -
answers
sections
threads
```

and the digest is `b0eae44ae9062359375b7a361857794617e5c28b5a6a2b7ac732ec36549857d8`. With the review-set `core`
the source line ends with `4:core` and the digest is
`27b7ef9d9b7303fc1c55808f00331469c99e8518e66af16a3317d1811e678059`, and with the empty review-set it ends with `0:`
and the digest is `d44de252d6f0c7d7d878db59fedc2f21d20592d8669050216b5d77487cbf0760`. For an upload with the
fingerprint `abc`, the checklist `default-1` with the section `REQ` ("Requirements"; checks `REQ-01` "Is the code
correct?" and `REQ-02` "Is it traced?") and the section `DES` ("Design"; check `DES-01` "Is it sound?"), defined in
that order, and one answer (check `REQ-01`, outcome `Pass`, no justification, answered by `Ann` / `ann`), the
canonical text is

```text
ReviewCenter record v1
fingerprint 3:abc
source upload
checklist 9:default-1
section 3:DES 6:Design
item 6:DES-01 12:Is it sound?
section 3:REQ 12:Requirements
item 6:REQ-01 20:Is the code correct?
item 6:REQ-02 13:Is it traced?
answers
answer 6:REQ-01 4:Pass - 3:Ann 3:ann
sections
threads
```

and the digest is `5f6e64aa7091e4156ec43493c56d9f0cc2407a24a92efec3012ae02e1f954e43`. For a record with the
fingerprint `abc`, one answer (check `REQ-01`, outcome `Fail`, justification `bad`, answered by `Ann` / `ann`), the
sections `DES` (does not apply, reason `none`) and `DOC` (applies), and one thread on `a.txt` (head side, lines 1 to
2, `Open`, created at time T) with the comment by `Ann` (`ann`) with the text `Please fix` at time T, the upload
canonical text is

```text
ReviewCenter record v1
fingerprint 3:abc
source upload
checklist -
answers
answer 6:REQ-01 4:Fail 3:bad 3:Ann 3:ann
sections
not-applicable 3:DES 4:none
threads
thread 5:a.txt 4:Head 1 2 4:Open {T ticks}
comment 3:Ann 3:ann 10:Please fix {T ticks}
```

The side vectors use the upload record with the fingerprint `abc`, no checklist, no answers, and one thread on `a.txt`
for lines 1 to 2, `Open`, created at the UTC time 2025-03-04 05:06:07 (ticks `638766615670000000`) with the
comment by `Ann` (`ann`) with the text `Please fix` at the same time. With the side `Head` the digest is
`fe1a3eea5aab9f90922ebdea3caa92b637333d4f082eb7a80b44eb1335acc896`, and with the side `Baseline` (written
`8:Baseline`) it is `68acc67bebb0c5decad4669037cc3f7618844cf0e59919cb911aef79e525cf0d`. A last vector covers the less
common values: a repository review (repository 7, the baseline `aaaa...` and the head `bbbb...` as above, selection
`**/*`, no review-set) with one added file
`c.txt` (no baseline hash, head hash `hs3`), one answer (check `REQ-01`, outcome `NotApplicable`, the empty
justification, answered by `Bob` / `bob`), and one thread on `a.txt` (head side, no lines, `Resolved`, created at the
time T above) with the comment by `Bob` (`bob`) with the text `Done` one hour after T. Its canonical text is

```text
ReviewCenter record v1
fingerprint 3:abc
source git 7 40:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 40:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 4:**/* -
changes
change 5:c.txt 5:Added - 3:hs3
checklist -
answers
answer 6:REQ-01 13:NotApplicable 0: 3:Bob 3:bob
sections
threads
thread 5:a.txt 4:Head - - 8:Resolved 638766615670000000
comment 3:Bob 3:bob 4:Done 638766651670000000
```

and the digest is `cd2da19bd94a03706d356d3dbeb34261c8dc769fe4124eb2bd98c7b9c955f03e`; with a final line
`reopens 1` it is `b934cbd343bcd2766bd778b1549b2c282f25d23f303d54530b4c3337db07f10e`. All answers were
calculated independently of the implementation with a short script (SHA-256 of the canonical text as written
above), not copied from the code under test.

The tests check the digests of the first two vectors, of the two review-set variants, of the checklist vector, of
the two side vectors, and of the last vector (without and with a reopen), the exact canonical text of the second, the
third, the fourth, and the last, and that the first two, the checklist vector, and the head-side vector keep their
digests with an explicit reopen count of 0.

#### Key Methods

**Canonicalize**: Builds the canonical text of a record.

- *Parameters*: `ReviewRecord record` — the record; the frozen checklist definition it carries is read for the
  checklist lines.
- *Returns*: `string` — the canonical text described above.
- *Preconditions*: The record is not null.
- *Postconditions*: The text does not depend on the identity of any row, on the order of the answers, sections,
  threads, changes, or checklist definition lists, or on the storage order of the threads. It does depend on the order
  of the comments inside a thread. The order independence assumes unique sort keys (see the preconditions above).

**Compute**: Calculates the digest of a record.

- *Parameters*: `ReviewRecord record` — the record.
- *Returns*: `string` — the SHA-256 of the UTF-8 bytes of `Canonicalize(record)`, as 64 lowercase hexadecimal
  characters.
- *Preconditions*: The record is not null.

#### Error Handling

A null record raises `ArgumentNullException`. A stored checklist definition that is not valid JSON, or not a complete
definition (`ChecklistDefinition.FromJson` rejects a missing version, section list, section, item, identifier, title or
text), raises `JsonException`, never a `NullReferenceException`: the store only holds text written by the definition
itself, so this signals corrupted data and is
not hidden. Whether a record is signable is not decided here (see `SigningService`).

#### Dependencies

- **ReviewRecord** and the thread, comment, answer, checklist, section, and reopen count (`ReviewRecord.Reopens`) types
  of the `Core` subsystem, which it
  reads.
- **.NET runtime library** — `System.Security.Cryptography.SHA256`, `System.Text.Encoding` for UTF-8, and
  `System.Globalization.CultureInfo` for the invariant number format.

#### Callers

- **SigningService** — computes the digest to show, to compare with the digest the signer saw, and to find the
  current signatures; `ReviewLock.IsApproved` (part of the same unit) computes it to decide whether a review is locked.
- **ReviewStore** (`Storage` subsystem) — computes the digest again inside the transaction that stores a signature and
  inside the write transactions that check the lock; it fills `ReviewRecord.Reopens` from the stored reopens.
- **Tests** — check the format and the known-answer vectors.
