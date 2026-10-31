### RecordDigest

![Core Structure](CoreView.svg)

#### Purpose

`RecordDigest` calculates the digest of a review record: the single value that a signature binds itself to. A
signature is current only while the digest of the record equals the digest that was signed, so the digest defines
exactly what a signature approves. The unit is a stateless static class and therefore thread-safe. Its input,
`ReviewRecord`, is an immutable snapshot of everything the digest covers, loaded by one call of the store so that the
digest and the signing blockers describe the same data.

The digest format is private to ReviewCenter: no other tool is expected to reproduce it, and it is versioned. This is
`v2`; nothing of an earlier version was released, so there is no earlier history to describe. The version grows when a
later release changes the format.

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
6. The report: every comment thread that has at least one comment **included in the report**
   (`ReviewComment.IncludeInReport`) is a block of the digest. A block holds the anchor of the thread (the file path,
   the side `Head` or `Baseline`, and the start and end line, or none for a whole file) and, in posting order, only
   the included comments: the display name of the author, the user name of the author, the text, and the time, each
   marked as the starting comment (`claim`, a Finding), a reply (`comment`), or a resolution note (`resolution`). A
   line of a thread means a line of the text of its side, so a head thread and a baseline thread on the same
   numbers are different discussions and give different digests. A thread that has no included comment is not in the
   digest at all.
7. The number of times an administrator reopened the review, but only when it is above zero (see below). A reopen is
   therefore part of what a signature approves: an earlier approval can never be current after a reopen.

**What the digest does not cover**:

- Chat: a comment that is not included in the report. Talking about the review therefore never makes a signature
  out of date.
- The state of a thread (`Open` or `Resolved`) and the creation time of a thread. Whether a thread is open is a
  separate rule: an open thread blocks signing and the conclusion of the review (see `SigningService` and
  `ReviewLock`), but it is not part of what a signature approves. Taking a comment into or out of the report, and
  promoting or demoting a thread, change the digest because they change the included comments.
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

**Canonical text, version 2**: the lines below are joined with a line feed (`\n`, no trailing line feed).

```text
ReviewCenter record v2
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
report
thread file {S:path} {S:side} {start} {end}                one block per thread with an included comment: this line,
claim {S:author} {S:username} {S:text} {ticks}             the starting comment, if it is included,
comment {S:author} {S:username} {S:text} {ticks}           each included reply, in posting order,
resolution {S:author} {S:username} {S:text} {ticks}        each included resolution note, in posting order
reopens {count}                                            only when the review was reopened (count above zero)
```

The `report` line is always present; a review without an included comment has the line alone and no block, so there is
no `report 0` line. The lines of a block are one per included comment in posting order, each with the label that fits
the comment (`claim` for the starting comment, `resolution` for a resolution note, otherwise `comment`).

A follow-up review (see `ReviewService`) adds text that is written only when it applies, like `reopens`: a line
`follows {S:reviewId}` straight after the source block (after the change lines of a review from a repository, before
the `checklist` line), and the suffix `carried {S:reviewId}`, after a space, at the end of an `answer` line, a
`not-applicable` line, and the `thread file` line of a thread that was carried over from that review.

Notation and rules:

- `{S:x}` is a string written length-prefixed as `{length}:{text}`, where the length is the number of UTF-16 code
  units, so no text can imitate a delimiter or the start of another line. An absent string (for example a missing
  justification, the baseline of a review without one, the review-set identifier of a review that was not planned
  from a set, or the head hash of a deleted file) is written as `-`; the empty text is written as `0:`.
- `{repositoryId}`, `{start}`, `{end}`, and `{count}` are the numbers in the invariant culture; `{start}` and `{end}`
  are `-`
  when the thread is on the whole file.
- `{createdTicks}` and `{ticks}` are the UTC ticks of the time, in the invariant culture.
- The outcome, the thread side, and the change kind are written by their enumeration names (the
  side as a length-prefixed string, for example `4:Head` or `8:Baseline`).
