package ReviewCenter {
    part def InboxStore {
        doc /* Persistence for the inbox: the light list of the reviews in which a user is a primary reviewer, an alternate, or the creator (without the files of a review), and the read state of a user for a review, which is written by conditional changes that never move it backwards and so give one result when several writers race, without an audit entry. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/InboxStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/InboxStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/inbox-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/inbox-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/inbox-store.yaml */
    }
}
