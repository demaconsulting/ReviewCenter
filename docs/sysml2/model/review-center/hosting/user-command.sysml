package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def UserCommand {
        doc /* Handler of the user add verb: creates a user in the database, taking the password from an environment variable, standard input, or a masked prompt, never from the command line, so the first system administrator (the --system-admin switch; without it the user is an ordinary user) can be created before anybody can sign in. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Hosting/UserCommand.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Hosting/UserCommandTests.cs");
            designFiles = ("docs/design/review-center/hosting/user-command.md");
            verificationFiles = ("docs/verification/review-center/hosting/user-command.md");
            requirementFiles = ("docs/reqstream/review-center/hosting/user-command.yaml");
        }
    }
}
