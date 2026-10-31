package ReviewCenter {
    part def HelpPage {
        doc /* Built-in help: the index at /help, one page for each help topic at /help/{topic} with a shared frame and links to the previous and next topic, the list of topics that feeds them, a friendly page for an unknown help address, and the Signed attestation topic that says what the signature proves and does not prove, the Managing project members topic that explains how an administrator of a project adds a member by the exact user name, creates an account with a temporary password, changes a role, and removes a member, and the API tokens topic that explains personal access tokens, the API, and what a token cannot do, and the Evidence settings topic that explains who may change the evidence settings of a project, the anonymous access switches and their exposure, the PDF content switches and their regulatory warning, and the Content scope section of every PDF, and the Two-factor sign-in topic that explains how to set it up, sign in and sign with a code, use the recovery codes, turn it off, and get a reset, and the Your inbox topic that explains the four groups of the inbox, the New marker, and Mark all as read; the topics on getting started, signing, and API tokens also mention the identity provider. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/HelpPage.razor, HelpTopicFrame.razor, HelpNotFoundPage.razor, HelpTopics.cs, HelpEvidenceSettingsPage.razor, and the other topic pages Help*Page.razor (screenshots in src/DemaConsulting.ReviewCenter/wwwroot/help/) */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/HelpPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/help-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/help-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/help-page.yaml */
    }
}