- The selection text is the text kept with the review: the patterns in the order given, one per line, with `!` in
  front of an exclusion.
- Changes are sorted by path, checklist sections and checks by id, answers by item id, and not-applicable sections
  by section id, all with the ordinal comparison. Each thread is first rendered to its block (the `thread file` line
  and its included comment lines joined by line feeds); the blocks are sorted with the ordinal comparison, so the
  order of threads, which the store does not guarantee, does not matter. The order of the comments inside a thread
  does matter and is kept as stored, because it is part of what was said.
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
  reopened has no such line, so its canonical text has no trace of reopening. After a reopen the line (or a higher
  count) makes the digest differ from
  every earlier one, whatever the content, so a signature given before the reopen stays in the record as history but
  can never be current again, not even when the content is changed back to what was signed.
- **The follow-up lines**: `ReviewRecord.FollowsReviewId` (the review the record follows, null for an ordinary review)
  and the `CarriedFromReviewId` of an answer, a section state, and a thread are written as described above, only when
  they are not null. A review that follows nothing has no such text, and a test pins this with a fixed
  vector. The identity of the carried row (`CarriedFromThreadId`) is not covered (it is a number of another review's
  row and tells nothing the carried comments do not), but the marker is: a carried answer and an answer given again
  with the same words never share a digest, and a signature of a follow-up therefore binds to which review it
  follows and to what it took over. A carried thread keeps the report flag of each of its comments, so a carried
  Finding is in the report of the follow-up with the marker, and carried chat is not. Answering a carried check again
  removes the marker and so changes the digest, as any change of an answer does.

**Known-answer vectors**: for an uploaded review with the fingerprint `abc`, no checklist, no answers, no sections
that do not apply, and no thread with an included comment, the canonical text is the seven lines

```text
ReviewCenter record v2
fingerprint 3:abc
source upload
checklist -
answers
sections
report
```

and the digest is `eb725d508e04716f367b32ac442820c89667cc920020b10eacebf181d7365ab3`. For a review from the
repository 7 with the fingerprint `abc`, the baseline `aaaa...` and the head `bbbb...` (forty characters each), the
selection `**/*`, no review-set, no checklist, a modified file `a.txt` (baseline hash `bs1`, head hash `hs1`) and a
deleted file `b.txt` (baseline hash `bs2`), and nothing else, the canonical text is

```text
ReviewCenter record v2
fingerprint 3:abc
source git 7 40:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 40:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 4:**/* -
changes
change 5:a.txt 8:Modified 3:bs1 3:hs1
change 5:b.txt 7:Deleted 3:bs2 -
checklist -
answers
sections
report
```

