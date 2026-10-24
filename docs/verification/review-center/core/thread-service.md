### ThreadService

#### Verification Approach

`ThreadService` is verified with unit tests defined in `ThreadServiceTests.cs`. The tests use the real service and
the real `FileText` with a fake in-memory store (`FakeReviewStore`) and a fixed clock, so every rule can be
exercised without a database. Failures are checked through the rule carried by the `DomainRuleException`, and every
rejection test also checks that nothing was stored. The line-count tests are theories over texts with Unix and
Windows line endings, with and without a final line ending. The author and text tests check the limit itself and
one character more. The behavior of the real database is verified by the Storage tests, not here.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A thread on the whole file and a thread on a line range are stored in the Open state with the creation time and
  the first comment.
- A range ending exactly at the last line is accepted, and a range beyond it, a range with one endpoint missing,
  a start below 1, an end before the start, a range on a binary or empty file, and a thread on an unknown file or
  review are rejected with `ThreadAnchorInvalid`.
- Blank or too long authors and texts are rejected, and padded values are stored trimmed.
- A reply is appended after the earlier comments without changing them or the state.
- Resolving and reopening change only the state and are idempotent.
- An unknown thread, or one of another review, is rejected with `ThreadNotFound`.
- Threads are listed in creation order with their comments, and the list is empty when there are none.

#### Test Scenarios

**ThreadService_StartThreadAsync_WholeFile_StoresOpenThreadWithFirstComment**: A thread without lines is started;
it is stored Open with no anchor lines, the clock time, and one comment with the given author and text. This
scenario is tested by `ThreadService_StartThreadAsync_WholeFile_StoresOpenThreadWithFirstComment`.

**ThreadService_StartThreadAsync_LineRange_StoresRange**: A single line and a range of several lines are started;
the start and end lines are stored as given. This scenario is tested by
`ThreadService_StartThreadAsync_LineRange_StoresRange`.

**ThreadService_StartThreadAsync_RangeLimits_AreInclusiveAndCountLinesBySeparator**: For texts with Unix and Windows
line endings, with and without a final line ending, a range ending at the last line is accepted and one line more is
rejected. This scenario is tested by `ThreadService_StartThreadAsync_RangeLimits_AreInclusiveAndCountLinesBySeparator`.

**ThreadService_StartThreadAsync_InvalidRange_ThrowsThreadAnchorInvalid**: A range with only one endpoint given, a
start of 0 or below, and an end before the start are rejected with `ThreadAnchorInvalid`. This scenario is tested by
`ThreadService_StartThreadAsync_InvalidRange_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_RangeOnBinaryOrEmptyFile_ThrowsThreadAnchorInvalid**: A line range on a binary file
and on an empty file is rejected with `ThreadAnchorInvalid`. This scenario is tested by
`ThreadService_StartThreadAsync_RangeOnBinaryOrEmptyFile_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_WholeFileOnBinaryFile_IsAllowed**: A thread on the whole of a binary file is
accepted. This scenario is tested by `ThreadService_StartThreadAsync_WholeFileOnBinaryFile_IsAllowed`.

**ThreadService_StartThreadAsync_UnknownFileOrReview_ThrowsThreadAnchorInvalid**: A thread on a path that is not in
the review, and on a review that does not exist, is rejected with `ThreadAnchorInvalid`. This scenario is tested by
`ThreadService_StartThreadAsync_UnknownFileOrReview_ThrowsThreadAnchorInvalid`.

**ThreadService_StartThreadAsync_BlankOrLongAuthor_ThrowsCommentAuthorInvalid**: A missing author, a white-space
author, and an author over the limit are rejected with `CommentAuthorInvalid`, while an author of exactly the limit
is accepted. This scenario is tested by `ThreadService_StartThreadAsync_BlankOrLongAuthor_ThrowsCommentAuthorInvalid`.

**ThreadService_StartThreadAsync_BlankOrLongText_ThrowsCommentTextInvalid**: A missing text, a white-space text, and
a text over the limit are rejected with `CommentTextInvalid`, while a text of exactly the limit is accepted. This
scenario is tested by `ThreadService_StartThreadAsync_BlankOrLongText_ThrowsCommentTextInvalid`.

**ThreadService_StartThreadAsync_PaddedAuthorAndText_AreTrimmed**: An author and text with surrounding white space
are stored without it. This scenario is tested by `ThreadService_StartThreadAsync_PaddedAuthorAndText_AreTrimmed`.

**ThreadService_ReplyAsync_ExistingThread_AppendsInOrderAndKeepsEarlierComments**: Two replies are added to a
thread, which is also resolved; the comments are in posting order, the earlier ones are unchanged, and the state is
unchanged. This scenario is tested by
`ThreadService_ReplyAsync_ExistingThread_AppendsInOrderAndKeepsEarlierComments`.

**ThreadService_ReplyAsync_InvalidAuthorOrText_ThrowsAndStoresNothing**: A reply with a blank author or text is
rejected with the matching rule and the thread keeps only its first comment. This scenario is tested by
`ThreadService_ReplyAsync_InvalidAuthorOrText_ThrowsAndStoresNothing`.

**ThreadService_ReplyAsync_UnknownThreadOrOtherReview_ThrowsThreadNotFound**: A reply to a thread that does not
exist, and to a thread of another review, is rejected with `ThreadNotFound`. This scenario is tested by
`ThreadService_ReplyAsync_UnknownThreadOrOtherReview_ThrowsThreadNotFound`.

**ThreadService_ResolveAsync_ReopenAsync_ChangeStateKeepCommentsAndAreIdempotent**: A thread is resolved twice and
reopened twice; the state follows each request, and the comments stay as they were. This scenario is tested by
`ThreadService_ResolveAsync_ReopenAsync_ChangeStateKeepCommentsAndAreIdempotent`.

**ThreadService_ResolveAsync_UnknownThread_ThrowsThreadNotFound**: Resolving and reopening a thread that does not
exist is rejected with `ThreadNotFound`. This scenario is tested by
`ThreadService_ResolveAsync_UnknownThread_ThrowsThreadNotFound`.

**ThreadService_ListAsync_SeveralThreads_ReturnsAllInOrderWithComments**: Threads on two files with replies are
listed; all are returned in creation order with their comments in posting order. This scenario is tested by
`ThreadService_ListAsync_SeveralThreads_ReturnsAllInOrderWithComments`.

**ThreadService_ListAsync_NoThreadsOrUnknownReview_ReturnsEmpty**: A review without threads and a review that does
not exist both give an empty list. This scenario is tested by
`ThreadService_ListAsync_NoThreadsOrUnknownReview_ReturnsEmpty`.

**ThreadService_Constructor_NullArguments_Throws**: A null store and a null clock are rejected with
`ArgumentNullException`. This scenario is tested by `ThreadService_Constructor_NullArguments_Throws`.
