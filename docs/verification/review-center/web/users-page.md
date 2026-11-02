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
- A locked user shows the end of the lockout in UTC and a button to unlock, which acts at once; a user with a temporary
  password shows a badge; the last sign-in shows in UTC or as "Never".
- The search matches the user name and the display name in any letter case, the filter shows only enabled, disabled,
  locked, or system administrator users, and both apply together.
- Disabling a user first shows a panel with the open reviews and the sole-administrator projects as links, or the note
  that nothing is waiting, changes nothing until the second click, and changes nothing when cancelled; a failure to load
  the impact shows an error and no panel; enabling needs no confirmation.
- Resetting the second factor and setting a password also need a second click after an explanation, and change nothing
  when cancelled.
- On the creation page, a valid new user is created with the chosen "System administrator" checkbox, the password field
  is emptied, and the browser returns to the list; the page has a Cancel link to the list; a rule violation is shown as
  an error, creates nothing, keeps the typed user name, and drops the typed password.
- A user is disabled and enabled again, and the system administrator flag is granted and taken away.
- A new password set for a user works, the old one does not, and the field is emptied.
- Disabling or demoting the last enabled system administrator is refused with an error.
- An action is refused with an error, and changes nothing, when the signed-in system administrator lost the flag or was
  disabled after the page was opened; the user list, the typed passwords, and the button that creates a user are then no
  longer rendered, and an administrator who takes the flag from themselves sees the change acknowledged but not the
  reloaded list.
- When the sign-in state of the open page ends because the administrator lost the flag, the page is replaced by the
  "Session ended" message and offers no actions; an action sent after the session ended, before the page learned of
  it, shows an error that says the user is no longer signed in, without the list, and changes nothing.
- An account of an identity provider carries an "OIDC" badge whose title names the provider and shows a note that it
  has no local password, with no password field and no "Set password" button; a local account has neither the badge
  nor the note.
- An enrolled local account carries a "TOTP" badge and a reset button for its second factor; a plain account and an
  account of an identity provider have neither; both stay while the feature is switched off, so that a dormant
  enrollment can be cleared; the reset removes the second factor, says so, and removes the badge and the button; a reset
  sent after the session ended shows the "no longer signed in" error and keeps the enrollment; the service behind the
  button refuses a caller who is not a system administrator.

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

**UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain**: The toggle button of an ordinary user is used, then the
button "Disable anyway" of the confirmation; the user is stored as disabled and shown as "Disabled", and then the
button is used again and the user is stored as enabled and shown as "Enabled" without a confirmation. This scenario is
tested by `UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain`.

**UsersPage_ToggleSystemAdmin_User_GrantsAndTakesAwayTheFlag**: The system administrator button of an ordinary user is
used twice; the user is stored as a system administrator and shown with the "System admin" badge, and then stored
without the flag and shown without the badge. This scenario is tested by
`UsersPage_ToggleSystemAdmin_User_GrantsAndTakesAwayTheFlag`.

**UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField**: A new password is typed in the row of a user
and set after the confirmation; the new password works, the old one does not, there is no error, and the field is
empty. This scenario is tested by
`UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField`.

**UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError**: The only system administrator is disabled and demoted
through the row controls; an error that mentions the administrator is shown, and the user stays enabled and a system
administrator. This scenario is tested by `UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError`.

**UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError**: Two system administrators and an ordinary user exist
and one administrator opens the page; that administrator then loses the flag, and the disable button of the ordinary
user is used; an error that says system administrator rights are required is shown, no user list or row is rendered, and
the user stays enabled. This scenario is tested by
`UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError`.

**UsersPage_Action_AdminDemotedAfterOpening_DropsUserListAndTypedPasswords**: Two system administrators and a reviewer
exist; one administrator opens the page and types a password in the row of the reviewer, then loses the flag, and the
Set password button is used. The error that says system administrator rights are required is shown, the list, the
rows, the password inputs, and the new user button are gone, the typed password is not in the markup, and the password
of the reviewer is unchanged. This scenario is tested by
`UsersPage_Action_AdminDemotedAfterOpening_DropsUserListAndTypedPasswords`.

**UsersPage_ToggleSystemAdmin_AdminDemotesSelf_ShowsStatusButDropsTheList**: Two system administrators exist and one
opens the page and removes the flag from their own row. The flag is stored as removed, the status message says that
the user is no longer a system administrator, and neither the list nor a row is rendered. This scenario is tested by
`UsersPage_ToggleSystemAdmin_AdminDemotesSelf_ShowsStatusButDropsTheList`.

**UsersPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions**: Two system administrators and an ordinary
user exist and
one administrator has the page open, with the sign-in state re-checked every 100 milliseconds; that administrator loses
the flag. Within the limit of 10 seconds the "Session ended" message appears, the page and the user controls are gone,
a click on the stale button changes nothing, and the ordinary user is still enabled. This scenario is tested by
`UsersPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions`.

**UsersPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing**: An administrator has the page open
and the sign-in state ends without the page being told; the disable button of a user is used. The error area says that
the user is no longer signed in and no list is rendered, and the user is still enabled. This scenario is tested by
`UsersPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing`.

**UsersPage_Render_ExternalUser_ShowsMarkerAndNoPasswordControls**: One account of an identity provider and one local
account exist and the provider is on (in `OidcPageTests.cs`). The row of the provider account has the badge "OIDC"
with a title that names the provider and the note "no local password", and has no password field and no "Set
password" button; the local row has no badge and still has the "Set password" button. This scenario is tested by
`UsersPage_Render_ExternalUser_ShowsMarkerAndNoPasswordControls`.

