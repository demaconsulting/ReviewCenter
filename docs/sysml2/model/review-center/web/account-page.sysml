package ReviewCenter {
    part def AccountPage {
        doc /* Page where a signed-in user chooses the appearance of the interface (system, light, or dark, kept in the browser only) and changes the own password by giving the current password and the new password twice (and a code or recovery code while the second factor is on), and manages the personal access tokens of the user: creates a token with a name and an expiry and shows its value once with a copy button, lists the tokens with their state and last use, and revokes a token. For a user of an OpenID Connect provider a sentence replaces the change-password card, because the account has no password. A card for the second factor of a local account shows its status and lets the user set it up (with the password, the secret as text, and a confirming code), see the ten recovery codes once, give up a setup, and turn it off or replace the recovery codes with the password and a code. For a user with a temporary password the page shows a banner and only the change-password form, and after the change sends the user to the login page. A card for e-mail notifications shows one sentence while the administrator has not set them up (and then reads nothing from the store); otherwise it lets the user enter an e-mail address, confirm it with a 6-digit code that was mailed to the address (the page never shows the code), ask for a new code after a pause, and remove the address, and it is shown to a user of a provider as well. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/AccountPage.razor and AccountPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AccountPageTests.cs, AccountTotpPageTests.cs and AccountNotificationPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/account-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/account-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/account-page.yaml */
    }
}
