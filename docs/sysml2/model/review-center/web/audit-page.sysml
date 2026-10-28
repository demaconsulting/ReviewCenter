package ReviewCenter {
    part def AuditPage {
        doc /* Pages that show the audit trail newest first, 50 to a page, through one shared table component with long hashes and fingerprints shortened and their full value in a tooltip and the paging buttons: the page limited to system administrators that shows the whole trail and lets them verify the hash chain and read the entry count and the full head hash (the system administrator flag is checked again in the user store on load and on every verification), and the project page that shows only the entries of one project to whoever administers it (a hidden or unadministered project looks like a project that does not exist) and holds no verification. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/AuditPage.razor, AuditPage.razor.cs, ProjectAuditPage.razor, ProjectAuditPage.razor.cs, AuditEntryTable.razor, and AuditEntryTable.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AuditPageTests.cs and ProjectAuditPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/audit-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/audit-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/audit-page.yaml */
    }
}
