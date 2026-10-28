## Identity

![Identity Structure](IdentityView.svg)

### Overview

The `Identity` subsystem lets a person sign in to the web server and tells the rest of the system who that person is.
It owns the connection between the local accounts of the `Core` subsystem and the ASP.NET Core authentication
framework: how a password is hashed, how the sign-in and sign-out requests are handled, what the authentication
cookie holds and when it is no longer accepted, and how the signed-in user is turned into the `Actor` that the Core
services record with a comment or a checklist answer. It contains four units: `PasswordHasher`, the implementation of
the Core password hasher interface on top of the password hasher of the ASP.NET Core shared framework; `SignIn`,
the cookie authentication setup, the `POST /auth/login` and `POST /auth/logout` endpoints, the return-URL check, the
rule that decides whether a signed-in user is still valid, and the conversion between the signed-in principal and an
`Actor`, together with its small options type `AuthOptions`, which is documented with `SignIn` as a supporting type;
`SignInThrottle`, the in-memory limit on sign-in attempts per client address and user name, with its options type
`SignInThrottleOptions`; and `RevalidatingAuthenticationStateProvider`, which applies that rule to every open
interactive page at a fixed interval. The types of the subsystem are internal to the application.
The rules about accounts (the password policy, the lockout, the last system administrator) are not here: they belong to
`UserService` in the `Core` subsystem. The rules about which project a user may see are not here either: they belong to
`ProjectAccess` in the `Core` subsystem, which reads the memberships at every call. The cookie holds only the one right
that does not depend on a project, the system administrator right.

### Interfaces

**PasswordHasher (as IPasswordHasher)**: The password hashing used by `UserService`.

- *Type*: In-process .NET class implementing the Core interface.
- *Role*: Provider.
- *Contract*: Hashes a password with a fresh random salt into a self-describing text, and checks a password against
  such a text.
- *Constraints*: Stateless and safe for concurrent use. The password and the hash are never logged.

**SignIn.AddSignIn and SignIn.MapSignIn**: Used by the `Hosting` subsystem.

- *Type*: In-process .NET static extension methods.
- *Role*: Provider.
- *Contract*: `AddSignIn` registers cookie authentication, authorization, the `Auth` options, the `Identity:Throttle`
  options and the sign-in throttle, and the revalidating
  authentication state provider for open pages. `MapSignIn` maps the two endpoints below.
- *Constraints*: `AddSignIn` must be called before the application is built, after the Razor components are added (its
  provider registration has to be the last one), and the application must use authentication and authorization before
  the endpoints that are protected.

**Login endpoint** (`POST /auth/login`): Receives the form of the login page.

- *Type*: HTTP `POST` of a form with the fields `username`, `password`, and `returnUrl`.
- *Role*: Provider.
- *Contract*: Asks the sign-in throttle first. When the throttle refuses, it checks no password, sets no cookie and
  redirects to `/login?error=throttled` (with the safe return URL when it is not `/`); the answer is the same for every
  user name. Otherwise it checks the password through `UserService`. On success it issues the authentication cookie
  and redirects to the return URL when that is a path of this site, otherwise to `/`; a user whose password is
  temporary is redirected to `/account` whatever address was asked for. On any failure it redirects to
  `/login?error=1` (with the safe return URL when it is not `/`); the failure is the same for an unknown user, a
  wrong password, a disabled account, and a locked account.
- *Constraints*: The antiforgery token is required; a request without a valid token is refused with HTTP status 400.
  A successful sign-in is recorded as `auth.sign-in`, best-effort, and a refusal by the throttle as
  `auth.sign-in-throttled`, best-effort and at most once per minute and typed name.

**Logout endpoint** (`POST /auth/logout`): Ends the session.

- *Type*: HTTP `POST` of a form with the antiforgery token.
- *Role*: Provider.
- *Contract*: Removes the authentication cookie and redirects to `/`. The sign-out is recorded as `auth.sign-out`,
  best-effort, before the cookie is removed.
- *Constraints*: The antiforgery token is required and is checked by the endpoint itself; a request without a valid
  token is refused with HTTP status 400 and the session is kept.

**Auth settings**: The `Auth` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables such as `Auth__SessionHours` and
  `Auth__RevalidationSeconds`).
