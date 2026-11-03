package ReviewCenter {
    part def NotificationOptions {
        doc /* The settings of e-mail notifications (off by default) read from the Notifications section: the transport, the sender and the base address, the SMTP server with its TLS mode and login, the folder of the file transport, and the schedule and limits; the start-up check that rejects invalid settings with messages that name the setting and never hold a value, the warnings for settings that work but deserve a second look, the resolution of the SMTP password from an inline value or a file, and the registration of the settings and of the sender that they choose. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Messaging/NotificationOptions.cs, src/DemaConsulting.ReviewCenter/Messaging/NotificationRegistration.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Messaging/NotificationOptionsTests.cs */
        comment designRef /* Design: docs/design/review-center/messaging/notification-options.md */
        comment verificationRef /* Verification: docs/verification/review-center/messaging/notification-options.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/messaging/notification-options.yaml */
    }
}
