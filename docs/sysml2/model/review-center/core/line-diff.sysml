package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def LineDiff {
        doc /* Compares the lines of a baseline and a head file with a shortest-edit-script algorithm, numbers the rows on both sides like the lines that comment anchors count, refuses inputs above a line and an edit bound, and divides the rows into changed regions with context and collapsible gaps, and arranges the rows of a region side by side, pairing a removed run with the added run that follows it. */

        metadata info : SoftwareInfo {
            description = "Compares the lines of a baseline and a head file with a shortest-edit-script algorithm, numbers the rows on both sides like the lines that comment anchors count, refuses inputs above a line and an edit bound, and divides the rows into changed regions with context and collapsible gaps, and arranges the rows of a region side by side, pairing a removed run with the added run that follows it.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/LineDiff.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/LineDiffTests.cs");
            designFiles = ("docs/design/review-center/core/line-diff.md");
            verificationFiles = ("docs/verification/review-center/core/line-diff.md");
            requirementFiles = ("docs/reqstream/review-center/core/line-diff.yaml");
        }
    }
}
