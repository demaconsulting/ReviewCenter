### UsersPage

#### Verification Approach

`UsersPage` is verified with component tests defined in `UsersPageTests.cs`, which use the bUnit test context to
render the real component over a real `UserService` with an in-memory fake store and a trivial password hasher, and
drive its forms and buttons. The signed-in user of the tests is set by the test authentication state of the shared
helper `PageTestBase`; the action tests sign in as an administrator stored in the fake store, because every action
re-checks the stored user. The restriction of the page to administrators is verified by the `SignIn` unit. Tests
locate elements by their `data-testid` attributes. The real database is covered by the `Storage` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- Every user is listed with the user name, display name, role, and enabled state, and with the controls to disable or
  enable, change the role, and set a password; the creation form is present; no password or hash is shown.
- A valid new user is created with the chosen role and the form is emptied; a rule violation is shown as an error,
  creates nothing, keeps the typed user name, and drops the typed password.
- A user is disabled and enabled again, and the chosen role is stored.
- A new password set for a user works, the old one does not, and the field is emptied.
- Disabling or demoting the last enabled administrator is refused with an error.
- An action is refused with an error, and changes nothing, when the signed-in administrator was demoted or disabled
  after the page was opened.

#### Test Scenarios

**UsersPage_Render_ExistingUsers_ListsThemWithControls**: An administrator and a reviewer exist; the page lists their
user names, display names, roles, and enabled states with the row controls and the create button, and the markup
contains neither the password nor the hash. This scenario is tested by
`UsersPage_Render_ExistingUsers_ListsThemWithControls`.

**UsersPage_CreateForm_ValidInput_CreatesUserAndClearsForm**: The creation form is filled in with a user name,
display name, the Admin role, and a password and submitted; the user is stored with the role and listed, there is
no error, and the user name and password fields are empty. This scenario is tested by
`UsersPage_CreateForm_ValidInput_CreatesUserAndClearsForm`.

**UsersPage_CreateForm_TooShortPassword_ShowsErrorAndCreatesNothing**: The creation form is submitted with a too
short password; an error that mentions the characters is shown, no user is stored, the user name stays in its field,
and the password field is empty. This scenario is tested by
`UsersPage_CreateForm_TooShortPassword_ShowsErrorAndCreatesNothing`.

**UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain**: The toggle button of a reviewer is used twice; the user is
stored as disabled and shown as "Disabled", and then stored as enabled and shown as "Enabled". This scenario is
tested by `UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain`.

**UsersPage_SetRole_ChosenRole_IsStored**: The Admin role is chosen for a reviewer and saved; the role is stored and
shown. This scenario is tested by `UsersPage_SetRole_ChosenRole_IsStored`.

**UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField**: A new password is typed in the row of a user
and set; the new password works, the old one does not, there is no error, and the field is empty. This scenario is
tested by `UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField`.

**UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError**: The only administrator is disabled and demoted through
the row controls; an error that mentions the administrator is shown, and the user stays enabled and an
administrator. This scenario is tested by `UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError`.

**UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError**: Two administrators and a reviewer exist and one
administrator opens the page; that administrator is then demoted, and the disable button of the reviewer is used; an
error that mentions the administrator is shown, and the reviewer stays enabled. This scenario is tested by
`UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError`.
