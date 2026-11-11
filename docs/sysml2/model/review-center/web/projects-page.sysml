package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ProjectsPage {
        doc /* Pages that list the projects the user may see (every project for a system administrator), as rows that link to the project, and hold the separate form to create a project, which only a system administrator may use. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/ProjectsPage.razor", "src/DemaConsulting.ReviewCenter/Web/ProjectsPage.razor.cs", "src/DemaConsulting.ReviewCenter/Web/NewProjectPage.razor", "src/DemaConsulting.ReviewCenter/Web/NewProjectPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/ProjectsPageTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/NewProjectPageTests.cs");
            designFiles = ("docs/design/review-center/web/projects-page.md");
            verificationFiles = ("docs/verification/review-center/web/projects-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/projects-page.yaml");
        }
    }
}
