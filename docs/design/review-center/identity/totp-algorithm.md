### TotpAlgorithm

![Identity Structure](IdentityView.svg)

#### Purpose

`TotpAlgorithm` implements the Core port `ITotpAlgorithm`: the time-based one-time password algorithm of RFC 6238 with
HMAC-SHA-1, 6 digits, and 30 second steps. Its single responsibility is to create secrets, write and read them as Base32
text, build the address for an authenticator app, compute codes, and check a typed code against a secret for a moment.
It is the only unit that references the Otp.NET library (see the Otp.NET OTS item), which keeps `Core` free of it.

#### Data Model

The unit is stateless and thread-safe. It has two constants: `StepSeconds` (30) and `Digits` (6). SHA-1, 6 digits, and
30 seconds are fixed because every authenticator app supports them. A secret has 20 random bytes (160 bits), taken from
the random generator of the library.

**Base32**: a secret is written as upper-case Base32 text without padding, which is the form an app accepts. Reading
ignores spaces and hyphens and does not depend on the case, so a secret typed in groups is accepted.

**Time step**: the number of whole 30 second steps since 1970-01-01 UTC (`StepOf`).

**Verification window**: a code is accepted for the step of the moment, the step before, and the step after (plus or
minus one), which tolerates a clock that is a little off. The unit double-checks that the matched step is within one
step of the current one.

#### Key Methods

**GenerateSecret**: Returns a new secret of 20 random bytes. The caller owns the array and clears it after use.

**ToBase32**: Writes a secret as Base32 text. **TryFromBase32**: Reads Base32 text; returns null when the text is not
valid.

**BuildUri**: Builds the `otpauth://totp/` address for an app.

- *Parameters*: The secret, the issuer, and the account name.
- *Returns*: The address with the secret, algorithm SHA-1, 6 digits, and a period of 30 seconds. It contains the secret
  and is treated like the secret.

**ComputeCode**: Computes the code of a moment with 6 to 8 digits (other values raise `ArgumentOutOfRangeException`).
The 8-digit form exists for the RFC test vectors.

**TryVerify**: Checks a typed code.

- *Parameters*: The secret, the typed code, the current moment, and the matched step as an output.
- *Returns*: True when the code is exactly six ASCII digits and right for one of the three steps; the matched step is
  the step of the code, which is what the replay protection records. False otherwise, with the matched step 0.

**StepOf**: Returns the time step of a moment.

#### Error Handling

A refused code is a false result, never an exception. Null arguments raise `ArgumentNullException`. A secret and a
code are never logged or put into a message.

#### Dependencies

- **Otp.NET** — the one-time password computation, the Base32 coding, the key generation, and the address builder.
- **Core ITotpAlgorithm** — the interface implemented.

#### Callers

- **TotpService** — through the Core interface.
- **TotpCheck** (`SelfTest` subsystem) — checks the unit against the RFC 6238 vectors.
- **ServerHost** — registers the unit as the implementation of the Core interface.
