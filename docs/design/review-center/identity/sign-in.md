### SignIn

![Identity Structure](IdentityView.svg)

#### Purpose

`SignIn` is the cookie sign-in of the web server. Its single responsibility is to establish who the user of a request
is: it configures cookie authentication and authorization, handles the login and logout requests, makes sure a return
address stays on this site, validates the authentication cookie on every request, and converts between the signed-in
principal and the `Actor` that the Core services record. It also owns the small options type `AuthOptions`.

#### Data Model

`SignIn` is a stateless static class, so it is thread-safe.

**CookieName**: `string` (constant) — `ReviewCenter.Auth`; the name of the authentication cookie.

**LoginPath**: `string` (constant) — `/login`; the page with the login form, to which anonymous requests for
protected pages are redirected.

**LoginEndpoint**, **LogoutEndpoint**: `string` (constants) — `/auth/login` and `/auth/logout`; the paths of the two
`POST` endpoints.

**DisplayNameClaimType**: `string` (constant) — `display_name`; the claim that carries the display name.

**AdminRole**: `string` (constant) — `Admin`; the role name that the user administration page requires.

**Claims**: the principal in the cookie has three claims: the user name (the name claim), the display name, and the
role (the role claim). It never has the password or its hash.

**AuthOptions** (supporting type): a settings object bound from the `Auth` configuration section, with one setting,
`SessionHours` (`int`, default 8, must be greater than 0): how many hours a sign-in lasts without activity. The
section is read from `appsettings.json` and from environment variables such as `Auth__SessionHours`.

**Cookie settings**: `HttpOnly`; `SameSite=Lax`; secure policy `SameAsRequest`, so the cookie is `Secure` for an https
request and also works on plain http during development; a session cookie (not persistent); a sliding expiry of
`SessionHours` hours.

#### Key Methods

**AddSignIn**: Registers authentication and authorization.

- *Parameters*: `IServiceCollection services`, `IConfiguration configuration` — holds the `Auth` section.
- *Returns*: `IServiceCollection` — the same collection.
- *Preconditions*: Neither argument is null.
- *Postconditions*: The cookie scheme is the default scheme, the cookie has the settings above, the `Auth` options are
  bound and validated, and authorization is registered.

An anonymous request for a protected page is redirected to `LoginPath` (the original address travels in the standard
`ReturnUrl` query value). A signed-in user without the needed role does not get a redirect: the access-denied
redirect is replaced by HTTP status 403 with a short "Access denied" page that links back to `/projects`. No
fallback authorization policy is set, so static assets, `/health`,
`/`, and `/login` stay anonymous and each protected page declares its own requirement. The session length is taken
from the options when the cookie options are first needed, and an invalid value is reported when the server starts.

**MapSignIn**: Maps the two endpoints.

- *Parameters*: `IEndpointRouteBuilder endpoints`.
- *Returns*: `IEndpointRouteBuilder` — the same builder.

**POST /auth/login**: Takes the form fields `username`, `password`, and `returnUrl`; the framework binds them as form
parameters, which makes it require the antiforgery token. The return URL is reduced with `SafeReturnUrl`. The user
name and password are checked with `UserService.AuthenticateAsync`. On success the principal (see Claims) is signed in
with the cookie scheme as a session cookie and the response redirects to the safe return URL. On failure the response
redirects to `/login?error=1`, adding `&ReturnUrl=` with the escaped safe return URL when it is not `/`, so a second
attempt still leads to the place the user wanted. The failure is the same for an unknown user, a wrong password, a
disabled account, and a locked account, so the response does not reveal which user names exist or that an account is
locked.

**POST /auth/logout**: Removes the cookie and redirects to `/`. This endpoint binds no form field, so the framework
does not check the antiforgery token by itself; the endpoint validates it through the antiforgery service and answers
HTTP status 400 without changing the session when the token is missing or wrong.

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
- *Postconditions*: The result always comes from the authentication state, never from a form value.

**Principal validation**: On every request that carries the cookie, the cookie is checked against the user store
(`UserService.GetAsync`). The cookie is rejected, and removed, when the user no longer exists, is disabled, or when
the role or the display name in the cookie differs from the stored one; the user then has to sign in again and gets
the current values. A changed password does not reject cookies, so it does not end other sessions of the user.

#### Error Handling

Null arguments raise `ArgumentNullException`. `GetActorAsync` raises `InvalidOperationException` ("Nobody is signed
in.") when the state has no authenticated user or lacks a claim; a protected page cannot be reached anonymously, so
this signals a programming error. Rule failures of the login are not errors: they are the generic failure redirect.
An invalid `SessionHours` value makes the options validation fail, which `ServerHost` reports when the server
starts. The login and logout endpoints do not log the password or the hash.

#### Dependencies

- **UserService** (Core) — checks the password and reads the user for the per-request validation.
- **Actor** and **UserRole** (Core) — the type produced for the pages and the role names of the claims.
- **ASP.NET Core shared framework** — cookie authentication, authorization, antiforgery, and the Blazor
  authentication state; see the ASP.NET Core integration design in the OTS chapter.

#### Callers

- **ServerHost** — calls `AddSignIn` while registering the services and `MapSignIn` while mapping the endpoints, and
  enables the authentication and authorization middleware.
- **LoginPage** — posts its form to `/auth/login` and uses `SafeReturnUrl`.
- **App** (`MainLayout`) — posts its logout form to `/auth/logout`, and reads the signed-in user for the header.
- **ReviewPage**, **ChecklistPage**, and **AccountPage** — call `GetActorAsync` (the account page to know whose
  password to change). **ProjectsPage**, **ProjectPage**, **ReviewPage**, **ChecklistPage**, and **AccountPage**
  require a signed-in user, and **UsersPage** requires the role `Admin`, through authorization attributes that this
  unit's registration makes effective.
