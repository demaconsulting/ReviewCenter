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
- A ballot to hand in, a dispute to deal with, a signature to give, a signature that no longer counts, a question of
  another user, a reply waiting, a failed check, a review that waits for others, and an update
  are shown in their own groups with the review, the chip, and a link to the review (to the checklist for the ballot
  and the dispute).
- The summary counts the reviews that need action and the updates separately, and adds the number of reviews that wait
  for others only when there is one.
- A row that the user has not seen carries the New marker, and the marker is gone once the review was seen.
- An open review past its due date is marked overdue, and one that is not yet due is not.
- Mark all as read stores the read state of every listed review and clears the markers.
- The Mark all as read button is offered while the inbox lists a review, and the page reports that it is interactive
  also when nothing is listed.
- A reply waiting is shown in the group Replies waiting with its title and the row of the review.
- An auditor is not offered a signature to give.
- The page reports through its root marker that it is interactive once rendered, and the router shows nothing of the
  inbox to a visitor who is not signed in.
- The route serves a signed-in user the inbox as an interactive page that waits for its live connection, and a
  visitor who is not signed in is sent to the login page.

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

**InboxPage_Render_QuestionForYou_ShowsRowLinkingToReview**: A reviewer who handed in finds, under To answer, a row
with the chip "Question for you" for an open question that another user started in the review (the To answer list holds
the chip); the row names the asker,
does not quote the question, counts the review once as action, and links to the review page, not to the checklist. This
scenario is tested by `InboxPage_Render_QuestionForYou_ShowsRowLinkingToReview`.

**InboxPage_Render_SignatureLost_ShowsSectionChipAndReason**: A signer whose signature no longer counts, while a new
finding blocks signing, sees the group "Your signature no longer counts" with the chip, the reason, and the link to the
review, counted as action. This scenario is tested by `InboxPage_Render_SignatureLost_ShowsSectionChipAndReason`.

**InboxPage_Render_FailedChecks_ShowsTitledSection**: A reviewer marks one check of the review of the author as Fail;
the author finds the group Failed checks with its title and the row of the review. This scenario is tested by
`InboxPage_Render_FailedChecks_ShowsTitledSection`.

**InboxPage_Render_Update_ShowsTitledSection**: Both reviewers signed a review, so it was approved after the user
signed; the user finds the group Updates with its title and the row of the review, which carries the chip Approved.
This scenario is tested by `InboxPage_Render_Update_ShowsTitledSection`.

**InboxPage_Render_UpdateRejected_ShowsTitledSectionAndChip**: The only reviewer of a review answers one check with
Fail, writes the conclusion text and signs, so the review is rejected; the author finds the group Updates with the row
that carries the chip Rejected, names the signer ("Signed by Ann"), and links to the review. This scenario is tested by
`InboxPage_Render_UpdateRejected_ShowsTitledSectionAndChip`.

**InboxPage_Render_UpdateReopened_ShowsTitledSectionAndChip**: The only reviewer of a review signs, so it is approved,
and a system administrator reopens it; the author finds the group Updates with the row that carries the chip Reopened
and names the administrator ("Reopened by Root"). This scenario is tested by
`InboxPage_Render_UpdateReopened_ShowsTitledSectionAndChip`.

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

**InboxPage_AfterRender_Interactive_SetsMarkerAndOffersMarkAllRead**: A user with a review waiting renders the page; the
root element has `data-interactive` equal to `true` and the Mark all as read button is offered. This scenario is tested
by `InboxPage_AfterRender_Interactive_SetsMarkerAndOffersMarkAllRead`.

**InboxPage_AfterRender_NothingWaiting_SetsInteractiveMarker**: A user with nothing waiting renders the page; the root
element has `data-interactive` equal to `true` although no row is listed. This scenario is tested by
`InboxPage_AfterRender_NothingWaiting_SetsInteractiveMarker`.

**InboxPage_Render_ItemsListed_OffersMarkAllReadButton**: A user with a review waiting renders the page; the row is
listed and the Mark all as read button is offered. This scenario is tested by
`InboxPage_Render_ItemsListed_OffersMarkAllReadButton`.

**InboxPage_Render_RepliesWaiting_ShowsTitledSection**: A reviewer asked a question and another user answered it; the
reviewer finds the group Replies waiting with its title and the row of the review. This scenario is tested by
`InboxPage_Render_RepliesWaiting_ShowsTitledSection`.

**InboxPage_Render_RepliesWaitingOnOwnReview_ShowsTitledSection**: A reviewer asked a question and another user
answered it; the author of the review, who did not take part in the thread, finds the group Replies waiting with its
title and the row of the review with the chip "1 waiting". This scenario is tested by
`InboxPage_Render_RepliesWaitingOnOwnReview_ShowsTitledSection`.

**InboxPage_Render_AnonymousThroughRouter_ShowsLoginLinkAndNoInbox**: Nobody is signed in and the router, which applies
the authorization rules of a page, is rendered at `/inbox`; the inbox and its rows are not shown, and a link to the
login page that returns to `/inbox` is. This scenario is tested by
`InboxPage_Render_AnonymousThroughRouter_ShowsLoginLinkAndNoInbox`.

**WebSubsystem_InboxPage_SignedIn_IsServedAsInteractivePage**: A signed-in user requests `/inbox` through the real
pipeline; the response is the inbox page, still marked `data-interactive="false"` because no live connection exists
yet; the test shows that the page is served with this not-yet-interactive marker, not the marker after the render.
This scenario is tested by
`WebSubsystem_InboxPage_SignedIn_IsServedAsInteractivePage` in `WebSubsystemTests.cs`, part of the evidence of the
subsystem requirement `ReviewCenter-Web-Inbox`.

**WebSubsystem_InboxPage_Unauthenticated_IsChallenged**: A visitor who is not signed in requests `/inbox` through the
real pipeline and is redirected to the login page, which returns to the inbox, and nothing of the inbox is in the body.
This scenario is tested by `WebSubsystem_InboxPage_Unauthenticated_IsChallenged` in `WebSubsystemTests.cs`, the evidence
of the subsystem requirement `ReviewCenter-Web-Inbox-SignedInOnly`.
