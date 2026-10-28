package ReviewCenter {
    part def AccountPage {
        doc /* Page where a signed-in user chooses the appearance of the interface (system, light, or dark, kept in the browser only) and changes the own password by giving the current password and the new password twice, and manages the personal access tokens of the user: creates a token with a name and an expiry and shows its value once with a copy button, lists the tokens with their state and last use, and revokes a token. For a user of an OpenID Connect provider a sentence replaces the change-password card, because the account has no password. For a user with a temporary password the page shows a banner and only the change-password form, and after the change sends the user to the login page. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/AccountPage.razor and AccountPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AccountPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/account-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/account-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/account-page.yaml */
    }
}
