### AccountPage

#### Verification Approach

`AccountPage` is verified with component tests defined in `AccountPageTests.cs`, which use the bUnit test context to
render the real component over a real `UserService` with an in-memory fake store and a trivial password hasher, and
drive its form. The signed-in user of the tests is `ann`, set by the test authentication state of the shared helper
`PageTestBase`. The browser script runtime is the bUnit `JSInterop` in loose mode, so the calls to
`reviewCenter.getTheme` and `reviewCenter.setTheme` are recorded or scripted instead of run; the application of the
stored appearance before the first paint is verified by the `App` unit. Tests locate elements by their `data-testid`
attributes. The real database is covered by the `Storage` subsystem and the sign-in by the `Identity` subsystem.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The page shows the form for the current, new, and repeated new password and the change button, and no message.
- The page shows the appearance options System, Light, and Dark with System selected when nothing is stored; choosing
  one is applied through the browser script at once and shown as selected; a stored choice is shown as selected.
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

**AccountPage_Render_AppearanceCard_ShowsThreeOptionsWithSystemSelected**: With no stored choice the appearance card
shows three radio options, and System is selected while Light and Dark are not. This scenario is tested by
`AccountPage_Render_AppearanceCard_ShowsThreeOptionsWithSystemSelected`.

**AccountPage_ChooseDark_AppliesThemeAndSelectsOption**: The Dark option is chosen; the browser script function
`reviewCenter.setTheme` is called with `dark`, Dark is selected, and System is not. This scenario is tested by
`AccountPage_ChooseDark_AppliesThemeAndSelectsOption`.

**AccountPage_Render_StoredLightChoice_IsSelected**: The browser reports a stored Light choice; after the page is
rendered Light is selected. This scenario is tested by `AccountPage_Render_StoredLightChoice_IsSelected`.
