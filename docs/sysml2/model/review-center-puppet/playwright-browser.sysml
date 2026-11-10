package ReviewCenterPuppet {
    private import SoftwareItemMetadata::*;

    part def PlaywrightBrowser {
        doc /* Launches headless Chromium through Playwright and performs one browser command at a time. */

        metadata info : SoftwareInfo {
            description = "Launches headless Chromium through Playwright and performs one browser command at a time.";
            sourceFiles = ("tools/puppet/PlaywrightBrowser.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Puppet.Tests/PlaywrightBrowserTests.cs");
            designFiles = ("docs/design/review-center-puppet/playwright-browser.md");
            verificationFiles = ("docs/verification/review-center-puppet/playwright-browser.md");
            requirementFiles = ("docs/reqstream/review-center-puppet/playwright-browser.yaml");
        }
    }
}
