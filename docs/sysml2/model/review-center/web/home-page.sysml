package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def HomePage {
        doc /* Static landing page with the product name and tagline and, for a signed-in user, a Get started list of links to the pages the user can use (the inbox with the number of reviews that need the user, and the projects; the administration pages only for a system administrator). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/HomePage.razor", "src/DemaConsulting.ReviewCenter/Web/HomePage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/HomePageTests.cs");
            designFiles = ("docs/design/review-center/web/home-page.md");
            verificationFiles = ("docs/verification/review-center/web/home-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/home-page.yaml");
        }
    }
}
