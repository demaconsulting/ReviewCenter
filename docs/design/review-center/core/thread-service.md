### ThreadService

![Core Structure](CoreView.svg)

#### Purpose

`ThreadService` owns the rules for review comments: it starts a thread on a file of a review, appends replies,
resolves and reopens threads, and reads the threads of a review back, and it shapes them for display (last activity,
an index by state, and a next-step sentence). It is also the home of the comment types
`ReviewThread`, `ReviewComment`, and `ReviewThreadState`, and of `FileText`, which defines what a text file and a
line are.

#### Data Model

**ReviewThread**: a data class (init-only properties) of `Id` (assigned by the store), `ProjectId`, `ReviewId`,
`FilePath` (the path of one file of the review), `Side` (which version of the file the lines belong to, default
`Head`), `StartLine` and `EndLine` (both null for a thread on the whole
file, otherwise an inclusive 1-based range), `State`, `Created`, `Comments` (a list of `ReviewComment`), and the
derived, not stored, `LastActivity`.

**ThreadSide**: a closed enumeration with the values `Head` (the version under review) and `Baseline` (the version
before the change, only for a review from a repository that has a baseline commit). A thread on the whole file is
always `Head`.

**ReviewComment**: a data class (init-only properties) of `Id` (assigned by the store), `ThreadId`, `Author` (the
display name of the person at the time of writing), `AuthorUsername` (the user name of the person), `Text`, and
`Created` (UTC).

**ReviewThreadState**: a closed enumeration with the values `Open` and `Resolved`.

**FileText**: a static class with `TryDecode`, which returns the text of file bytes or null when the bytes contain a
NUL byte or are not valid UTF-8 (a binary file), `SplitLines`, which splits text into lines, and `TryReadLines`,
which decodes bytes and splits them in one call and returns null for a binary file. Lines end at `\n`
and one `\r` before it is dropped, so Windows and Unix line endings count alike. A final line ending does not add a
line, an empty file has no lines, and a lone `\r` not followed by `\n` (inside a line or at the very end of the
file) is kept. The review page shows the lines, `ThreadService` validates ranges against them, and `LineDiff`
numbers its rows with them, so all three use this one definition.

Rules for the input: the text is not empty
after trimming and has at most 4000 characters (the constants are in `ReviewLimits`); it is stored trimmed. A
thread is anchored either to the whole file (no lines given) or to a range in which both lines are given, the start
line is at least 1, the end line is not before the start line, and the end line is not beyond the last line of the
side the thread is on. A binary file and an empty file have no lines, so they can be commented only as a whole. A file
that was deleted in a review from a repository has no head content, so on the head side it can be commented only as
a whole; its baseline lines can be commented on the baseline side.

**Side rules**: the side of a thread decides which content the range is checked against. The `Head` side uses the
head content of the file. The `Baseline` side uses the baseline content and is allowed only when the review is from a
repository and has a baseline commit, and the file is `Modified` or `Deleted` (an `Added` file has no baseline). A
whole-file anchor cannot name the `Baseline` side; whole-file threads are stored as `Head`.

**Immutability**: posted comments are never changed or removed. The service and the store interface offer no way to
edit or delete a comment or a thread. The only change to an existing thread is its state, which is not attributed
to anyone or timestamped yet.

**Author**: the author of a comment is the signed-in user, passed as an `Actor` by the page, never typed text. The
comment stores the display name of the user at that time (`Author`) and the user name (`AuthorUsername`), so a later
change of the display name does not rewrite the record, and the author can still be told from another user with the
same display name. A null actor is a programming error and raises `ArgumentNullException`; there is no rule about the
author, because the display name of a user was validated when the user was created.

#### Key Methods

**StartThreadAsync**: Starts a thread with its first comment.

- *Parameters*: `int projectId`, `string reviewId`, `string filePath`, `int? startLine`, `int? endLine`,
  `Actor actor`, `string? text`, `ThreadSide side`, and a cancellation token. An overload without `side` starts a
  `Head` thread, so callers that do not know about sides keep their call shape.
- *Returns*: `Task<ReviewThread>` — the stored thread in the `Open` state with its first comment, the side, and the
  creation time from the clock.
- *Postconditions*: Nothing is stored when a rule fails.

