package ReviewCenterPuppet {
    private import SoftwareItemMetadata::*;

    part def ControlChannel {
        doc /* The control file and the loopback TCP protocol (one JSON line per request and response) between the puppet command and the daemon. */

        metadata info : SoftwareInfo {
            description = "The control file and the loopback TCP protocol (one JSON line per request and response) between the puppet command and the daemon.";
            sourceFiles = ("tools/puppet/ControlChannel.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Puppet.Tests/ControlChannelTests.cs");
            designFiles = ("docs/design/review-center-puppet/control-channel.md");
            verificationFiles = ("docs/verification/review-center-puppet/control-channel.md");
            requirementFiles = ("docs/reqstream/review-center-puppet/control-channel.yaml");
        }
    }
}
