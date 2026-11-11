package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def NotificationMessage {
        doc /* One e-mail message to one recipient, which refuses at construction every part that could change the header of the mail (a recipient that is not one valid address, a line break or other control character in the subject), together with the contract of a sender, the sender that stands in when no mail server is set up, and the failure type of a send. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/NotificationMessage.cs", "src/DemaConsulting.ReviewCenter.Core/INotificationSender.cs", "src/DemaConsulting.ReviewCenter.Core/NoNotificationSender.cs", "src/DemaConsulting.ReviewCenter.Core/NotificationSendException.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/NotificationMessageTests.cs");
            designFiles = ("docs/design/review-center/core/notification-message.md");
            verificationFiles = ("docs/verification/review-center/core/notification-message.md");
            requirementFiles = ("docs/reqstream/review-center/core/notification-message.yaml");
        }
    }
}
