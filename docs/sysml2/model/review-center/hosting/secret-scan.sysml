package ReviewCenter {
    part def SecretScan {
        doc /* Read-only scan that tells whether the secrets stored in the database (repository tokens and second-factor secrets) can still be decrypted with the configured key ring. It counts the key files of the ring and how many are not encrypted, opens the database read-only (a missing database, table, or row means no secrets and nothing is created), unprotects every stored value with the production protectors and discards the plaintext at once, and returns counts and row ids only. It never protects a value and never asks a ring without key files, so it cannot make Data Protection create a key. The server uses it to refuse a start with a lost key ring, and the ConfiguredKeys self-validation check uses it to report on a restore. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/SecretScan.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/SecretScanTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/secret-scan.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/secret-scan.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/secret-scan.yaml */
    }
}
