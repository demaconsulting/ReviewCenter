### RepositoryService

![Core Structure](CoreView.svg)

#### Purpose

`RepositoryService` manages the registry of git repositories that reviews can be created from. Its single
responsibility is to enforce the rules of that registry: only administrators change it, names, locations, and
credentials are valid, tokens are stored encrypted and never given back, and every change is audited without any secret.
It also hands the decrypted access data to the code that reads a repository.

#### Data Model

The service is a singleton. It holds the `IRepositoryStore`, the `ICredentialProtector`, the `UserService` (to re-check
the acting administrator), and a `TimeProvider`. A semaphore serializes every change, so the checks and the write of one
request are not interleaved with another request's.

**Limits**: `MaxNameLength` 40, `MaxLocationLength` 500, `MaxUsernameLength` 200, `MaxTokenLength` 1000.

**Contract types** (defined in `Core`, in `SourceRepository.cs` and `IGitRepository.cs`):

- **SourceRepository**: `Id`, `Name`, `Location`, `Username`, `HasToken`, `Enabled`, `CreatedAt`, `UpdatedAt`. It has no
  member that can hold a token, so a registration can be listed and shown without risk.
- **RepositorySummary**: `Id` and `Name` of an enabled repository, for pickers.
- **NewRepository** and **RepositoryUpdate**: what the service hands to the store (the token already encrypted).
- **TokenAction** (`Keep`, `Replace`, `Clear`) and **TokenChange**: what an update does with the token; its `ToString`
  hides the token.
- **RepositoryAccess**: `RepositoryId`, `Name`, `Location`, `Username`, `Token` — the access data for a read; its
  `ToString` prints only `Repository {id}`.

#### Key Methods

**CreateAsync**: Registers a repository.

- *Parameters*: `string name`, `string location`, `string? username`, `string? token`, `Actor actor`.
- *Returns*: `SourceRepository` — the stored registration.
- *Preconditions*: The actor is an enabled administrator.
- *Postconditions*: The registration exists, enabled, with the token encrypted, and one `repository.created` audit entry
  was stored in the same transaction.
- *Algorithm*: Re-check the actor; validate the name, the location, the user name and token, and their combination;
  encrypt the token; add through the store with an audit draft built from the stored registration. The audit detail is
  `repository 'N', location 'L', user name set|none, token set|none`.

**UpdateAsync**: Changes a registration.

- *Parameters*: `int id`, `string name`, `string location`, `string? username`, `TokenChange token`, `Actor actor`.
- *Returns*: `SourceRepository` — the registration after the change.
- *Preconditions*: As for `CreateAsync`; the repository exists.
- *Postconditions*: When nothing changed, the registration is returned and no audit entry is written. Otherwise one
  `repository.updated` entry lists the changed fields.
- *Algorithm*: Re-check the actor; validate the new values as for creation (a `Replace` with an empty token is
  `RepositoryCredentialInvalid`); compare with the stored registration to build a list of changes from the items "name",
  "location", "user name", "token replaced", and "token cleared" (the last only when a token existed); store through the
  store with the audit detail `repository 'N': <changes>`.

**SetEnabledAsync**: Enables or disables a repository.

- *Parameters*: `int id`, `bool enabled`, `Actor actor`.
- *Returns*: `SourceRepository`.
- *Postconditions*: A real change writes `repository.enabled` or `repository.disabled` with the detail
  `repository 'N'`; setting the state it already has writes nothing. A repository is never deleted.

**ListAsync**: Lists all registrations for an administrator (`Actor actor`), ordered by name. **ListEnabledAsync**
lists the
`RepositorySummary` of the enabled repositories for any caller.

**GetAccessAsync** (internal to Core): Returns the access data for a read.

- *Parameters*: `int id`.
- *Returns*: `RepositoryAccess` — including the decrypted token.
- *Postconditions*: Only an enabled repository is returned.
- *Algorithm*: Load the registration; unknown is `RepositoryNotFound`, disabled is `RepositoryDisabled`. Load the
  encrypted token, if any, and decrypt it; text that cannot be decrypted is `CredentialsUnreadable` with a message that
  names the repository and says an administrator must enter the token again.

**Validation rules**:

- *Name*: trimmed; letters, digits, `.`, `_`, and `-`; starts with a letter or digit; at most 40 characters
  (`RepositoryNameInvalid`). Uniqueness ignores case and is decided by the store (`RepositoryNameTaken`).
- *Location*: 1 to 500 characters, no control characters, not starting with `-`. Either an `https://`, `http://`, or
  `file://` address that is a valid absolute address without white space, without user information, without a query
  string or fragment, and (except for
  `file`) with a host, or a fully qualified local path. SSH and scp-style addresses are refused
  (`RepositoryLocationInvalid`).
- *Credentials*: a user name or token is accepted only for an `https://` location. A token needs a user name. The user
  name has at most 200 characters, no `:`, and no control characters; the token has at most 1000 characters and no white
  space or control characters (`RepositoryCredentialInvalid`).

#### Error Handling

Every violation is a `DomainRuleException`: `ActorNotAllowed` for a caller that is not an enabled administrator,
`RepositoryNameInvalid`, `RepositoryNameTaken`, `RepositoryLocationInvalid`, `RepositoryCredentialInvalid`,
`RepositoryNotFound`, `RepositoryDisabled`, or `CredentialsUnreadable`. Nothing is stored when a rule fails. No message,
audit detail, or `ToString` contains a token. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **IRepositoryStore** — persistence with audit (implemented by `RepositoryStore`).
- **ICredentialProtector** — token encryption (implemented by `CredentialProtector`).
- **UserService** — the administrator re-check.
- **AuditDraft**, **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **RepositoriesPage** — registers, edits, enables, disables, and lists repositories.
- **Review creation from a repository** (a later slice) — lists the enabled repositories and obtains the access data.
- **ServerHost** — registers the service as a singleton.
