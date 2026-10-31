package ReviewCenter {
    part def KeysCommand {
        doc /* Handler of the keys reprotect verb: after a secret scan shows that every stored secret can be read, rolls a new Data Protection key with the configured protection and protects every stored repository token and second-factor secret again with it, one transaction per table, and reports the counts, the keys that are still referenced and the next steps. It is the only verb that writes keys and secrets, refuses without any change when a secret cannot be read, and never prints a secret. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/KeysCommand.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/KeysCommandTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/keys-command.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/keys-command.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/keys-command.yaml */
    }
}
