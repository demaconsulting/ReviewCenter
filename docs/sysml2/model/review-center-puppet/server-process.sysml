package ReviewCenterPuppet {
    part def ServerProcess {
        doc /* Starts the ReviewCenter server on a free loopback port, waits for it to be healthy, and stops it. */

        comment sourceRef /* Source: tools/puppet/ServerProcess.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/ServerProcessTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet/server-process.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet/server-process.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet/server-process.yaml */
    }
}
