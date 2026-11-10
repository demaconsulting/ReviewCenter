package ReviewCenterPuppet {
    private import SoftwareItemMetadata::*;

    part def BrowserDaemon {
        doc /* The long-lived process that owns the persistent browser, serves commands, and shuts down on stop. */

        metadata info : SoftwareInfo {
            description = "The long-lived process that owns the persistent browser, serves commands, and shuts down on stop.";
            sourceFiles = ("tools/puppet/BrowserDaemon.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Puppet.Tests/PuppetEndToEndTests.cs");
            designFiles = ("docs/design/review-center-puppet/browser-daemon.md");
            verificationFiles = ("docs/verification/review-center-puppet/browser-daemon.md");
            requirementFiles = ("docs/reqstream/review-center-puppet/browser-daemon.yaml");
        }
    }
}
