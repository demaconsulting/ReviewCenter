### PersonalAccessTokenService

![Core Structure](CoreView.svg)

#### Purpose

`PersonalAccessTokenService` owns the rules for personal access tokens: it creates a token for a user, lists the
tokens of a user, revokes one, and decides whether a presented token is valid. A token is a secret that lets a script
act as its
owner on the API. The unit is also the home of the supporting types of tokens: `PersonalAccessToken`, the interface
`IPersonalAccessTokenStore`, the result types `CreatedPersonalAccessToken`, `TokenValidation`, and `TokenFailure`, the
token rules of `DomainRule`, and the token limits of `ReviewLimits`.

#### Data Model

**PersonalAccessToken**: a data class of `Id` (assigned by the store), `UserId` and `User` (the owner, loaded only when
a lookup asks for it), `Name` (the label the owner chose), `DisplayId`, `TokenHash`, `Created`, `Expires`, `LastUsed`
(`null` until first use), and `Revoked` (`null` while the token is not revoked). `IsActive(now)` is true when the
token is neither revoked nor expired at the time given. The value of a token is never a property: only its hash is stored.

**The value**: `rcpat_` followed by the base64url text without padding of 32 random bytes (43 characters, 49 in all). The
random bytes come from the cryptographic random generator. The value is returned once, by `CreateAsync`, in a
`CreatedPersonalAccessToken` together with the stored token, and exists nowhere else.

**The hash and the display id**: The stored hash is the lowercase hexadecimal SHA-256 of the whole value. A token has a
high-entropy secret, so a fast hash is enough and permits a lookup by hash; a slow password hash would only make every
API request slow. The display id is a separate random text of 8 characters from an alphabet of 32 digits and
uppercase letters (without the letters I, L, O, and U, which are easy to mix up). It has no relation to the secret,
so showing it in a list, in the
audit trail, or in a claim gives nothing away. It lets the owner tell the tokens apart and lets the trail name a token.

**IPersonalAccessTokenStore**: the persistence interface, with `AddAsync` (stores the token and an audit entry that is
built from the stored token), `ListForUserAsync` (newest first), `FindByHashAsync` (the token with its owner loaded, or
`null`), `RevokeAsync` (marks a token of the given owner revoked and saves the audit entry; `null` when the token is not
found, belongs to someone else, or is already revoked), and `TouchAsync` (sets the last-used time and writes no audit
entry). It is implemented by `PersonalAccessTokenStore` in the `Storage` subsystem.

**TokenFailure**: the reason a presented token was refused, for the audit trail only: `None` (accepted), `Malformed`,
`Unknown`, `Expired`, `Revoked`, `OwnerDisabled`, and `OwnerLockedOut`. **TokenValidation** carries the failure, the token
that matched (if any), and the owner; `Succeeded` is true when the failure is `None`.

**ReviewLimits constants** added for tokens: `MaxActiveTokensPerUser` (20), `MaxTokenNameLength` (100),
`TokenExpiryChoicesInDays` (30, 90, and 365), `DefaultTokenExpiryDays` (90), and `TokenLastUsedGranularity`
(5 minutes). **DomainRule values** added: `TokenNameInvalid`, `TokenExpiryInvalid`, `TokenLimitReached`, and
`TokenNotFound`.

**Serialization**: Creating and revoking run one at a time under a semaphore, so the limit of active tokens cannot be
passed by two requests racing. This holds for one instance, so the service is a singleton.

#### Key Methods

**CreateAsync**: Creates a token for the signed-in user.

- *Parameters*: `Actor actor`, `string? name`, `int expiryDays`, and a cancellation token.
- *Returns*: `Task<CreatedPersonalAccessToken>` — the stored token and its value.
- *Postconditions*: The token and a `token.created` audit entry (detail: the name, the display id, and the expiry time in
  UTC) are stored together. Nothing is stored when a rule fails.

The checks are in this order, and the first violation is reported: an actor that came in with an API token is refused
(`ActorNotAllowed`, so a token cannot mint another token); the name is trimmed and must have 1 to 100 characters without
control characters (`TokenNameInvalid`); the expiry must be one of the choices (`TokenExpiryInvalid`); the owner is read
by user name and must exist and be enabled (`ActorNotAllowed`); and the owner must have fewer than 20 active tokens
(`TokenLimitReached`). Revoked and expired tokens do not count towards the limit.

**ListAsync**: Lists the tokens of the actor, newest first, with the state they have. The owner must exist
(`ActorNotAllowed`). No value is ever returned.

**RevokeAsync**: Revokes a token of the actor.

- *Parameters*: `Actor actor`, `int tokenId`, and a cancellation token.
- *Postconditions*: The revocation time and a `token.revoked` audit entry are stored together.

An actor with an API token is refused (`ActorNotAllowed`). A token that does not exist, belongs to another user, or is
already revoked is reported the same way, as `TokenNotFound`, so a user cannot learn which numbers belong to others.

**AuthenticateAsync**: Decides whether a presented text is a valid token.

- *Parameters*: `string? presented` and a cancellation token.
- *Returns*: `Task<TokenValidation>`.
- *Postconditions*: On success the last-used time may be updated.

The order of the checks is: the text has the shape of a token (the prefix, a total of 49 characters, and only base64url
characters, otherwise `Malformed` and nothing is read); the token is found by the hash of the text (`Unknown`); the
stored hash is compared once more with the computed one in constant time, so the decision does not rest on the database
comparison alone; the token is not revoked (`Revoked`); it has not expired (`Expired`); the owner is enabled
(`OwnerDisabled`); and the owner is not locked out (`OwnerLockedOut`). The role of the owner is the one in the owner
object that was just read, so a changed role applies at once. When the token is accepted and its last-used time is
older than 5 minutes, the store is asked to update it, so a busy script does not write on every request; this update is
not audited. The failure reason never reaches a caller outside the system.

#### Error Handling

Rule violations raise `DomainRuleException` with the matching `DomainRule`. A null store, user store, or clock at
construction raises `ArgumentNullException`. `AuthenticateAsync` does not throw for a bad token: it returns a refusal.
Store failures propagate unchanged. The messages never contain a token value or a hash.

#### Dependencies

- **IPersonalAccessTokenStore** — stores and finds tokens (injected).
- **IUserStore** — reads the owner of a token by user name (injected).
- **TimeProvider** — supplies the creation, expiry, and last-used times (injected).
- **Actor**, **AuditDraft**, **User**, **DomainRule**, **ReviewLimits** — Core types.

#### Callers

- **AccountPage** — creates, lists, and revokes the tokens of the signed-in user.
- **ApiTokenAuthentication** — validates the token of a request.
- **ServerHost** — registers the service as a singleton.
