package ReviewCenterPuppet {
    part def DaemonHost {
        doc /* Long-lived session process that owns the server, the browser, and the control channel. */

        comment sourceRef /* Source: tools/puppet/DaemonHost.cs, tools/puppet/ServerProcessRunner.cs, tools/puppet/PlaywrightBrowser.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/DaemonHostTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet/daemon-host.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet/daemon-host.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet/daemon-host.yaml */
    }
}
