### RepositoryService

![Core Structure](CoreView.svg)

#### Purpose

`RepositoryService` manages the registry of git repositories that reviews can be created from. Its single
responsibility is to enforce the rules of that registry: every repository belongs to exactly one project, only the
administrators of that project (project administrators and system administrators) change it, names, locations, and
credentials are valid, tokens are stored encrypted and never given back, and every change is audited without any
secret. It also hands the decrypted access data to the code that reads a repository.

#### Data Model

The service is a singleton. It holds the `IRepositoryStore`, the `ICredentialProtector`, the `IProjectAccess` (to
decide who may see and administer the project, checked again on every call), and a `TimeProvider`. A semaphore
serializes every change and every read of the access data,
so the checks and the write of one request are not interleaved with another request's, and the location and the token
that `GetAccessAsync` returns always belong to the same version of a registration.

**Limits**: `MaxNameLength` 40, `MaxLocationLength` 500, `MaxUsernameLength` 200, `MaxTokenLength` 1000.

**Contract types** (defined in `Core`, in `SourceRepository.cs` and `IGitRepository.cs`):

- **SourceRepository**: `Id`, `ProjectId`, `Name`, `Location`, `Username`, `HasToken`, `Enabled`, `CreatedAt`,
  `UpdatedAt`. It has no member that can hold a token, so a registration can be listed and shown without risk.
- **RepositorySummary**: `Id` and `Name` of an enabled repository, for pickers.
- **NewRepository** and **RepositoryUpdate**: what the service hands to the store (the token already encrypted).
- **TokenAction** (`Keep`, `Replace`, `Clear`) and **TokenChange**: what an update does with the token; its `ToString`
  hides the token.
- **RepositoryAccess**: `RepositoryId`, `Name`, `Location`, `Username`, `Token` — the access data for a read; its
  `ToString` prints only `Repository {id}`.

#### Key Methods

**CreateAsync**: Registers a repository.

- *Parameters*: `int projectId`, `string name`, `string location`, `string? username`, `string? token`, `Actor actor`.
- *Returns*: `SourceRepository` — the stored registration.
- *Preconditions*: The actor administers the project (`IProjectAccess.RequireAdministerAsync`).
- *Postconditions*: The registration exists in the project, enabled, with the token encrypted, and one
  `repository.created` audit entry carrying the project was stored in the same transaction.
- *Algorithm*: Require administration of the project; validate the name, the location, the user name and token, and
  their combination; encrypt the token; add through the store with an audit draft built from the stored
  registration. The audit detail is `repository 'N', location 'L', user name set|none, token set|none`.

**UpdateAsync**: Changes a registration.

- *Parameters*: `int id`, `string name`, `string location`, `string? username`, `TokenChange token`, `Actor actor`.
- *Returns*: `SourceRepository` — the registration after the change.
- *Preconditions*: The repository exists and the actor administers its project; the project is taken from the
  repository, never from the caller.
- *Postconditions*: When nothing changed, the registration is returned and no audit entry is written. Otherwise one
  `repository.updated` entry, carrying the project, lists the changed fields.
- *Algorithm*: Find the repository and require that the actor may see its project (otherwise `RepositoryNotFound`, so a
  hidden repository reads like a missing one), then require administration (a plain member gets `ActorNotAllowed`);
  validate the new values as for creation (a `Replace` with an empty token is
  `RepositoryCredentialInvalid`, and so is a new token contained in the new name, user name, or location, or whose
  percent-escaped form is, compared ignoring case, so `%2f` and `%2F` are the same); then, under
  the semaphore, when the stored token is kept and the name, the user name, or the location changes, decrypt the stored
  token and refuse the change with `RepositoryCredentialInvalid` if it is contained in the new name, user name, or
  location (a stored token that cannot be decrypted is not compared); compare with the stored registration to build a
  list of changes from the items "name", "location", "user name", "token replaced", and "token cleared" (the last only
  when a token existed); store through the store with the audit detail `repository 'N': <changes>`.

