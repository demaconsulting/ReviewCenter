### OidcSignIn

![Identity Structure](IdentityView.svg)

#### Purpose

`OidcSignIn` lets people sign in with a generic OpenID Connect (OIDC) provider, and lets a user of such an account
confirm their identity for a signature by signing in at the provider again. It registers the provider, maps the
endpoints that start and finish a sign-in and a confirmation, and checks that a confirmation is fresh and belongs to
the user who asked. It does not check passwords and does not decide who is allowed to sign: accounts are found or
created by `UserService`, signatures are checked by `SigningService`. The unit also owns the settings type
`OidcOptions` and its validator `OidcOptionsValidator`. Everything is off unless the settings enable it.

#### Data Model

`OidcSignIn` is an internal static class without state, so it is thread-safe. The values that belong to one started
sign-in travel in the protected state of the framework handler (the authentication properties), so neither the
provider nor the browser can change them.

**Constants**: the endpoints `/auth/oidc/login` (starts a sign-in), `/auth/oidc/complete` (ends it) and
`/auth/oidc/reauth` (starts a confirmation of identity); the callback `/signin-oidc` that the handler serves; the
scheme names `oidc` (the provider) and `ReviewCenter.External` (a short-lived cookie that hands the validated claims
from the handler to the complete endpoint); the query value `error=oidc` of the login page; and the query `reauth`
with the values `ok` and `failed` that tell a page how a confirmation ended. The hand-over cookie and a started sign-in
stay valid for 5 minutes. An `auth_time` may lie at most 60 seconds in the future.

**Properties of a started confirmation**: a marker, the user identity, the project, the review identifier and the
slot. They are what the proof is bound to later.

**OidcOptions** (supporting type): a settings object bound from the `Identity:Oidc` section, which comes from
`appsettings.json` or from environment variables such as `Identity__Oidc__Authority`. The client secret belongs in an
environment variable or a secret store. The type prints nothing. The settings are:

| Setting                | Type   | Default                | Rule when enabled                                       |
|------------------------|--------|------------------------|---------------------------------------------------------|
| `Enabled`              | bool   | false                  | Everything below is read only when true                 |
| `Authority`            | string | empty                  | Required, at most 512 characters, https                 |
| `ClientId`             | string | empty                  | Required, at most 256 characters, no spaces             |
| `ClientSecret`         | string | empty                  | Required (confidential client), at most 512             |
| `Scopes`               | string | `openid profile email` | Must hold `openid`, at most 20 scopes of 128 characters |
| `DisplayName`          | string | `OpenID Connect`       | Required, at most 50 characters                         |
| `UsernameClaim`        | string | `preferred_username`   | Required, at most 64 characters, no spaces              |
| `RequireEmailVerified` | bool   | false                  | The claim `email_verified` must be `true`               |
| `ReauthMaxAgeSeconds`  | int    | 120                    | From 10 to 900                                          |

The scopes together hold at most 512 printable ASCII characters. `Authority` may use http only for a loopback host
(`localhost` or a loopback address) and has no user, query or fragment. Text settings have no control characters.

#### Key Methods

**AddOidcSignIn**: Registers the provider when sign-in with a provider is enabled.

- *Parameters*: `IServiceCollection services`, `IConfiguration configuration` — a null value raises
  `ArgumentNullException`.
- *Returns*: the same service collection.
- *Postconditions*: When `Identity:Oidc:Enabled` is not true nothing is registered. Otherwise the hand-over cookie
  scheme and the `AddOpenIdConnect` scheme are added. The handler is configured when it is first used, after the
  validator has run: authorization code flow (`response_type=code`, query response mode) with PKCE, `SaveTokens` off,
  no call of the user-info endpoint, claims kept as sent (no mapping) with the issuer claim kept, the configured
  scopes, `RequireHttpsMetadata` off only for a loopback http authority, and correlation and nonce cookies that work
  on plain http in development. The handler checks state, nonce, signature, issuer, audience and lifetime of the
  identity token.

**MapOidcSignIn**: Maps the endpoints, only when sign-in with a provider is enabled; otherwise the addresses are
answered like any unknown address.

- `POST /auth/oidc/login` (antiforgery token required): reduces the return address with `SignIn.SafeReturnUrl` and
  challenges the provider; the way back is the complete endpoint with that address.
- `GET /auth/oidc/complete`: authenticates the hand-over cookie and removes it whatever happens. It then refuses (all
  to the login page with `error=oidc`, one message that does not tell the cause) when there are no claims, when
  `RequireEmailVerified` is set and the e-mail address is not verified, or when `UserService.SignInExternalAsync`
  refuses. The account is found by issuer and subject only; the user name claim (else the part of the e-mail address
  before the `@`, else the subject) only suggests the name of a new account. A new account has no project, no system
  administrator right and no password. On success the normal session cookie is issued (not persistent) and the browser
  is sent to the local return address. Disabled accounts get the same answer as any failure; the audit trail says why.
- `POST /auth/oidc/reauth` (signed-in user and antiforgery token required): takes the project, review identifier,
  slot and return address. It asks `SigningService.RequireConfirmableAsync` whether this user may sign this slot; a
  refusal, a missing session value or a review identifier that is too long goes back to the local address with
  `reauth=failed` and nothing is sent to the provider. Otherwise the binding is stored in the authentication properties
  and the provider is challenged.

