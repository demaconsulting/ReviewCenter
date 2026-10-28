package ReviewCenter {
    part def AuditStore {
        doc /* Persists the audit trail: appends every entry with the next sequence number and the hash of its predecessor under a process-wide gate, stores a change of data and its entry in one Serializable transaction, and offers paged and batched reads (the whole trail, one project, or one review) but no update or delete. Includes the AuditAppender shared by all stores. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/AuditStore.cs and AuditAppender.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/AuditStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/audit-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/audit-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/audit-store.yaml */
    }
}
