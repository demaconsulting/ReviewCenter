### ProjectAccess

![Core Structure](CoreView.svg)

#### Purpose

`ProjectAccess` is the single place that decides who may see a project, work in it, administer it, and read its audit
trail. Every other unit that restricts access asks it through the `IProjectAccess` interface and never reads
memberships or the administrator flag itself, so one rule holds on every page, route, and service.

#### Data Model

**ProjectRole**: an enumeration of what a member may do in one project: `User` (a member: sees the project, creates
reviews, comments, answers checklists, and signs), `ProjectAdmin` (a member who also manages the members and the
repositories of the project and reads its audit trail), and `Auditor` (read-only: sees everything a member sees and
reads and exports the audit trail, but changes nothing and cannot be named in a reviewer slot or sign). Project
administrators and system administrators assign the role through `ProjectService` like any other; no API route manages
members.

**ProjectMembership**: an immutable record of `UserId`, `ProjectId`, and `Role`. A user has at most one membership
per project; the pair (user, project) is its identity.

**ProjectMember**: an immutable record of `UserId`, `Username`, `DisplayName`, `Enabled`, and `Role`, the listing
form of a member for the people who manage a project.

**IProjectAccess**: the interface of the unit, so that the services depend on the rules and not on their
implementation.

**Level** (private): `Read`, `Contribute`, or `Administer`, the answer of the internal decision. The levels are
ordered; `Read` sees the project, `Contribute` also works in it, and `Administer` also manages it.

The rules are:

| Actor | View | Contribute | Administer | Audit |
| ----- | ---- | ---------- | ---------- | ----- |
| missing, command line, unknown, disabled, or temporary-password user | nothing | nothing | nothing | nothing |
| enabled system administrator | every project | every project | every project | every project |
| member with role `User` | member projects | member projects | nothing | nothing |
| member with role `ProjectAdmin` | member projects | member projects | member projects | member projects |
| member with role `Auditor` | member projects | nothing | nothing | member projects |
| member with a stored role that is not defined | nothing | nothing | nothing | nothing |
| enabled user without a membership | nothing | nothing | nothing | nothing |

"Member projects" means the projects of the actor's memberships with that role. In terms of the access levels,
`Read` is the level of an `Auditor` (the project and its audit trail), `Contribute` is that of a `User`, and
`Administer` is that of a `ProjectAdmin`; so an Auditor reads everything that a member reads, plus the audit trail and
its export, and changes nothing. The Auditor does not see the member list, the repositories page, or the users page,
which stay with those who administer.

A stored role that is not defined fails closed: it is treated like no membership at all (`RoleOfAsync` answers null),
so it gains no right, the project reads as not found, `IsMemberAsync` is false, and the project is not listed.

The unit holds the injected `IUserStore` and `IReviewStore` and no other state. A user is found by `Actor.Username`
on every call and nothing is cached.

#### Key Methods

**IsSystemAdminAsync**: Tells whether the actor is an enabled system administrator.

- *Parameters*: `Actor? actor` and a cancellation token.
- *Returns*: `Task<bool>`.
- *Preconditions*: None; a null actor is allowed and is not an administrator.

**CanViewAsync**, **CanContributeAsync**, **CanAdministerAsync**, and **CanAuditAsync**: Tell whether the actor may
see a project, work in it (create reviews, comment, answer checklists, plan, and sign), administer it, or read its
audit trail.

- *Parameters*: `Actor? actor`, `int projectId`, and a cancellation token.
- *Returns*: `Task<bool>`.
- *Postconditions*: Nothing is changed. A project that does not exist is not visible to anybody, a system
  administrator included. `CanAuditAsync` is true for the `Read` and `Administer` levels, not for `Contribute`.

**IsMemberAsync**: Tells whether a user, given by identity and not by actor, has a membership in a project. It is used
to check the reviewers of a slot and the signer, where the system administrator right must not count.

**ListVisibleProjectsAsync**: Lists the projects an actor may see, ordered as the store orders projects.

