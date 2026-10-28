### ProjectService

![Core Structure](CoreView.svg)

#### Purpose

`ProjectService` owns the rules for projects: it validates and normalizes project input, creates projects (for system
administrators only), reads the projects an actor may see, and manages the members of a project. It is also the home of
the supporting types shared by the whole domain: the `Project` type, the store interface `IReviewStore`, the rule
failure type `DomainRuleException` with the `DomainRule` enumeration, and the limits in `ReviewLimits`. Who may see
what is decided by `ProjectAccess`; this unit only asks.

#### Data Model

**Project**: a data class (init-only properties) of `Id` (`int`, assigned by the store), `Name` (`string`), `Description`
(`string`, may be empty), and `Created` (`DateTimeOffset`, from the injected clock).

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

**DomainRuleException**: an exception with a `Rule` (`DomainRule`) and a user-presentable message.

**IReviewStore**: the persistence interface of the domain, with the operations `AddProjectAsync`,
`GetProjectAsync`, `ListProjectsAsync`, `ReviewExistsAsync`, `AddReviewAsync`, `GetReviewAsync`,
`ListReviewsAsync`, and `GetFileContentAsync`, and for project membership `AddMembershipAsync`,
`RemoveMembershipAsync`, `SetMembershipRoleAsync`, `GetMembershipAsync`, `ListMembershipsAsync` (the members of a
project), and `ListMembershipsForUserAsync`. The three changing membership operations take the audit draft that is
saved in the same transaction; the removal and the role change report whether a membership existed. It is implemented
by `ReviewStore` in the `Storage` subsystem.

The service holds the injected store, user store, project access, and clock and no other state. The membership types
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

**AddMemberAsync**, **SetMemberRoleAsync**, and **RemoveMemberAsync**: Add a user to a project with a role, change the
role, and remove the membership.

- *Parameters*: the `Actor`, the project identity, the user name, the `ProjectRole` where one is
  needed, and a cancellation token.
- *Preconditions*: The actor is a system administrator (`ActorNotAllowed` otherwise) and may see the project
  (`ProjectNotFound`). The user must exist (`UserNotFound`); the role must be defined (`ArgumentOutOfRangeException`).
- *Postconditions*: Adding fails with `MembershipExists` when the user already belongs; changing and removing fail
  with `MembershipNotFound` when the user does not. Nothing is changed when a rule fails.

**Audit**: `CreateAsync` builds the audit draft `project.created` (detail `project 'Name'`) from the stored project, so
it can name the identity the database assigned, and passes it to the store, which saves it in the same transaction. A
creation that is refused records nothing. The member operations build `project.member-added` and
`project.member-role-changed` (detail `user 'name', role Role`) and `project.member-removed` (detail `user 'name'`),
each carrying the `ProjectId` so that the history of one project can be found, and pass them to the store with the
change. See the `AuditService` unit for the action codes.

#### Error Handling

Rule violations raise `DomainRuleException` before any store change: `ActorNotAllowed`, `ProjectNotFound`,
`ProjectNameInvalid`, `ProjectDescriptionTooLong`, `UserNotFound`, `MembershipExists`, and `MembershipNotFound`.
Constructing the service with a null store, user store, access, or clock raises `ArgumentNullException`. Store
failures propagate unchanged.

#### Dependencies

- **IReviewStore** — persists and reads projects and memberships (injected).
- **IUserStore** — finds the user to add or change (injected).
- **IProjectAccess** — decides who may create, see, and administer (injected).
- **TimeProvider** — supplies the creation time (injected).

#### Callers

- **ProjectsPage**, **ProjectPage**, and **ApiEndpoints** — create, list, and read projects, and list and manage
  members.
- **ServerHost** — registers the service for the pages.
