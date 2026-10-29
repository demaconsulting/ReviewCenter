### TotpAlgorithm

#### Verification Approach

`TotpAlgorithm` is verified with unit tests defined in `TotpAlgorithmTests.cs`. The tests create the class directly
and call it with a fixed instant, so no clock is needed and no mocking is applied. The library Otp.NET inside the unit
is not trusted: every code is compared with the published vectors of RFC 4226 and RFC 6238 and with `TotpOracle`,
an independent reference in the test project. `TotpOracle` computes HOTP and TOTP with only the HMAC-SHA-1 of the
runtime and has its own Base32 reader and writer; its own correctness is first shown against the published values of
RFC 4226. See the OTS verification of Otp.NET for the package. The `ReviewCenter_Totp` check of the self-validation
repeats the published vectors on the installed platform; it is verified in the `ValidationChecks` unit.

#### Test Environment

N/A - standard test environment. The tests need no database, no files, and no network.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The reference reproduces the ten published HOTP values of RFC 4226 Appendix D.
- The production code reproduces the six published SHA-1 vectors of RFC 6238 Appendix B, and gives the same six digit
  code as the reference for 200 random secrets and instants.
- A code of the current step or of the step before or after it is accepted and the step that is reported is
  `floor(unix time / 30)` plus the offset; a code two steps away, and a code that is not exactly six ASCII digits
  (missing, empty, short, long, with a letter or a space, or with other digit characters), is refused and reports step
  zero.
- A generated secret has 160 bits and differs between calls; its Base32 text has 32 symbols without padding and gives
  the same bytes through the production reader and the reference.
- Typed Base32 text with spaces, hyphens, and lower case is read; missing, empty, and invalid text gives nothing.
- The setup address starts with `otpauth://totp/`, holds the issuer in the label and in the parameters, the secret,
  and the parameters SHA-1, 6 digits, and 30 seconds.
- Missing arguments and a digit count outside 6 to 8 are refused.

#### Test Scenarios

**TotpOracle_Hotp_Rfc4226Values_MatchThePublishedTable**: The reference computes the HOTP values for the counters 0 to
9 of the published secret; they equal the table of RFC 4226 Appendix D, which makes the reference a trustworthy
oracle. This scenario is tested by `TotpOracle_Hotp_Rfc4226Values_MatchThePublishedTable`.

**TotpAlgorithm_ComputeCode_Rfc6238Vectors_MatchThePublishedTable**: The six published eight digit vectors are
computed with the production code and with the reference; both equal the published codes. This scenario is tested by
`TotpAlgorithm_ComputeCode_Rfc6238Vectors_MatchThePublishedTable`.

**TotpAlgorithm_ComputeCode_ManyRandomSecrets_MatchTheReference**: With a fixed seed, 200 random secrets are used at
random instants up to the year 2096; the six digit code of the production code equals that of the reference every time.
This scenario is tested by `TotpAlgorithm_ComputeCode_ManyRandomSecrets_MatchTheReference`.

**TotpAlgorithm_TryVerify_MatchedStep_IsFloorOfUnixTimeOverThirty**: A theory over the offsets -1, 0, and 1; the code
of that step is computed by the reference and verified in the middle of a step. It is accepted and the reported step is
the independently computed one, and `StepOf` returns the current step. This scenario is tested by
`TotpAlgorithm_TryVerify_MatchedStep_IsFloorOfUnixTimeOverThirty`.

**TotpAlgorithm_TryVerify_OutsideTheWindow_IsRefused**: The codes two steps before and after the current one (chosen
so that they differ from the codes of the window) are verified; both are refused. This scenario is tested by
`TotpAlgorithm_TryVerify_OutsideTheWindow_IsRefused`.

**TotpAlgorithm_TryVerify_MalformedCode_IsRefused**: A theory over a null code, an empty code, five digits, seven
digits, a letter, a leading space, and Arabic-Indic digits; each is refused with step zero. This scenario is tested by
`TotpAlgorithm_TryVerify_MalformedCode_IsRefused`.

**TotpAlgorithm_GenerateSecret_Is160BitsAndRoundTripsThroughBase32**: Two secrets are generated and each is written to
Base32 and read back with the production code and with the reference; the secrets have 20 bytes and differ, the text
has 32 symbols without `=`, and every way round gives the same bytes and the same text. This scenario is tested by
`TotpAlgorithm_GenerateSecret_Is160BitsAndRoundTripsThroughBase32`.

**TotpAlgorithm_TryFromBase32_TypedText_IsToleratedAndNonsenseIsRefused**: The Base32 text of the published secret is
read in its canonical form and as a person types it (lower case, spaces, and hyphens); both give the published
secret. A null text, an empty text, text with a symbol that is not Base32, and `018` give nothing. This scenario is
tested by `TotpAlgorithm_TryFromBase32_TypedText_IsToleratedAndNonsenseIsRefused`.

**TotpAlgorithm_BuildUri_HasTheOtpauthForm**: An address is built for an issuer with a space and an account name with an
at sign; it has the scheme, the issuer in the label and as a parameter (`Review%20Center`), the secret, and the
parameters `algorithm=SHA1`, `digits=6`, and `period=30`. This scenario is tested by
`TotpAlgorithm_BuildUri_HasTheOtpauthForm`.

**TotpAlgorithm_InvalidArguments_Throw**: Null secrets, a null issuer, and a null account name raise
`ArgumentNullException`; digit counts of 5 and 9 raise `ArgumentOutOfRangeException`. This scenario is tested by
`TotpAlgorithm_InvalidArguments_Throw`.
