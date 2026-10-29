## Otp.NET

This document describes the integration and usage design for the `Otp.NET` OTS software item (NuGet package
`Otp.NET`).

### Purpose

Otp.NET is a small library that computes one-time passwords. ReviewCenter uses it to compute and verify the
time-based one-time passwords of RFC 6238 for the optional second factor of local accounts, to create the random secret,
to convert the secret to and from Base32 text, and to build the `otpauth://` address that authenticator apps read. The
library is MIT licensed and has no dependencies of its own.

### Features Used

- The time-based one-time password (`Totp`) with HMAC-SHA-1, 6 digits, and a step of 30 seconds, to compute a code
- The verification with a window (`VerificationWindow`) of one step before and after, which reports the matched time
  step
- `KeyGeneration` for the random secret of 160 bits
- `Base32Encoding` to write and read the secret as Base32 text
- `OtpUri` to build the `otpauth://totp/` address with the issuer, the account name, and the parameters

### Integration Pattern

Only the `TotpAlgorithm` unit of the `Identity` subsystem references the package. The rest of the product, including
`Core`, uses the Core interface `ITotpAlgorithm`, so the library can be replaced without touching the rules. The unit
adds checks of its own around the library: a code must be exactly six ASCII digits, and the matched step must lie within
one step of the current one. The library knows nothing about replay protection, lockout, or recovery codes; those rules
belong to `TotpService`.

The package has no self-validation mode. The evidence that it behaves as the authenticator apps expect is the passing
tests of `TotpAlgorithm`, which compare the codes with the published RFC 6238 vectors and with an independent
implementation written in the tests, and the self-validation check `ReviewCenter_Totp`, which runs the same vectors at
installation. An upgrade of the package that changes a code shows as a failing test and a failing check.
