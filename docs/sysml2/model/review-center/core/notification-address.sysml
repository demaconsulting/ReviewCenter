package ReviewCenter {
    part def NotificationAddress {
        doc /* The check that a text is exactly one plain e-mail address that a notification may be mailed to, and its normal form (trimmed, lower case): no list, display name, comment, quoted part, address in brackets, control character, or character outside ASCII, and fixed limits on the length of the whole address and of the local part. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/NotificationAddress.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/NotificationAddressTests.cs */
        comment designRef /* Design: docs/design/review-center/core/notification-address.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/notification-address.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/notification-address.yaml */
    }
}
