package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def NotificationAddress {
        doc /* The check that a text is exactly one plain e-mail address that a notification may be mailed to, and its normal form (trimmed, lower case): no list, display name, comment, quoted part, address in brackets, control character, or character outside ASCII, and fixed limits on the length of the whole address and of the local part. */

        metadata info : SoftwareInfo {
            description = "The check that a text is exactly one plain e-mail address that a notification may be mailed to, and its normal form (trimmed, lower case): no list, display name, comment, quoted part, address in brackets, control character, or character outside ASCII, and fixed limits on the length of the whole address and of the local part.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/NotificationAddress.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/NotificationAddressTests.cs");
            designFiles = ("docs/design/review-center/core/notification-address.md");
            verificationFiles = ("docs/verification/review-center/core/notification-address.md");
            requirementFiles = ("docs/reqstream/review-center/core/notification-address.yaml");
        }
    }
}