- *Role*: Consumer.
- *Contract*: `SessionHours` is how many hours a sign-in lasts without activity (default 8). `RevalidationSeconds` is
  how many seconds pass between two checks of the signed-in user by an open page (default 30).
- *Constraints*: A `SessionHours` value that is not greater than 0, or a `RevalidationSeconds` value below 5, stops the
  start-up with an error that names the setting.

**Identity:Throttle settings**: The `Identity:Throttle` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables such as `Identity__Throttle__MaxFailuresPerName`,
  `Identity__Throttle__MaxFailuresPerAddress`, and `Identity__Throttle__WindowSeconds`).
- *Role*: Consumer.
- *Contract*: `MaxFailuresPerName` is how many sign-in attempts one client address may make for one user name in a
  window (default 5). `MaxFailuresPerAddress` is how many it may make over all names (default 20).
  `WindowSeconds` is the length of the window (default 300).
- *Constraints*: A limit that is not greater than 0, a limit per address that is lower than the limit per name, or a
  window that is not between 1 and 86400 seconds stops the start-up with an error that names the setting.

**RevalidatingAuthenticationStateProvider (as the authentication state provider)**: Used by the framework for every
open interactive page (circuit).

- *Type*: In-process .NET class derived from the revalidating state provider of the ASP.NET Core server framework.
- *Role*: Provider.
- *Contract*: After each interval, re-reads the signed-in user and keeps the sign-in state only while the user exists,
  is enabled, and has the same system administrator state and display name; otherwise the state becomes anonymous and
  the components that listen to it (`SessionGate`) react. A user that cannot be read counts as not valid (fail closed).
- *Constraints*: The first check happens one interval after the state is set; scoped, one instance per circuit.

**SignIn.GetActorAsync**: Used by the pages of the `Web` subsystem.

- *Type*: In-process .NET extension method on the authentication state provider.
- *Role*: Provider.
- *Contract*: Returns the `Actor` (user name and display name) of the signed-in user.
- *Constraints*: Throws `DomainRuleException` with the rule `ActorNotAllowed` and the message "You are no longer signed
  in, so nothing was saved. Reload the page to sign in again." when nobody is signed in.

**SignIn.GetActorOrNullAsync**: Used by the pages of the `Web` subsystem that must still render for an anonymous or
changed session.

- *Type*: In-process .NET extension method on the authentication state provider.
- *Role*: Provider.
- *Contract*: Returns the `Actor` of the signed-in user, or null when nobody is signed in.
- *Constraints*: Never throws `DomainRuleException`; the caller decides what a missing actor means (project access
  gives no access to a null actor).

**UserService (consumed)**: The `Core` service that checks passwords and reads users.

- *Type*: In-process .NET class.
- *Role*: Consumer.

**AuditService (consumed)**: The `Core` service that records sign-in and sign-out.

- *Type*: In-process .NET class.
- *Role*: Consumer.

### Design

Signing in is plain HTTP, not an event of the Blazor live connection. The login page is a server-rendered form that
posts to `/auth/login`; the endpoint checks the password with `UserService.AuthenticateAsync`, which applies the
lockout, and issues the authentication cookie. A cookie can only be set on an HTTP response, and a form post needs no
live connection, so signing in works before any connection exists. The two endpoints have their own paths because
`/login` is answered by the login page, which would otherwise compete with a `POST` endpoint of the same path.

The cookie holds the user name, the display name, and, only for a system administrator, the `SystemAdmin` role claim,
and never the password or its hash, and never a project role or a membership. A user whose account still has a
temporary password gets the claim `must_change_password` instead of the system administrator role (even when the
account is a system administrator), so the session cannot reach any protected data until the password is changed. It is
`HttpOnly`,
`SameSite=Lax`, and `Secure` whenever the request is https (so it also works over plain http during development); it
is a session cookie whose ticket slides over the `Auth:SessionHours` window. On every request the cookie is checked
against the user store through `UserService`: the cookie is rejected, and the user must sign in again, when the user
no longer exists, is disabled, or has a different system administrator state, temporary password state, or display
name than the cookie holds.
Pages are protected by
authorization attributes of the `Web` subsystem. The cookie handler answers an anonymous request for a protected page
with a redirect to `/login`, and a signed-in user who lacks the needed role (the system administrator role) with HTTP
status 403. What a user may see in a project is decided by `ProjectAccess` from the memberships at every call, so
the cookie does not need to change when a membership does.

