package ReviewCenter {
    part def SmtpNotificationSender {
        doc /* Delivery of one e-mail through an SMTP server with MailKit: one connection for each send with the TLS mode of the settings, sign-in when a user name is set, a MIME message with a sender name, a recipient, a subject, and a text and an HTML part, and failures reported as a send error without password, address, or server text; a canceled token ends the send. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Messaging/SmtpNotificationSender.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Messaging/SmtpNotificationSenderTests.cs */
        comment designRef /* Design: docs/design/review-center/messaging/smtp-notification-sender.md */
        comment verificationRef /* Verification: docs/verification/review-center/messaging/smtp-notification-sender.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/messaging/smtp-notification-sender.yaml */
    }
}
