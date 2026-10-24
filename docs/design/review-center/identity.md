## Identity

![Identity Structure](IdentityView.svg)

### Overview

The `Identity` subsystem lets a person sign in to the web server and tells the rest of the system who that person is.
It owns the connection between the local accounts of the `Core` subsystem and the ASP.NET Core authentication
framework: how a password is hashed, how the sign-in and sign-out requests are handled, what the authentication
cookie holds and when it is no longer accepted, and how the signed-in user is turned into the `Actor` that the Core
services record with a comment or a checklist answer. It contains two units: `PasswordHasher`, the implementation of
the Core password hasher interface on top of the password hasher of the ASP.NET Core shared framework; and `SignIn`,
the cookie authentication setup, the `POST /auth/login` and `POST /auth/logout` endpoints, the return-URL check, and
the conversion between the signed-in principal and an `Actor`, together with its small options type `AuthOptions`,
which is documented with `SignIn` as a supporting type. The types of the subsystem are internal to the application.
The rules about accounts (the password policy, the lockout, the last administrator) are not here: they belong to
`UserService` in the `Core` subsystem.

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
- *Contract*: `AddSignIn` registers cookie authentication, authorization, and the `Auth` options. `MapSignIn` maps
  the two endpoints below.
- *Constraints*: `AddSignIn` must be called before the application is built, and the application must use
  authentication and authorization before the endpoints that are protected.

**Login endpoint** (`POST /auth/login`): Receives the form of the login page.

- *Type*: HTTP `POST` of a form with the fields `username`, `password`, and `returnUrl`.
- *Role*: Provider.
- *Contract*: Checks the password through `UserService`. On success it issues the authentication cookie and redirects
  to the return URL when that is a path of this site, otherwise to `/`. On any failure it redirects to
  `/login?error=1` (with the safe return URL when it is not `/`); the failure is the same for an unknown user, a
  wrong password, a disabled account, and a locked account.
- *Constraints*: The antiforgery token is required; a request without a valid token is refused with HTTP status 400.

**Logout endpoint** (`POST /auth/logout`): Ends the session.

- *Type*: HTTP `POST` of a form with the antiforgery token.
- *Role*: Provider.
- *Contract*: Removes the authentication cookie and redirects to `/`.
- *Constraints*: The antiforgery token is required and is checked by the endpoint itself; a request without a valid
  token is refused with HTTP status 400 and the session is kept.

**Auth settings**: The `Auth` configuration section.

- *Type*: Configuration (`appsettings.json` and environment variables such as `Auth__SessionHours`).
- *Role*: Consumer.
- *Contract*: `SessionHours` is how many hours a sign-in lasts without activity (default 8).
- *Constraints*: A value that is not greater than 0 stops the start-up with an error.

**SignIn.GetActorAsync**: Used by the pages of the `Web` subsystem.

- *Type*: In-process .NET extension method on the authentication state provider.
- *Role*: Provider.
- *Contract*: Returns the `Actor` (user name and display name) of the signed-in user.
- *Constraints*: Throws `InvalidOperationException` when nobody is signed in.

**UserService (consumed)**: The `Core` service that checks passwords and reads users.

- *Type*: In-process .NET class.
- *Role*: Consumer.

### Design

Signing in is plain HTTP, not an event of the Blazor live connection. The login page is a server-rendered form that
posts to `/auth/login`; the endpoint checks the password with `UserService.AuthenticateAsync`, which applies the
lockout, and issues the authentication cookie. A cookie can only be set on an HTTP response, and a form post needs no
live connection, so signing in works before any connection exists. The two endpoints have their own paths because
`/login` is answered by the login page, which would otherwise compete with a `POST` endpoint of the same path.

The cookie holds the user name, the display name, and the role, and never the password or its hash. It is `HttpOnly`,
`SameSite=Lax`, and `Secure` whenever the request is https (so it also works over plain http during development); it
is a session cookie whose ticket slides over the `Auth:SessionHours` window. On every request the cookie is checked
against the user store through `UserService`: the cookie is rejected, and the user must sign in again, when the user
no longer exists, is disabled, or has a different role or display name than the cookie holds. Pages are protected by
authorization attributes of the `Web` subsystem. The cookie handler answers an anonymous request for a protected page
with a redirect to `/login`, and a signed-in user who lacks the needed role with HTTP status 403.

Passwords are hashed with the framework hasher (PBKDF2 with a random salt and an iteration count stored inside the
hash). The adapter exists because `Core` has no reference to the web framework: it defines the two-method
`IPasswordHasher` interface, tests of `Core` use a trivial fake, and this subsystem supplies the real one.

The pages never take the author of a comment or an answer from a form field. They ask for the `Actor` of the current
authentication state, which works in a statically rendered page and on the live connection alike, and pass it to the
Core services.

Known limitations, all deliberate for this slice: the lockout counts failures per user name, not per source address,
so someone who knows a user name can lock that account for 5 minutes at a time; a running live connection keeps the
authentication state it started with until the page is reloaded, so a user who is disabled or signed out elsewhere
keeps the open page working until then (every new request is checked again; the user administration actions are the
exception, as they re-check the stored user each time); and changing a password does
not end the other sessions of the user. Behind a proxy that ends TLS, the server must be told to trust the forwarded
headers so that the cookie is issued as `Secure`.
