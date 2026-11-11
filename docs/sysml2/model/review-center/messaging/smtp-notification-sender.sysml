package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def SmtpNotificationSender {
        doc /* Delivery of one e-mail through an SMTP server with MailKit: one connection for each send with the TLS mode of the settings, sign-in when a user name is set, a MIME message with a sender name, a recipient, a subject, and a text and an HTML part, and failures reported as a send error without password, address, or server text; a canceled token ends the send. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Messaging/SmtpNotificationSender.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Messaging/SmtpNotificationSenderTests.cs");
            designFiles = ("docs/design/review-center/messaging/smtp-notification-sender.md");
            verificationFiles = ("docs/verification/review-center/messaging/smtp-notification-sender.md");
            requirementFiles = ("docs/reqstream/review-center/messaging/smtp-notification-sender.yaml");
        }
    }
}
