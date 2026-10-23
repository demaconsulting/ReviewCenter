package ReviewCenterPuppet {
    part def ControlChannel {
        doc /* Loopback-only, token-authenticated newline-delimited JSON channel between client commands and the daemon. */

        comment sourceRef /* Source: tools/puppet/ControlProtocol.cs, tools/puppet/ControlServer.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/ControlChannelTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet/control-channel.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet/control-channel.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet/control-channel.yaml */
    }
}
