### TotpSecretProtector

![Identity Structure](IdentityView.svg)

#### Purpose

`TotpSecretProtector` implements the Core port `ITotpSecretProtector`: it encrypts the secret of a second factor before
it is stored and decrypts it when a code is checked, using ASP.NET Core Data Protection. Its single responsibility is
that no secret is ever stored as plain text, and that a stored value cannot be moved to another user.

#### Data Model

The unit holds a Data Protection provider and is thread-safe. The purpose string is
`DemaConsulting.ReviewCenter.TotpSecrets.v1`; for each user a sub-purpose made of the user identity is added, so the
ciphertext of one user cannot be decrypted for another. The keys are the ones of the existing key ring, which the
server persists for the personal access tokens and the repository credentials too (see `ServerHost`); the unit has no
key store of its own. The encrypted value is written as Base64 text and fits the column of the enrollment.

#### Key Methods

**Protect**: Encrypts the secret of a user.

- *Parameters*: The identity of the user and the secret bytes.
- *Returns*: The encrypted text, safe to store.

**Unprotect**: Decrypts a stored secret.

- *Parameters*: The identity of the user and the stored text.
- *Returns*: The secret bytes; the caller clears them after use.
- *Postconditions*: A cryptographic failure (for example a lost key ring, a value that belongs to another user, or a
  damaged text) or text that is not Base64 raises `DomainRuleException` with `TotpSecretUnreadable`. The message tells
  an administrator to reset the second factor. The inner exception is dropped, so nothing of the value reaches a log.

#### Error Handling

An unreadable secret is reported as `TotpSecretUnreadable` as above; a user whose secret cannot be read can still sign
in with a recovery code. Null arguments raise `ArgumentNullException`. A secret or a stored value is never logged or put
into a message.

#### Dependencies

- **ASP.NET Core Data Protection** — encrypts and decrypts the value (see the ASP.NET Core OTS item).
- **Core ITotpSecretProtector** — the interface implemented, and `DomainRule.TotpSecretUnreadable`.

#### Callers

- **TotpService** — through the Core interface.
- **ServerHost** — registers the unit as the implementation of the Core interface.
