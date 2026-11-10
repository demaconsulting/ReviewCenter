package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def CliSubsystem {
        doc /* Command-line argument parsing, output-channel management, and exit-code tracking, including the user add verb and its --system-admin switch. */

        metadata info : SoftwareInfo {
            description = "Command-line argument parsing, output-channel management, and exit-code tracking, including the user add verb and its --system-admin switch.";
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Cli/CliSubsystemTests.cs");
            designFiles = ("docs/design/review-center/cli.md");
            verificationFiles = ("docs/verification/review-center/cli.md");
            requirementFiles = ("docs/reqstream/review-center/cli.yaml");
        }

        part context : Context;
    }
}
