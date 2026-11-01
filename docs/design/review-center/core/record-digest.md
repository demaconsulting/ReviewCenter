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
4. The consolidated standing of the ballots (see `BallotConsolidation`): only **submitted** ballots take part. For every
   check of a section that applies, the check id and whether the submitted ballots agree on it (with the agreed outcome:
   `Pass`, `Fail`, or `NotApplicable`), dispute it, or have not all answered it yet (pending). The checks of a section
   that the submitted ballots all mark as not applicable, or dispute, are not written.
5. Every checklist section that the submitted ballots agree is not applicable, or dispute: the section id and which of
   the two it is. A section that applies has no line.
6. The outcome of the consolidation: `none` while anything is pending or disputed or no ballot is submitted,
   otherwise `approved` or `rejected`. A signature attests this outcome.
7. The review conclusion text (`ReviewRecord.ReviewConclusionText`, the free-text conclusion of the review, never the
   approved or rejected conclusion of a ballot), but only when there is one. A change of the text, or setting or
   clearing it, changes the digest.
8. The report: every comment thread that has at least one comment **included in the report**
   (`ReviewComment.IncludeInReport`) is a block of the digest. A block holds the anchor of the thread (the file path,
   the side `Head` or `Baseline`, and the start and end line, or none for a whole file) and, in posting order, only
   the included comments: the display name of the author, the user name of the author, the text, and the time, each
   marked as the starting comment (`claim`, a Finding), a reply (`comment`), or a resolution note (`resolution`). A
   line of a thread means a line of the text of its side, so a head thread and a baseline thread on the same
   numbers are different discussions and give different digests. A thread that has no included comment is not in the
   digest at all. A thread about a check of the checklist has no file: its anchor is the check identifier alone.
9. The number of times an administrator reopened the review, but only when it is above zero (see below). A reopen is
   therefore part of what a signature approves: an earlier approval can never be current after a reopen.

**What the digest does not cover**:

- Chat: a comment that is not included in the report. Talking about the review therefore never makes a signature
  out of date.
- The state of a thread (`Open` or `Resolved`) and the creation time of a thread. Whether a thread is open is a
  separate rule: an open thread blocks signing and the conclusion of the review (see `SigningService` and
  `ReviewLock`), but it is not part of what a signature approves. Taking a comment into or out of the report, and
  promoting or demoting a thread, change the digest because they change the included comments.
- Draft ballots, and a slot that has no ballot. Only the agreed standing of the submitted ballots is written, so a
  reviewer who is still working never makes a signature out of date. A second ballot that agrees with the first does
  not change the digest; the first ballot that disagrees does, and it changes it again when the disagreement is settled.
- The individual answers: the justification, who answered (the display name and the user name), and when (`AnsweredAt`),
  and the reason, the person, and the time of a not-applicable section (`Reason`, `SetBy`, `SetByUsername`, `SetAt`).
  What a signature approves is what the reviewers agree on, not how they worded it or which of them said it, so a
  reworded justification or another person giving the same outcome does not invalidate a signature. The decision is
  pinned by tests.
- The slots of the ballots: their identities and their order, and the review that an answer or a section was carried
  from (`CarriedFromReviewId` of an answer or a section state).
