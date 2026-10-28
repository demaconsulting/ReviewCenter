### UsersPage

#### Verification Approach

`UsersPage` and `NewUserPage` are verified with component tests defined in `UsersPageTests.cs`, which use the bUnit test
context to render the real components over a real `UserService` with an in-memory fake store and a trivial password
hasher, and drive its forms and buttons. The signed-in user of the tests is set by the test authentication state of
the shared
helper `PageTestBase`; the action tests sign in as an administrator stored in the fake store, because every action
re-checks the stored user. The restriction of the page to administrators is verified by the `SignIn` unit. Tests
locate elements by their `data-testid` attributes. The real database is covered by the `Storage` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- Every user is listed with the user name, display name, enabled state, and a "System admin" badge for a system
  administrator, and with the controls to disable or enable, grant or take away the system administrator flag, and set
  a password (there is no role selection); the list page has a button that leads to the page of the creation form
  but not the form itself; no password or hash is shown.
- On the creation page, a valid new user is created with the chosen "System administrator" checkbox, the password field
  is emptied, and the browser returns to the list; the page has a Cancel link to the list; a rule violation is shown as
  an error, creates nothing, keeps the typed user name, and drops the typed password.
- A user is disabled and enabled again, and the system administrator flag is granted and taken away.
- A new password set for a user works, the old one does not, and the field is emptied.
- Disabling or demoting the last enabled system administrator is refused with an error.
- An action is refused with an error, and changes nothing, when the signed-in system administrator lost the flag or was
  disabled after the page was opened.
- When the sign-in state of the open page ends because the administrator lost the flag, the page is replaced by the
  "Session ended" message and offers no actions; an action sent after the session ended, before the page learned of
  it, shows an error that says the user is no longer signed in and changes nothing.

#### Test Scenarios

**UsersPage_Render_ExistingUsers_ListsThemWithControls**: A system administrator and an ordinary user exist; the page
lists their user names, display names, and enabled states, the "System admin" badge of the administrator only, and the
row controls (including the button "Make system admin" for the ordinary user and "Remove system admin" for the
administrator) and the button that leads to the creation page, the creation form is not on the page, and the markup
contains neither the password nor the hash. This scenario is tested by
`UsersPage_Render_ExistingUsers_ListsThemWithControls`.

**NewUserPage_CreateForm_ValidInput_CreatesUserAndClearsForm**: The creation page is filled in with a user name,
display name, the "System administrator" checkbox ticked, and a password and submitted; the user is stored as a system
administrator, there is no error, the
password field is empty, the browser returns to the list, and the Cancel link leads to the list. This scenario is
tested by `NewUserPage_CreateForm_ValidInput_CreatesUserAndClearsForm`.

**NewUserPage_CreateForm_TooShortPassword_ShowsErrorAndCreatesNothing**: The creation page is submitted with a too
short password; an error that mentions the characters is shown, no user is stored, the user name stays in its field,
and the password field is empty. This scenario is tested by
`NewUserPage_CreateForm_TooShortPassword_ShowsErrorAndCreatesNothing`.

**UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain**: The toggle button of an ordinary user is used twice; the user
is stored as disabled and shown as "Disabled", and then stored as enabled and shown as "Enabled". This scenario is
tested by `UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain`.

**UsersPage_ToggleSystemAdmin_User_GrantsAndTakesAwayTheFlag**: The system administrator button of an ordinary user is
used twice; the user is stored as a system administrator and shown with the "System admin" badge, and then stored
without the flag and shown without the badge. This scenario is tested by
`UsersPage_ToggleSystemAdmin_User_GrantsAndTakesAwayTheFlag`.

**UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField**: A new password is typed in the row of a user
and set; the new password works, the old one does not, there is no error, and the field is empty. This scenario is
tested by `UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField`.

**UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError**: The only system administrator is disabled and demoted
through the row controls; an error that mentions the administrator is shown, and the user stays enabled and a system
administrator. This scenario is tested by `UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError`.

**UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError**: Two system administrators and an ordinary user exist
and one administrator opens the page; that administrator then loses the flag, and the disable button of the ordinary
user is used; an error that says system administrator rights are required is shown, and the user stays enabled. This
scenario is tested by
`UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError`.

**UsersPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions**: Two system administrators and an ordinary
user exist and
one administrator has the page open, with the sign-in state re-checked every 100 milliseconds; that administrator loses
the flag. Within the limit of 10 seconds the "Session ended" message appears, the page and the user controls are gone,
a click on the stale button changes nothing, and the ordinary user is still enabled. This scenario is tested by
`UsersPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions`.

**UsersPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing**: An administrator has the page open
and the sign-in state ends without the page being told; the disable button of a user is used. The error area says that
the user is no longer signed in, and the user is still enabled. This scenario is tested by
`UsersPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing`.
