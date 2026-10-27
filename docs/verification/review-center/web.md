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
`web.yaml`), not by these subsystem tests; the Puppet end-to-end test drives a real browser against a real server
over the live connection (sign in, open Projects from the header, create a project, switch the theme).

### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

### Acceptance Criteria

- All integration tests pass with zero failures.
- The home route answers HTTP status 200 with a document titled "ReviewCenter" that contains the product name and the
  tagline.
- The Help index, the help topic pages, and the About page are served to a signed-in user over HTTP (their content is
  verified by the component tests of `AboutPage` and `HelpPage`, and their service to an anonymous visitor by the
  system test `ReviewCenter_ServeVerb_Started_ServesHelpAndAboutPagesWithoutSignIn`).
- The projects route, which is the interactive page that the subsystem tests use for the marker, is served with the
  `data-interactive` marker set to `false`.
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
- Every interactive element of every page has a `data-testid`, no identifier repeats within a page, and the breadcrumb links
  have their own identifiers (`breadcrumb-projects-link`, `breadcrumb-repositories-link`, `breadcrumb-users-link`),
  distinct from the header tabs.
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
- Links to files, lines and threads (the routes, the highlight, the replacement of the address, the thread links, the
  messages for addresses that name nothing, and the return to a link after signing in over HTTP) are verified by the
  component tests of `ReviewPage` in `ReviewDeepLinkTests.cs`, which render the real router, and by the tests of
  `SignIn`; the subsystem test renders the router for a signed-out visitor (see the requirement
  `ReviewCenter-Web-DeepLinks` in `web.yaml`).

### Test Scenarios

**WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline**: The pipeline is started in memory
and the home route is requested; the response is 200 and contains the document title and the tagline. This
scenario is tested by
`WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline`.

**WebSubsystem_ProjectsRoute_InitialResponse_IsNotYetInteractive**: The projects route is requested
over plain HTTP by a signed-in user, where no live connection exists; the `data-interactive` marker in the response
is `false`. This scenario is tested by
`WebSubsystem_ProjectsRoute_InitialResponse_IsNotYetInteractive`.

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
rules that keep the diff inside the page width: each row is a grid of two equal columns, long lines wrap inside their
cell, the code area still scrolls inside itself as a safety net, empty placeholders are muted, inline threads and
collapsed-run buttons stick to the left edge, below 40 rem the cells stack and the placeholders and the repeated
baseline copy of an unchanged line are hidden, and a narrower line-number gutter applies on a phone. This scenario
is tested by
`WebSubsystem_Stylesheet_DiffRules_KeepCodeScrollingInsideItself`. The measured width of the page at 390 by 844
pixels is checked in a real browser by the acceptance walk-through.

**WebSubsystem_Stylesheet_ThreadLink_IsButtonStyled**: The stylesheet source is read and checked: no `a.thread-link`
rule makes
the link look like plain text, and the thread header sizes the link like the buttons beside it, so the link and the Copy
link button of a thread look alike. This scenario is tested by `WebSubsystem_Stylesheet_ThreadLink_IsButtonStyled`.

**WebSubsystem_Stylesheet_DiffCommentControl_ShowsOnHoverFocusAndTouch**: The stylesheet source is read and the rules of
the `+` control of a diff row are checked: it is hidden with `opacity: 0`, shown on row hover, focus within the row, a
selected row, and keyboard focus, always shown under `@media (hover: none)`, and never taken out of the layout or the
tab order with `display: none` or `visibility: hidden`. This scenario is tested by
`WebSubsystem_Stylesheet_DiffCommentControl_ShowsOnHoverFocusAndTouch`.

**Routes_DeepLink_SignedOut_LoginLinkKeepsEscapesAndQuery**: The router is rendered for a signed-out visitor at the
address of a line link of a file whose name has a space and a hash, with a baseline side. The "Sign in required" link
is `/login?ReturnUrl=` followed by the whole address escaped once more, and no review page is drawn. This scenario is
tested by `Routes_DeepLink_SignedOut_LoginLinkKeepsEscapesAndQuery`.

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
(home, login, about, the help index, a help topic, an unknown help topic, account, users, the new user page,
repositories, the new repository page, projects, the new project page, a project, the new review page of a project,
the plan page of a project that has a registered repository, the
plan page and the project page of a missing project, a review, a missing review, a checklist) is
requested over the in-memory server; every link, button, visible input, select, and text area in each response has a
`data-testid` in kebab-case. This scenario is tested by
`WebSubsystem_AllPages_EveryInteractiveElementHasTestId`.

**WebSubsystem_AllPages_TestIdsAreUniqueAndBreadcrumbsDistinct**: A user with the Admin role is signed in and the project
page, the new review page, the plan page, the review page, its checklist page, its history page, the new repository
page, and the new user page are requested. In each response no `data-testid` value occurs twice, the breadcrumb link
has its prefixed identifier (`breadcrumb-projects-link`, `breadcrumb-repositories-link`, or
`breadcrumb-users-link`), and the
header tab of the same name occurs exactly once. This scenario is tested by
`WebSubsystem_AllPages_TestIdsAreUniqueAndBreadcrumbsDistinct`.

**Repositories page**: The page of the repository registry is verified by its unit tests, which render it with the
component test host. It lists repositories with the token state only and no delete control, which is tested by
`RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete`, and it stores a token without ever showing it,
which is tested by `NewRepositoryPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields`. A reviewer who requests the
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