- The hint of a checklist section that says when it can be skipped. It is display help, not wording of a check.
- Sections that apply. Applying is the default and carries no reason, so only the exception is recorded.
- The identities (row numbers) of threads and comments, and the order in which the store happens to return
  them or the changes or the ballots, or the order the checklist definition lists its sections and checks in.
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
checks
check {S:itemId} agreed {S:outcome}                        one line per check of a section that applies, sorted by
check {S:itemId} disputed                                  item id: agreed with the outcome, disputed, or
check {S:itemId} pending                                   pending (not yet answered by every submitted ballot)
sections
applicability {S:sectionId} not-applicable                 one line per section that the submitted ballots agree does
applicability {S:sectionId} disputed                       not apply, or dispute, sorted by section id
outcome none|approved|rejected                             the outcome of the consolidation
conclusion-text {S:text}                                   only when the review has a review conclusion text
report
thread file {S:path} {S:side} {start} {end}                one block per thread with an included comment: this line,
thread check {S:itemId}                                    or, for a thread about a check, this line instead of it,
claim {S:author} {S:username} {S:text} {ticks}             the starting comment, if it is included,
comment {S:author} {S:username} {S:text} {ticks}           each included reply, in posting order,
resolution {S:author} {S:username} {S:text} {ticks}        each included resolution note, in posting order
reopens {count}                                            only when the review was reopened (count above zero)
```

The `checks` and `sections` marker lines and the `outcome` line are always present, in this order, straight after the
checklist block; a review without a checklist has the markers with no lines under them and the outcome `none`. The
standing is read from `BallotConsolidation.Consolidate` over the frozen checklist and the
ballots of the record, where only submitted ballots count. A `check` line is written for every check of a section that
applies (checks of a section that is not applicable or disputed have no line, so the dispute of a section is one
`applicability` line), with `agreed` and the outcome as a length-prefixed enumeration name (for example
`4:Pass` or `13:NotApplicable`) when all submitted ballots gave the same outcome, `disputed` when they differ, and
`pending` when no ballot is submitted or a submitted ballot has no answer to it. Only the outcome is written: neither
the justification nor who answered nor when. An `applicability` line is written only for a section that is not
applicable or disputed, never for one that applies.

The `report` line is always present; a review without an included comment has the line alone and no block, so there is
no `report 0` line. The lines of a block are one per included comment in posting order, each with the label that fits
the comment (`claim` for the starting comment, `resolution` for a resolution note, otherwise `comment`).

The block of a thread about a check starts with `thread check {S:itemId}` in place of the `thread file` line: the
length-prefixed identifier of the check and nothing else, because such a thread has no file, no lines, no side, and is
never carried. Its comment lines are those of any block. A record whose threads all are about files gives the same text
as before, so the version stays `v2` and no signature is made out of date. In the ordinal sort a `thread check` block
comes before a `thread file` block.

The `conclusion-text` line is written only when the review has a review conclusion text, straight after the `outcome`
line and before `report`; with no text there is no such line, so every record without a text keeps the digest it had.
The text is written length-prefixed, so a line break inside it cannot imitate a line of its own. It is the text as
stored (normalized by `ChecklistService`: LF line breaks, trimmed), at most 4000 characters.

A follow-up review (see `ReviewService`) adds text that is written only when it applies, like `reopens`: a line
`follows {S:reviewId}` straight after the source block (after the change lines of a review from a repository, before
the `checklist` line), and the suffix `carried {S:reviewId}`, after a space, at the end of the `thread file` line of a
thread that was carried over from that review. A carried answer or section is a row that the reviewers of the
follow-up agree with in their ballots; where it came from is not written.

Notation and rules:

- `{S:x}` is a string written length-prefixed as `{length}:{text}`, where the length is the number of UTF-16 code
  units, so no text can imitate a delimiter or the start of another line. An absent string (for example the baseline of
  a review without one, the review-set identifier of a review that was not planned from a set, or the head hash of a
  deleted file) is written as `-`; the empty text is written as `0:`.
- `{repositoryId}`, `{start}`, `{end}`, and `{count}` are the numbers in the invariant culture; `{start}` and `{end}`
  are `-`
  when the thread is on the whole file.
- `{createdTicks}` and `{ticks}` are the UTC ticks of the time, in the invariant culture.
- The outcome, the thread side, and the change kind are written by their enumeration names (the
  side as a length-prefixed string, for example `4:Head` or `8:Baseline`).
- The selection text is the text kept with the review: the patterns in the order given, one per line, with `!` in
  front of an exclusion.
- Changes are sorted by path, checklist sections and checks by id, `check` lines by item id, and `applicability` lines
  by section id, all with the ordinal comparison. Each thread is first rendered to its block (the `thread file` line
  and its included comment lines joined by line feeds); the blocks are sorted with the ordinal comparison, so the
  order of threads, which the store does not guarantee, does not matter. The order of the comments inside a thread
  does matter and is kept as stored, because it is part of what was said.
- The header line holds the format version. Any change to this format, to what is covered, or to the way a value is
  written that alters the digest of an existing record must change the version, because it makes all earlier
  signatures out of date.
- **Preconditions of the claims above**: (1) The order independence holds for lists whose sort keys are unique; this is
  an assumption about the input, not something the digest enforces. The keys are unique in a stored record, because the
  store keys the answers and the section states of a ballot by check id and section id, a review has one ballot per
  slot, and files (and so changes) are keyed by path within a review, and
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
  and the `CarriedFromReviewId` of a thread are written as described above, only when they are not null. A review
  that follows nothing has no such text, and a test pins this with a fixed vector. The identity of the carried row
  (`CarriedFromThreadId`) is not covered (it is a number of another review's row and tells nothing the carried
  comments do not), but the marker is: a carried thread and a thread started in the review never share a digest, and a
  signature of a follow-up therefore binds to which review it follows and to which threads it took over. A carried
  thread keeps the report flag of each of its comments, so a carried Finding is in the report of the follow-up with
  the marker, and carried chat is not. The `CarriedFromReviewId` of an answer or a section state is not covered: a
  carried answer only seeds the draft ballot of a slot, and the digest sees the agreed outcome that the ballots then
  give, whether an answer was carried or given again.

**Known-answer vectors**: for an uploaded review with the fingerprint `abc`, no checklist, no ballots, and no thread
with an included comment, the canonical text is the eight lines

```text
ReviewCenter record v2
fingerprint 3:abc
source upload
checklist -
checks
sections
outcome none
report
```

and the digest is `4c93e63f9251787ecc5a267ee3d3f28922757b299294add1709bf8a401fad5cd`. For a review from the
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
checks
sections
outcome none
report
```

