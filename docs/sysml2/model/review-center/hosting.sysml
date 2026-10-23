package ReviewCenter {
    part def HostingSubsystem {
        doc /* Web server composition: listen address, endpoints, and structured logging for the serve verb. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/HostingSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting.yaml */

        part serverHost : ServerHost;
    }
}
