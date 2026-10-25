### RepositoriesPage

#### Verification Approach

`RepositoriesPage` is verified with component tests defined in `RepositoriesPageTests.cs`, which use the bUnit test
context to render the real component over a real `RepositoryService` with the in-memory fake repository store
(`FakeRepositoryStore`), a fake token protector (`FakeCredentialProtector`), and a real `UserService` with an in-memory
fake user store. The signed-in user of the tests is set by the test authentication state of the shared helper
`PageTestBase`; the tests sign in as an administrator stored in the fake user store, because the page re-checks the stored
user. The restriction of the route to administrators is verified through the real server pipeline in the `SignInTests`
of the `Identity` subsystem. Tests locate elements by their `data-testid` attributes, and check that a token never
appears in the markup
by searching the rendered text for the token that the test typed.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The list shows the name, location, user name, token state, and enabled state of each repository; it never shows a token
  and has no delete control.
- The add form registers a repository, stores the token only through the service, empties the token field, and the token
  is never in the markup; an invalid location is shown as an error, keeps the typed name, and creates nothing.
- Editing keeps, replaces, or clears the token as chosen, and cancelling closes the form and changes nothing.
- A repository can be disabled and enabled again.
- An administrator who was demoted after the page was opened is refused with an error when acting, and the page of such
  an administrator ends its session when the sign-in state is re-checked, so that no actions remain.
- An action sent after the session ended, but before the page learned of it, shows a clear message and changes nothing.

#### Test Scenarios

**RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete**: Repositories are listed with location, user name,
and the token state; neither a token nor a delete control is shown. This scenario is tested by
`RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete`.

**RepositoriesPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields**: The add form registers the repository, the
token is stored through the service, the token field is emptied, and the token is not in the markup. This scenario is
tested by `RepositoriesPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields`.

**RepositoriesPage_CreateForm_InvalidLocation_ShowsErrorAndCreatesNothing**: An invalid location is shown as an error, the
typed name is kept, and nothing is created. This scenario is tested by
`RepositoriesPage_CreateForm_InvalidLocation_ShowsErrorAndCreatesNothing`.

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

**RepositoriesPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError**: An action is refused with an error when the
signed-in administrator was demoted after the page was opened. This scenario is tested by
`RepositoriesPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError`.

**RepositoriesPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions**: When the sign-in state is re-checked after
the administrator was demoted, the page shows the session-ended message and no actions. This scenario is tested by
`RepositoriesPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions`.

**RepositoriesPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing**: An action sent after the session
ended, but before the page learned of it, shows a clear message and changes nothing. This scenario is tested by
`RepositoriesPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing`.

**Route restriction**: A reviewer who requests `/repositories` receives the access denied message and an administrator
receives the page, which is tested by `SignIn_RepositoriesPage_ReviewerAndAdmin_OnlyAdminIsAllowed`; an anonymous visitor
is sent to the login page, which is tested by `SignIn_ProtectedPage_Anonymous_RedirectsToLogin`.
