### AuditPage

#### Verification Approach

`AuditPage` and `ProjectAuditPage` are verified with component tests defined in `AuditPageTests.cs`,
`ProjectAuditPageTests.cs`, `AuditEntryTableTests.cs`, and `AuditExportLinksTests.cs` (the export links), and the
cleaning of the address in `AuditFilterStateTests.cs`
(plain unit tests), which use the bUnit test context to render
the real components over a real `AuditService` with the in-memory fake audit store (`FakeAuditStore`, which can also
alter a stored entry to break the chain) and a real `UserService` with an in-memory fake user store; the tests of the
project page also use a real `ProjectService` and `ProjectAccess` over the in-memory fake review store
(`FakeReviewStore`). The signed-in user
of the tests is set by the test authentication state of the shared helper `PageTestBase`; the tests sign in as a
system administrator stored in the fake user store, because the page re-checks the stored user. The restriction of the
route to system administrators is verified through the router in `Routes_Render_*` tests of the same file. The page of
a project is rendered at its address, so that the query values of the paging reach it as in the browser. Tests locate
elements by their
`data-testid` attributes. The real database and hashing are covered by the `Storage` and `AuditChain` units.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- A system administrator sees the whole trail newest first, with the sequence, time, actor, action, review, and detail
  of each
  entry; an empty trail shows the empty message; a trail over the page size (50 by default; 25, 50, and 100 are offered)
  is shown in pages with links.
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
- A project administrator or a system administrator who opens the audit page of a project (the audit-read access) sees
  the events of that
  project only, newest first, in pages of the chosen size with links, in the table of the whole audit trail, and a
  message when nothing was recorded; a plain member, a user who is no member, and a missing project give the not-found
  message and no
  entries; a user who lost the right after the page was opened, by revoked membership, demotion, or the end of the
  session, sees the not-found message and no entries.
- The filter form (actor, action, review, dates, page size, and the project on the whole-trail page) limits the entries
  and shows its values again; its state is in the address; the links to the older and newer page carry the filter and the
  page size; a filter that matches nothing says so and offers to clear it; a project page never shows another
  project or an entry without a project, whatever the address says.
- Hostile values in the address (markup, huge texts, negative or unknown sizes, bad dates, unknown actions, reversed
  dates) never raise and are shown, if at all, as encoded text.
- Both pages show a CSV and a JSON export link and the note; the links go to `/audit/export` or
  `/projects/{id}/audit/export`, carry the filter values that are set and the project of the whole-trail page as
  escaped query values, never carry the page size or the paging positions, and a hostile value is encoded so that it can
  neither add a parameter nor leave the attribute.

#### Test Scenarios

**AuditExportLinks_GlobalPage_CarryTheFilterWithoutPagingValues**: The whole-trail page is rendered with a filter, a page
size, and a position; the CSV and JSON links point to `/audit/export` with the filter values and the project but no
size, `before` or `after`. This scenario is tested by `AuditExportLinks_GlobalPage_CarryTheFilterWithoutPagingValues`
and, for the query itself, `AuditFilterState_ToExportQuery_FilterValues_CarriedWithoutPagingValues`.

**AuditExportLinks_ProjectPage_PointToTheProjectExportWithTheFilter**: The project page links to the export of its
project with the filter, and bare links carry only the format. This scenario is tested by
`AuditExportLinks_ProjectPage_PointToTheProjectExportWithTheFilter` and `AuditExportLinks_NoFilter_AreBareExportLinks`.

**AuditExportLinks_HostileValues_AreEncoded**: A value with a quote, markup, and `&format=json` is escaped in the link
and cannot add a parameter or break out of the attribute. This scenario is tested by
`AuditExportLinks_HostileValues_AreEncoded`.

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

**ProjectAuditPage_Render_ProjectAdmin_ShowsOnlyThisProjectsEvents**: A project administrator of Apollo opens the audit
page of Apollo; the events of Apollo are shown, with the link back to the project, and neither the events of Gemini nor
the event without a project. This scenario is tested by
`ProjectAuditPage_Render_ProjectAdmin_ShowsOnlyThisProjectsEvents`.

**ProjectAuditPage_Render_SystemAdmin_ShowsTheProjectsEvents**: A system administrator opens the audit page of Gemini;
the events of Gemini are shown and the events of Apollo are not. This scenario is tested by
`ProjectAuditPage_Render_SystemAdmin_ShowsTheProjectsEvents`.

**ProjectAuditPage_Render_NoAdministerRight_ShowsNotFoundAndNoEntries**: A plain member, a user who is no member, and a
missing project each give the not-found message, no table, and no event or project name. This scenario is tested by
`ProjectAuditPage_Render_NoAdministerRight_ShowsNotFoundAndNoEntries`.

**ProjectAuditPage_Render_Auditor_ShowsTheTrailOfOwnProjectOnly**: An Auditor of the project opens the audit page and
sees the entries of that project only. This scenario is tested by `ProjectAuditPage_Render_Auditor_ShowsTheTrailOfOwnProjectOnly`.

**ProjectAuditPage_Render_ManyEvents_PagesByFiftyNewestFirst**: More than 50 events are recorded for the project; the
first page shows 50 rows with the newest on top and a link to the older page, and no link to a newer one; the older
page, opened at the address of that link, shows the remaining rows, none of the first page, and the Newer link. This
scenario is tested by `ProjectAuditPage_Render_ManyEvents_PagesByFiftyNewestFirst`.

