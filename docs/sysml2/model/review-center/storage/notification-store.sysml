package ReviewCenter {
    part def NotificationStore {
        doc /* Persistence of the e-mail address for notifications: one preference for each user with the confirmed address and the pending address with the salted hash of its code, and the log of the mails that were sent, with the reservation of a confirmation (pause and limits checked together with the change), the counting of attempts, and the confirmation each decided by one transaction or one conditional change, so that exactly one request wins, and the audit entry written in the same transaction. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/NotificationStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/NotificationStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/notification-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/notification-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/notification-store.yaml */
    }
}
