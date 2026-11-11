package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def NotificationComposer {
        doc /* The composition of one notification mail from the inbox items that are new for a user: the choice of the new items by their state, the plain-text and HTML body with the group, title, review, and state of each item and a link to the review through the configured address of the server, a fixed subject for an update and for the daily summary, a limit on the number of listed items and on the size of the mail, and no detail text, outcome, or text of a comment. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/NotificationComposer.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/NotificationComposerTests.cs");
            designFiles = ("docs/design/review-center/core/notification-composer.md");
            verificationFiles = ("docs/verification/review-center/core/notification-composer.md");
            requirementFiles = ("docs/reqstream/review-center/core/notification-composer.yaml");
        }
    }
}