and the digest is `bb2723058729bb669e8c8858e0b4f913fd786e59d68d9d785093994b08012842`. With the review-set `core`
the source line ends with `4:core` and the digest is
`a595db28364f05ba194b28dfee6d5ab6668160f4fd1639dd98ddc043a8991c74`, and with the empty review-set it ends with `0:`
and the digest is `8759a10e14651b6bb8e6b434f01dd6ac9d4de4cb0d8876dfd6eb8bfca8f6959a`. For an upload with the
fingerprint `abc`, the checklist `default-1` with the section `REQ` ("Requirements"; checks `REQ-01` "Is the code
correct?" and `REQ-02` "Is it traced?") and the section `DES` ("Design"; check `DES-01` "Is it sound?"), defined in
that order, and one answer (check `REQ-01`, outcome `Pass`, no justification, answered by `Ann` / `ann`), the
canonical text is

```text
ReviewCenter record v2
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
report
```

and the digest is `2a6b6bc4de3a50d758ee5d5a9438c10c2f398576946ce3a828d5713ef70d91ef`. For a record with the
fingerprint `abc`, one answer (check `REQ-01`, outcome `Fail`, justification `bad`, answered by `Ann` / `ann`), the
sections `DES` (does not apply, reason `none`) and `DOC` (applies), and one thread on `a.txt` (head side, lines 1 to
2, `Open`) whose starting comment by `Ann` (`ann`) with the text `Please fix` at time T is included in the report, the
upload canonical text is

```text
ReviewCenter record v2
fingerprint 3:abc
source upload
checklist -
answers
answer 6:REQ-01 4:Fail 3:bad 3:Ann 3:ann
sections
not-applicable 3:DES 4:none
report
thread file 5:a.txt 4:Head 1 2
claim 3:Ann 3:ann 10:Please fix {T ticks}
```

The side vectors use the upload record with the fingerprint `abc`, no checklist, no answers, and one thread on `a.txt`
for lines 1 to 2, `Open`, whose starting comment by `Ann` (`ann`) with the text `Please fix` at the UTC time
2025-03-04 05:06:07 (ticks `638766615670000000`) is included in the report. With the side `Head` the digest is
`00aacca477c6cb3b6e37c39f8252caa0852a4d28a9f2364c3865f632cc08651f`, and with the side `Baseline` (written
`8:Baseline`) it is `2890acd760711bfee36a2319e0cb9c881fb0c21cf2d9b2ecb1c9b23e743b45aa`. The state and the creation
time of the thread are not written, so they are not part of these vectors. A last vector covers the less
common values: a repository review (repository 7, the baseline `aaaa...` and the head `bbbb...` as above, selection
`**/*`, no review-set) with one added file
`c.txt` (no baseline hash, head hash `hs3`), one answer (check `REQ-01`, outcome `NotApplicable`, the empty
justification, answered by `Bob` / `bob`), and one thread on `a.txt` (head side, no lines, `Resolved`) with the
starting comment by `Bob` (`bob`) with the text `Done`, included in the report, one hour after the time T above. Its
canonical text is

```text
ReviewCenter record v2
fingerprint 3:abc
source git 7 40:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 40:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 4:**/* -
changes
change 5:c.txt 5:Added - 3:hs3
checklist -
answers
answer 6:REQ-01 13:NotApplicable 0: 3:Bob 3:bob
sections
report
thread file 5:a.txt 4:Head - -
claim 3:Bob 3:bob 4:Done 638766651670000000
```

and the digest is `c8bb5168eb86e1540291fe341d89c94ca71c711e0031112226508caf3358503e`; with a final line
`reopens 1` it is `1bd2f9a28c73eda31b0c1f71b1a3f07df4d09a1e1b5c2d58f3525070d170f4b8`. All answers were
calculated independently of the implementation with a short script (SHA-256 of the canonical text as written
above), not copied from the code under test.

The tests check the digests of the first two vectors, of the two review-set variants, of the checklist vector, of
the two side vectors, and of the last vector (without and with a reopen), the exact canonical text of the second, the
third, the fourth, and the last, and that the first two, the checklist vector, and the head-side vector keep their
digests with an explicit reopen count of 0. They also pin the report block (see the verification design of this
unit): chat, the thread state, and the thread creation time do not change the digest; an included starting comment, an
included reply, and an included resolution note do; the labels `claim`, `comment`, and `resolution`; the order of the
included comments; and the carried marker.

#### Key Methods

**Canonicalize**: Builds the canonical text of a record.

- *Parameters*: `ReviewRecord record` — the record; the frozen checklist definition it carries is read for the
  checklist lines.
- *Returns*: `string` — the canonical text described above.
- *Preconditions*: The record is not null.
- *Postconditions*: The text does not depend on the identity of any row, on the order of the answers, sections,
  threads, changes, or checklist definition lists, or on the storage order of the threads, and not on chat, the state
  of a thread, or its creation time. It does depend on the order of the included comments inside a thread. The order
  independence assumes unique sort keys (see the preconditions above).

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
  current signatures; `ReviewLock.IsConcluded` (part of the same unit) computes it to decide whether a review is
  concluded and so locked.
- **ReviewStore** (`Storage` subsystem) — computes the digest again inside the transaction that stores a signature and
  inside the write transactions that check the lock; it fills `ReviewRecord.Reopens` from the stored reopens.
- **Tests** — check the format and the known-answer vectors.
