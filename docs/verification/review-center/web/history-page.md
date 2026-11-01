### HistoryPage

#### Verification Approach

`HistoryPage` is verified with component tests defined in `HistoryPageTests.cs`, which use the bUnit test context to
render the real component over a real `AuditService`, `ReviewService`, and `ProjectService` with the in-memory fakes
(`FakeAuditStore` and the fake review store). The signed-in user is set by the test authentication state of the shared
helper `PageTestBase`. Tests locate elements by their `data-testid` attributes. The route-level behavior for a visitor
who is not signed in is verified through the router test `Routes_Render_NobodySignedInOpensHistoryRoute_DoesNotShowEntries`
in the same file. The History link on the review page is verified by `ReviewPageTests.cs`. The real database and the
store are covered by the `Storage` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- A review with entries shows only its own entries, newest first, with the time, actor, action, and detail of each.
- A review without entries shows the empty message; an unknown review shows the not-found message and no entries.
- A history of more than 50 entries shows the newest 50 with an Older link, and the links lead to the other pages; a
  link is shown only when entries exist behind it.
- Markup in an actor, an action, or a detail is shown as text and not interpreted.
- The page links back to the review, and the review page links to the history.
- A visitor who is not signed in sees no entries.
- A viewer who has not handed in the own ballot in a review with several slots sees the detail of the answer entries of
  other reviewers replaced by a note, with the action, the actor, and the time shown; an administrator sees the details.

#### Test Scenarios

**HistoryPage_HiddenViewer_HidesAnswerDetailsOnly**: A reviewer with a draft opens the history of a review with several
slots; the answer entries of the other reviewer show the hidden note and the actor, and the other entries are as
stored. This scenario is tested by `HistoryPage_HiddenViewer_HidesAnswerDetailsOnly`.

**HistoryPage_Administrator_SeesFullAnswerDetails**: An administrator sees the full detail of the same entries. This
scenario is tested by `HistoryPage_Administrator_SeesFullAnswerDetails`.

**HistoryPage_Render_ReviewWithEntries_ShowsOnlyItsEntriesNewestFirst**: The trail holds entries of two reviews; the page
of one review lists its entries only, newest first. This scenario is tested by
`HistoryPage_Render_ReviewWithEntries_ShowsOnlyItsEntriesNewestFirst`.

**HistoryPage_Render_NoEntries_ShowsEmptyMessage**: A review without entries shows the empty message. This scenario is
tested by `HistoryPage_Render_NoEntries_ShowsEmptyMessage`.

**HistoryPage_Render_UnknownReview_ShowsNotFound**: An address of a review that does not exist shows the not-found
message and no table. This scenario is tested by `HistoryPage_Render_UnknownReview_ShowsNotFound`.

**HistoryPage_Render_LongHistory_PagesByFiftyWithOlderAndNewerLinks**: A history of 120 entries shows 50 entries with an
Older link only; the Older link leads to the next page, which has both links, and so on to the last page, which has a
Newer link only. This scenario is tested by `HistoryPage_Render_LongHistory_PagesByFiftyWithOlderAndNewerLinks`.

**HistoryPage_Render_MarkupInEntries_IsEscaped**: An entry whose actor and detail contain markup is rendered as text,
with no element created from it. This scenario is tested by `HistoryPage_Render_MarkupInEntries_IsEscaped`.

**HistoryPage_Render_ExistingReview_LinksBackToTheReview**: The page has a link back to the review. This scenario is
tested by `HistoryPage_Render_ExistingReview_LinksBackToTheReview`; the link from the review page to the history by
`ReviewPage_Render_ExistingReview_ShowsHistoryLink`.

**Routes_Render_NobodySignedInOpensHistoryRoute_DoesNotShowEntries**: Nobody is signed in and the history address is
opened; the page is not rendered and no entry is shown. This scenario is tested by
`Routes_Render_NobodySignedInOpensHistoryRoute_DoesNotShowEntries`.

**HistoryPage_Render_NonMember_LooksLikeAMissingReview**: A review with an entry is opened by a user who is no member of
the project. The not-found text equals the text for a project that does not exist (apart from the project number), and
no table, project name, review title, entry or actor is shown. This scenario is tested by
`HistoryPage_Render_NonMember_LooksLikeAMissingReview`.

**HistoryPage_Reparameterize_MembershipRevoked_ShowsNotFoundAndNoEntries**: A member has the history open, and the
membership is removed; the page is given new parameters (the address moves to another position). It loads again,
shows the not-found message, and none of the entries, the project name or the review title remains. The page has no
other action, so this is the one point at which an open history page reads. This scenario is tested by
`HistoryPage_Reparameterize_MembershipRevoked_ShowsNotFoundAndNoEntries`.
