package ReviewCenter {
    part def AuditStore {
        doc /* Persists the audit trail: appends every entry with the next sequence number and the hash of its predecessor under a process-wide gate, stores a change of data and its entry in one Serializable transaction, and offers paged and batched reads (narrowed by an audit filter: project, review, actor, action or group, and a range of UTC days, with the time stored as sortable UTC text and indexes on project, actor and action) and the time of the newest entry of every review of a project in one read (`LastActivityAsync`, the last activity of a review) but no update or delete. Includes the AuditAppender shared by all stores. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/AuditStore.cs and AuditAppender.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/AuditStoreTests.cs and AuditStoreFilterTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/audit-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/audit-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/audit-store.yaml */
    }
}
