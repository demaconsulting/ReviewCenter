### InboxStore

#### Verification Approach

`InboxStore` is verified with unit tests defined in `InboxStoreTests.cs`. The tests use the real store over a real
SQLite database file in a temporary folder (`TestStore`), add the users, project, reviews, and slots directly, and read
the rows back with a second context. A real database is used because the candidate query joins slots, reviews, and
users, and because the rule that the read state never moves backwards is enforced by a conditional update that has
exactly one outcome per race, which only the database can show; many racing writers are released together to prove it.
The tests of the `ReviewCenterDbContext` model for the table and its indexes are described in the verification of that
unit. How the service uses the store is verified by the `InboxService` tests over an in-memory stand-in.

#### Test Environment

N/A - standard test environment (a temporary SQLite database file is created by each test and removed afterwards).

#### Acceptance Criteria

- All tests pass with zero failures.
- A review is a candidate when the user is its primary reviewer, its alternate, or its creator (the creator in any
  spelling of the case), and reviews of other people are left out.
- The candidate listing returns light records and loads no file content.
- The first mark inserts a row, a later one updates it, and an older one changes nothing.
- Many racing writers neither fail nor lose the highest value.
- The listing of states returns only the rows of the asked user.
- Deleting a review deletes its states only, and deleting a user deletes the states of that user.
- Missing and empty arguments are refused.

#### Test Scenarios

**InboxStore_ListInvolvedReviewsAsync_PrimaryAlternateAndCreator_AreFound**: Reviews in which the user is primary
reviewer, alternate, or creator (in another spelling of the case) are listed, and the reviews of other people are not.
This scenario is tested by `InboxStore_ListInvolvedReviewsAsync_PrimaryAlternateAndCreator_AreFound`.

**InboxStore_ListInvolvedReviewsAsync_DoesNotLoadFiles_ReturnsLightRecords**: A review with a large file is described
by the same few values as one without, so no file content is loaded. This scenario is tested by
`InboxStore_ListInvolvedReviewsAsync_DoesNotLoadFiles_ReturnsLightRecords`.

**InboxStore_MarkSeenAsync_InsertsThenUpdatesAndNeverMovesBackwards**: The first mark inserts a row, a later one updates
it, and an older one changes nothing. This scenario is tested by
`InboxStore_MarkSeenAsync_InsertsThenUpdatesAndNeverMovesBackwards`.

**InboxStore_MarkSeenAsync_ParallelCalls_NoExceptionAndHighestWins**: Many racing writers neither fail nor lose the
highest value. This scenario is tested by `InboxStore_MarkSeenAsync_ParallelCalls_NoExceptionAndHighestWins`.

**InboxStore_ListStatesAsync_ReturnsOnlyTheUsersRows**: The listing returns only the rows of the asked user. This
scenario is tested by `InboxStore_ListStatesAsync_ReturnsOnlyTheUsersRows`.

**InboxStore_DeletingTheReview_DeletesTheStates**: Deleting a review deletes the states of that review and no others.
This scenario is tested by `InboxStore_DeletingTheReview_DeletesTheStates`.

**InboxStore_DeletingTheUser_DeletesTheStates**: Deleting a user deletes the states of that user. This scenario is
tested by `InboxStore_DeletingTheUser_DeletesTheStates`.

**InboxStore_NullArguments_Throw**: Null and empty arguments are refused. This scenario is tested by
`InboxStore_NullArguments_Throw`.
