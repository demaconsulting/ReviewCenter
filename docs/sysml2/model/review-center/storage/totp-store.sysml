package ReviewCenter {
    part def TotpStore {
        doc /* Persistence of the second factor: one enrollment for each user and the hashes of its recovery codes, with every race (the next time step, the spending of a recovery code, the confirmation of a setup) decided by one conditional change, and the audit entry written in the same transaction. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Storage/TotpStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Storage/TotpStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/storage/totp-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/storage/totp-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/storage/totp-store.yaml */
    }
}
