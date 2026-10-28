### ApiTokenAuthentication

![Api Structure](ApiView.svg)

#### Purpose

`ApiTokenAuthentication` makes a personal access token the way to sign in to the API, and only to the API. Its single
responsibility is to turn a token in the `Authorization` header (a bearer token, or the password of HTTP Basic
credentials, which is what ReviewMark sends) into the principal of the token's owner, or into one generic
refusal, and to leave every other kind of request unauthenticated by this scheme. It also decides the one exception to
"a token is needed": a project can open its evidence index and its attestation PDFs to a caller that sends no
credentials at all (the unit holds the policies, the handler, and the limiter for that exception).

#### Data Model

The unit is three files.

`ApiTokenAuthentication` is a static class. It holds the constants (the scheme `ReviewCenterToken`, the policies
`Api`, `ApiEvidenceIndex`, and `ApiAttestation`, the claim `rc_auth` with the value `pat`, and the claim
`rc_token_id`) and the extension method `AddApiTokenAuthentication`.

`AnonymousEvidenceAccess.cs` holds `AnonymousEvidenceRequirement`, an authorization requirement that carries a function
from the evidence settings of a project to the one switch that the route depends on (`AnonymousIndex` or
`AnonymousAttestations`), and `AnonymousEvidenceHandler`, the stateless handler that decides it. The handler uses only
`EvidenceSettingsService`.

`AnonymousRateLimit.cs` holds `AnonymousRateLimit`, a singleton that counts the requests of each client address in a
fixed window of one minute and allows 30 requests in a window. The counters are in memory (a concurrent dictionary of
one small counter object per address, locked while it is changed), so they start empty after a restart, and the
dictionary is pruned of ended windows when it holds more than 4096 addresses. The address is the one that
`SignIn.ClientAddress` gives, the address of the direct connection (`Connection.RemoteIpAddress`). Behind a reverse
proxy without `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` it is the address of the proxy, so all anonymous callers share
one counter and the limit is reached sooner (it fails safe). This is the same limitation as for `SignInThrottle`
(see `identity/sign-in-throttle.md`).

`ApiTokenHandler` is the authentication handler of the scheme. A new instance serves each request. It holds the three
services it uses (`PersonalAccessTokenService`, `SignInThrottle`, and `AuditService`) and no state of its own; the
marker that a request was throttled is an item of the request. The principal it builds has the claims name and display
name of the owner, the `SystemAdmin` role claim only when the owner is a system administrator (the same claim types
as the cookie principal), `rc_auth`, and `rc_token_id` (the display id
of the token, which is not secret).

#### Key Methods

**AddApiTokenAuthentication**: Registers the scheme, the policies, the handler, and the limiter.

- *Parameters*: The service collection. Must not be null.
- *Returns*: The same collection.
- *Postconditions*: The scheme is registered as an additional scheme; the default and challenge schemes (the cookie) are
  unchanged. The authorization policy `Api` requires the scheme and an authenticated user. The policies
  `ApiEvidenceIndex` and `ApiAttestation` use the same scheme and, instead of "authenticated user", the requirement
  `AnonymousEvidenceRequirement` with the switch `AnonymousIndex` or `AnonymousAttestations`. `AnonymousEvidenceHandler`
  and `AnonymousRateLimit` are registered as singletons.
- *Preconditions*: `EvidenceSettingsService` and `TimeProvider` are registered.

**AnonymousEvidenceHandler.HandleRequirementAsync**: Decides the requirement of the two open policies.

- *Postconditions*:
  - A request whose token was accepted (an authenticated user) satisfies the requirement. The route then applies the
    normal project rules to the owner of the token, so a non-member still gets the 404 of a missing project.
  - A request without an authenticated user that carries an `Authorization` header of any kind never satisfies the
    requirement, so a wrong, expired, or revoked token is still answered with the 401 challenge and is never treated as
    anonymous.
  - A request with no `Authorization` header and a `projectId` route value reads the effective evidence settings of the
    project (`EvidenceSettingsService.GetEffectiveAsync`) and satisfies the requirement only when the switch of the
    route is on. A project that does not exist, and a project whose switch is off, do not satisfy it, so both get the
    same generic 401: the answer never tells whether the project exists.
  - A request without a usable `projectId` route value does not satisfy the requirement.
