package ReviewCenter {
    part def AuditPage {
        doc /* Page, limited to system administrators, that shows the whole audit trail newest first, 50 to a page, with long hashes and fingerprints shortened and their full value in a tooltip, and lets a system administrator verify the hash chain and read the entry count and the full head hash. The system administrator flag is checked again in the user store on load and on every verification. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/AuditPage.razor and AuditPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AuditPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/audit-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/audit-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/audit-page.yaml */
    }
}
