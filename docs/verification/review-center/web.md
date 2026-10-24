## Web

### Verification Approach

The `Web` subsystem is verified by integration tests defined in `WebSubsystemTests.cs`. Each
test starts the real server pipeline on an in-memory test server, with a temporary database, through
`ServerHost.CreateApplication` and requests routes over HTTP, so the `App` and the page units run together with
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
- Every interactive element of every page has a `data-testid`.

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

**WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload**: A review is created through the Core services of the
real pipeline and two checks are answered, one Pass and one Fail with a justification. The checklist page is
requested twice and the review page once. Both loads of the checklist page say "56 checks unanswered.", show the
section progress "2 of 8", the pressed Pass button, and the saved justification; the review page shows "2 of 58
checks answered", a link to the checklist page, and "56 checklist checks unanswered." as the next step. This
scenario is tested by `WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload`.

**WebSubsystem_AllPages_EveryInteractiveElementHasTestId**: Every route (home, projects, a project, a missing
project, a review, a missing review, a checklist) is requested over the in-memory server; every link, button, input, select,
and text area in each response has a `data-testid` in kebab-case. This scenario is tested by
`WebSubsystem_AllPages_EveryInteractiveElementHasTestId`.
