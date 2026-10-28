package ReviewCenter {
    part def UserCommand {
        doc /* Handler of the user add verb: creates a user in the database, taking the password from an environment variable, standard input, or a masked prompt, never from the command line, so the first system administrator (the --system-admin switch; without it the user is an ordinary user) can be created before anybody can sign in. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/UserCommand.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/UserCommandTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/user-command.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/user-command.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/user-command.yaml */
    }
}
