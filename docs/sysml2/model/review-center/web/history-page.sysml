package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def HistoryPage {
        doc /* Page that shows the audit entries recorded for one review, newest first, 50 to a page, to every signed-in user who may see the project of the review, with all text rendered as plain text. */

        metadata info : SoftwareInfo {
            description = "Page that shows the audit entries recorded for one review, newest first, 50 to a page, to every signed-in user who may see the project of the review, with all text rendered as plain text.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/HistoryPage.razor", "src/DemaConsulting.ReviewCenter/Web/HistoryPage.razor.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/HistoryPageTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/BallotMaskingPageTests.cs");
            designFiles = ("docs/design/review-center/web/history-page.md");
            verificationFiles = ("docs/verification/review-center/web/history-page.md");
            requirementFiles = ("docs/reqstream/review-center/web/history-page.yaml");
        }
    }
}
