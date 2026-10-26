## Web

### Verification Approach

The `Web` subsystem is verified by integration tests defined in `WebSubsystemTests.cs`. Each
test starts the real server pipeline on an in-memory test server, with a temporary database, through
`ServerHost.CreateApplication` and requests routes over HTTP (the pages behind the sign-in are requested by a user
signed in through the test helper `TestBrowser`), so the `App` and the page units run together with
the Core services and the Storage database exactly as they do in production. Tests assert on the returned
HTML. Behavior that needs a live browser connection (typing, selecting files, submitting forms, and the switch of
the `data-interactive` marker from `false` to `true`) is outside what plain HTTP requests can exercise. It is
verified at unit level by the component tests of the page units (see the children of the requirements in
`web.yaml`), not by these subsystem tests; the Puppet end-to-end test drives a real browser against a real server,
but it only relies on the marker (its navigation waits for it) and does not assert it.

### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

### Acceptance Criteria

- All integration tests pass with zero failures.
- The home route answers HTTP status 200 with a document titled "ReviewCenter" that contains
  every control the user interface tests drive.
- The server-rendered response is marked as not yet interactive, so that drivers wait for the
  live connection. (The marker's switch to interactive is verified by the page units' component tests.)
- A project and a review created through the services are presented by the pages with the correct fingerprint.
  (The creation forms and uploads are verified by the page units' component tests.)
- Threads created through the Core service are presented by the review page: the next-step line, the thread index,
  the summary, and the per-file open count are correct, and the Details section is collapsed.
- The review page links the checklist page and shows its progress; the checklist page presents the answers stored
  through the Core service, also when requested again.
- A review with a reviewer slot that was signed through the Core service is presented as Approved by the review page
  and the project page, with the signer and the record digest on the review page; when the record changes
  afterwards, both pages present it as In review and the review page shows the signature as out of date.
- The `Web` subsystem's pages are presented to a signed-in user; the sign-in itself is verified in the `Identity`
  subsystem.
- When the sign-in state of an open page ends, the page is replaced by the "Session ended" message and offers no
  actions. This needs the live state of a page, which plain HTTP requests do not have, so it is verified at unit level
  by the component tests of `SessionGate`, `ReviewPage`, `UsersPage`, and `RepositoriesPage` (see the children of the requirement
  `ReviewCenter-Web-SessionEnd` in `web.yaml`), together with the `Identity` subsystem test of the provider that ends
  the state.
- A user with the Admin role who opens `/repositories` is shown the page of the registry; a reviewer is refused.
- Every interactive element of every page has a `data-testid`.
- The creation of a review from a repository (source choice, repository form, refusals, and skipped entries) and the
  display of the change kinds, the source, and the Diff / Head / Baseline views need a live connection, so they are
  verified at unit level by the component tests of `ProjectPage` and `ReviewPage` (see the children of the requirement
  `ReviewCenter-Web-RepositoryReviews` in `web.yaml`).
- The diff view of a review page (collapsed unchanged lines, line comments on both sides, threads under their lines,
  and the too-large state) needs a live connection, so it is verified at unit level by the component tests of
  `ReviewPage`; the subsystem test reads the stylesheet that keeps the code area scrolling inside itself on a phone
  (see the requirement `ReviewCenter-Web-DiffReview` in `web.yaml`).
- The plan page and the planning links need a live connection for the form and the creation, so they are verified at
  unit level by the component tests of `PlanPage`, `ProjectPage`, and `ReviewPage` (see the children of the requirement
  `ReviewCenter-Web-PlanReviews` in `web.yaml`); the subsystem test requests the plan route of a project that has a
  registered repository.

### Test Scenarios

**WebSubsystem_HomeRoute_Requested_RendersTitleAndControls**: The pipeline is started in memory
and the home route is requested; the response is 200 and contains the document title, the Hello
button, the counter button, and the counter value. This scenario is tested by
`WebSubsystem_HomeRoute_Requested_RendersTitleAndControls`.

**WebSubsystem_HomeRoute_InitialResponse_IsNotYetInteractive**: The home route is requested
over plain HTTP, where no live connection exists; the `data-interactive` marker in the response
is `false`. This scenario is tested by
`WebSubsystem_HomeRoute_InitialResponse_IsNotYetInteractive`.

**WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages**: The pipeline is started in memory on a temporary
database, a project with a review of three files (one of them binary) is created through the Core services, and
the projects page, the project page, and the review page are requested. The project and review are listed and
linked, and the review page shows the title, the Draft state, the fingerprint, and the path and hash of each
file, equal to the known values. This scenario is tested by
`WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages`.

**WebSubsystem_ReviewWithThreads_ArePresentedByThePage**: A review with one open whole-file thread and one resolved
line thread is created through the Core services of the real pipeline, and the review page is requested. The
summary shows one open and one resolved thread, the file shows "1 open thread", the next-step line names the open
thread, the index lists the open thread with an activity time read back from the database, and the Details section
is collapsed. This scenario is tested by
`WebSubsystem_ReviewWithThreads_ArePresentedByThePage`.

**WebSubsystem_Stylesheet_DiffRules_KeepCodeScrollingInsideItself**: The stylesheet source is read and checked for the
rules that keep the diff inside the page width: the code area scrolls horizontally inside itself, a row is as wide as
its longest line, inline threads and collapsed-run buttons stick to the left edge of the scrolled area, and a
narrower line-number gutter applies on a phone. This scenario is tested by
`WebSubsystem_Stylesheet_DiffRules_KeepCodeScrollingInsideItself`. The measured width of the page at 390 by 844
pixels is checked in a real browser by the acceptance walk-through.

**WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload**: A review is created through the Core services of the
real pipeline and two checks are answered, one Pass and one Fail with a justification. The checklist page is
requested twice and the review page once. Both loads of the checklist page say "56 checks unanswered.", show the
section progress "2 of 8", the pressed Pass button, and the saved justification; the review page shows "2 of 58
checks answered", a link to the checklist page, and "56 checklist checks unanswered, 1 check answered Fail." as the
next step. This
scenario is tested by `WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload`.

**WebSubsystem_SignedReview_ApprovalIsPresentedAndGoesOutOfDateWhenRecordChanges**: The pipeline is started in memory
on a temporary database, and a second user, a project, and a review with every check answered Pass are created
through the real Core services, with one reviewer slot of the signed-in user (primary) and the second user
(alternate). The review page shows the badge "In review" and the sign form of the slot. The second user signs
through `SigningService` with the digest read from the service; the review page then shows "Approved", the status
"Signed by Bob on ..." and the full record digest in the title of the Details element, and the project page shows
"Approved" for the review. A thread is then started, which changes the record; both pages show "In review" again
and the review page shows "Signature out of date". This scenario is tested by
`WebSubsystem_SignedReview_ApprovalIsPresentedAndGoesOutOfDateWhenRecordChanges`.

**WebSubsystem_AllPages_EveryInteractiveElementHasTestId**: A user with the Admin role is signed in, and every route
(home, login, account, users, repositories, a project, the plan page of a project that has a registered repository, the
plan page and the project page of a missing project, a review, a missing review, a checklist) is
requested over the in-memory server; every link, button, visible input, select, and text area in each response has a
`data-testid` in kebab-case. This scenario is tested by
`WebSubsystem_AllPages_EveryInteractiveElementHasTestId`.

**Repositories page**: The page of the repository registry is verified by its unit tests, which render it with the
component test host. It lists repositories with the token state only and no delete control, which is tested by
`RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete`, and it stores a token without ever showing it,
which is tested by `RepositoriesPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields`. A reviewer who requests the
route is refused and an administrator receives the page, which is tested by
`SignIn_RepositoriesPage_ReviewerAndAdmin_OnlyAdminIsAllowed` in the `Identity` subsystem tests.

**Audit pages**: The history and audit pages are verified by their unit tests, which render them with the component
test host. The history page shows only the entries of its review, newest first, which is tested by
`HistoryPage_Render_ReviewWithEntries_ShowsOnlyItsEntriesNewestFirst`; a visitor who is not signed in does not get
the entries, which is tested by `Routes_Render_NobodySignedInOpensHistoryRoute_DoesNotShowEntries`. The audit page
shows the entry count and the full head hash of an intact chain, which is tested by
`AuditPage_Verify_IntactChain_ShowsCountAndFullHeadHash`, and a reviewer who opens the audit route gets an access
denied message and no entries, which is tested by `Routes_Render_ReviewerOpensAuditRoute_ShowsAccessDeniedNotTrail`.
The subsystem test of all pages does not include the two new routes; their `data-testid` attributes are checked in
the unit tests.
