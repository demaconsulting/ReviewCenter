### ProjectService

![Core Structure](CoreView.svg)

#### Purpose

`ProjectService` owns the rules for projects: it validates and normalizes project input, creates projects (for system
administrators only), reads the projects an actor may see, and manages the members of a project (for the administrators
of that project and for system administrators). It adds an existing user by the exact user name, creates a local account
as part of adding it, changes a role, and removes a member, and it protects the last project administrator and throttles
guessing of user names. It is also the home of
the supporting types shared by the whole domain: the `Project` type, the store interface `IReviewStore`, the rule
failure type `DomainRuleException` with the `DomainRule` enumeration, and the limits in `ReviewLimits`. Who may see
what is decided by `ProjectAccess`; this unit only asks.

#### Data Model

**Project**: a data class (init-only properties) of `Id` (`int`, assigned by the store), `Name` (`string`), `Description`
(`string`, may be empty), `Created` (`DateTimeOffset`, from the injected clock), `Evidence` (the evidence settings), and
`RequireIndependentReviewers` (`bool`, default `true`): whether the person who created a review is kept out of its
reviewer slots and cannot sign it (see the `SigningService` unit, which enforces it). Review Center only knows who
created the review in the tool; the authors of git commits are not matched.

**ReviewLimits**: constants that define every limit of the domain once, so that the pages, the services, and the
storage agree:

| Constant | Value | Meaning |
| ---------- | ------- | --------- |
| `MaxFileSizeBytes` | 2 MiB | largest size of one uploaded file |
| `MaxFileCount` | 200 | most files in one review |
| `MaxTotalSizeBytes` | 20 MiB | largest total size of the files of one review |
| `MaxPathLength` | 400 | longest file path |
| `MaxProjectNameLength` | 100 | longest project name |
| `MaxProjectDescriptionLength` | 1000 | longest project description |
| `MaxReviewIdLength` | 100 | longest review id |
| `MaxReviewTitleLength` | 200 | longest review title |
| `MaxReopenReasonLength` | 250 | longest reason for reopening an approved review (fits one audit entry) |

**NewMemberAccount**: an immutable record of `Member` (the new `ProjectMember`) and `TemporaryPassword` (`string`, the
clear-text temporary password). The password exists only in this value: it is stored as a hash only and is never
audited. Its `ToString` omits the password (it prints `[temporary secret hidden]`), so logging or printing the value
cannot leak it.

**Throttle constants**: `MaxFailedAttempts` (10), `ThrottleWindow` (10 minutes), and `MemberAddFailedAction`
(`project.member-add-failed`).

**Attempt reservation** (private): the throttle reserves an attempt before the user is looked up, not after it has
failed. `ReserveAttempt` checks the limit and records the attempt in one step under the lock, so attempts of one actor
that run at the same time cannot all pass the check before any failure is counted (a system administrator gets no
reservation and is never counted). `ReleaseAttempt` gives only that reservation back, and only when the attempt
succeeded; a failure, or any exception, leaves the reservation counted. It follows the reservation pattern of
`SignInThrottle` in the `Identity` subsystem, simplified to one counter per actor (that class belongs to the web
project and counts per client address, so it is not reused).

**DomainRuleException**: an exception with a `Rule` (`DomainRule`) and a user-presentable message. Three rules belong to
member management: `LastProjectAdmin` (the change would leave the project without an enabled project administrator),
`TooManyAttempts` (too many failed adds lately), and `PasswordChangeRequired` (the account still has a temporary
password).

**IReviewStore**: the persistence interface of the domain, with the operations `AddProjectAsync`,
`GetProjectAsync`, `ListProjectsAsync`, `ReviewExistsAsync`, `AddReviewAsync`, `GetReviewAsync`,
`ListReviewsAsync`, and `GetFileContentAsync`, for project membership `AddMembershipAsync`,
`RemoveMembershipAsync`, `SetMembershipRoleAsync`, `GetMembershipAsync`, `ListMembershipsAsync` (the members of a
project), and `ListMembershipsForUserAsync`. The setting of a project is changed with
`UpdateRequireIndependentReviewersAsync`, a compare-and-set that takes the audit draft. The three changing
membership operations take the audit draft that is
saved in the same transaction; the removal and the role change report whether a membership existed and take a
`requireAnotherAdmin` flag that makes the store refuse, with `LastProjectAdmin`, a change that would leave the project
without an enabled project administrator (the check and the change are one atomic step). It is implemented by
`ReviewStore` in the `Storage` subsystem.

The service holds the injected store, user store, project access, password hasher, clock, and optional audit service,
and one piece of state: the in-memory attempt reservations per actor (by user name without regard to case), guarded by a
lock that is never held across an await. That state is per process and lost on restart. The membership types
`ProjectMembership`, `ProjectRole`, and `ProjectMember` are described with the `ProjectAccess` unit.

