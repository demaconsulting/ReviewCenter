package ReviewCenter {
    part def FileNotificationSender {
        doc /* Delivery of one e-mail as a file for demonstrations, development, and tests: one .eml file for each message with a new name in the configured folder, which is created when needed, holding the message that an SMTP server would receive, never overwriting a file, and failures reported as a send error without the path. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Messaging/FileNotificationSender.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Messaging/FileNotificationSenderTests.cs */
        comment designRef /* Design: docs/design/review-center/messaging/file-notification-sender.md */
        comment verificationRef /* Verification: docs/verification/review-center/messaging/file-notification-sender.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/messaging/file-notification-sender.yaml */
    }
}
