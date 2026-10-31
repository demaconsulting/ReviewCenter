package ReviewCenter {
    part def AuditPage {
        doc /* Pages that show the audit trail newest first, 25, 50 (default), or 100 to a page, through one shared table component with long hashes and fingerprints shortened and their full value in a tooltip and the paging buttons, and one shared GET filter form (actor, action, review, dates, page size, and a project selector on the whole-trail page) whose state is in the address and is carried by the paging links, with hostile values dropped: the page limited to system administrators that shows the whole trail and lets them verify the hash chain and read the entry count and the full head hash (the system administrator flag is checked again in the user store on load and on every verification), and the project page that shows only the entries of one project to project administrators, system administrators, and Auditors, through the audit-read access (a hidden or unreadable project looks like a project that does not exist) and holds no verification. Both pages show CSV and JSON export links that carry the current filter (never the page size or position) to the export routes, with a note that the export covers everything that matches the filter. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/AuditPage.razor, AuditPage.razor.cs, ProjectAuditPage.razor, ProjectAuditPage.razor.cs, AuditEntryTable.razor, AuditEntryTable.razor.cs, AuditFilterForm.razor, AuditFilterForm.razor.cs, and AuditFilterState.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AuditPageTests.cs, ProjectAuditPageTests.cs, AuditEntryTableTests.cs, AuditFilterStateTests.cs, and AuditExportLinksTests.cs */
        comment designRef /* Design: docs/design/review-center/web/audit-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/audit-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/audit-page.yaml */
    }
}