**UsersPage second factor scenarios**: These tests are in `UsersTotpPageTests.cs`, rendered with bUnit over the real
`TotpService` with in-memory stores, with a system administrator signed in. With one enrolled and one plain user, only
the enrolled user has the badge "TOTP" and the reset button, and there is exactly one badge; this is tested by
`UsersPage_Render_EnrolledAndPlainUsers_BadgeAndResetOnlyForEnrolled`. An account of an identity provider has neither,
which is tested by `UsersPage_Render_ExternalUser_HasNoTotpControls`. The reset removes the enrollment, the status says
the second factor "was reset", the badge and the button are gone, and no error is shown; this is tested by
`UsersPage_ResetTotp_EnrolledUser_RemovesEnrollmentAndShowsStatus`. With the feature switched off the badge and the
button are still shown, which is tested by `UsersPage_Render_FeatureDisabled_StillShowsBadgeAndReset`. A reset after the
session ended shows the error "no longer signed in" and the user stays enrolled, which is tested by
`UsersPage_ResetTotp_SessionEndedBeforePageKnows_ShowsErrorAndKeepsEnrollment`. The service behind the button refuses a
caller who is not a system administrator (a project administrator is an ordinary user) with `ActorNotAllowed` and keeps
the enrollment, which is tested by `TotpService_Reset_NotSystemAdmin_IsRefusedAndKeepsEnrollment`, defined in the same
file.

**UsersPage status scenarios**: These tests are in `UsersPageTests.cs` over the fake store and a clock the test sets. A
user who is locked for five more minutes shows the badge "Locked until 05:11 UTC" and the button "Unlock"; a user with a
temporary password shows the badge "Temporary password"; the last sign-in shows as `yyyy-MM-dd HH:mm UTC` or "Never";
and an unlocked user has no badge and no button, which is tested by
`UsersPage_Render_StatusBadges_ShowLockedTemporaryAndLastSignIn`. A lockout that ended is not shown, which is tested by
`UsersPage_Render_ExpiredLockout_IsNotShownAsLocked`. The button "Unlock" clears the lockout at once, with no
confirmation, shows a status and removes the badge, which is tested by
`UsersPage_Unlock_LockedUser_ClearsLockoutInOneClick`.

**UsersPage search and filter scenarios**: The search matches part of the user name or the display name in any letter
case, and clearing the search lists everybody again, which is tested by
`UsersPage_Search_TypedText_MatchesUserNameOrDisplayNameIgnoringCase`. Each of the choices Enabled, Disabled, Locked,
and System administrators lists exactly the matching users, and All lists everybody, which is tested by
`UsersPage_Filter_EachStatus_ListsOnlyMatchingUsers`. The search and a filter apply together, which is tested by
`UsersPage_SearchAndFilter_Combined_BothApply`.

**UsersPage disable confirmation scenarios**: A user who holds an open review slot and the only administrator of a
project is disabled through the row; the first click only opens the panel, which lists the review and the project as
links to `/projects/{projectId}/reviews/{reviewId}` and `/projects/{projectId}`, and the user stays enabled; the button
"Disable anyway" then disables the user and closes the panel. Each open slot is listed with its state in words, "Not
signed" or "Signed, then the review changed", never as an enumeration name. This is tested by
`UsersPage_Disable_UserWithImpact_ShowsLinksAndChangesNothingUntilConfirmed`. For a user with nothing waiting the panel
says so and still needs the second click, which is tested by
`UsersPage_Disable_UserWithoutImpact_SaysNothingIsWaitingAndStillNeedsTheClick`. The button "Cancel" closes the panel
and changes nothing, which is tested by `UsersPage_Disable_Cancel_ClosesThePanelAndChangesNothing`. Enabling a disabled
user needs no confirmation, which is tested by `UsersPage_Enable_DisabledUser_NeedsNoConfirmation`. When the impact
cannot be loaded (here the user does not exist in the store the impact service reads) the error of the row is shown and
no panel opens, which is tested by `UsersPage_Disable_ImpactLoadRefused_ShowsErrorAndOpensNoPanel`.

**UsersPage other confirmation scenarios**: The first click on "Reset two-factor" opens a panel that says that the
authenticator and the recovery codes are removed and the user has to set up two-factor sign-in again, and removes
nothing; "Cancel" closes it and the enrollment stays; this is tested by
`UsersPage_ResetTotp_FirstClickAndCancel_ExplainsAndRemovesNothing` in `UsersTotpPageTests.cs`. The first click on "Set
password" with a typed password opens a panel that says that the user must change the password at the next sign-in, can
only use the Account, Help, and About pages, cannot sign reviews, and that API tokens stop working until then, and
changes nothing; the second click sets the password and the account has the temporary state, and the status says that
the user must change the password, which is tested by
`UsersPage_SetPassword_FirstClick_ExplainsThenSecondClickMakesItTemporary`. In the row of the signed-in administrator
the panel says instead that the own password is not temporary, with no forced change and no restriction; the second
click sets the password, the account does not get the temporary state, and the status says "it is not temporary",
which is tested by `UsersPage_SetPassword_OwnRow_SaysThePasswordIsNotTemporary`. "Cancel", and an empty password
field, change nothing, which is tested by `UsersPage_SetPassword_CancelOrEmptyField_ChangesNothing`.
