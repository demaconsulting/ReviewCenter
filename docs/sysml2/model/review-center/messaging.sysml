package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def MessagingSubsystem {
        doc /* Delivery of the e-mail of the optional e-mail notifications: the Notifications settings with their start-up check, warnings, and password resolution, the choice of the transport by the settings (nothing while notifications are off, an SMTP server, or one file for each message), and the delivery of one message through the Core sender interface with failures that never reveal a password, an address, or text of the server, and the background worker that runs the notification pass at the polling interval. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Messaging/MessagingSubsystemTests.cs");
            designFiles = ("docs/design/review-center/messaging.md");
            verificationFiles = ("docs/verification/review-center/messaging.md");
            requirementFiles = ("docs/reqstream/review-center/messaging.yaml");
        }

        part notificationOptions : NotificationOptions;
        part smtpNotificationSender : SmtpNotificationSender;
        part fileNotificationSender : FileNotificationSender;
        part notificationWorker : NotificationWorker;
    }
}
