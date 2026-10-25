package ReviewCenter {
    part def GitRunner {
        doc /* Runs the git command as a child process without a shell, with an argument list, a curated environment, and an access token passed only through the environment of the child process, redacts secrets from output and errors, enforces a timeout that ends the whole process tree, and checks the installed git version. Also defines the git settings. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/ContentSources/GitRunner.cs and GitOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/ContentSources/GitRunnerTests.cs */
        comment designRef /* Design: docs/design/review-center/content-sources/git-runner.md */
        comment verificationRef /* Verification: docs/verification/review-center/content-sources/git-runner.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/content-sources/git-runner.yaml */
    }
}
