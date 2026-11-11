package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def TotpChallengeStore {
        doc /* In-memory pending sign-in of a user who typed the right password and still owes the second factor: a random token of which only the hash is kept, bound to the user and the client address, ended after five minutes or five wrong codes, given to one request at a time, dropped for a user on request (also for a challenge that is being checked, which is then not given back), and limited to 10000 challenges, also when a challenge is given back, by dropping the ended ones and then the oldest. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/TotpChallengeStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/TotpChallengeStoreTests.cs");
            designFiles = ("docs/design/review-center/core/totp-challenge-store.md");
            verificationFiles = ("docs/verification/review-center/core/totp-challenge-store.md");
            requirementFiles = ("docs/reqstream/review-center/core/totp-challenge-store.yaml");
        }
    }
}
