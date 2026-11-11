package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def FileNotificationSender {
        doc /* Delivery of one e-mail as a file for demonstrations, development, and tests: one .eml file for each message with a new name in the configured folder, which is created when needed, holding the message that an SMTP server would receive, never overwriting a file, and failures reported as a send error without the path. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Messaging/FileNotificationSender.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Messaging/FileNotificationSenderTests.cs");
            designFiles = ("docs/design/review-center/messaging/file-notification-sender.md");
            verificationFiles = ("docs/verification/review-center/messaging/file-notification-sender.md");
            requirementFiles = ("docs/reqstream/review-center/messaging/file-notification-sender.yaml");
        }
    }
}
