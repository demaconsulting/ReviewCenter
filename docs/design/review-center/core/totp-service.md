### TotpService

![Core Structure](CoreView.svg)

#### Purpose

`TotpService` owns the rules for the optional second factor of local password accounts: a time-based one-time password
(TOTP) from an authenticator app, with recovery codes. It sets the factor up with a password and a first code, turns it
off, issues new recovery codes, lets a system administrator reset it, starts and completes the second step of a
sign-in, and checks the code that a signature needs. It is the one place where these rules live, so the pages, the
sign-in, and the signing service get the same answer. It is also the home of the supporting types of the second factor:
`UserTotp`, `TotpRecoveryCode`, `TotpSettings`, `TotpEnrollment`, `TotpStatus`, `TotpSignInOutcome`, and
`TotpSignInResult`, the interfaces `ITotpStore`, `ITotpAlgorithm`, and `ITotpSecretProtector`, and the second factor
rules of `DomainRule`. The service is a singleton.

#### Data Model

**Algorithm**: HMAC-SHA-1 with 6 digits and 30 second steps, which every authenticator app supports. The secret has 160
bits. A code is accepted for the current step and one step before and after (a window of plus or minus one). Core uses
the algorithm only through `ITotpAlgorithm`, implemented in the `Identity` subsystem (see `TotpAlgorithm`), so Core stays
free of libraries.

**UserTotp**: the enrollment of one user: `UserId`, `ProtectedSecret` (encrypted, never the secret), `Created`,
`ConfirmedAt` (null while the enrollment is pending), and `LastAcceptedStep` (the replay state). `IsActive` is true when
`ConfirmedAt` is set. A pending enrollment is ignored by sign-in. **TotpRecoveryCode**: `Id`, `UserId`, `CodeHash`,
`Created`, and `UsedAt`.

**TotpSettings**: a record of `Enabled` and `Issuer` (the name shown in the authenticator app; `DefaultIssuer` is
`ReviewCenter`). It comes from the configuration of the server (see `TotpSignIn`). A service built without settings
treats the feature as enabled.

**ITotpStore**: the persistence interface, implemented by `TotpStore` in the `Storage` subsystem. Each method that
decides a race is one conditional change whose result tells whether this caller made it (see `TotpStore`).
**ITotpAlgorithm**: `GenerateSecret`, `ToBase32`, `TryFromBase32`, `BuildUri`, `ComputeCode`, `TryVerify` (returns the
matched step), and `StepOf`. **ITotpSecretProtector**: `Protect` and `Unprotect` of the secret of a user, bound to the
user; unreadable data is reported as `TotpSecretUnreadable`.

**TotpEnrollment**: the answer of `BeginEnrollmentAsync`: `SecretBase32` and `OtpauthUri`, shown once. There is no QR
code; the person types the secret or pastes the address into the app. **TotpStatus**: `FeatureEnabled`, `Available`
(the account may use the feature), `Enrolled`, and `RecoveryCodesLeft`.

**Recovery codes**: ten codes of 16 characters from an alphabet of 32 symbols (80 bits each), shown as four groups of
four characters separated by hyphens. Only a hash is stored: the SHA-256 value, in hexadecimal, of the text
`ReviewCenter.TotpRecovery.v1:`, the user identity, a colon, and the normalized code. Typed input is normalized: hyphens
and spaces are removed, case is ignored, and the letters O, I, and L are read as 0, 1, and 1. A recovery code is
accepted at sign-in and to turn the factor off. It is never accepted for a signature or to issue new recovery codes.

**Replay protection**: a code is accepted only when its time step is greater than `LastAcceptedStep`, and
`ITotpStore.TryAdvanceStepAsync` records it in one compare-and-set statement. A code can therefore be used once, and
two requests with the same code cannot both succeed. A code that was used to sign in cannot be used again for a
signature in the same 30 second step: the person waits for the next code.

**Who may use it**: a local account that is enabled and has no temporary password (`MustChangePassword`). An external
account has its own second factor at the provider, a personal access token actor cannot change it, and a temporary
password must be changed first. Otherwise the operations report `TotpNotAvailable`.

