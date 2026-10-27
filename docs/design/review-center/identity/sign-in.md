### SignIn

![Identity Structure](IdentityView.svg)

#### Purpose

`SignIn` is the cookie sign-in of the web server. Its single responsibility is to establish who the user of a request
is: it configures cookie authentication and authorization, handles the login and logout requests, makes sure a return
address stays on this site, validates the authentication cookie on every request, and converts between the signed-in
principal and the `Actor` that the Core services record. It also owns the small options type `AuthOptions`, and it
asks `SignInThrottle` before it checks a password.

#### Data Model

`SignIn` is a stateless static class, so it is thread-safe.

**CookieName**: `string` (constant) — `ReviewCenter.Auth`; the name of the authentication cookie.

**LoginPath**: `string` (constant) — `/login`; the page with the login form, to which anonymous requests for
protected pages are redirected.

**LoginEndpoint**, **LogoutEndpoint**: `string` (constants) — `/auth/login` and `/auth/logout`; the paths of the two
`POST` endpoints.

**DisplayNameClaimType**: `string` (constant) — `display_name`; the claim that carries the display name.

**AdminRole**: `string` (constant) — `Admin`; the role name that the user administration page requires.

**ThrottledError**: `string` (constant) — `throttled`; the value of the `error` query of the login page that asks for
the "too many attempts" message (any other value asks for the generic message).

**Claims**: the principal in the cookie has three claims: the user name (the name claim), the display name, and the
role (the role claim). It never has the password or its hash.

**AuthOptions** (supporting type): a settings object bound from the `Auth` configuration section, with two settings.
`SessionHours` (`int`, default 8, must be greater than 0): how many hours a sign-in lasts without activity.
`RevalidationSeconds` (`int`, default 30, must be at least 5, the constant `MinimumRevalidationSeconds`): how many
seconds pass between two checks of the signed-in user by an open page; it is also the longest time a disabled, removed,
or changed user can keep an open page. The section is read from `appsettings.json` and from environment variables such
as `Auth__SessionHours` and `Auth__RevalidationSeconds`.

**Cookie settings**: `HttpOnly`; `SameSite=Lax`; secure policy `SameAsRequest`, so the cookie is `Secure` for an https
request and also works on plain http during development; a session cookie (not persistent); a sliding expiry of
`SessionHours` hours.

#### Key Methods

**AddSignIn**: Registers authentication and authorization.

- *Parameters*: `IServiceCollection services`, `IConfiguration configuration` — holds the `Auth` section.
- *Returns*: `IServiceCollection` — the same collection.
- *Preconditions*: Neither argument is null.
- *Postconditions*: The cookie scheme is the default scheme, the cookie has the settings above, the `Auth` options are
  bound and validated, the `Identity:Throttle` options (see `SignInThrottle`) are bound and validated, the
  `SignInThrottle` is registered as a singleton, authorization is registered, and
  `RevalidatingAuthenticationStateProvider` is registered as the scoped authentication state provider.

The provider is registered with a plain `AddScoped`, not `TryAddScoped`: the Razor components registration of the
framework registers its own default provider with `TryAdd`, and the host calls `AddSignIn` after it, so the last
registration is the one that wins. That ordering is a dependency of the host and is commented in the code.

An anonymous request for a protected page is redirected to `LoginPath` (the original address travels in the standard
`ReturnUrl` query value). The address is the raw request target as the client wrote it, so escaped characters of a
deep link (`%20`, `%23`, `%3F`, a literal `%2F` or `%25`) and its query come back unchanged; the decoded path would
turn a literal `%2F` of a file name into a separator. Where the server gives no raw target, each path segment of the
decoded path is escaped again. The value is reduced with `SafeReturnUrl`, so only a local path is kept, and a request
for `/` carries no return address. A signed-in user without the needed role does not get a redirect: the access-denied
redirect is replaced by HTTP status 403 with a short "Access denied" page that links back to `/projects`. No
fallback authorization policy is set, so static assets, `/health`,
`/`, and `/login` stay anonymous and each protected page declares its own requirement. The session length and the
revalidation interval are taken from the options when they are first needed, and an invalid value (a session length
that is not above 0, or a revalidation interval below 5 seconds) is reported when the server starts. The throttle
settings are validated the same way: each limit must be above 0, the limit per address must not be lower than the
limit per name, and the window must be between 1 second and one day; the error names the setting
(`Identity:Throttle:...`).

**MapSignIn**: Maps the two endpoints.

- *Parameters*: `IEndpointRouteBuilder endpoints`.
- *Returns*: `IEndpointRouteBuilder` — the same builder.

**POST /auth/login**: Takes the form fields `username`, `password`, and `returnUrl`; the framework binds them as form
parameters, which makes it require the antiforgery token. The return URL is reduced with `SafeReturnUrl`. The client
address is the address of the connection (see the limitations in the `SignInThrottle` design), with an IPv4 address
that is mapped into IPv6 shown as IPv4. First the endpoint asks `SignInThrottle.TryBegin` with the client address
and the typed name. When the throttle refuses, nothing else is checked, even a right password is not looked at, no
cookie is set, a `auth.sign-in-throttled` entry is recorded (see below), and the response redirects to
`/login?error=throttled`, with `&ReturnUrl=` as for a failure. The answer is the same for every user name, so it does
not reveal which names exist. Otherwise the user name and password are checked with
`UserService.AuthenticateAsync`. On success `SignInThrottle.Succeeded` gives the attempts back, the principal (see
Claims) is signed in with the cookie scheme as a session cookie and the response redirects to the safe return URL. On
failure the response redirects to `/login?error=1`, adding `&ReturnUrl=` with the escaped safe return URL when it is
not `/`, so a second attempt still leads to the place the user wanted. The failure is the same for an unknown user, a
wrong password, a disabled account, and a locked account, so the response does not reveal which user names exist or
that an account is locked. The throttled answer is the only different one, and it depends on the client address and
the typed name, not on the account.
After the cookie has been issued, the sign-in is recorded as `auth.sign-in` through `AuditService.TryRecordAsync`
(awaited, best-effort: a failure is logged and never changes the response).

