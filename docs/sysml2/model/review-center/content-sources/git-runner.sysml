package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def GitRunner {
        doc /* Runs the git command as a child process without a shell, with an argument list, a curated environment, and an access token passed only through the environment of the child process, redacts secrets from output and errors, enforces a timeout that ends the whole process tree, and checks the installed git version. Also defines the git settings, whose timeout (`Git:TimeoutSeconds`) is validated at start-up to 1 to 3600 seconds. */

        metadata info : SoftwareInfo {
            description = "Runs the git command as a child process without a shell, with an argument list, a curated environment, and an access token passed only through the environment of the child process, redacts secrets from output and errors, enforces a timeout that ends the whole process tree, and checks the installed git version. Also defines the git settings, whose timeout (`Git:TimeoutSeconds`) is validated at start-up to 1 to 3600 seconds.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/ContentSources/GitRunner.cs", "src/DemaConsulting.ReviewCenter/ContentSources/GitOptions.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/ContentSources/GitRunnerTests.cs");
            designFiles = ("docs/design/review-center/content-sources/git-runner.md");
            verificationFiles = ("docs/verification/review-center/content-sources/git-runner.md");
            requirementFiles = ("docs/reqstream/review-center/content-sources/git-runner.yaml");
        }
    }
}