Passwords are hashed with the framework hasher (PBKDF2 with a random salt and an iteration count stored inside the
hash). The adapter exists because `Core` has no reference to the web framework: it defines the two-method
`IPasswordHasher` interface, tests of `Core` use a trivial fake, and this subsystem supplies the real one.

The pages never take the author of a comment or an answer from a form field. They ask for the `Actor` of the current
authentication state, which works in a statically rendered page and on the live connection alike, and pass it to the
Core services.

Known limitations, all deliberate: the account lockout counts failures per user name, not per source address,
so someone who knows a user name can lock that account for 5 minutes at a time (the sign-in throttle limits by
address and name and does not change that); the throttle and the coalescing of audit events are kept in memory of one
process and are lost on restart, so they fit a single server instance (see the `SignInThrottle` design); and changing
a password does not end the other sessions of the user. Behind a proxy that ends TLS, the server must be told to
trust the forwarded headers so that the cookie is issued as `Secure`; the same setting decides which client address the
throttle sees. Without it the throttle sees the address of the proxy for every user. With
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` it sees the address in `X-Forwarded-For`, which the framework then accepts
from any sender; the server does not restrict which proxies it trusts. Keeping the header trustworthy is the
responsibility of the deployment: the proxy must overwrite the header and be the only route to the server. Otherwise
a client can choose its own address and avoid the per-address and per-name limits (the lockout of the user name
still applies). This is not mitigated in the code.

Further known limitations, all deliberate and not mitigated: the lockout is per user name, so someone who knows a
user name can keep that account locked for 5 minutes at a time, also from an address that the throttle allows (the
throttle limits how often one address can try and does not prevent this); and the response time of a failed sign-in
can differ slightly between a known and an unknown user (a failure on a known account also stores the failure counter
and an audit entry), so timing can in principle reveal which user names exist (see the `UserService` design). The
throttle holds at most 10,000 counters and evicts the oldest ones when it is full, so a distributed flood can push
out older counters (see the `SignInThrottle` design).

**Sign-in throttle**: Before the password is checked the login endpoint asks `SignInThrottle` whether the client
address has attempts left, once per name and once over all names. A refused attempt is not authenticated at all and
gets the throttled answer, which is the same for known and unknown names; the account lockout of `UserService` stays
in force for the attempts that are let through. Signature re-authentication is not throttled by address: the user is
already signed in, and the lockout applies.

**Audit events of sign-in and sign-out**: The two endpoints record events that change no data in the audit trail
through `AuditService.TryRecordAsync`: `auth.sign-in` after the cookie has been issued, and `auth.sign-out` before the
cookie is removed, with the actor taken from the server-side principal of the request, never from the form. The
login endpoint also records the refusal by the throttle, as `auth.sign-in-throttled` through
`AuditService.TryRecordCoalescedAsync`, at most once per minute and typed name. The
recording is awaited but best-effort: a failure is logged and never changes the response, so a broken audit trail
cannot lock everybody out. Failed sign-ins (a wrong password every time; disabled-account failures at most once per
minute, account and reason), attempts on locked accounts, and lockouts are recorded by `UserService`
(see its design), because the endpoint cannot tell the reasons apart and must not either. The client address is not
part of any audit entry: the entry has no place for it, and adding one would change the hash chain format. The
best-effort rule is a deliberate trade-off: an
attacker who can make the recording fail can sign in without an entry.

An open page (a live connection) makes no request, so the cookie check never reaches it. A scoped
`RevalidatingAuthenticationStateProvider` therefore re-reads the user every `Auth:RevalidationSeconds` seconds and
applies the same rule as the cookie check (`SignIn.IsCurrent`); when the user is disabled, removed, or changed, or
cannot be read, the sign-in state of the page becomes anonymous. Each protected page is a root component of its own
live connection, while the header and the router are served once, so only a component inside the page can react: the
`SessionGate` component of the `Web` subsystem replaces the page with a "Session ended" message. Two consequences are
documented behavior: the page reacts up to one revalidation interval after the change, and during that window actions
that do not re-check the user are still accepted (signing a review and the actions of the users page re-check the stored
user on every action and refuse at once); and the header keeps showing the old name until the next navigation or reload.
An action that meets an already ended state is refused by `GetActorAsync` with a message and saves nothing; every
action that changes something on the protected pages asks for the actor first, even where its Core service takes none.
