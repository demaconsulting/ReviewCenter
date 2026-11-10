package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpStore {
        doc /* Persistence of the second factor: one enrollment for each user, a pending enrollment that concurrent starts replace without error and never over a confirmed one, and the hashes of its recovery codes, with every race (the next time step, the spending of a recovery code, the confirmation of a setup) decided by one conditional change, and the audit entry written in the same transaction. */

        metadata info : SoftwareInfo {
            description = "Persistence of the second factor: one enrollment for each user, a pending enrollment that concurrent starts replace without error and never over a confirmed one, and the hashes of its recovery codes, with every race (the next time step, the spending of a recovery code, the confirmation of a setup) decided by one conditional change, and the audit entry written in the same transaction.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Storage/TotpStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Storage/TotpStoreTests.cs");
            designFiles = ("docs/design/review-center/storage/totp-store.md");
            verificationFiles = ("docs/verification/review-center/storage/totp-store.md");
            requirementFiles = ("docs/reqstream/review-center/storage/totp-store.yaml");
        }
    }
}
