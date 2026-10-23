package ReviewCenterPuppet {
    doc
    /*
     * ReviewCenterPuppet is the Puppet development tool: a command-line program that lets a
     * developer or coding agent start the ReviewCenter web application in a headless browser
     * and drive it with short commands (start, goto, click, fill, text, screenshot, snapshot,
     * stop, list). It is built, tested, and reviewed in this repository but is not part of the
     * distributed ReviewCenter software package.
     */

    part def ReviewCenterPuppetSystem {
        doc /* The Puppet development tool as a whole; a separate software system, not distributed. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/PuppetSystemTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet.yaml */

        part puppetProgram : PuppetProgram;
        part commandLine : CommandLine;
        part selectorResolver : SelectorResolver;
        part sessionStore : SessionStore;
        part controlChannel : ControlChannel;
        part commandExecutor : CommandExecutor;
        part daemonHost : DaemonHost;
        part daemonLauncher : DaemonLauncher;
    }
}
