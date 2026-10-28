### AuditPage

#### Verification Approach

`AuditPage` is verified with component tests defined in `AuditPageTests.cs`, which use the bUnit test context to render
the real component over a real `AuditService` with the in-memory fake audit store (`FakeAuditStore`, which can also
alter a stored entry to break the chain) and a real `UserService` with an in-memory fake user store. The signed-in user
of the tests is set by the test authentication state of the shared helper `PageTestBase`; the tests sign in as a
system administrator stored in the fake user store, because the page re-checks the stored user. The restriction of the
route to system administrators is verified through the router in `Routes_Render_*` tests of the same file. Tests locate
elements by their
`data-testid` attributes. The real database and hashing are covered by the `Storage` and `AuditChain` units.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- A system administrator sees the whole trail newest first, with the sequence, time, actor, action, review, and detail
  of each
  entry; an empty trail shows the empty message; a trail over 50 entries is shown in pages of 50 with links.
- Markup in the trail is shown as text and not interpreted.
- A long hexadecimal value in the detail of an entry is shown as its first 12 characters and an ellipsis, with the full
  value as the tooltip, and the surrounding words are kept.
- Verifying an intact trail shows the entry count and the full head hash; verifying a tampered trail shows the first
  broken entry; verifying an empty trail reports it as intact with zero entries.
- A user who is not a system administrator, and a user whose claims say system administrator while the user store says
  otherwise, are
  refused with an error and see no entries.
- A user who lost the flag after the page was opened is refused when verifying, and the trail is not read.
- A user who is not a system administrator and opens the route is shown the access denied message and no entries; a
  system administrator who opens the
  route is shown the page.

#### Test Scenarios

**AuditPage_Render_Admin_ShowsEntriesNewestFirst**: A system administrator opens the page; the entries are listed newest
first with their columns. This scenario is tested by `AuditPage_Render_Admin_ShowsEntriesNewestFirst`.

**AuditPage_Render_DetailWithLongHash_ShowsShortenedValueWithFullValueInTooltip**: An entry whose detail holds a 64
character hash between plain words is shown with the hash cut to its first 12 characters and an ellipsis, the words are
kept, the full hash is the tooltip of the shortened value, and the full hash is not in the text of the cell. This
scenario is tested by `AuditPage_Render_DetailWithLongHash_ShowsShortenedValueWithFullValueInTooltip`.

**AuditPage_Render_EmptyTrail_ShowsEmptyMessage**: An empty trail shows the empty message and no table. This scenario is
tested by `AuditPage_Render_EmptyTrail_ShowsEmptyMessage`.

**AuditPage_Render_LongTrail_PagesByFifty**: A trail of more than 50 entries shows 50 and the Older link, and the links
lead to the other pages. This scenario is tested by `AuditPage_Render_LongTrail_PagesByFifty`.

**AuditPage_Render_MarkupInEntries_IsEscaped**: Entries with markup in their text are rendered as text. This scenario is
tested by `AuditPage_Render_MarkupInEntries_IsEscaped`.

**AuditPage_Render_NonAdmin_IsRefusedAndShowsNoEntries**: An ordinary user opens the page component; the error "System
administrator rights are required." is shown, and no entry. This scenario is tested by
`AuditPage_Render_NonAdmin_IsRefusedAndShowsNoEntries`.

**AuditPage_Render_ClaimsAdminButStoreSaysNot_IsRefused**: The authentication state claims the system administrator
role, but
the stored user is not a system administrator; the page refuses. This scenario is tested by
`AuditPage_Render_ClaimsAdminButStoreSaysNot_IsRefused`.

**AuditPage_Verify_IntactChain_ShowsCountAndFullHeadHash**: The Verify button is pressed on an intact trail; the page
shows the count and the full 64 character head hash. This scenario is tested by
`AuditPage_Verify_IntactChain_ShowsCountAndFullHeadHash`.

**AuditPage_Verify_TamperedChain_ShowsFirstBadEntry**: An entry is altered and the Verify button is pressed; the page
names the first bad entry. This scenario is tested by `AuditPage_Verify_TamperedChain_ShowsFirstBadEntry`.

**AuditPage_Verify_EmptyTrail_ReportsIntactWithZeroEntries**: Verification of an empty trail reports it intact with zero
entries. This scenario is tested by `AuditPage_Verify_EmptyTrail_ReportsIntactWithZeroEntries`.

**AuditPage_Verify_UserDemotedAfterOpening_IsRefusedWithoutReadingTrail**: The system administrator loses the flag after
the page
opened and presses Verify; the page shows the error and the store records no read for the verification. This scenario is
tested by `AuditPage_Verify_UserDemotedAfterOpening_IsRefusedWithoutReadingTrail`.

**Routes_Render_ReviewerOpensAuditRoute_ShowsAccessDeniedNotTrail**: An ordinary user opens `/audit` through the router;
the
access denied message is shown and no entry. This scenario is tested by
`Routes_Render_ReviewerOpensAuditRoute_ShowsAccessDeniedNotTrail`.

**Routes_Render_AdminOpensAuditRoute_ShowsAuditPage**: A system administrator opens `/audit` through the router; the
page is
shown. This scenario is tested by `Routes_Render_AdminOpensAuditRoute_ShowsAuditPage`.
