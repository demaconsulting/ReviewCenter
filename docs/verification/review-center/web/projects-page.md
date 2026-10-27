### ProjectsPage

#### Verification Approach

`ProjectsPage` and `NewProjectPage` are verified with component tests defined in `ProjectsPageTests.cs` and
`NewProjectPageTests.cs`, which use the bUnit test context to render the real components over a real `ProjectService`
and an in-memory fake store, and drive the form. bUnit runs the component life-cycle, including `OnAfterRender`, as a
live connection does. Tests locate elements by their `data-testid` attributes (the page units share the helper
`PageTestBase`). The real database is covered by the `Storage` subsystem and the integration tests of the `Web`
subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- Without projects the list page shows an empty message and the New project button but no form; with projects it
  lists each as a row with a link to its page and its description when it has one.
- The page for a new project shows the form and a Cancel link back to the list, and no list.
- Submitting a valid name creates the project and opens its page; submitting a blank name, or submitting after the
  sign-in state ended, shows an error and creates nothing.
- The interactive marker is `true` after each page has rendered on a live connection.

#### Test Scenarios

**ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndNewProjectButton**: The list page is rendered with no
projects; the empty message and the New project button, which leads to `/projects/new`, are present, and the list
and the creation form are not. This scenario is tested by
`ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndNewProjectButton`.

**ProjectsPage_Render_ExistingProjects_ListsThemWithLinks**: Two projects exist; each is listed with a link to
`/projects/{id}`, and only the one with a description shows it. This scenario is tested by
`ProjectsPage_Render_ExistingProjects_ListsThemWithLinks`.

**NewProjectPage_Render_Default_ShowsFormAndCancelLink**: The page for a new project is rendered; the name,
description, and create controls and a Cancel link to `/projects` are present, the project list is not, and the root
element has `data-interactive` equal to `true`. This scenario is tested by
`NewProjectPage_Render_Default_ShowsFormAndCancelLink`.

**NewProjectPage_CreateForm_ValidInput_CreatesProjectAndOpensIt**: A name and a description are entered and the form
is submitted; the project is stored, no error is shown, and the browser is sent to the page of the new project. This
scenario is tested by `NewProjectPage_CreateForm_ValidInput_CreatesProjectAndOpensIt`.

**NewProjectPage_CreateForm_BlankName_ShowsErrorAndCreatesNothing**: The form is submitted with a blank name; an
error that mentions the name is shown and no project is stored. This scenario is tested by
`NewProjectPage_CreateForm_BlankName_ShowsErrorAndCreatesNothing`.

**NewProjectPage_CreateForm_SessionEndedBeforePageKnows_ShowsClearErrorAndCreatesNothing**: A name is entered, and
the sign-in state ends without the page being told; the form is submitted. An error says that the user is no longer
signed in, and no project is stored. This scenario is tested by
`NewProjectPage_CreateForm_SessionEndedBeforePageKnows_ShowsClearErrorAndCreatesNothing`.

**ProjectsPage_AfterRender_Interactive_SetsMarker**: The list page is rendered; its root element has
`data-interactive` equal to `true`. This scenario is tested by `ProjectsPage_AfterRender_Interactive_SetsMarker`.
