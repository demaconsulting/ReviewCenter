package ReviewCenter {
    part def UsersPage {
        doc /* System administrator page that lists the users, leads to the separate page that creates a user (with a System administrator checkbox), enables or disables a user, grants or takes away the system administrator flag, and sets a password (an account of an OpenID Connect provider has an OIDC badge that names the provider and no password control), and shows the rule message when the change is refused, for instance when the last enabled system administrator would be lost. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/UsersPage.razor and UsersPage.razor.cs, and src/DemaConsulting.ReviewCenter/Web/NewUserPage.razor and NewUserPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/UsersPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/users-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/users-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/users-page.yaml */
    }
}
