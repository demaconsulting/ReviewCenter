package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AuditService {
        doc /* Records the events that change no data (sign-in, sign-out, failed sign-in, lockout) on a best-effort basis, coalesces repeated failure events to one entry per key and minute with a suppressed count (cancellation-aware, and one suppressed-events entry per minute when the key table is full), reads the audit trail one page at a time narrowed by a validated filter (project, review, actor, action or group of actions, and a range of UTC days; page size 50 by default, at most 200), and verifies the hash chain by streaming. Exports the entries that match a filter as CSV (with CSV-injection neutralization of text cells) or JSON in batches of 500 in chain order up to a snapshot taken at the start, after verifying the chain once so that its status (intact, or broken with the first broken sequence) is known before the first byte, and records the export as an audit.exported entry after the stream, also when it was cancelled. Also defines the audit filter and the vocabulary of action codes, the audit store contract, and the page type. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/AuditService.cs", "src/DemaConsulting.ReviewCenter.Core/AuditExport.cs", "src/DemaConsulting.ReviewCenter.Core/AuditExportWriter.cs", "src/DemaConsulting.ReviewCenter.Core/AuditFilter.cs", "src/DemaConsulting.ReviewCenter.Core/AuditActions.cs", "src/DemaConsulting.ReviewCenter.Core/IAuditStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/AuditServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/AuditExportTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/AuditExportWriterTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/AuditFilterTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/AuditActionsTests.cs");
            designFiles = ("docs/design/review-center/core/audit-service.md");
            verificationFiles = ("docs/verification/review-center/core/audit-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/audit-service.yaml");
        }
    }
}
