### RevalidatingAuthenticationStateProvider

![Identity Structure](IdentityView.svg)

#### Purpose

`RevalidatingAuthenticationStateProvider` keeps the sign-in of an open interactive page honest. Its single
responsibility is to re-check, at a fixed interval, that the user of an open page (a Blazor circuit, the live
connection of one browser tab) still exists, is enabled, and has the same role and display name as at sign-in, and to
end the sign-in state of that page when this is no longer so. The cookie is only checked when a request is made, but an
open interactive page makes none, so without this unit a user who is disabled, removed, or changed while a page is open
would keep that page.

#### Data Model

The class is internal and sealed. It derives from the revalidating state provider of the ASP.NET Core server framework,
which owns the loop, and is registered with a scoped lifetime, so there is one instance per circuit (and one per
statically rendered request, whose loop ends when the request ends).

**_users**: `UserService` — the user service that supplies the stored user. It is a singleton and is safe for
concurrent reads.

**_logger**: `ILogger` — writes the one warning of a failed lookup.

**_interval**: `TimeSpan` — the time between two checks, fixed for the life of the instance. The public constructor
takes it from `AuthOptions.RevalidationSeconds` (default 30, validated to be at least 5 at start-up); an internal
constructor takes it directly so tests can use a very short interval.

**State**: the authentication state itself (a `ClaimsPrincipal` with the name, display-name, and role claims of
the cookie) is held by the framework base class. The framework sets it from the cookie principal of the request when the
circuit starts, and raises its state-changed event, which starts the loop. The unit adds no state of its own and is
safe to touch from the thread-pool thread on which the loop continues, because it reads only the singleton service and
the immutable state it is given.

#### Key Methods

**RevalidationInterval** (property): Returns the interval of the checks. The framework waits this long, then validates,
and only while the state is authenticated. The first check therefore happens one interval after the state is set, and
the loop restarts on every change of the state.

**ValidateAuthenticationStateAsync**: Called by the framework loop; delegates to `IsCurrentAsync`. When the answer is
`false`, the framework replaces the state with an anonymous one and raises its state-changed event; components that
listen to the state (see `SessionGate` in the `App` unit) react to it.

**IsCurrentAsync**: Decides whether the signed-in user of a state still matches the user store.

- *Parameters*: `AuthenticationState state` — the state to check; `CancellationToken cancellationToken` — cancelled
  when the circuit is closed.
- *Returns*: `Task<bool>` — `true` when the user exists, is enabled, and has the same role and display name as in the
  state; `false` when not, when the state has no user name, and when the user could not be read.
- *Preconditions*: `state` is not null.
- *Postconditions*: Nothing is changed in the store or in the state; the framework acts on the result.

The method takes the user name from the state (none: `false`), reads the user with `UserService.GetAsync`, and applies
`SignIn.IsCurrent`, the single rule that the cookie validator also uses, so a page that is already open cannot be
judged more leniently than a new request.

#### Error Handling

The check **fails closed**. Any exception from reading the user, other than cancellation (for example a database
error), is caught, logged once at warning level with the user name and no secret, and answered with `false`, so the
sign-in state of the page ends. A transient error therefore loses what was typed on the page, but an unverifiable user
never keeps a page open.

A **cancelled** check is not a failure. When the token is cancelled, which happens when the circuit scope is disposed,
the method throws a `TaskCanceledException` that carries the token. The framework loop ends quietly on exactly this
exception; any other exception would be logged by the framework as an error and would end the sign-in state of a
provider that is being torn down. Null arguments raise `ArgumentNullException`.

Known behavior, documented rather than hidden: the page reacts one revalidation interval after the change at the latest,
so within that window actions that do not re-check the user are still accepted (the signing of a review and the actions
of the users page re-check the stored user each time and refuse at once); and the page header belongs to the
statically rendered layout, so it keeps the old name until the next navigation or reload.

#### Dependencies

- **UserService** (Core) — reads the stored user.
- **SignIn** (Identity) — `IsCurrent`, the shared rule.
- **AuthOptions** (Identity) — the interval, through `IOptions`.
- **ASP.NET Core shared framework** — the revalidating server authentication state provider base class, which owns
  the loop, and logging; see the ASP.NET Core integration design in the OTS chapter.

#### Callers

- **ASP.NET Core framework** — creates one instance per scope and runs the loop; `SignIn.AddSignIn` registers the class
  as the authentication state provider, replacing the framework default.
- **SessionGate** (App) — reads its state through the cascading authentication state and re-renders when the state
  changes. **MainLayout** (App) reads the state once when it is rendered and does not react to a change.
