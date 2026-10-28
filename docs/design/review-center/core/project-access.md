### ProjectAccess

![Core Structure](CoreView.svg)

#### Purpose

`ProjectAccess` is the single place that decides who may see a project and who may administer it. Every other unit
that restricts access asks it through the `IProjectAccess` interface and never reads memberships or the
administrator flag itself, so one rule holds on every page, route, and service.

#### Data Model

**ProjectRole**: an enumeration of what a member may do in one project: `User` (a member: sees the project, creates
reviews, comments, answers checklists, and signs) and `ProjectAdmin` (a member who also manages the repositories of
the project).

**ProjectMembership**: an immutable record of `UserId`, `ProjectId`, and `Role`. A user has at most one membership
per project; the pair (user, project) is its identity.

**ProjectMember**: an immutable record of `UserId`, `Username`, `DisplayName`, `Enabled`, and `Role`, the listing
form of a member for the people who manage a project.

**IProjectAccess**: the interface of the unit, so that the services depend on the rules and not on their
implementation.

**Level** (private): `View` or `Administer`, the answer of the internal decision.

The rules are:

| Actor | View | Administer |
| ----- | ---- | ---------- |
| missing, the command line, an unknown user name, a disabled user | nothing | nothing |
| enabled system administrator | every existing project | every existing project |
| member with role `User` | the projects of the memberships | nothing |
| member with role `ProjectAdmin` | the projects of the memberships | the projects of those memberships |
| enabled user without a membership | nothing | nothing |

The unit holds the injected `IUserStore` and `IReviewStore` and no other state. A user is found by `Actor.Username`
on every call and nothing is cached.

#### Key Methods

**IsSystemAdminAsync**: Tells whether the actor is an enabled system administrator.

- *Parameters*: `Actor? actor` and a cancellation token.
- *Returns*: `Task<bool>`.
- *Preconditions*: None; a null actor is allowed and is not an administrator.

**CanViewAsync** and **CanAdministerAsync**: Tell whether the actor may see or administer a project.

- *Parameters*: `Actor? actor`, `int projectId`, and a cancellation token.
- *Returns*: `Task<bool>`.
- *Postconditions*: Nothing is changed. A project that does not exist is not visible to anybody, a system
  administrator included.

**IsMemberAsync**: Tells whether a user, given by identity and not by actor, has a membership in a project. It is used
to check the reviewers of a slot and the signer, where the system administrator right must not count.

**ListVisibleProjectsAsync**: Lists the projects an actor may see, ordered as the store orders projects.

- *Returns*: All projects for an enabled system administrator; for an enabled user the projects of the memberships of
  the user; an empty list for any other actor.

**RequireViewAsync**: Throws unless the actor may see the project.

- *Postconditions*: The failure is a `DomainRuleException` with `ProjectNotFound` and one fixed message, the same
  whether the project does not exist or is not visible, so that existence never leaks.

**RequireAdministerAsync**: Throws unless the actor may administer the project.

- *Postconditions*: `ProjectNotFound` (as above) when the project is not visible, and `ActorNotAllowed` when the actor
  sees the project but is no administrator of it.

**RequireSystemAdminAsync**: Throws `ActorNotAllowed` unless the actor is an enabled system administrator.

**Decision**: The private `ResolveAsync` returns the enabled user behind an actor, or nothing for a missing actor, the
command line, a user name that no user has, and a disabled user. The private `RoleOfAsync` returns `Administer` for
an enabled system administrator when the project exists, otherwise the level given by the membership, or nothing when
there is none. An actor that came through a personal access token carries the user name of the token owner, so it
gets the access of the owner by this lookup and needs no rule of its own.

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
  **ChecklistService**, and **ReviewPlanner** — require access before they act, or filter what they return.
- **EvidenceIndexService** and **AttestationService** — require that the actor may see the project they read.
- **ProjectPage**, **RepositoriesPage**, and **NewRepositoryPage** — decide what to offer to the signed-in user.
- **ServerHost** — registers the unit as a singleton for the pages and the API.