**ProjectAuditPage_Render_ProjectWithoutEvents_SaysNothingWasRecorded**: A project without events shows the empty
message. This scenario is tested by `ProjectAuditPage_Render_ProjectWithoutEvents_SaysNothingWasRecorded`.

**ProjectAuditPage_Load_MembershipRevokedWhileOpen_DropsTheEntries**: The membership of the project administrator is
revoked while the page is open and the page is loaded again; the not-found message is shown with no table, no row, and
no event or project name. This scenario is tested by `ProjectAuditPage_Load_MembershipRevokedWhileOpen_DropsTheEntries`.

**ProjectAuditPage_Load_DemotedOrSessionEndedWhileOpen_DropsTheEntries**: The project administrator is demoted, or the
sign-in state ends without the page knowing, and the page is loaded again; the not-found message is shown and no
entries. This scenario is tested by `ProjectAuditPage_Load_DemotedOrSessionEndedWhileOpen_DropsTheEntries`.

**ProjectAuditPage_AfterRender_Interactive_SetsMarker**: The page is rendered; its root element has `data-interactive`
equal to `true`. This scenario is tested by `ProjectAuditPage_AfterRender_Interactive_SetsMarker`.

**AuditPage_Render_Admin_ShowsFilterFormWithProjectSelector**: A system administrator opens the page; the filter form
is shown with the fields actor, action (grouped by prefix), review, from, to, page size, and the project selector,
and no clear link while nothing is set. This scenario is tested by
`AuditPage_Render_Admin_ShowsFilterFormWithProjectSelector`.

**AuditPage_Render_QueryFilters_LimitTheEntriesAndFillTheForm**: The page is opened with query values for the actor,
action, review, and dates; only the matching entries are shown, the form shows the values, and the clear link is
present. This scenario is tested by `AuditPage_Render_QueryFilters_LimitTheEntriesAndFillTheForm`, and the project
selector by `AuditPage_Render_ProjectQuery_ShowsOnlyThatProjectsEntries`.

**AuditPage_Render_FilterMatchesNothing_ShowsNoMatchStateAndClearLink**: A filter that matches nothing shows "No entry
matches the filters." and the clear link, not the message of an empty trail. This scenario is tested by
`AuditPage_Render_FilterMatchesNothing_ShowsNoMatchStateAndClearLink`.

**AuditPage_Render_HostileQueryValues_AreHandledAndEncoded**: Markup, bad dates, a negative and an unknown page size,
and an unknown action in the query are dropped or shown as text, and the page renders. This scenario is tested by
`AuditPage_Render_HostileQueryValues_AreHandledAndEncoded`, `AuditPage_Render_HugeActorText_IsCutAndPageRenders`,
and `AuditPage_Render_FromAfterTo_ShowsMessageAndIgnoresTheDates`.

**AuditPage_Render_FilterWithManyMatches_LinksCarryTheFilter**: A filter with more matches than a page shows the page
size asked for, and the Older and Newer links carry the filter and the page size. This scenario is tested by
`AuditPage_Render_FilterWithManyMatches_LinksCarryTheFilter`,
`ProjectAuditPage_Render_FilterWithManyMatches_LinksCarryTheFilter`, and the component alone by
`AuditEntryTable_Render_WithFilter_LinksCarryTheFilter` and
`AuditEntryTable_Render_EmptyPage_TellsNoMatchFromNothingRecorded`.

**ProjectAuditPage_Render_QueryFilters_LimitTheProjectsEntries**: The audit page of a project is opened with query
values; only the matching entries of the project are shown and the form has no project selector. This scenario is
tested by `ProjectAuditPage_Render_QueryFilters_LimitTheProjectsEntries`, and with a filter that matches nothing and
hostile values by `ProjectAuditPage_Render_NoMatchAndHostileValues_AreSafe`.

**ProjectAuditPage_Render_FilterAndScoping_NeverShowsOtherProjectsOrGlobalEntries**: Even with a project in the query, the
page shows only the entries of its own project and none without a project. This scenario is tested by
`ProjectAuditPage_Render_FilterAndScoping_NeverShowsOtherProjectsOrGlobalEntries`.

**AuditFilterState_FromQuery_NoValues_IsTheDefaultState**: Without query values the state matches everything with the
default size. This scenario is tested by `AuditFilterState_FromQuery_NoValues_IsTheDefaultState`, good values read and
written back by `AuditFilterState_FromQuery_GoodValues_AreReadAndWrittenBack`, and the project of a project page
winning over the query by `AuditFilterState_ToFilter_ProjectPageProject_WinsOverTheQueryProject`.

**AuditFilterState_FromQuery_BadValues_FallBackToDefaults**: Bad dates, an unknown action, a negative, zero, and huge
page size, and a bad project fall back to the defaults without raising; huge texts are cut; reversed dates are dropped.
This scenario is tested by `AuditFilterState_FromQuery_BadValues_FallBackToDefaults`,
`AuditFilterState_FromQuery_HugeTexts_AreBoundedAndTheFilterIsValid`, and
`AuditFilterState_FromQuery_ReversedDatesAndSize_DropDatesAndSizeIsNotNarrowing`, and the escaping of the written
query by `AuditFilterState_ToQuery_TextWithSpecialCharacters_IsEscaped`.