#### Key Methods

**CreateAsync**: Creates a project.

- *Parameters*: `string? name`, `string? description`, the `Actor`, and a cancellation token.
- *Returns*: `Task<Project>` — the stored project with its assigned identity.
- *Preconditions*: The actor is a system administrator (`ActorNotAllowed` otherwise); other invalid input is reported
  as a rule failure.
- *Postconditions*: The project is stored with trimmed name and description, and the creation time from the
  clock, and has no members. Nothing is stored when a rule fails.

Checks the system administrator right first, then trims both values (a null description is treated as empty), then
checks that the name is not empty and not longer than the limit (`ProjectNameInvalid`) and that the description is
not longer than the limit (`ProjectDescriptionTooLong`). Names need not be unique.

**ListAsync**: Lists the projects the actor may see, ordered by name and then identity, as `ProjectAccess` lists them.

**GetAsync**: Reads one project by identity for an actor; returns null when it does not exist or the actor may not see
it, so a hidden project reads like a missing one.

**ListMembersAsync**: Lists the members of a project as `ProjectMember` values ordered by user name. The actor must be
an administrator of the project (`ProjectNotFound` when it is not visible, `ActorNotAllowed` for a plain member).

**AddMemberAsync**: Adds an existing user to a project by the exact user name.

- *Parameters*: the `Actor`, the project identity, the user name, the `ProjectRole`, and a cancellation token.
- *Returns*: `Task<ProjectMember>` — the new member.
- *Preconditions*: The actor administers the project or is a system administrator (`ProjectNotFound` or
  `ActorNotAllowed` otherwise, as `RequireAdministerAsync` decides); the role is defined and offered
  (`ArgumentOutOfRangeException`; `Auditor` is declared but refused until it is offered); a non-exempt actor has fewer
  than `MaxFailedAttempts` reserved attempts in the last `ThrottleWindow` (`TooManyAttempts` otherwise); the attempt is
  reserved atomically before the user lookup and stays counted unless the add succeeds.
- *Postconditions*: The membership is stored with the audit event `project.member-added`. The name is trimmed and looked
  up exactly: there is no search and no operation lists users, so a project administrator cannot browse the user base.
  A name that does not exist and the name of a disabled user are refused with the same message and the rule
  `UserNotFound`; the failure is counted against the actor (system administrators are exempt from counting) and audited
  as `project.member-add-failed` (detail `user name 'typed'`, best effort). `MembershipExists` is reported when the
  user already belongs. Nothing is stored when a rule fails.

**CreateMemberAccountAsync**: Creates a local account and adds it to the project in one step.

- *Parameters*: the `Actor`, the project identity, the new user name, the display name, the `ProjectRole`, and a
  cancellation token.
- *Returns*: `Task<NewMemberAccount>` — the new member and the temporary password, returned only here.
- *Preconditions*: The same access and throttle checks as `AddMemberAsync` (the attempt is reserved before the account
  is stored; input refused before any write gives the reservation back, so a typing mistake is not a failed guess);
  the user name and display name pass the same
  rules as for a user created by a system administrator (`UserNameInvalid`, `DisplayNameInvalid`); the role is
  defined and offered, as for `AddMemberAsync`.
- *Postconditions*: The account is created with `IsSystemAdmin` false, enabled, `MustChangePassword` true, and the hash
  of a generated temporary password, and the membership is created, in one transaction through
  `IUserStore.AddToProjectAsync`, with the audit event `project.account-created` (detail
  `user 'name', role Role, temporary password`; the password itself is never audited). A taken name raises
  `UserNameTaken` with the neutral message "That user name is not available.", counted and audited like a failed add.

The temporary password has 16 characters drawn from `RandomNumberGenerator` over an unambiguous alphabet (no digits 0
and 1, no letters O, I, and l), so it can be read out and typed without mistakes and is above the policy minimum. The
account can never be a system administrator and cannot exist without the membership. Every sign-in rule of
`MustChangePassword` is described with the `UserService`, `ProjectAccess`, and `PersonalAccessTokenService` units.

**SetMemberRoleAsync** and **RemoveMemberAsync**: Change the role of a member and remove the membership.

- *Parameters*: the `Actor`, the project identity, the user name, the `ProjectRole` where one is needed, and a
  cancellation token. `SetMemberRoleAsync` returns the `ProjectMember` as it is now; `RemoveMemberAsync` returns a task.
- *Preconditions*: The actor administers the project or is a system administrator. The role passed to
  `SetMemberRoleAsync` is defined and offered (`ArgumentOutOfRangeException`; `Auditor` is refused until it is
  offered). An unknown name and a non-member are
  refused with `MembershipNotFound` and one message, so the name of an unrelated user is never confirmed.
