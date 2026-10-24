### ProjectsPage

#### Verification Approach

`ProjectsPage` is verified with component tests defined in `ProjectsPageTests.cs`, which use the bUnit test context
to render the real component over a real `ProjectService` and an in-memory fake store, and drive its form. bUnit
runs the component life-cycle, including `OnAfterRender`, as a live connection does. Tests locate elements by
their `data-testid` attributes (the page units share the helper `PageTestBase`). The real database is covered by
the `Storage` subsystem and the integration tests of the `Web` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- Without projects the page shows an empty message and the form; with projects it lists each with a link to its
  page and its description when it has one.
- Submitting a valid name creates the project and lists it; submitting a blank name shows an error and creates
  nothing.
- The interactive marker is `true` after the page has rendered on a live connection.

#### Test Scenarios

**ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndForm**: The page is rendered with no projects; the empty
message and the name, description, and create controls are present, and the list is not. This scenario is tested
by `ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndForm`.

**ProjectsPage_Render_ExistingProjects_ListsThemWithLinks**: Two projects exist; each is listed with a link to
`/projects/{id}`, and only the one with a description shows it. This scenario is tested by
`ProjectsPage_Render_ExistingProjects_ListsThemWithLinks`.

**ProjectsPage_CreateForm_ValidInput_AddsProjectToList**: A name is entered and the form is submitted; the project
is stored and listed, with no error. This scenario is tested by
`ProjectsPage_CreateForm_ValidInput_AddsProjectToList`.

**ProjectsPage_CreateForm_BlankName_ShowsErrorAndCreatesNothing**: The form is submitted with a blank name; an
error that mentions the name is shown and no project is stored. This scenario is tested by
`ProjectsPage_CreateForm_BlankName_ShowsErrorAndCreatesNothing`.

**ProjectsPage_AfterRender_Interactive_SetsMarker**: The page is rendered; its root element has
`data-interactive` equal to `true`. This scenario is tested by `ProjectsPage_AfterRender_Interactive_SetsMarker`.
