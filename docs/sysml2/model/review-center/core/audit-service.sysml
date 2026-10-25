package ReviewCenter {
    part def AuditService {
        doc /* Records the events that change no data (sign-in, sign-out, failed sign-in, lockout) on a best-effort basis, reads the audit trail one page at a time, and verifies the hash chain by streaming. Also defines the audit store contract and the page type. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/AuditService.cs and IAuditStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/AuditServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/audit-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/audit-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/audit-service.yaml */
    }
}