**Challenge**: after a right password `BeginSignIn` issues a token for `TotpChallengeStore` (5 minutes, 5 wrong
codes, bound to user, address, and return address). `CompleteSignInAsync` takes the challenge, so a token is tried by
one request at a time.

**Lockout**: the codes share the failure counter and the lockout of the passwords (5 failures, 5 minutes), kept by
`UserService`. A wrong code counts like a wrong password; a complete sign-in resets the counter.

**Second step outcome**: `TotpSignInOutcome` has `Success`, `Failed`, `ChallengeInvalid`, and `LockedOut`.
`TotpSignInResult` carries the outcome, the `User` on success, `UsedRecoveryCode`, and `RecoveryCodesLeft`.

**DomainRule values** added for the second factor: `TotpNotAvailable`, `TotpAlreadyEnrolled`, `TotpNotEnrolled`,
`TotpCodeIncorrect`, `TotpEnrollmentExpired`, and `TotpSecretUnreadable`.

#### Key Methods

**GetStatusAsync**: Reads the status of the signed-in user. For an account that cannot use the feature, or when the
feature is off, `Available` is false.

**ListEnrolledUserIdsAsync**: Reads the identities of the users with a confirmed factor, for the user page.
**IsEnrolledAsync**: Tells whether a user name has a confirmed factor; false when the feature is off.

**BeginEnrollmentAsync**: Starts the setup.

- *Parameters*: The `Actor`, the password, and a cancellation token.
- *Returns*: `Task<TotpEnrollment>` — the new secret as Base32 text and as an address for the app.
- *Postconditions*: The password is checked with `UserService.ConfirmPasswordAsync` (a wrong password counts toward the
  lockout and is reported as `CurrentPasswordIncorrect`). An enrollment that is already confirmed is refused with
  `TotpAlreadyEnrolled`. The secret is stored encrypted as a pending enrollment, replacing an earlier pending one. The
  secret is returned only here and is not recorded anywhere else.

**ConfirmEnrollmentAsync**: Finishes the setup with the first code.

