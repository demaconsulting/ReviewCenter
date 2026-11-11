package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AuditPage {
        doc /* Pages that show the audit trail newest first, 25, 50 (default), or 100 to a page, through one shared table component with long hashes and fingerprints shortened and their full value in a tooltip and the paging buttons, and one shared GET filter form (actor, action, review, dates, page size, and a project selector on the whole-trail page) whose state is in the address and is carried by the paging links, with hostile values dropped: the page limited to system administrators that shows the whole trail and lets them verify the hash chain and read the entry count and the full head hash (the system administrator flag is checked again in the user store on load and on every verification), and the project page that shows only the entries of one project to project administrators, system administrators, and Auditors, through the audit-read access (a hidden or unreadable project looks like a project that does not exist) and holds no verification. Both pages show CSV and JSON export links that carry the current filter (never the page size or position) to the export routes, with a note that the export covers everything that matches the filter. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/AuditPage.razor", "src/DemaConsulting.ReviewCenter/Web/AuditPage.razor.cs", "src/DemaConsulting.ReviewCenter/Web/ProjectAuditPage.razor", "src/DemaConsulting.ReviewCenter/Web/ProjectAuditPage.razor.cs", "src/DemaConsulting.ReviewCenter/Web/AuditEntryTable.razor", "src/DemaConsulting.ReviewCenter/Web/AuditEntryTable.razor.cs", "src/DemaConsulting.ReviewCenter/Web/AuditFilterForm.razor", "src/DemaConsulting.ReviewCenter/Web/AuditFilterForm.razor.cs", "src/DemaConsulting.ReviewCenter/Web/AuditFilterState.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/AuditPageTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/ProjectAuditPageTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/AuditEntryTableTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/AuditFilterStateTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/AuditExportLinksTests.cs");
            designFiles = ("docs/design/review-center/web/audit-page.md");
            verificationFiles = ("docs/verification/review-center/web/audit-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/audit-page.yaml");
        }
    }
}