**POST /auth/logout**: Removes the cookie and redirects to `/`. This endpoint binds no form field, so the framework
does not check the antiforgery token by itself; the endpoint validates it through the antiforgery service and answers
HTTP status 400 without changing the session when the token is missing or wrong. Before the cookie is removed, the
sign-out is recorded as `auth.sign-out` (best-effort, as for the sign-in), with the actor taken from the server-side
`HttpContext.User`, never from the form.

**SafeReturnUrl**: Reduces a requested return URL to one that stays on this site.

- *Parameters*: `string? returnUrl`.
- *Returns*: `string` — the URL when it is a path of this site, otherwise `/`.

A URL is kept only when it starts with a single `/`, is not followed by a second `/` or a `\` (browsers read `//host`
and `/\host` as another host), and contains no control characters. Anything else, including an empty or missing value
and an absolute URL, becomes `/`. This stops the login from being used to send a user to another site (an open
redirect). The login page applies the same check before it writes the value into the form, and the endpoint applies
it again because the form value is client-supplied.

**GetActorAsync**: Builds the `Actor` of the signed-in user.

- *Parameters*: `AuthenticationStateProvider provider` — the authentication state of the page (it works in a
  statically rendered page and on the live connection).
- *Returns*: `Task<Actor>` — the user name (from the name claim) and the display name (from the display-name claim).
- *Postconditions*: The result always comes from the authentication state, never from a form value. When the state has
  no authenticated user or lacks a claim, the method throws `DomainRuleException` with the rule `ActorNotAllowed`.

**IsCurrent**: The single rule that decides whether a signed-in principal still matches the stored user.

- *Parameters*: `ClaimsPrincipal? principal`, `User? user` — the user as stored now, or null when there is none.
- *Returns*: `bool` — `true` only when both are present, the user is enabled, and the role and the display name in the
  principal equal the stored ones.
- *Postconditions*: Pure; nothing is changed.

**Principal validation**: On every request that carries the cookie, the cookie is checked against the user store
(`UserService.GetAsync`) with `IsCurrent`. The cookie is rejected, and removed, when the user no longer exists, is
disabled, or when the role or the display name in the cookie differs from the stored one; the user then has to sign in
again and gets the current values. A changed password does not reject cookies, so it does not end other sessions of the
user. The periodic check of an open page (see the `RevalidatingAuthenticationStateProvider` design) uses the same
`IsCurrent`, so the cookie and an open page cannot drift apart.

#### Error Handling

Null arguments raise `ArgumentNullException`. `GetActorAsync` raises a `DomainRuleException` with the rule
`ActorNotAllowed` and the message "You are no longer signed in, so nothing was saved. Reload the page to sign in
again." when the state has no authenticated user or lacks a claim. That is a rule violation, not a programming error,
because an open page can lose its user between two clicks (the state ends when the user is disabled, removed, or
changed), and the pages already show a rule violation as a message next to the control that was used instead of failing.
Rule failures of the login are not errors: they are the generic failure redirect.
An invalid `SessionHours` or `RevalidationSeconds` value, or an invalid `Identity:Throttle` value, makes the options
validation fail, which `ServerHost` reports when the server starts. The login and logout endpoints do not log the
password or the hash. A failure to record an audit event is logged as a warning and swallowed (see the `AuditService`
unit for why). The throttled refusal is recorded as `auth.sign-in-throttled` through
`AuditService.TryRecordCoalescedAsync` (actor text `not signed in`, detail `too many attempts, name '<typed>'`, the typed
name cleaned and cut to the length of a user name, at most one entry per minute and lower-cased name); the client
address is not recorded, because the audit entry has no place for it (adding one would change the hash chain), so the
address is only in the web server log.

#### Dependencies

- **UserService** (Core) — checks the password and reads the user for the per-request validation.
- **AuditService** (Core) — records the sign-in, the sign-out, and the throttled refusals.
- **SignInThrottle** (Identity) — decides whether an attempt may go on, and takes attempts back after a success.
- **Actor**, **User**, **UserRole**, and **DomainRuleException** (Core) — the type produced for the pages, the user
  that `IsCurrent` compares with, the role names of the claims, and the refusal of `GetActorAsync`.
- **RevalidatingAuthenticationStateProvider** (Identity) — registered by `AddSignIn`; it calls `IsCurrent`.
- **ASP.NET Core shared framework** — cookie authentication, authorization, antiforgery, and the Blazor
  authentication state; see the ASP.NET Core integration design in the OTS chapter.

#### Callers

- **ServerHost** — calls `AddSignIn` while registering the services (after the Razor components) and `MapSignIn`
  while mapping the endpoints, and enables the authentication and authorization middleware.
- **RevalidatingAuthenticationStateProvider** — calls `IsCurrent`.
- **LoginPage** — posts its form to `/auth/login`, uses `SafeReturnUrl`, and uses `ThrottledError` to choose its message.
- **App** (`MainLayout`) — posts its logout form to `/auth/logout`, and reads the signed-in user for the header.
- **ReviewPage**, **ChecklistPage**, **AccountPage**, and **UsersPage** — call `GetActorAsync` (the account page to know
  whose password to change). **ProjectsPage**, **ProjectPage**, **ReviewPage**, **ChecklistPage**, and **AccountPage**
  require a signed-in user, and **UsersPage** requires the role `Admin`, through authorization attributes that this
  unit's registration makes effective.