- *Postconditions*: A project administrator may not remove or demote the last enabled project administrator, themselves
  included (`LastProjectAdmin`, with a friendly message); a system administrator may. The service passes
  `requireAnotherAdmin` as the opposite of the system administrator exemption, and the store makes the check and the
  change one atomic step inside its serialized audit transaction, so two concurrent changes cannot empty the project.
  Nothing is changed when a rule fails.

**SetRequireIndependentReviewersAsync**: Switches the "require independent reviewers" rule of a project.

- *Parameters*: the `Actor`, the project identity, the `expected` value (what the caller read and based the change on),
  the new `value`, and a cancellation token.
- *Returns*: `Task<bool>` — `true` when the value was changed, `false` when `value` equals `expected` (nothing is
  stored or recorded).
- *Preconditions*: The actor is not an API token (`ActorNotAllowed`: "Project settings cannot be changed with an API
  token; sign in to the web pages.") and administers the project or is a system administrator (`ProjectNotFound` or
  `ActorNotAllowed`, as `RequireAdministerAsync` decides; asked again on every call).
- *Postconditions*: The store changes the value only while it still equals `expected` (compare-and-set), together with
  the audit entry `project.independent-reviewers-changed` (detail `RequireIndependentReviewers on->off` or
  `off->on`). When the stored value is no longer `expected`, `RecordChanged` is raised ("The setting was changed by
  someone else. Reload the page and try again.") and nothing is stored. The rule is not retroactive: it is applied
  when a reviewer is chosen and when a review is signed, and existing signatures stay valid.

**Audit**: `CreateAsync` builds the audit draft `project.created` (detail `project 'Name'`) from the stored project, so
it can name the identity the database assigned, and passes it to the store, which saves it in the same transaction. A
creation that is refused records nothing. The member operations build `project.member-added` and
`project.member-role-changed` (detail `user 'name', role Role`) and `project.member-removed` (detail `user 'name'`),
each carrying the `ProjectId` so that the history of one project can be found, and pass them to the store with the
change; `project.account-created` is saved by the user store with the new user and membership. Failed adds are recorded
through `AuditService.TryRecordAsync` as `project.member-add-failed`; the throttle caps how many entries one actor can
cause. See the `AuditService` unit for the action codes.

#### Error Handling

Rule violations raise `DomainRuleException` before any store change: `ActorNotAllowed`, `ProjectNotFound`,
`ProjectNameInvalid`, `ProjectDescriptionTooLong`, `UserNotFound`, `MembershipExists`, `MembershipNotFound`,
`UserNameInvalid`, `DisplayNameInvalid`, `UserNameTaken`, `TooManyAttempts`, `LastProjectAdmin`, and `RecordChanged` (a
stale review rule). The messages for a
name that cannot be added, a user that is not a member, and a taken name are deliberately generic, so the service cannot
be used to find out which accounts exist. A failure to write the audit entry of a failed add does not hide the refusal.
Constructing the service with a null store, user store, access, hasher, or clock raises `ArgumentNullException`; the
audit service is optional. Store failures propagate unchanged, except that the `LastProjectAdmin` and `UserNameTaken`
failures of the stores are turned into the friendly messages above.

#### Known Limitations

- The membership operations check the authorization (`RequireAdministerAsync`) before the store transaction and do not
  revalidate the actor inside it. An administrator whose right is revoked (removed from the project, demoted, or
  disabled) within the few milliseconds between the check and the store transaction of one handler is therefore not
  stopped for that one operation; the next call is refused. This is the same class of limitation as for the access
  rules of project-based access control (see `ProjectAccess`: a revocation takes effect on the next call), is
  deliberate, and is not mitigated.
- Adding a member by the exact user name necessarily tells a project administrator whether an enabled account with
  that name exists, because a successful add shows it. That is accepted: successful adds are visible in the member list,
  audited (`project.member-added`) and reversible by removing the member, failures are throttled and audited, and
  system administrators and project administrators can already create accounts, which reveals name collisions
  (`UserNameTaken`). There is still no search and no list of users.

#### Dependencies

- **IReviewStore** — persists and reads projects and memberships (injected).
- **IUserStore** — finds the user to add or change and creates a user with a membership (injected).
- **IProjectAccess** — decides who may create, see, and administer, and who is a system administrator (injected).
- **IPasswordHasher** — hashes the temporary passwords of created accounts (injected).
- **TimeProvider** — supplies the creation time and the throttle window (injected).
- **AuditService** — records failed adds (injected, optional).

#### Callers

- **ProjectsPage**, **ProjectPage**, **NewMemberPage**, **NewMemberAccountPage**, **ProjectAuditPage**, and
  **ApiEndpoints** — create, list, and read projects, and list and manage members; **ProjectPage** also reads and
  switches the review rule.
- **ServerHost** — registers the service for the pages.
