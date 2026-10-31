### TotpSignIn

![Identity Structure](IdentityView.svg)

#### Purpose

`TotpSignIn` is the second step of a sign-in for an account that has a second factor. After the password was right
(see `SignIn`), the person types the current code of the authenticator app, or a recovery code, on a second page; this
unit maps the endpoint that receives it, sets and clears the cookie that carries the challenge, and starts the session
when the code is right. It does not check codes and does not decide who needs one: that is `TotpService`. The unit also
owns the settings type `TotpOptions` and its validator `TotpOptionsValidator`, and registers the settings for the rest
of the server. The step is not mapped when the settings turn the feature off.

#### Data Model

`TotpSignIn` is an internal static class without state, so it is thread-safe. The state of a started second step is the
challenge in `TotpChallengeStore`; the browser holds only its token.

**Constants**: the cookie `ReviewCenter.TotpChallenge`, the page `/login/totp` (`LoginPath`), the endpoint
`/auth/login-totp` (`CodeEndpoint`), and the query value `error=totp-expired` that the login page understands.

**Challenge cookie**: `HttpOnly`, `SameSite=Strict`, `Secure` when the request used https, path `/`, essential for the
application, and valid for the 5 minutes of the challenge. It holds the token and nothing else.

**TotpOptions** (supporting type): a settings object bound from the `Identity:Totp` section. `Enabled` is true by
default. `Issuer` is the name shown in the authenticator app; it defaults to `ReviewCenter`.
**TotpOptionsValidator** checks the issuer, also when the feature is off: it must have 1 to 64 characters after
trimming and contain no colon (the address format uses it as a separator) and no control character. The messages name
the setting and never contain its value. `AddTotpSignIn` registers the options with their validation and the Core
record `TotpSettings` (the switch and the issuer) as a singleton.

**Switching the feature off**: with `Enabled` false the setup is hidden, the second step is not mapped, signatures need
no code, and an enrolled account signs in with the password alone. This is a documented trade-off of the switch: it is
for an installation that must lock the feature away entirely. The enrollments are kept and apply again when the feature
is switched on.

#### Key Methods

**AddTotpSignIn**: Registers the options, the validator, and `TotpSettings`.

- *Parameters*: The service collection and the configuration.
- *Returns*: The service collection.

**MapTotpSignIn**: Maps `POST /auth/login-totp`. The code comes from a form field, so the antiforgery token of the form
is required.

- *Postconditions*, in this order:
  1. The challenge is read from the cookie (`Peek`). When there is none, the cookie is deleted and the person goes to
     the login page with `error=totp-expired`.
  2. The step is limited by `SignInThrottle` for the client address and the name of the challenge. A refused attempt is
     recorded coalesced as `auth.sign-in-throttled` with the detail `too many attempts, second step, name '<name>'` and
     the person goes back to `/login/totp?error=throttled`. A refusal does not use the challenge.
  3. `TotpService.CompleteSignInAsync` checks the code. `Failed` redirects to `/login/totp?error=1` (the challenge
     stays, up to its five wrong codes). Any other result that is not a success (an ended or used challenge, a locked
     account) deletes the cookie and goes to the login page with `error=totp-expired`.
  4. On success the throttle is told (`Succeeded`, which clears the counter of the name), the session cookie is issued
     with the principal of `SignIn` (not persistent), the challenge cookie is deleted, `auth.sign-in` is recorded with
     the detail `second factor: authenticator code` or `second factor: recovery code`, and the person goes to the
     account page when a password change is pending, otherwise to the safe return address of the challenge. The time of
     the sign-in has been stored by `TotpService` by then (`LastSignInUtc` of the user); this unit stores nothing.
- A wrong code is not given back to the throttle, so guessing codes uses up the same quota as guessing passwords.

**CookieOptions**: Builds the options of the challenge cookie for a request.

#### Error Handling

Every refusal is a redirect, never a message that tells what was wrong. A code, a recovery code, and the token are never
logged or recorded. An invalid `Issuer` is reported at startup by `ServerHost` (see the `ServerHost` unit).

#### Dependencies

- **TotpService** — starts and completes the second step and peeks the challenge.
- **SignInThrottle** — limits the attempts of the second step.
- **SignIn** — builds the principal of the session and the safe return address.
- **AuditService** — records the best-effort entries of the second step.
- **ASP.NET Core** — endpoints, antiforgery, cookies, options, and the authentication of the session.

#### Callers

- **SignIn** — uses `CookieOptions` when a right password starts the second step.
- **TotpLoginPage** (`Web` subsystem) — is the page that posts to the endpoint.
- **ServerHost** (`Hosting` subsystem) — calls `AddTotpSignIn` and `MapTotpSignIn`.