- *Returns*: All projects for an enabled system administrator; for an enabled user the projects of the memberships of
  the user; an empty list for any other actor.

**RequireViewAsync**: Throws unless the actor may see the project.

- *Postconditions*: The failure is a `DomainRuleException` with `ProjectNotFound` and one fixed message, the same
  whether the project does not exist or is not visible, so that existence never leaks.

**RequireContributeAsync**: Throws unless the actor may work in the project.

- *Postconditions*: `ProjectNotFound` (as above) when the project is not visible, and `ActorNotAllowed` when the actor
  sees the project but may only read it.

**RequireAdministerAsync**: Throws unless the actor may administer the project.

- *Postconditions*: `ProjectNotFound` (as above) when the project is not visible, and `ActorNotAllowed` when the actor
  sees the project but is no administrator of it.

**RequireAuditAsync**: Throws unless the actor may read the audit trail of the project.

- *Postconditions*: `ProjectNotFound` (as above) when the project is not visible, and `ActorNotAllowed` when the actor
  sees the project but is neither an administrator nor an auditor of it.

**RequireSystemAdminAsync**: Throws `ActorNotAllowed` unless the actor is an enabled system administrator.

**Decision**: The private `ResolveAsync` returns the enabled user behind an actor, or nothing for a missing actor, the
command line, a user name that no user has, a disabled user, and a user that still has a temporary password
(`User.MustChangePassword`): such a user has no access to anything, a system administrator flag included, until the
password is changed. The private `RoleOfAsync` returns `Administer` for
an enabled system administrator when the project exists, otherwise the level given by the role of the membership
(`ProjectAdmin` is `Administer`, `User` is `Contribute`, and `Auditor` or any role that is not defined is `Read`), or
nothing when there is no membership. An actor that came through a personal access token carries the user name of the
token owner, so it gets the access of the owner by this lookup and needs no rule of its own.

**Page guards**: `ProjectViewGuard` (in the Web subsystem) re-checks the same rules at the start of every action a
page handles on an open circuit. Besides the view, administer, and system administrator guards it offers
`GuardContributeAsync` and `GuardAuditAsync`. `GuardAuditAsync` behaves like the administer guard: a refused user gets
`false` and the page's access-lost callback runs once. `GuardContributeAsync` is different on purpose: a user who
cannot see the project gets `false` and the callback runs once, but a user who sees the project and may only read it
gets `false` without the callback, so that a page can keep what it loaded and show that the access is read-only. The
guard decides with one pass: it asks the contribute question first and asks visibility only when work is refused, so
a revocation between the two lookups is reported as lost rather than as read-only.

#### Error Handling

Failures are `DomainRuleException`: `ProjectNotFound` for anything not visible, `ActorNotAllowed` for a missing right
that the actor may know about. The `Can...` methods never throw for a denied actor and answer `false`. Constructing the
unit with a null store raises `ArgumentNullException`. Store failures propagate unchanged. The unit has no state, so a
failure leaves nothing to clean up.

#### Dependencies

- **IUserStore** — reads the user of an actor (injected).
- **IReviewStore** — reads projects and memberships (injected).

#### Callers

- **ProjectService**, **RepositoryService**, **ReviewService**, **SigningService**, **ThreadService**,
  **ChecklistService**, and **ReviewPlanner** — require access before they act, or filter what they return. Every
  operation that changes something in a project (a thread, a comment, a checklist answer, a slot, a signature, a
  review, a plan, and the member and repository pickers behind them) requires the right to work
  (`RequireContributeAsync`); the reads keep `RequireViewAsync`.
- **EvidenceIndexService** and **AttestationService** — require that the actor may see the project they read.
- **ProjectPage**, **NewMemberPage**, **NewMemberAccountPage**, **ProjectAuditPage**, **RepositoriesPage**, and
  **NewRepositoryPage** — decide what to offer to the signed-in user.
- **ServerHost** — registers the unit as a singleton for the pages and the API.
