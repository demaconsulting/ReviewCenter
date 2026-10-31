### CredentialProtector

![ContentSources Structure](ContentSourcesView.svg)

#### Purpose

`CredentialProtector` encrypts and decrypts the access tokens of private repositories. Its single responsibility is to
turn a token into text that can be stored in the database and back, using the ASP.NET Core Data Protection system, so
that a copy of the database alone does not reveal a token.

#### Data Model

The class holds one `IDataProtector` created from the supplied provider for the purpose string
`DemaConsulting.ReviewCenter.RepositoryCredentials.v1`. The purpose isolates the tokens from every other use of the key
ring (the sign-in cookies, for example): text protected for another purpose cannot be unprotected here, and a token
cannot be unprotected as a cookie. The version suffix allows a later change of the format. The class is stateless
otherwise and thread-safe.

#### Key Methods

**Protect**: Encrypts a token.

- *Parameters*: `string plainText` — the token.
- *Returns*: `string` — protected text that is safe to store; it differs for every call, because the protector adds
  random data.
- *Preconditions*: `plainText` is not null.

**Unprotect**: Decrypts stored text.

- *Parameters*: `string protectedText` — text returned earlier by `Protect`.
- *Returns*: `string` — the token.
- *Preconditions*: `protectedText` is not null.
- *Postconditions*: The text was produced by this purpose and a key of the current key ring.

#### Error Handling

Text that cannot be decrypted (the key ring was lost or replaced, or the text is damaged or from another purpose)
raises a
`CryptographicException` in the framework. The unit catches it and throws a `DomainRuleException` for
`CredentialsUnreadable` with the message "A stored repository token could not be decrypted, probably because the key
ring changed. Enter the token again." The inner exception is not chained, so no detail of the cryptographic failure
reaches a page or a log. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **ICredentialProtector**, **DomainRuleException**, **DomainRule** — Core.
- **ASP.NET Core Data Protection** — the provider, the key ring, and the algorithms. The key ring is a folder that
  `Hosting` creates and checks (see the `ContentSources` and `ServerHost` designs); its keys are encrypted at rest
  when the operator allows it (see `KeyRingProtection`).

#### Callers

- **RepositoryService** — encrypts a token when a repository is registered or its token replaced, and decrypts it when
  the access data is needed.
- **ServerHost** — registers it as the Core token protector.
