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
`FilePath` (the path of one file of the review), `StartLine` and `EndLine` (both null for a thread on the whole
file, otherwise an inclusive 1-based range), `State`, `Created`, `Comments` (a list of `ReviewComment`), and the
derived, not stored, `LastActivity`.

**ReviewComment**: a data class (init-only properties) of `Id` (assigned by the store), `ThreadId`, `Author`, `Text`,
and `Created` (UTC).

**ReviewThreadState**: a closed enumeration with the values `Open` and `Resolved`.

**FileText**: a static class with `TryDecode`, which returns the text of file bytes or null when the bytes contain a
NUL byte or are not valid UTF-8 (a binary file), and `SplitLines`, which splits text into lines. Lines end at `\n`
and one `\r` before it is dropped, so Windows and Unix line endings count alike. A final line ending does not add a
line, an empty file has no lines, and a lone `\r` not followed by `\n` (inside a line or at the very end of the
file) is kept. The review page shows the lines
and `ThreadService` validates ranges against them, so both use this one definition.

Rules for the input: the author is not empty after trimming and has at most 100 characters; the text is not empty
after trimming and has at most 4000 characters (the constants are in `ReviewLimits`); both are stored trimmed. A
thread is anchored either to the whole file (no lines given) or to a range in which both lines are given, the start
line is at least 1, the end line is not before the start line, and the end line is not beyond the last line of the
file. A binary file and an empty file have no lines, so they can be commented only as a whole.

**Immutability**: posted comments are never changed or removed. The service and the store interface offer no way to
edit or delete a comment or a thread. The only change to an existing thread is its state, which is not attributed
to anyone or timestamped yet.

**Temporary identity**: there is no sign-in yet, so the author is free text that is not verified and can be any
name. It is in the same position as the single anonymous operator of the rest of the product, and is to be replaced
by the signed-in user when identity is added.

#### Key Methods

**StartThreadAsync**: Starts a thread with its first comment.

- *Parameters*: `int projectId`, `string reviewId`, `string filePath`, `int? startLine`, `int? endLine`,
  `string? author`, `string? text`, and a cancellation token.
- *Returns*: `Task<ReviewThread>` — the stored thread in the `Open` state with its first comment and the creation
  time from the clock.
- *Postconditions*: Nothing is stored when a rule fails.

The rules are checked in this order, and the first violation is reported: the author (`CommentAuthorInvalid`); the
text (`CommentTextInvalid`); the shape of the anchor, that is both lines or neither, start at least 1, and end not
before start (`ThreadAnchorInvalid`); that the review and the file exist (`ThreadAnchorInvalid`); and, only when a
range is given, that the file is text and the end line is within its line count (`ThreadAnchorInvalid`). Checks that
need no I/O come first so that a bad request fails cheaply, and file content is read only when a range needs it.

**ReplyAsync**: Appends a comment to an existing thread of the review.

- *Parameters*: `int projectId`, `string reviewId`, `int threadId`, `string? author`, `string? text`, and a
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

**NextStep**: A static, pure function that words the next action for the threads of a review as one sentence: "No
discussion yet - select a file and comment on a line." when there are no threads, "1 open thread to resolve." or
"{n} open threads to resolve." when some are open, and "All threads resolved." otherwise.

**ReviewThread.LastActivity**: A read-only value derived from the comments, the time of the latest comment (the
thread's `Created` when it has none). It is not stored, so the database is unchanged.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null store or clock at construction
raises `ArgumentNullException`. Store failures propagate unchanged.

#### Dependencies

- **IReviewStore** — checks the review, reads file content, and stores and reads threads (injected).
- **TimeProvider** — supplies the creation times (injected).

#### Callers

- **ReviewPage** — starts, answers, resolves, reopens, and lists threads, and uses `FileText` to show lines.
- **ServerHost** — registers the service for the pages.