and the digest is `b80fa632a2aa11861ecb6bf8442c85a1485f80b0f0e84a408c9e78705835c747`. With the review-set `core`
the source line ends with `4:core` and the digest is
`748c61eff2e4702ee896753c341c9c66c08cd51826c6a8d0e276f510de7d275a`, and with the empty review-set it ends with `0:`
and the digest is `390050cf8169267cb7d48b3e327b1f4e5c182e42b9a06427e02a2edc635adb5a`. For an upload with the
fingerprint `abc`, the checklist `default-1` with the section `REQ` ("Requirements"; checks `REQ-01` "Is the code
correct?" and `REQ-02` "Is it traced?") and the section `DES` ("Design"; check `DES-01` "Is it sound?"), defined in
that order, and one submitted ballot with one answer (check `REQ-01`, outcome `Pass`, no justification, answered by
`Ann` / `ann`), the canonical text is

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
checks
check 6:DES-01 pending
check 6:REQ-01 agreed 4:Pass
check 6:REQ-02 pending
sections
outcome none
report
```

and the digest is `a4ca2841f1831fa27b4c7b241bb2f77ca1977c91b2fe47d72b135c07af29e38c`. The answerer and the absent
justification are not written. For a record with the same checklist and fingerprint, one submitted ballot with one
answer (check `REQ-01`, outcome `Fail`, justification `bad`, answered by `Ann` / `ann`), the section `DES` marked as
not applicable (reason `none`) and the section `DOC` marked as applicable, and one thread on `a.txt` (head side,
lines 1 to 2, `Open`) whose starting comment by `Ann` (`ann`) with the text `Please fix` at time T is included in the
report, the upload canonical text is

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
checks
check 6:REQ-01 agreed 4:Fail
check 6:REQ-02 pending
sections
applicability 3:DES not-applicable
outcome none
report
thread file 5:a.txt 4:Head 1 2
claim 3:Ann 3:ann 10:Please fix {T ticks}
```

The check `DES-01` has no line because its section is not applicable, and `DOC` has none because it applies.

The side vectors use the upload record with the fingerprint `abc`, no checklist, no ballots, and one thread on `a.txt`
for lines 1 to 2, `Open`, whose starting comment by `Ann` (`ann`) with the text `Please fix` at the UTC time
2025-03-04 05:06:07 (ticks `638766615670000000`) is included in the report. With the side `Head` the digest is
`15c6f2265ae8e9c29aea106ccaa687a7da34e99ebe18116cbeb8930c3e647bd7`, and with the side `Baseline` (written
`8:Baseline`) it is `1d50c27f46fcc273951f1f36b8ecba3245dfc743fe7bf1656f4f1424920ee753`. The state and the creation
time of the thread are not written, so they are not part of these vectors. A last vector covers the less
common values: a repository review (repository 7, the baseline `aaaa...` and the head `bbbb...` as above, selection
`**/*`, no review-set) with one added file
`c.txt` (no baseline hash, head hash `hs3`), the checklist `v1` with the section `A` ("Sec") and the check `A-1`
("q"), one submitted ballot with one answer (check `A-1`, outcome `NotApplicable`, the empty justification, answered
by `Bob` / `bob`), and one thread on `a.txt` (head side, no lines, `Resolved`) with the
starting comment by `Bob` (`bob`) with the text `Done`, included in the report, one hour after the time T above. Its
canonical text is

```text
ReviewCenter record v2
fingerprint 3:abc
source git 7 40:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 40:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 4:**/* -
changes
change 5:c.txt 5:Added - 3:hs3
checklist 2:v1
section 1:A 3:Sec
item 3:A-1 1:q
checks
check 3:A-1 agreed 13:NotApplicable
sections
outcome approved
report
thread file 5:a.txt 4:Head - -
claim 3:Bob 3:bob 4:Done 638766651670000000
```

and the digest is `245104c8669f8eedbc77a42250fcbd0971c3aa53bc6d052f6717cde03d3984ba`; with a final line
`reopens 1` it is `13914386de652fabc98c7568dc4c8a1130abfaaeeba28b727520bedf309b9c5b`. All answers were
calculated independently of the implementation with a short script (SHA-256 of the canonical text as written
above), not copied from the code under test.

With a review conclusion text, the upload record with the fingerprint `abc` above (no checklist, no ballots, no
report block) gets the line `conclusion-text 11:Looks good.` between `outcome none` and
`report`, and the digest is `fcae2e5e3f3404ec7715b8a10002f87f488112d7eb88e45a0c12711ea8376442`. With the text
`Line one` and `Line two` on two lines (17 characters, one line feed) the line is `conclusion-text 17:Line one`
followed by a line feed and `Line two`, and the digest is
`118f5fc446ef656ffbf30883b3392f9d85c5c32b18f4ab5a34edb4e666d8ae21`. The tests pin both, and that a record without a
text has no `conclusion-text` line and the digest of the first vector.

The tests check the digests of the first two vectors, of the two review-set variants, of the checklist vector, of
the two side vectors, and of the last vector (without and with a reopen), the exact canonical text of the second, the
third, the fourth, and the last, and that the first two, the checklist vector, and the head-side vector keep their
digests with an explicit reopen count of 0. They also pin the report block (see the verification design of this
unit): chat, the thread state, and the thread creation time do not change the digest; an included starting comment, an
included reply, and an included resolution note do; the labels `claim`, `comment`, and `resolution`; the order of the
included comments; and the carried marker. A further set of tests pins the consolidated standing: the `check` line of an
agreed, a disputed, and a pending check, the `applicability` line of an agreed not-applicable and of a disputed
section, the sorting of the check lines, and the three outcome lines; that a draft ballot, a reworded justification, a
different answerer, the order and the identities of the slots, and a carried marker of an answer do not change the
digest; and that a second ballot that agrees leaves the digest as it was while the first disagreement changes it.

#### Key Methods

**Canonicalize**: Builds the canonical text of a record.

- *Parameters*: `ReviewRecord record` — the record; the frozen checklist definition it carries is read for the
  checklist lines, and its ballots are consolidated for the `check`, `applicability`, and `outcome` lines.
- *Returns*: `string` — the canonical text described above.
- *Preconditions*: The record is not null.
- *Postconditions*: The text does not depend on the identity of any row, on the order of the ballots, sections,
  threads, changes, or checklist definition lists, or on the storage order of the threads, and not on chat, the state
  of a thread, or its creation time, nor on a draft ballot, a justification, or who answered. It does depend on the
  order of the included comments inside a thread. The order independence assumes unique sort keys (see the
  preconditions above).

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

- **ReviewRecord** and the thread, comment, ballot, checklist, and reopen count (`ReviewRecord.Reopens`) types
  of the `Core` subsystem, which it
  reads.
- **BallotConsolidation** — consolidates the submitted ballots of the record into the standing that is written.
- **.NET runtime library** — `System.Security.Cryptography.SHA256`, `System.Text.Encoding` for UTF-8, and
  `System.Globalization.CultureInfo` for the invariant number format.

#### Callers

- **SigningService** — computes the digest to show, to compare with the digest the signer saw, and to find the
  current signatures; `ReviewLock.IsConcluded` (part of the same unit) computes it to decide whether a review is
  concluded and so locked.
- **ReviewStore** (`Storage` subsystem) — computes the digest again inside the transaction that stores a signature and
  inside the write transactions that check the lock; it fills `ReviewRecord.Reopens` from the stored reopens and
  `ReviewRecord.Ballots` from the stored ballots, drafts included (the digest ignores the drafts).
- **Tests** — check the format and the known-answer vectors.
