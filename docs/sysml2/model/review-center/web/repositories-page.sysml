package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def RepositoriesPage {
        doc /* Page at /projects/{id}/repositories, for a project administrator or a system administrator of that project (anybody else sees the page of a missing project), that lists the git repositories of the project and lets the administrator edit, disable, and enable them, and leads to the separate page that adds a repository to the project (a repository is never deleted). The access token is write-only and is never shown. The right to manage the project is checked again on load and on every action. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/RepositoriesPage.razor", "src/DemaConsulting.ReviewCenter/Web/RepositoriesPage.razor.cs", "src/DemaConsulting.ReviewCenter/Web/NewRepositoryPage.razor", "src/DemaConsulting.ReviewCenter/Web/NewRepositoryPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/RepositoriesPageTests.cs");
            designFiles = ("docs/design/review-center/web/repositories-page.md");
            verificationFiles = ("docs/verification/review-center/web/repositories-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/repositories-page.yaml");
        }
    }
}