The rules are checked in this order, and the first violation is reported: the text (`CommentTextInvalid`); the shape
of the anchor, that is both lines or neither, start at least 1, and end not
before start (`ThreadAnchorInvalid`); that a whole-file anchor does not name the `Baseline` side
(`ThreadAnchorInvalid`); that the review and the file exist (`ThreadAnchorInvalid`); that the side may carry lines
for this file (`ThreadAnchorInvalid`: a `Head` range on a deleted file, "A deleted file has no head lines; comment on
its baseline lines or on the whole file instead."; a `Baseline` range on a review without a baseline commit or on an
added file, "This file has no baseline lines to comment on."); and, only when a range is given, that the content of
the chosen side is text and the end line is within its line count (`ThreadAnchorInvalid`). Checks that
need no I/O come first so that a bad request fails cheaply, and file content is read only when a range needs it.

**ReplyAsync**: Appends a comment to an existing thread of the review.

- *Parameters*: `int projectId`, `string reviewId`, `int threadId`, `Actor actor`, `string? text`, and a
  cancellation token.
- *Returns*: `Task<ReviewComment>` — the stored comment.
- *Postconditions*: Earlier comments are unchanged and the state of the thread does not change, so a resolved thread
  still accepts replies. A thread that does not exist in the review is reported as `ThreadNotFound`.

**ResolveAsync** and **ReopenAsync**: Set the state of a thread to `Resolved` or `Open`. Repeating a request leaves
the state as requested. A thread that does not exist in the review is reported as `ThreadNotFound`.

**ListAsync**: Lists all threads of a review, for every file, in creation order, each with its comments in posting
order; the list is empty when there are none or the review does not exist.

**BuildIndex**: A static, pure function that shapes the threads of a review for an index.

- *Parameters*: `IEnumerable<ReviewThread> threads` and `ReviewThreadState? state` — the state to keep, or `null`
  for all.
- *Returns*: `IReadOnlyList<ReviewThread>` — the matching threads, newest `LastActivity` first, and the higher `Id`
  first when the times are equal, so the order is stable.

The sort is done in memory because the database cannot order by a time stamp. Nothing is stored or read.

**NextStep**: A static, pure function that words the next action for the threads of a review and, optionally, the
progress of its checklist (`ChecklistProgress?`) and the number of checks answered Fail (default 0) as one short
sentence. Without a checklist: "No
discussion yet - select a file and comment on a line." when there are no threads, "1 open thread to resolve." or
"{n} open threads to resolve." when some are open, and "All threads resolved." otherwise. With a checklist the
unanswered checks come first, then the checks answered Fail, then the open threads, and the sentence stays one line:
"{n} checklist checks unanswered, {k} checks answered Fail, {m} open threads to resolve." (a part that is zero is
left out), "Checklist complete; {k} checks answered Fail, {m} open threads to resolve." when the checklist is
complete but failed checks or open threads remain, "Checklist complete and all threads resolved." when there are
threads and all are resolved, and "Checklist complete." when the checklist is complete and there are no threads
(singular forms are used for one). Having no threads yet is not a task when a checklist exists, so the checklist
leads. The failed checks are counted by the caller (`SigningService`), because each one blocks signing; with the
default of 0 the wording is the same as before signing existed.

**ReviewThread.LastActivity**: A read-only value derived from the comments, the time of the latest comment (the
thread's `Created` when it has none). It is not stored, so the database is unchanged.

**Audit**: Starting a thread, replying, resolving, and reopening take the `Actor` and record `thread.started`,
`thread.replied`, `thread.resolved`, and `thread.reopened`. The draft is saved by the store in the same transaction as
the change, and only when the change took effect (a thread that does not exist records nothing). The text of a comment
is never written to the trail: the detail holds the identifiers, the anchor (a line anchor on the `Baseline` side
carries the suffix `(baseline)`; a head anchor is worded as before), and the first 8 hex characters of the SHA-256 of the
text, so that a later edit of the text would be noticed without copying the text into a second place. See the
`AuditService` unit for the detail formats.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null store or clock at construction,
or a null actor, raises `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — checks the review, reads file content, and stores and reads threads (injected).
- **TimeProvider** — supplies the creation times (injected).

#### Callers

- **ReviewPage** — starts, answers, resolves, reopens, and lists threads, and uses `FileText` to show lines.
- **ServerHost** — registers the service for the pages.
