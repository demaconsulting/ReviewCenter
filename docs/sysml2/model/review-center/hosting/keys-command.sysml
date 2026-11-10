package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def KeysCommand {
        doc /* Handler of the keys reprotect verb: after a secret scan shows that every stored secret can be read, rolls a new Data Protection key with the configured protection and protects every stored repository token and second-factor secret again with it, one transaction per table, and reports the counts, the keys that are still referenced and the next steps. It is the only verb that writes the key ring and these stored secret columns, refuses without any change when a secret cannot be read, and never prints a secret. */

        metadata info : SoftwareInfo {
            description = "Handler of the keys reprotect verb: after a secret scan shows that every stored secret can be read, rolls a new Data Protection key with the configured protection and protects every stored repository token and second-factor secret again with it, one transaction per table, and reports the counts, the keys that are still referenced and the next steps. It is the only verb that writes the key ring and these stored secret columns, refuses without any change when a secret cannot be read, and never prints a secret.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Hosting/KeysCommand.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Hosting/KeysCommandTests.cs");
            designFiles = ("docs/design/review-center/hosting/keys-command.md");
            verificationFiles = ("docs/verification/review-center/hosting/keys-command.md");
            requirementFiles = ("docs/reqstream/review-center/hosting/keys-command.yaml");
        }
    }
}