- *Parameters*: The `Actor`, the code, and a cancellation token.
- *Returns*: `Task<IReadOnlyList<string>>` — the ten recovery codes, shown once.
- *Postconditions*: A pending enrollment is required (`TotpNotEnrolled`) and lives 15 minutes (`TotpEnrollmentExpired`,
  (`TotpEnrollmentExpired`, and the person starts the setup again). A locked account is refused like a wrong code. A
  wrong or reused code counts toward the
  lockout, is recorded as `auth.totp-failed` (enrollment), and is reported as `TotpCodeIncorrect`. A right code
  confirms the row, stores the recovery code hashes, and records `auth.totp-enrolled` in one transaction. The failure
  counter is cleared and the challenges of the user are invalidated.

**CancelEnrollmentAsync**: Removes a pending enrollment of the actor.

**DisableAsync**: Turns the factor off.

- *Parameters*: The `Actor`, the password, a code or a recovery code, and a cancellation token.
- *Postconditions*: Both proofs are required. The password is checked first, then the code. The factor and its recovery
  codes are removed and `auth.totp-disabled` is recorded in one transaction (a spent recovery code is recorded as
  `auth.totp-recovery-used`). The challenges of the user are invalidated. Sessions that are already open stay valid.

**RegenerateRecoveryCodesAsync**: Replaces the recovery codes.

- *Parameters*: The `Actor`, the password, an authenticator code, and a cancellation token.
- *Returns*: The ten new codes, shown once. The old codes stop working. A recovery code is not accepted here.
- *Postconditions*: `auth.totp-recovery-regenerated` is recorded in the same transaction.

**ChangeOwnPasswordAsync**: Changes the own password; with a second factor on it needs a code too.

- *Parameters*: The `Actor`, the current password, the new password, a code or a recovery code (may be null), and a
  cancellation token.
- *Postconditions*: A personal access token actor is refused (`ActorNotAllowed`). The rule is enforced in
  `UserService.ChangeOwnPasswordCoreAsync`, which gets a verifier from this service: for an active enrollment the code
  is checked as at sign-in (replay rule, recovery codes allowed, an unreadable secret counts as a wrong code), a missing
  or wrong code counts on the shared failure counter and is reported as `TotpCodeIncorrect`, and the counter is cleared
  only after the password, the new password, and the code were all right. A recovery code that was spent is recorded as
  `auth.totp-recovery-used`. A user without an enrollment, with a temporary password, or while the feature is switched
  off changes the password as before, with no code.

**ResetAsync**: Removes the factor of another user, for a person who lost the phone and the recovery codes.

- *Parameters*: The administrator as `Actor`, the user name, and a cancellation token.
- *Postconditions*: Only an enabled system administrator without a temporary password may do it, and not with a personal
  access token (`ActorNotAllowed`). It works while the feature is switched off. The factor is removed, the challenges
  of the user are invalidated, and `auth.totp-reset` is recorded with the administrator as actor, in one transaction.
  Sessions that are already open stay valid.

**BeginSignIn**: Starts the second step after a right password.

- *Parameters*: The `User`, the client address, and the return address.
- *Returns*: The challenge token for the cookie.

**PeekChallenge**: Reads a challenge without using it (the login page). **CompleteSignInAsync**: Completes the second
step.

- *Parameters*: The token, the client address, the code, and a cancellation token.
- *Returns*: `Task<TotpSignInResult>`.
- *Postconditions*: The challenge is taken first (`ChallengeInvalid` when there is none). The user and the row are read
  again: a missing, disabled, or external user, an inactive row, or a feature that is off gives `ChallengeInvalid`. A
  locked account gives `LockedOut` without counting. A wrong code is counted through
  `UserService.RegisterSecondFactorFailureAsync` and recorded as `auth.totp-failed` (sign-in); when it starts the
  lockout the result is `LockedOut`, otherwise the challenge is returned (`Failed`) unless the user was invalidated while
  the code was checked (see `TotpChallengeStore.Return`). A right code clears the failure
  counter and gives `Success`.

**Checking a code** (private): a code of exactly six digits is an authenticator code: the secret is decrypted, checked
with `TryVerify`, and the matched step is recorded with `TryAdvanceStepAsync`; the secret array is cleared afterwards.
An unreadable secret counts as a wrong code, so a recovery code still works. Any other text is normalized as a recovery
code (where recovery codes are allowed) and spent with `TryConsumeRecoveryCodeAsync`.

**VerifyForSignatureAsync** (internal): Checks the code of a signature for `SigningService`. Only an authenticator code
is accepted. A wrong or reused code is counted through `RegisterSecondFactorFailureAsync` and recorded as
`signature.reauth-failed` with the detail `wrong code`; a right code clears the counter.

#### Error Handling

A violated rule raises `DomainRuleException` with the rule value named above. The message for a wrong code is "The code
is not valid or was already used." and never says which part was wrong. An unreadable secret raises
`TotpSecretUnreadable` with a message that tells the administrator to reset the factor. A null argument raises
`ArgumentNullException`. Store failures propagate unchanged. A secret, a code, a recovery code, and a token are never
logged, audited, or put into a message.

#### Dependencies

- **ITotpStore** — persists enrollments and recovery codes (injected).
- **ITotpAlgorithm** — generates and checks codes (injected).
- **ITotpSecretProtector** — encrypts and decrypts the secret (injected).
- **UserService** — reads users, confirms the password, and keeps the failure counter and the lockout (injected).
- **TotpChallengeStore** — holds the challenges of the second step (injected).
- **TotpSettings** — the switch and the issuer (optional, injected).
- **.NET runtime library** — `TimeProvider`, random bytes, and SHA-256.

The service does not take `AuditService`: the audited changes are stored by `TotpStore` in the same transaction as the
change, and the best-effort entries go through `UserService`.

#### Callers

- **AccountPage** (`Web` subsystem) — status, setup, turning off, and new recovery codes of the signed-in user.
- **UsersPage** (`Web` subsystem) — lists the users with a factor and resets one.
- **ReviewPage** (`Web` subsystem) — asks `IsEnrolledAsync` to show the code field.
- **SignIn** and **TotpSignIn** (`Identity` subsystem) — start and complete the second step.
- **TotpLoginPage** (`Web` subsystem) — peeks the challenge.
- **SigningService** — checks the code of a signature.
- **ServerHost** (`Hosting` subsystem) — registers the service as a singleton.
