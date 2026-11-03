package ReviewCenter {
    part def NotificationMessage {
        doc /* One e-mail message to one recipient, which refuses at construction every part that could change the header of the mail (a recipient that is not one valid address, a line break or other control character in the subject), together with the contract of a sender, the sender that stands in when no mail server is set up, and the failure type of a send. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/NotificationMessage.cs, INotificationSender.cs, NoNotificationSender.cs and NotificationSendException.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/NotificationMessageTests.cs */
        comment designRef /* Design: docs/design/review-center/core/notification-message.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/notification-message.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/notification-message.yaml */
    }
}
