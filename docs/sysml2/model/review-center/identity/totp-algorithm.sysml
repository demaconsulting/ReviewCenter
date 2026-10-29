package ReviewCenter {
    part def TotpAlgorithm {
        doc /* The one-time codes of the second factor by RFC 6238 (SHA-1, six digits, thirty second steps): creates a 160-bit secret, reads and writes it as base32 text, builds the otpauth address for an authenticator app, computes the code of a step, and checks a typed code within one step either side, reporting the step that matched. The only unit that uses the Otp.NET library. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/TotpAlgorithm.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/TotpAlgorithmTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/totp-algorithm.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/totp-algorithm.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/totp-algorithm.yaml */
    }
}
