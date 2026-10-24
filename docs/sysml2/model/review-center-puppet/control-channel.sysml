package ReviewCenterPuppet {
    part def ControlChannel {
        doc /* The control file and the loopback TCP protocol (one JSON line per request and response) between the puppet command and the daemon. */

        comment sourceRef /* Source: tools/puppet/ControlChannel.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/ControlChannelTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet/control-channel.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet/control-channel.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet/control-channel.yaml */
    }
}
