package ReviewCenter {
    part def HostingSubsystem {
        doc /* Web server composition: listen address, endpoints, structured logging, database wiring, and the authentication pipeline for the serve verb, and the user add verb that creates users. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/HostingSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting.yaml */

        part serverHost : ServerHost;
        part userCommand : UserCommand;
    }
}