**Handler events**:

- *Redirect*: for a confirmation only, adds `prompt=login` and `max_age=0` to the request.
- *Message received*: counts the callback with `SignInThrottle.TryBeginAddressOnly` (client address only, no name
  counter, so one shared address cannot block every person behind it before the address limit) and refuses it
  with the reason `throttled`, without reading it, when the throttle refuses.
- *Ticket received*: a normal sign-in is handed on to the complete endpoint and gives its throttle slot back. A
  confirmation is answered here, so the claims never replace the session. The stored user of the session cookie is
  read; when it is gone or disabled the reason is `session-ended`. Otherwise `VerifyReauth` decides; on success a proof
  is issued for the user, project, review and slot and the browser returns to the local address with `reauth=ok`. A
  refused confirmation is recorded with `UserService.RecordExternalReauthFailureAsync` (the reason class of
  `VerifyReauth`, unchanged) and the browser returns with `reauth=missing-auth-time` or `reauth=stale-auth-time` for
  those two reasons, and with `reauth=failed` for every other reason, so the page can say what an administrator has to
  fix without telling more about any other cause.
- *Remote failure*: the failure text only selects a reason class, never stored or shown.

**VerifyReauth**: Decides whether a fresh sign-in is a confirmation of the user who asked.

- *Parameters*: the stored `User`, the validated claims (may be null), the authentication properties of the started
  confirmation, the current time and the `OidcOptions`.
- *Returns*: `null` when every condition holds, otherwise the first reason class: `not-external`, `other` (no claims,
  or the binding is missing, is for another user or is not numeric), `issuer-mismatch`, `subject-mismatch`,
  `missing-auth-time` (absent, not a whole number, negative, or outside the range of dates) or `stale-auth-time`
  (older than `ReauthMaxAgeSeconds`, or more
  than 60 seconds in the future).
- *Postconditions*: The issuer and the subject must equal the stored ones of the account, not just be valid ones. The
  method reads nothing else and changes nothing, so it fails closed on any doubt.

**ClassifyFailure**: Maps a failure of the handler to one of the reason classes `provider-error`, `state-or-nonce`,
`token-invalid` and `other`.

**WithReauth**: Sets exactly one `reauth` query value on a local address and drops any fragment.

**OidcOptionsValidator.Validate**: Does nothing while `Enabled` is false. Otherwise it reports only the first problem,
checking in the order authority, client identifier, client secret, scopes, display name, user name claim and age. A
message names the setting (for example `Identity:Oidc:Authority`) and the rule, and never a value, so the secret cannot
reach a log. A null options object raises `ArgumentNullException`.

#### Error Handling

Settings are validated when the server starts (see the `ServerHost` design), so a wrong value stops the start with a
message that names the setting. At run time every failure of a sign-in ends the same way for the person: a redirect to
the login page with one generic message and no cookie. A failed confirmation goes back to the page of the signature
with `reauth=missing-auth-time` or `reauth=stale-auth-time` when that is the reason, and with `reauth=failed` for
every other reason (the recorded reason class is the same in every case). Only a fixed reason class is recorded
through `UserService`: `auth.oidc-sign-in-failed` with
`no-hand-over`, `email-not-verified`, `invalid-identity`, `throttled`, `session-ended` or a handler class, and
`signature.reauth-failed` for a refused confirmation of a known user. Successful sign-ins are recorded as
`auth.oidc-sign-in` and the first creation of an account as `account.oidc-created` (by `UserService`, with only the
host of the issuer). No token, claim, error text, code or secret is ever logged or recorded. Return addresses are
reduced to local ones when a sign-in starts and again when it ends, so the sign-in is no open redirect. Sign-out is
local only: the provider is never called. The signing step spends the proof once (see `ReauthProofStore`); without
one it is refused and recorded. A `DomainRuleException` of the start of a confirmation is handled locally; other
exceptions propagate to the framework.

#### Dependencies

- **OidcOptions** and **OidcOptionsValidator** (this unit) — the settings and their start-up check.
- **UserService** (Core) — account matching by issuer and subject, account creation, and the audit records.
- **SigningService** and **ReauthProofStore** (Core) — the check that a user may confirm a slot, and the single-use
  proof that the signing step spends.
- **SignInThrottle** and **SignIn** (Identity) — the throttle per address, the safe return address, the client address,
  and the session principal.
- **Microsoft.AspNetCore.Authentication.OpenIdConnect** (OTS) — the protocol handler; see the OTS design.
- **TimeProvider** (.NET) — the clock for `auth_time`; tests supply a movable one.

#### Callers

- **ServerHost** — calls `AddOidcSignIn` and `MapOidcSignIn`, and reads the options once at start-up so the validator
  runs. `SignIn.AddSignIn` registers the options and the validator.
- **LoginPage**, **ReviewPage**, **AccountPage** and **UsersPage** — read the settings to show the button, the
  confirmation button, and the kind of account; their forms post to the endpoints above.
- Tests — call `VerifyReauth`, `WithReauth` and the validator directly, and the endpoints through a fake provider.
