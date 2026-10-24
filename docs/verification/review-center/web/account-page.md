### AccountPage

#### Verification Approach

`AccountPage` is verified with component tests defined in `AccountPageTests.cs`, which use the bUnit test context to
render the real component over a real `UserService` with an in-memory fake store and a trivial password hasher, and
drive its form. The signed-in user of the tests is `ann`, set by the test authentication state of the shared helper
`PageTestBase`. Tests locate elements by their `data-testid` attributes. The real database is covered by the
`Storage` subsystem and the sign-in by the `Identity` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The page shows the form for the current, new, and repeated new password and the change button, and no message.
- A valid change changes the password of the signed-in user, shows a confirmation, and empties the fields.
- A wrong current password, new passwords that do not match, and a new password that breaks the policy are each shown
  as an error and change nothing.

#### Test Scenarios

**AccountPage_Render_SignedIn_ShowsFormControls**: The page is rendered; the page root and the current, new, repeat,
and change controls are present, and no error and no confirmation are shown. This scenario is tested by
`AccountPage_Render_SignedIn_ShowsFormControls`.

**AccountPage_ChangePassword_ValidInput_ChangesOwnPasswordAndClearsFields**: The form is filled in with the right
current password and a valid new one, and submitted; the confirmation "Your password was changed." is shown, the new
password works, the old one does not, and the fields are empty. This scenario is tested by
`AccountPage_ChangePassword_ValidInput_ChangesOwnPasswordAndClearsFields`.

**AccountPage_ChangePassword_InvalidInput_ShowsErrorAndChangesNothing**: A theory over a wrong current password, a
repeated password that differs, and a new password that is too short; an error naming the problem is shown, no
confirmation is shown, and the old password still works. This scenario is tested by
`AccountPage_ChangePassword_InvalidInput_ShowsErrorAndChangesNothing`.
