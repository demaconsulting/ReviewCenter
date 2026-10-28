package ReviewCenter {
    part def ReauthProofStore {
        doc /* In-memory single-use proof that a user of an identity provider has just confirmed their identity for one signature: bound to the user, project, review and slot, ended after five minutes, spent by the first successful consume, and limited to 10000 proofs by dropping the ended ones and then the oldest. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ReauthProofStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ReauthProofStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/core/reauth-proof-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/reauth-proof-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/reauth-proof-store.yaml */
    }
}
