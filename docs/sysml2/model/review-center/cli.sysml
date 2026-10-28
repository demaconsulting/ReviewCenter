package ReviewCenter {
    part def CliSubsystem {
        doc /* Command-line argument parsing, output-channel management, and exit-code tracking, including the user add verb and its --system-admin switch. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Cli/CliSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/cli.md */
        comment verificationRef /* Verification: docs/verification/review-center/cli.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/cli.yaml */

        part context : Context;
    }
}
