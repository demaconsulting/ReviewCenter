package ReviewCenterPuppet {
    part def BrowserDaemon {
        doc /* The long-lived process that owns the persistent browser, serves commands, and shuts down on stop. */

        comment sourceRef /* Source: tools/puppet/BrowserDaemon.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/PuppetEndToEndTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet/browser-daemon.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet/browser-daemon.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet/browser-daemon.yaml */
    }
}
