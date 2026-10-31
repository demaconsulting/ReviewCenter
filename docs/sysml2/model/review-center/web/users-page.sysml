package ReviewCenter {
    part def UsersPage {
        doc /* System administrator page that lists the users with their status (a badge with the end of a lockout, a badge for a temporary password, and the time of the last sign-in), narrows the list by a search of the user name and display name and by a filter (enabled, disabled, locked, system administrators), leads to the separate page that creates a user (with a System administrator checkbox), enables a user at once, unlocks a locked user at once, and disables a user, resets the second factor of a user, or sets a password only after an inline confirmation that needs a second click (the confirmation to disable lists the open reviews and the sole-administrator projects of the user, from `UserImpactService`, as links and never blocks), grants or takes away the system administrator flag, and shows the rule message when the change is refused, for instance when the last enabled system administrator would be lost. An account of an OpenID Connect provider has an OIDC badge that names the provider and no password control. A badge marks an account with a second factor, and a button resets it for a person who lost the phone. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/UsersPage.razor and UsersPage.razor.cs, and src/DemaConsulting.ReviewCenter/Web/NewUserPage.razor and NewUserPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/UsersPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/users-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/users-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/users-page.yaml */
    }
}
