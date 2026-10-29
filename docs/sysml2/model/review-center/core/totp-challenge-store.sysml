package ReviewCenter {
    part def TotpChallengeStore {
        doc /* In-memory pending sign-in of a user who typed the right password and still owes the second factor: a random token of which only the hash is kept, bound to the user and the client address, ended after five minutes or five wrong codes, given to one request at a time, dropped for a user on request, and limited to 10000 challenges by dropping the ended ones and then the oldest. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/TotpChallengeStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/TotpChallengeStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/core/totp-challenge-store.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/totp-challenge-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/totp-challenge-store.yaml */
    }
}
