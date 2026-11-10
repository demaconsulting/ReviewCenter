package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def Context {
        doc /* Command-line argument parser and I/O owner for one tool invocation, including the user add verb and its --system-admin switch. */

        metadata info : SoftwareInfo {
            description = "Command-line argument parser and I/O owner for one tool invocation, including the user add verb and its --system-admin switch.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Cli/Context.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Cli/ContextTests.cs");
            designFiles = ("docs/design/review-center/cli/context.md");
            verificationFiles = ("docs/verification/review-center/cli/context.md");
            requirementFiles = ("docs/reqstream/review-center/cli/context.yaml");
        }
    }
}
