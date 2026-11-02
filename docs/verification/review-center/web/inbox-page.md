### InboxPage

#### Verification Approach

`InboxPage` is verified with component tests defined in `InboxPageTests.cs`, which use the bUnit test context to render
the real component over the real `InboxService`, with in-memory stand-ins for the stores (`InboxWebTestBase`). The
signed-in user is set through the test authentication state of `PageTestBase`, so each test chooses between an
anonymous visitor, an ordinary reviewer, an author, and an auditor. The page is wrapped in the real session gate, so
the missing session is shown as it is in the product. Tests locate elements by their `data-testid` attributes and by
the links of the rendered markup. The unread markers of the review page, which use the same service, are verified in
`ReviewPageUnreadTests.cs` and described in the verification of the `ReviewPage` unit. The route protection is verified
in the `Web` subsystem tests, and the whole path in a browser by the system test.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- A visitor who is not signed in sees the session-ended message and no inbox.
- A user with nothing waiting sees the empty state, a summary of zero, and no Mark all as read button.
- A ballot to hand in, a dispute to deal with, a signature to give, a reply waiting, and a review that waits for others
  are shown in their own groups with the review, the chip, and a link to the review (to the checklist for the first two).
- The summary counts the reviews that need action and the updates separately, and adds the number of reviews that wait
  for others only when there is one.
- A row that the user has not seen carries the New marker, and the marker is gone once the review was seen.
- An open review past its due date is marked overdue, and one that is not yet due is not.
- Mark all as read stores the read state of every listed review and clears the markers.
- An auditor is not offered a signature to give.

#### Test Scenarios

**InboxPage_Render_Anonymous_IsNotShown**: Nobody is signed in and the page is rendered; the session-ended message is
shown instead of the inbox. This scenario is tested by `InboxPage_Render_Anonymous_IsNotShown`.

**InboxPage_Render_NothingWaiting_ShowsEmptyState**: A user with nothing waiting sees the empty message, a zero
summary, and no Mark all as read button. This scenario is tested by `InboxPage_Render_NothingWaiting_ShowsEmptyState`.

**InboxPage_Render_ToAnswerAndRepliesWaiting_ShowsSectionsRowsChipsAndLinks**: A reviewer who has not handed in the
ballot and has a thread that was answered sees both groups, each row with the review, the chip, and a link to the review
(to the checklist for To answer). This scenario is tested by
`InboxPage_Render_ToAnswerAndRepliesWaiting_ShowsSectionsRowsChipsAndLinks`.

**InboxPage_Render_NewSections_ShowTitlesChipsAndChecklistLinks**: Reviewers in the standings To answer, Disputed, Ready
to sign and Waiting for others see their group with the title, the chip and the link; To answer and Disputed link to the
checklist. This scenario is tested by `InboxPage_Render_NewSections_ShowTitlesChipsAndChecklistLinks`.

**InboxPage_Render_WaitingForOthers_NotCountedInSummary**: A review that waits for others is listed, adds "waiting for
others" to the summary, and is not counted as action. This scenario is tested by
`InboxPage_Render_WaitingForOthers_NotCountedInSummary`.

**InboxPage_Render_UnreadItem_ShowsDot**: A row that the user has not seen carries the New marker (here a To answer row
with the chip "Not started"), and after the review was seen the marker is gone. This scenario is tested by
`InboxPage_Render_UnreadItem_ShowsDot`.

**InboxPage_Render_OverdueReview_ShowsOverdueChip**: A review past its due date shows the overdue chip, and one that is
not yet due shows none. This scenario is tested by `InboxPage_Render_OverdueReview_ShowsOverdueChip`.

**InboxPage_Render_SummaryLine_ShowsActionAndUpdateCounts**: The summary states the number of reviews that need action
and the number of updates. This scenario is tested by `InboxPage_Render_SummaryLine_ShowsActionAndUpdateCounts`.

**InboxPage_MarkAllRead_ClearsUnreadDots**: The user presses Mark all as read; the read state of every listed review is
stored and the markers are gone. This scenario is tested by `InboxPage_MarkAllRead_ClearsUnreadDots`.

**InboxPage_Render_Auditor_ShowsNoSignItems**: An auditor who is named in a slot is shown no signature to give. This
scenario is tested by `InboxPage_Render_Auditor_ShowsNoSignItems`.
