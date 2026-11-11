package ReviewCenterPuppet {
    private import SoftwareItemMetadata::*;

    part def PuppetProgram {
        doc /* Entry point and client of the Puppet tool; parses one command, starts the daemon or sends the command to it, and prints the result. */

        metadata info : SoftwareInfo {
            sourceFiles = ("tools/puppet/PuppetProgram.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Puppet.Tests/PuppetProgramTests.cs");
            designFiles = ("docs/design/review-center-puppet/puppet-program.md");
            verificationFiles = ("docs/verification/review-center-puppet/puppet-program.md");
            requirementFiles = ("docs/reqstream/review-center-puppet/puppet-program.yaml");
        }
    }
}
