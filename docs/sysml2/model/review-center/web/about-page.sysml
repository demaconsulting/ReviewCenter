package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def AboutPage {
        doc /* Static page at /about, open to everyone, that says in plain words what ReviewCenter does and shows the full version, the copyright holder, and a link to the project page on GitHub. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/AboutPage.razor");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/AboutPageTests.cs");
            designFiles = ("docs/design/review-center/web/about-page.md");
            verificationFiles = ("docs/verification/review-center/web/about-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/about-page.yaml");
        }
    }
}
