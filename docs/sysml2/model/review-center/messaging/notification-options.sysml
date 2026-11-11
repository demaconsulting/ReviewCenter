package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def NotificationOptions {
        doc /* The settings of e-mail notifications (off by default) read from the Notifications section: the transport, the sender and the base address, the SMTP server with its TLS mode and login, the folder of the file transport, and the schedule and limits; the start-up check that rejects invalid settings with messages that name the setting and never hold a value, the warnings for settings that work but deserve a second look, the resolution of the SMTP password from an inline value or a file, and the registration of the settings and of the sender that they choose, and of the background notification worker that is built from them (only while notifications are enabled with a transport). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Messaging/NotificationOptions.cs", "src/DemaConsulting.ReviewCenter/Messaging/NotificationRegistration.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Messaging/NotificationOptionsTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Messaging/NotificationWorkerRegistrationTests.cs");
            designFiles = ("docs/design/review-center/messaging/notification-options.md");
            verificationFiles = ("docs/verification/review-center/messaging/notification-options.md");
            requirementFiles = ("docs/reqstream/review-center/messaging/notification-options.yaml");
        }
    }
}
