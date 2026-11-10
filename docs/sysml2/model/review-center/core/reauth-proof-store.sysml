package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ReauthProofStore {
        doc /* In-memory single-use proof that a user of an identity provider has just confirmed their identity for one signature: bound to the user, project, review and slot, ended after five minutes, spent by the first successful consume, and limited to 10000 proofs by dropping the ended ones and then the oldest. */

        metadata info : SoftwareInfo {
            description = "In-memory single-use proof that a user of an identity provider has just confirmed their identity for one signature: bound to the user, project, review and slot, ended after five minutes, spent by the first successful consume, and limited to 10000 proofs by dropping the ended ones and then the oldest.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/ReauthProofStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/ReauthProofStoreTests.cs");
            designFiles = ("docs/design/review-center/core/reauth-proof-store.md");
            verificationFiles = ("docs/verification/review-center/core/reauth-proof-store.md");
            requirementFiles = ("docs/reqstream/review-center/core/reauth-proof-store.yaml");
        }
    }
}
