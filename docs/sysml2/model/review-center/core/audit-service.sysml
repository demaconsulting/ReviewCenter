package ReviewCenter {
    part def AuditService {
        doc /* Records the events that change no data (sign-in, sign-out, failed sign-in, lockout) on a best-effort basis, coalesces repeated failure events to one entry per key and minute with a suppressed count (cancellation-aware, and one suppressed-events entry per minute when the key table is full), reads the audit trail one page at a time narrowed by a validated filter (project, review, actor, action or group of actions, and a range of UTC days; page size 50 by default, at most 200), and verifies the hash chain by streaming. Also defines the audit filter and the vocabulary of action codes, the audit store contract, and the page type. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/AuditService.cs, AuditFilter.cs, AuditActions.cs, and IAuditStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/AuditServiceTests.cs, AuditFilterTests.cs, and AuditActionsTests.cs */
        comment designRef /* Design: docs/design/review-center/core/audit-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/audit-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/audit-service.yaml */
    }
}
