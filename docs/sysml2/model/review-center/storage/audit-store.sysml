package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AuditStore {
        doc /* Persists the audit trail: appends every entry with the next sequence number and the hash of its predecessor under a process-wide gate, stores a change of data and its entry in one Serializable transaction, and offers paged and batched reads (narrowed by an audit filter: project, review, actor, action or group, and a range of UTC days, with the time stored as sortable UTC text and indexes on project, actor and action) and the time of the newest entry of every review of a project in one read (`LastActivityAsync`, the last activity of a review) but no update or delete. Also reads the newest sequence number and the entries that match a filter in ascending sequence order after a position and up to an upper sequence (keyset batches, for the export). Includes the AuditAppender shared by all stores. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Storage/AuditStore.cs", "src/DemaConsulting.ReviewCenter/Storage/AuditAppender.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Storage/AuditStoreTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Storage/AuditStoreFilterTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Storage/AuditStoreExportTests.cs");
            designFiles = ("docs/design/review-center/storage/audit-store.md");
            verificationFiles = ("docs/verification/review-center/storage/audit-store.md");
            requirementFiles = ("docs/reqstream/review-center/storage/audit-store.yaml");
        }
    }
}
