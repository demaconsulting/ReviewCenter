### RepositoriesPage

#### Verification Approach

`RepositoriesPage` and `NewRepositoryPage` are verified with component tests defined in `RepositoriesPageTests.cs`, which
use the bUnit test context to render the real components over a real `RepositoryService` with the in-memory fake
repository store
(`FakeRepositoryStore`), a fake token protector (`FakeCredentialProtector`), and a real `UserService` with an in-memory
fake user store. The signed-in user of the tests is set by the test authentication state of the shared helper
`PageTestBase`; the tests sign in as a system administrator, or as a project member of a given role, stored in the fake
user store, because the page re-checks the stored user. The restriction of the route to the people who manage the
project is verified by the component tests below, and through the real server pipeline in the `SignInTests` of the
`Identity` subsystem. Tests locate elements by their `data-testid` attributes, and check that a token never
appears in the markup
by searching the rendered text for the token that the test typed.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The list, at `/projects/{id}/repositories`, shows the name, location, and enabled state of each repository of that
  project, and the user name and token state where they are set; repositories of other projects are not listed; it
  never shows a token and has no delete control, and it has no form but a button that leads to the add page.
- The add page, at `/projects/{id}/repositories/new`, holds the form and a Cancel link back to the list. The form
  registers a repository in the project, stores the token only
  through the service, empties the token field, returns to the list, and the token is never in the markup; an invalid
  location is shown as an error, keeps the typed name, and creates nothing.
- Editing keeps, replaces, or clears the token as chosen, and cancelling closes the form and changes nothing.
- A repository can be disabled and enabled again.
- A project administrator manages the repositories of the project; a plain member and a user who is no member see only
  the not-found message of a missing project, with no repositories and no controls, on both pages.
- An administrator who lost the right after the page was opened is refused with an error when acting, and the page of
  such an administrator ends its session when the sign-in state is re-checked, so that no actions remain.
- An action sent after the session ended, but before the page learned of it, shows a clear message and changes nothing.

#### Test Scenarios

**RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete**: A private and a public repository of the
project are listed
with location; the user name and token badges appear only for the private one; neither a token nor a delete control is
shown; the add button links to the add page of the project and the form is not on the list page. This scenario is
tested by `RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete`.

**RepositoriesPage_Render_OtherProjectsRepositories_AreNotListed**: A repository exists in the project and another one
in a second project; the page of the first project lists only its own. This scenario is tested by
`RepositoriesPage_Render_OtherProjectsRepositories_AreNotListed`.

**RepositoriesPage_Render_ProjectAdmin_ShowsRepositoriesAndActions**: A user who is no system administrator but a
project administrator of the project opens the page, sees the repository and the add button, and can disable the
repository. This scenario is tested by `RepositoriesPage_Render_ProjectAdmin_ShowsRepositoriesAndActions`.

**RepositoriesPage_Render_MemberOrNonMember_ShowsNotFoundOnly**: An ordinary user who is a plain member of the project,
and one who is no member, each render the list and the add page; both show only the `project-not-found` message, the
same one as for a missing project, with no repository row, no add button, and no form. This scenario is tested by
`RepositoriesPage_Render_MemberOrNonMember_ShowsNotFoundOnly`.

**NewRepositoryPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields**: The add page registers the repository, the
token is stored through the service, the token field is emptied, the token is not in the markup, the browser returns to
the list, and the Cancel link leads to the list. This scenario is tested by
`NewRepositoryPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields`.

**NewRepositoryPage_CreateForm_InvalidLocation_ShowsErrorAndCreatesNothing**: An invalid location is shown as an error,
the typed name is kept, and nothing is created. This scenario is tested by
`NewRepositoryPage_CreateForm_InvalidLocation_ShowsErrorAndCreatesNothing`.

**RepositoriesPage_Edit_KeepToken_ChangesFieldsAndKeepsToken**: Editing with the default action changes the fields and
keeps the stored token. This scenario is tested by `RepositoriesPage_Edit_KeepToken_ChangesFieldsAndKeepsToken`.

**RepositoriesPage_Edit_ReplaceToken_StoresNewTokenWriteOnly**: Replacing the token stores the new one, which never
appears in the markup. This scenario is tested by `RepositoriesPage_Edit_ReplaceToken_StoresNewTokenWriteOnly`.

**RepositoriesPage_Edit_ClearToken_RemovesToken**: Clearing the token removes it, so the repository is shown as having no
token. This scenario is tested by `RepositoriesPage_Edit_ClearToken_RemovesToken`.

**RepositoriesPage_Edit_Cancel_ClosesFormAndChangesNothing**: Cancelling an edit closes the form and changes nothing. This
scenario is tested by `RepositoriesPage_Edit_Cancel_ClosesFormAndChangesNothing`.

**RepositoriesPage_ToggleEnabled_Repository_DisablesAndEnablesAgain**: A repository is disabled and enabled again, and
disabling is the only way to retire it. This scenario is tested by
`RepositoriesPage_ToggleEnabled_Repository_DisablesAndEnablesAgain`.

**RepositoriesPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError**: Two system administrators and a repository
exist and one administrator opens the page; that administrator then loses the flag and tries to disable the repository
from the page that is still open. The action is refused with an error that says administrator rights are required, and
the repository stays enabled. This scenario is tested by
`RepositoriesPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError`.

**RepositoriesPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions**: When the sign-in state is re-checked
after the administrator lost the flag, the page shows the session-ended message and no actions. This scenario is tested
by `RepositoriesPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions`.

**RepositoriesPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing**: An action sent after the session
ended, but before the page learned of it, shows a clear message and changes nothing. This scenario is tested by
`RepositoriesPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing`.

**Route restriction**: Through the real server pipeline, a user who is no member of a project requests
`/projects/{id}/repositories` and receives a page that says the project does not exist, while a system administrator
receives the page with HTTP status 200, which is tested by `SignIn_RepositoriesPage_NonMemberAndSystemAdmin_OnlyAdminIsServed`;
an anonymous visitor is sent to the login page, which is tested by `SignIn_ProtectedPage_Anonymous_RedirectsToLogin`.
