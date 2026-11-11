package ReviewCenterPuppet {
    private import SoftwareItemMetadata::*;

    part def SelectorResolver {
        doc /* Turns user selector text into a Playwright selector; a plain name means a data-testid. */

        metadata info : SoftwareInfo {
            sourceFiles = ("tools/puppet/SelectorResolver.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Puppet.Tests/SelectorResolverTests.cs");
            designFiles = ("docs/design/review-center-puppet/selector-resolver.md");
            verificationFiles = ("docs/verification/review-center-puppet/selector-resolver.md");
            requirementFiles = ("docs/reqstream/review-center-puppet/selector-resolver.yaml");
        }
    }
}
