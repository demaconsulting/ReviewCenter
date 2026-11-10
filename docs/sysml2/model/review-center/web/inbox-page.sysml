package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def InboxPage {
        doc /* Page at /inbox, for a signed-in user, that lists what waits for the user in eight groups (To answer, Disputed, Ready to sign, Your signature no longer counts, Replies waiting, Failed checks, Waiting for others, and Updates), each row with the project, the review, a chip, an optional due or overdue mark, a New marker for what the user has not seen, and a link to the review, with a summary of what needs action and what is an update, an empty state, and a Mark all as read button that stores the read state of every listed review. A visitor without a session sees the session-ended message. */

        metadata info : SoftwareInfo {
            description = "Page at /inbox, for a signed-in user, that lists what waits for the user in eight groups (To answer, Disputed, Ready to sign, Your signature no longer counts, Replies waiting, Failed checks, Waiting for others, and Updates), each row with the project, the review, a chip, an optional due or overdue mark, a New marker for what the user has not seen, and a link to the review, with a summary of what needs action and what is an update, an empty state, and a Mark all as read button that stores the read state of every listed review. A visitor without a session sees the session-ended message.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/InboxPage.razor", "src/DemaConsulting.ReviewCenter/Web/InboxPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/InboxPageTests.cs");
            designFiles = ("docs/design/review-center/web/inbox-page.md");
            verificationFiles = ("docs/verification/review-center/web/inbox-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/inbox-page.yaml");
        }
    }
}
