### ProjectsPage

#### Verification Approach

`ProjectsPage` and `NewProjectPage` are verified with component tests defined in `ProjectsPageTests.cs`,
`NewProjectPageTests.cs`, and, for what each user may see and do, `ProjectAccessPageTests.cs`, which use the bUnit test
context to render the real components over a real `ProjectService`
and an in-memory fake store, and drive the form. bUnit runs the component life-cycle, including `OnAfterRender`, as a
live connection does. Tests locate elements by their `data-testid` attributes (the page units share the helper
`PageTestBase`). The real database is covered by the `Storage` subsystem and the integration tests of the `Web`
subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- Without projects the list page shows an empty message and, for a system administrator, the New project button but no
  form; with projects it lists each as a row with a link to its page and its description when it has one.
- The list shows a member only the projects the member belongs to, and a system administrator every project; only a
  system administrator is offered the New project button. A user without a project is told why the list is empty and
  to ask an administrator.
- The page for a new project shows the form and a Cancel link back to the list, and no list.
- Submitting a valid name creates the project and opens its page; submitting a blank name, or submitting after the
  sign-in state ended, shows an error and creates nothing; a user who is no system administrator is refused with an
  error that names the system administrator and creates nothing.
- The interactive marker is `true` after each page has rendered on a live connection.

#### Test Scenarios

**ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndNewProjectButton**: The list page is rendered for a system
administrator with no
projects; the empty message and the New project button, which leads to `/projects/new`, are present, and the list
and the creation form are not. This scenario is tested by
`ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndNewProjectButton`.

**ProjectsPage_Render_ExistingProjects_ListsThemWithLinks**: Two projects exist; each is listed with a link to
`/projects/{id}`, and only the one with a description shows it. This scenario is tested by
`ProjectsPage_Render_ExistingProjects_ListsThemWithLinks`.

**ProjectsPage_Render_MemberAndSystemAdmin_ListOnlyWhatEachMaySee**: Two projects exist and the user is a member of the
first only. Rendered for the member, the list has the row of the first project, no row of the second, and no New project
button; rendered for the system administrator, it has both rows and the button. This scenario is tested by
`ProjectsPage_Render_MemberAndSystemAdmin_ListOnlyWhatEachMaySee`.

**ProjectsPage_Render_UserWithoutMembership_ExplainsTheEmptyListAndOffersNoCreateButton**: A project exists that the
user does not belong to; the empty message mentions the administrator, there is no list and no New project button. This
scenario is tested by
`ProjectsPage_Render_UserWithoutMembership_ExplainsTheEmptyListAndOffersNoCreateButton`.

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

**NewProjectPage_CreateForm_MemberWhoIsNoSystemAdmin_IsRefusedAndCreatesNothing**: A member who is no system
administrator fills in the form and submits it; the error names the system administrator and the number of stored
projects is unchanged. This scenario is tested by
`NewProjectPage_CreateForm_MemberWhoIsNoSystemAdmin_IsRefusedAndCreatesNothing`.

**ProjectsPage_AfterRender_Interactive_SetsMarker**: The list page is rendered; its root element has
`data-interactive` equal to `true`. This scenario is tested by `ProjectsPage_AfterRender_Interactive_SetsMarker`.
