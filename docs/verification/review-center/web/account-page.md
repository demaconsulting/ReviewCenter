### AccountPage

#### Verification Approach

`AccountPage` is verified with component tests defined in `AccountPageTests.cs`, which use the bUnit test context to
render the real component over a real `UserService` and a real `PersonalAccessTokenService` with in-memory fake stores
and a trivial password hasher, a clock that the test can move (for the expiry of a token), and drive its forms. The
signed-in user of the tests is `ann`, set by the test authentication state of the shared helper
`PageTestBase`. The browser script runtime is the bUnit `JSInterop` in loose mode, so the calls to
`reviewCenter.getTheme`, `reviewCenter.setTheme`, and `reviewCenter.copyText` are recorded or scripted instead of
run; the application of the
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
- A user who signs in with an identity provider sees a sentence that names the provider instead of the password form,
  and has no password field and no change button.
- A wrong current password, new passwords that do not match, and a new password that breaks the policy are each shown
  as an error and change nothing.
- A user whose session carries the must-change-password claim sees a banner and the password form, and no tokens card
  and no appearance card; a normal session shows no banner; replacing the temporary password sends the browser to the
  sign-in page with the changed notice flag and clears the flag of the stored user.
- The page shows a card of personal access tokens with the name field, the choices of 30, 90 (selected), and 365 days,
  the list (or a note that it is empty), and a link to the help topic; creating a token shows its value once with a copy
  button and lists the token without the value; an empty name and the twenty-first active token are each shown as an
  error and create nothing; the copy button hands the value to the browser and shows a hint when the browser refuses; and
  an active token is revoked from the list while an expired one has no revoke button.

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

**AccountPage_Render_TokensCard_ShowsFormEmptyListAndHelpLink**: The page is rendered for a user without tokens. The
card, the name input, the create button, and the three expiry choices are present, with 90 days selected; the empty-list
note is shown, no value box is shown, and the only link in the card is the help topic `/help/api-tokens`, styled as a
button. This scenario is tested by `AccountPage_Render_TokensCard_ShowsFormEmptyListAndHelpLink`.

**AccountPage_CreateToken_ValidInput_ShowsValueOnceAndListsToken**: A name is typed, 30 days is chosen, and the form is
submitted. The value box shows a value that starts with `rcpat_`, the confirmation "The token was created." is shown,
and the name field is empty. The list has the token with its name, display id, state Active, the expiry 30 days later,
and "never" as last use, and the value is not in the list. Dismissing the box removes the value from the page for good.
This scenario is tested by `AccountPage_CreateToken_ValidInput_ShowsValueOnceAndListsToken`.

**AccountPage_CreateToken_EmptyName_ShowsErrorAndCreatesNothing**: The form is submitted with an empty name; an error is
shown, no value box is shown, and the store holds no token. This scenario is tested by
`AccountPage_CreateToken_EmptyName_ShowsErrorAndCreatesNothing`.

**AccountPage_CreateToken_TwentyActive_ShowsLimitError**: The user has twenty active tokens and submits another; the
error names the limit of 20, no value box is shown, and the store still holds twenty tokens. This scenario is tested by
`AccountPage_CreateToken_TwentyActive_ShowsLimitError`.

**AccountPage_CopyToken_NewToken_CopiesThroughBrowserScript**: The browser script `reviewCenter.copyText` reports success;
after a token is created and the copy button is pressed, the script received exactly the value and the page shows
"Copied.". This scenario is tested by `AccountPage_CopyToken_NewToken_CopiesThroughBrowserScript`.

**AccountPage_CopyToken_BrowserRefuses_ShowsHintAndKeepsToken**: The script reports that the copy failed; the page shows
the hint to copy the value by hand and the value stays on the page. This scenario is tested by
`AccountPage_CopyToken_BrowserRefuses_ShowsHintAndKeepsToken`.

**AccountPage_RevokeToken_ActiveToken_MarksRevokedAndRemovesButton**: The user has two tokens and revokes the first; the
first shows Revoked and has no revoke button, the second stays Active with its button, and the confirmation "The token
was revoked." is shown. This scenario is tested by `AccountPage_RevokeToken_ActiveToken_MarksRevokedAndRemovesButton`.

**AccountPage_Render_ExpiredToken_ShowsExpiredWithoutRevokeButton**: A 30-day token is created and the clock is moved
31 days; the page shows the token as Expired without a revoke button. This scenario is tested by
`AccountPage_Render_ExpiredToken_ShowsExpiredWithoutRevokeButton`.

**AccountPage_Render_TemporaryPassword_ShowsBannerAndOnlyPasswordForm**: A user whose session carries the
must-change-password claim opens the page; the banner and the password form are shown, and neither the tokens card nor
the appearance card is. This scenario is tested by
`AccountPage_Render_TemporaryPassword_ShowsBannerAndOnlyPasswordForm`.

**AccountPage_Render_NormalSession_ShowsNoBanner**: A normal session shows no banner, and the tokens card is there. This
scenario is tested by `AccountPage_Render_NormalSession_ShowsNoBanner`.

**AccountPage_ChangePassword_TemporaryPassword_RedirectsToLoginWithNotice**: A flagged user fills in the form with the
current and a new password and submits; the browser is sent to `/login?changed=1` and the stored user no longer has the
flag. This scenario is tested by `AccountPage_ChangePassword_TemporaryPassword_RedirectsToLoginWithNotice`.

**AccountPage_Render_ExternalUser_ShowsNoPasswordForm**: The signed-in user is an account of an identity provider and
the provider is on (in `OidcPageTests.cs`); the page shows the sentence that names the provider, and has no current
password field and no change button. This scenario is tested by
`AccountPage_Render_ExternalUser_ShowsNoPasswordForm`.