**SetEnabledAsync**: Enables or disables a repository.

- *Parameters*: `int id`, `bool enabled`, `Actor actor`.
- *Returns*: `SourceRepository`.
- *Postconditions*: A real change writes `repository.enabled` or `repository.disabled` (carrying the project) with the
  detail `repository 'N'`; setting the state it already has writes nothing. A repository is never deleted. The rights
  are checked as for `UpdateAsync`.

**ListAsync**: Lists all registrations of a project for an administrator of that project (`Actor actor`, `int
projectId`), ordered by name. **ListEnabledAsync** (`Actor actor`, `int projectId`) lists the `RepositorySummary` of the
enabled repositories of the project for any actor who may see the project.

**GetAccessAsync** (internal to Core): Returns the access data for a read.

- *Parameters*: `int projectId`, `int id`.
- *Returns*: `RepositoryAccess` — including the decrypted token.
- *Postconditions*: Only an enabled repository of that project is returned.
- *Algorithm*: Take the semaphore, so no update can run between the reads. Load the registration; unknown, or one that
  belongs to another project, is `RepositoryNotFound`, disabled is `RepositoryDisabled`. Load the
  encrypted token, if any, and decrypt it; text that cannot be decrypted is `CredentialsUnreadable` with a message that
  names the repository and says an administrator must enter the token again.

**Validation rules**:

- *Name*: trimmed; letters, digits, `.`, `_`, and `-`; starts with a letter or digit; at most 40 characters
  (`RepositoryNameInvalid`). Uniqueness ignores case, is decided by the store, and is per project, so two projects may
  use the same name (`RepositoryNameTaken`).
- *Location*: 1 to 500 characters, no control characters, not starting with `-`. Either an `https://`, `http://`, or
  `file://` address that is a valid absolute address without white space, without user information, without a query
  string or fragment, and (except for
  `file`) with a host, or a fully qualified local path. A plain `http://` address is accepted only when the host, as
  parsed by `Uri`, is exactly `localhost` (any letter case), `127.0.0.1`, or `[::1]`; names that only resemble one,
  such as
  `localhost.example.com` or `127.0.0.1.example.com` are refused, because plain http can be tampered with. The check
  runs for both create and update. SSH and scp-style addresses are refused
  (`RepositoryLocationInvalid`).
- *Credentials*: a user name or token is accepted only for an `https://` location (so a loopback `http://` location
  never carries a token; it is refused with `RepositoryCredentialInvalid`). A token needs a user name. The user
  name has at most 200 characters, no `:`, and no control characters; the token has at most 1000 characters and no white
  space or control characters. A token must also differ from the repository name, from
  the user name, and from the location (exact, case-sensitive comparison; the location is compared as stored, which is
  the trimmed input), because those are written to audit entries and messages. All of
  these are `RepositoryCredentialInvalid`, with a message that does not contain the token.

#### Error Handling

Every violation is a `DomainRuleException`: `ActorNotAllowed` for a member of the project who is no administrator,
`ProjectNotFound` for a project the actor may not see, `RepositoryNameInvalid`, `RepositoryNameTaken`,
`RepositoryLocationInvalid`, `RepositoryCredentialInvalid`, `RepositoryNotFound` (also for a repository the actor may
not see and for one of another project), `RepositoryDisabled`, or `CredentialsUnreadable`. Nothing is stored when a
rule fails. No message, audit detail, or `ToString` contains a token. Null arguments raise `ArgumentNullException`.

#### Dependencies

- **IRepositoryStore** — persistence with audit (implemented by `RepositoryStore`).
- **ICredentialProtector** — token encryption (implemented by `CredentialProtector`).
- **IProjectAccess** — the view and administer checks, repeated on every call.
- **AuditDraft**, **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **RepositoriesPage** — registers, edits, enables, disables, and lists the repositories of a project.
- **ReviewService** and **ReviewPlanner** — list the enabled repositories of a project and obtain the access data.
- **ServerHost** — registers the service as a singleton.