- *Notes*: Each switch opens only its own route; the handler holds no state.

**AnonymousRateLimit.TryAcquire**: Counts a request of an address.

- *Parameters*: The client address (`null` when it is not known, which is counted under one shared name).
- *Returns*: `true` when the request is within the limit, and in an out parameter how long until the window ends.
- *Postconditions*: The first request of an address, or the first request after a window has ended, starts a new window
  with a count of one. Every later request in the window adds one. The 31st and later requests of a window are refused.
  The clock is the `TimeProvider`, so a test can move it.

**HandleAuthenticateAsync**: Authenticates a request.

- *Preconditions*: `SignInThrottle`, `PersonalAccessTokenService`, and `AuditService` are registered.
- *Postconditions*:
  - The token is taken from the `Authorization` header. With the scheme `Bearer` it is the value. With the scheme
    `Basic`
    the value is decoded from Base64 as UTF-8 text and split at the first colon, and the token is the text after it (the
    user name is ignored, and a password may hold colons); text that is not valid Base64 or has no colon gives no
    result.
    A header with another scheme, a missing header, or an empty token gives no result. It is not an attempt and is not
    counted; the challenge answers 401. After this step Basic and Bearer take the same path: the same throttle, the same
    validation, the same audit events, and the same generic refusal, so a wrong Basic password is not distinguishable
    from a wrong bearer token.
  - Otherwise the attempt is counted first, with the client address from `SignIn.ClientAddress` and the throttle name
    `api-token:` followed by the address. The name is per address on purpose, so one client with a wrong token cannot
    block the others. When the throttle refuses, the request is marked, nothing is checked, a
    `token.use-throttled` event is recorded, and the result is a failure that the challenge answers with 429.
  - The token is validated by `PersonalAccessTokenService.AuthenticateAsync`. On a refusal a `token.use-failed` event is
    recorded and the result is a failure (the attempt stays counted).
  - On success the reservation of the throttle is released as a success, so a good token does not use up the budget of
    the
    address, and the result is the principal of the owner.
  - The audit events are best effort and coalesced per address and minute (`AuditService.TryRecordCoalescedAsync`), and
    their detail names the address, the reason, and the display id of the token when a stored token matched; it never
    holds the value of a token.

**HandleChallengeAsync**: Answers a request that needs a token and has none that was accepted.

- *Postconditions*: A problem description with status 429 when the request was throttled; otherwise status 401 with the
  two `WWW-Authenticate` headers, `Bearer` and `Basic realm="ReviewCenter", charset="UTF-8"` (a client such as
  ReviewMark does not answer a challenge, it only sends Basic in advance). The 401 is the same for every reason.

**HandleForbiddenAsync**: Answers a 403 problem description for a user who is not allowed.

#### Error Handling

A refusal is an ordinary answer, not an exception. The reason is for the audit trail only: no header, a malformed token,
an unknown token, an expired token, a revoked token, a disabled owner, a locked-out owner, and an owner who must change
the password (audited as "owner must change password") all give the same 401. The
audit write is best effort: a failure is logged by `AuditService` and does not change the answer. An exception of the
token
service or the store is the generic error of the host. No token value, secret part, or text with the token prefix is
written to the log for any outcome (a token is created, accepted, or refused); the audit trail and the log name a token
by its display id only.

#### Dependencies

- **PersonalAccessTokenService**, **TokenValidation**, **TokenFailure** — Core; the checks of a token.
- **SignInThrottle** — Identity; the count of attempts per address.
- **SignIn** — Identity; the client address (`ClientAddress`) and the display name claim type.
- **AuditService**, **AuditDraft** — Core; the audit events of a refusal.
- **EvidenceSettingsService**, **EvidenceSettings** — Core; the per-project switches that open the two routes.

#### Callers

- **ServerHost** — registers the scheme and the policies.
- **ApiEndpoints** — its route group requires the `Api` policy, and the two routes of the second group require
  `ApiEvidenceIndex` and `ApiAttestation`; its filter of that group calls `AnonymousRateLimit`.
