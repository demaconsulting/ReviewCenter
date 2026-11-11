package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def Program {
        doc /* Entry point and execution orchestrator. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Program.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/ProgramTests.cs");
            designFiles = ("docs/design/review-center/program.md");
            verificationFiles = ("docs/verification/review-center/program.md");
            requirementFiles = ("docs/reqstream/review-center/program.yaml");
        }
    }
}
