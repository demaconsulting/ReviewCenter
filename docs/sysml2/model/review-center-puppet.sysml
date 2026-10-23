package ReviewCenterPuppet {
    doc
    /*
     * ReviewCenterPuppet is the Puppet development tool: a small command-line program that runs a
     * steps file (go to a page, click, fill, read text, screenshot) against the ReviewCenter web
     * application in a headless browser, so a coding agent can see the UI. It is built, tested, and
     * reviewed in this repository but is not part of the distributed ReviewCenter software package.
     */

    part def ReviewCenterPuppetSystem {
        doc /* The Puppet development tool as a whole; a separate software system, not distributed. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/PuppetSystemTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet.yaml */

        part puppetProgram : PuppetProgram;
        part stepRunner : StepRunner;
        part selectorResolver : SelectorResolver;
        part serverProcess : ServerProcess;
        part playwrightBrowser : PlaywrightBrowser;
    }
}
