## Otp.NET Verification

This document provides the verification evidence for the Otp.NET OTS software item. Requirements for this OTS item are
defined in the Otp.NET OTS Software Requirements document.

### Required Functionality

Otp.NET computes the time-based one-time password of RFC 6238 with HMAC-SHA-1, which is the same code that every
authenticator app shows for the same secret and time. It reports which time step a verified code belongs to, and it
converts a secret to and from Base32. A passing test confirms that the second factor of the sign-in accepts the codes
of the apps of the people and that the replay protection stores the real time step.

### Verification Approach

Otp.NET has no self-validation mode and is verified by the project's own tests, which call it only through the
`TotpAlgorithm` unit of the Identity subsystem. The tests do not trust the library. They compare its codes with the
published vectors of RFC 6238 and with an independent reference (`TotpOracle` in the test project), which implements
HOTP and TOTP from RFC 4226 and RFC 6238 with only the HMAC-SHA-1 of the runtime and has its own Base32 reader and
writer. The reference is itself checked against the published values of RFC 4226. The self-validation check
`ReviewCenter_Totp` repeats the published vectors on the installed platform. Each scenario names a specific test method
that depends on the package under verification. A passing pipeline run for the scenario constitutes evidence that the
requirement is satisfied.

### Test Scenarios

#### TotpAlgorithm_ComputeCode_Rfc6238Vectors_MatchThePublishedTable

**Scenario**: The six published SHA-1 vectors of RFC 6238 Appendix B (eight digits, secret `12345678901234567890`) are
computed with the product and with the reference.

**Expected**: Both give exactly the published codes, so the library computes the code that every app computes.

**Requirement coverage**: `ReviewCenter-OTS-OtpNet-Totp`.

#### TotpAlgorithm_ComputeCode_ManyRandomSecrets_MatchTheReference

**Scenario**: 200 random secrets of 20 bytes and random instants (from a fixed seed) are used to compute six digit
codes with the product and with the reference.

**Expected**: No code differs, also for the codes that start with zero.

**Requirement coverage**: `ReviewCenter-OTS-OtpNet-Totp`.

#### TotpAlgorithm_TryVerify_MatchedStep_IsFloorOfUnixTimeOverThirty

**Scenario**: A theory over the offsets -1, 0, and 1; the code of the step at the offset is computed by the reference
and verified at an instant in the middle of a step.

**Expected**: The code is accepted and the step that the library reports equals the independently computed
`floor(unix time / 30)` plus the offset.

**Requirement coverage**: `ReviewCenter-OTS-OtpNet-Totp`.

#### TotpAlgorithm_GenerateSecret_Is160BitsAndRoundTripsThroughBase32

**Scenario**: Two secrets are generated, encoded to Base32 and decoded again with the product and with the reference.

**Expected**: Each secret has 20 bytes, the two differ, the text has 32 symbols without padding, and every way round
gives the same bytes and the same text.

**Requirement coverage**: `ReviewCenter-OTS-OtpNet-Totp`.

#### ValidationChecks_Totp_Run_PassesWithRfcVectors

**Scenario**: The `ReviewCenter_Totp` check of the self-validation is run.

**Expected**: The check computes the published vectors, the window of one step, and the Base32 form, and reports a
result that names RFC 6238.

**Requirement coverage**: `ReviewCenter-OTS-OtpNet-Totp`.
