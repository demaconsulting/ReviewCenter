## Api

![Api Structure](ApiView.svg)

### Overview

The `Api` subsystem is the way for a script or a pipeline to work with ReviewCenter without a browser. It is a small
JSON
interface under `/api` that reads the projects its user may see and their reviews, creates a draft review from
uploaded files or from a registered
git repository, downloads the signed attestation of a complete review, and lists the completed reviews of a project as
the
evidence index that ReviewMark reads. The last two can also be read without a token when the project allows it. It
holds no domain rule: every endpoint calls
the same Core services as the pages, so the limits, the validation, the audit trail, and the record digest are the same
for both. It contains three units: `ApiTokenAuthentication`, the authentication scheme and the policies that accept a
personal access token and, on the two routes a project can open, a request with no credentials; `ApiEndpoints`, the
routes, the request and answer types, and the mapping of a
broken rule to an HTTP status; and `ApiDocument`, the OpenAPI description of the interface and its snapshot. The units
are internal to the application. The subsystem uses one OTS item, `Microsoft.AspNetCore.OpenApi`, for the description,
and the authentication and endpoint features of the ASP.NET Core shared framework.

The tokens themselves belong elsewhere: `PersonalAccessTokenService` and its store, in `Core` and `Storage`, create,
validate, and revoke them, and `AccountPage` of `Web` is where a user manages them. This subsystem only accepts them.

### Interfaces

**Endpoints**: The routes of the JSON API, mapped by `ApiEndpoints`.

- *Type*: HTTP, JSON (`application/json`) bodies, and a PDF.
- *Role*: Provider.
- *Contract*: Every route under `/api` requires a token (bearer, or the password of HTTP Basic credentials), except the
  OpenAPI document and the two routes that a project can open (see below).
  - `GET /api/projects` returns the projects that the owner of the token may see: all of them for a system
    administrator, and for anybody else only the projects they are a member of.
  - `GET /api/projects/{projectId}/reviews` returns the review summaries of a project, 404 when the project does not
    exist or the owner of the token may not see it.
  - `GET /api/projects/{projectId}/reviews/{reviewId}` returns the details of one review, with the slots, the counts,
    the
    approval state, and what is still missing.
  - `POST /api/projects/{projectId}/reviews` creates a draft review from `files` or from a `repository` (exactly one of
    them) and answers 201 with a `Location` header and the created review.
  - `GET /api/projects/{projectId}/reviews/{reviewId}/attestation.pdf` returns the signed PDF of a complete review. What
    it contains follows the evidence settings of the project. It can be read without a token when the project allows
    anonymous attestation PDFs.
  - `GET /api/projects/{projectId}/evidence/index.json` returns `{"reviews":[{id, fingerprint, date, result, file}]}`
    for the completed reviews of a project that were planned from a review-set (the index of a ReviewMark evidence
    source), 404 when the project does not exist or the owner of the token may not see it. It can be read without a
    token when the project allows an anonymous evidence index.
  - A request to one of these two routes without any `Authorization` header is served only when the evidence settings
    of the project switch that route on; otherwise it gets the same generic 401 as a missing token, whether or not the
    project exists. A request that carries a token that is refused is never treated as anonymous and gets the 401. A
    caller with a valid token who is not a member of the project gets the 404 of a missing project. Callers without a
    token are limited to 30 requests a minute for each client address (429 with `Retry-After`).
  - Every route that names a project, except the attestation, first asks `ProjectService` whether the owner may see
    the project, and a project the owner may not see gets the 404 problem, with the same body, as a project that does
    not exist. The attestation route gets the same effect from `AttestationService`, which checks the project before
    it reads the review.
  - Any other address under `/api`, with any standard HTTP method (`GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`,
    `OPTIONS`), answers a JSON problem 404.
  - A failure is a problem description (`application/problem+json`).
- *Constraints*: There is no route that signs, changes a reviewer slot, or creates or revokes a token; the actor of
  every
  call is the owner of the token, acting with the system administrator right and the project memberships that the
  owner has now. The exception is a call to one of the two open routes without a token, whose actor is nobody (the
  audit trail records "anonymous") and which can read only what the project has switched on.

**Token authentication**: The `ReviewCenterToken` scheme and the `Api`, `ApiEvidenceIndex`, and `ApiAttestation`
policies.

- *Type*: ASP.NET Core authentication handler and authorization policies.
- *Role*: Provider (the identity of the caller, to the endpoints).
- *Contract*: A request is authenticated only by a personal access token in the `Authorization` header, sent as a bearer
  token or as
  the password of HTTP Basic credentials (the user name is ignored). A valid token gives a principal with the
  user name and display name of the owner, the `SystemAdmin` role claim only when the owner is a system administrator,
  and the claims `rc_auth` (value `pat`) and `rc_token_id`. A request
  with
  no token, or with a token that is refused, gets 401 with `WWW-Authenticate` headers for `Bearer` and `Basic`, always
  with the
  same answer; too many refusals from one address get 429 with a `Retry-After` header. The two policies of the open
  routes accept, besides a valid
  token, a request with no `Authorization` header when the project switch of the route is on.
- *Constraints*: The scheme is not the default scheme; the cookie stays the default for the pages. The policies apply to
  the route groups of the API and to nothing else, so a token never signs a user in on a page, and a cookie never
  authenticates a request under `/api`.

**OpenAPI document**: `GET /api/openapi.json`.

- *Type*: HTTP endpoint, JSON.
- *Role*: Provider.
- *Contract*: Without a token, the generated OpenAPI description of the routes above, with the HTTP bearer and Basic
  security
  schemes on every operation, and an additional empty security alternative (no credentials) on the evidence index and
  the attestation operations, and on those only. The committed snapshot `docs/api/openapi.json` is the reviewed copy
  of it.
- *Constraints*: The document holds no secret and lists no server address.

**Core services (consumed)**: `PersonalAccessTokenService`, `ProjectService`, `ReviewService`, `SigningService`,
`ThreadService`, `ChecklistService`, `AttestationService`, `EvidenceIndexService`, `EvidenceSettingsService`, and
`AuditService`.

- *Type*: In-process .NET classes.
- *Role*: Consumer.
- *Contract*: Token validation, projects, reviews, the approval and counts of a review, the attestation, the evidence
  index, the evidence settings of a project, and the audit
  record of a refused token.
- *Constraints*: None.

### Design

**Why a separate token scheme and not the cookie**: A cookie is carried by a browser and can be sent by any page the
user visits, so an endpoint that accepts it must defend against forged requests. A token is carried only by a program
that has it on purpose. Keeping the two apart removes a class of attacks: a token is not accepted on any page and a
session cookie is not accepted on `/api`, so a forged cross-site request cannot reach the API and a leaked token cannot
open the user interface, change a password, or sign. The scheme is registered as an additional scheme, and the
authorization policy of the API names it explicitly, so the default of the pages is unchanged.

**A token is not a way to sign**: A signature records that a named person confirmed a review with the password. A token
can be held by a script, so a signature made with one would prove nothing about a person. Two independent measures keep
signing out of reach of a token. `ApiEndpoints` has no route that signs or changes a slot, and a test asserts that none
exists. `SigningService` refuses an actor that came in with a token for every operation that changes slots or
signatures, before any check, so a later route that forgot the rule would still be refused. A token can create a draft
review, which is only a draft until reviewers sign it in the web pages; the review records the user who created it and
that it was created through the API.

**Basic credentials for ReviewMark**: ReviewMark can send only HTTP Basic credentials, so the scheme also reads a token
from the
password of a Basic `Authorization` header and ignores the user name. Nothing else changes: the token goes through the
same
throttle, the same validation, the same audit events, and the same generic refusal, so Basic widens how a token can be
sent
and not what a token can do. The sign-in cookie and the web pages never accept Basic credentials, and a token is not a
way to
sign in anywhere but under `/api`. The Basic header is as secret as the bearer header: neither is logged or audited.

**Anonymous access is a decision of the project, per route**: A project administrator can switch on, in the evidence
settings of the project, an anonymous evidence index and anonymous attestation PDFs, each on its own (both are off by
default). This exists so that a pipeline or an auditor without an account can use the evidence the project chose to
publish. The exception is built so that it stays narrow. It is two routes in their own group with their own policies,
and a test enumerates the endpoints so that no third route can join them. The handler of the policy lets a request
through without credentials only when the request carries no `Authorization` header at all: a wrong or expired token is
therefore still a 401 and is never silently downgraded to an anonymous request, which would hide a broken pipeline.
A switch that is off, and a project that does not exist, give the same generic 401 as a missing token, so the answer
reveals nothing about the project. A caller with a valid token follows the normal rules (a non-member gets the 404 of a
missing project). The attestation that an anonymous request gets is made by the same code as any other, with the same
signature, and is recorded in the audit trail with the actor `anonymous`; what it contains follows the evidence
settings of the project. Because an attestation costs a signature, callers without a token are limited to 30 requests a
minute for each client address, in memory. A reverse proxy must pass the client address on (as for the web sign-in),
or all such callers share one count.

**The evidence index**: `GET /api/projects/{projectId}/evidence/index.json` turns the approvals of a project into the
evidence
source of ReviewMark, so that a pipeline can check that every review-set has a current review in ReviewCenter. The rule
for
the entries is in `EvidenceIndexService`; the endpoint adds the shape of the document and the absolute address of each
attestation, taken from the request, so that it is the address the client used. The index lists only reviews that were
planned from a review-set, because ReviewMark matches by the review-set id. A review that is created through this API
has no
review-set id and is not listed; an optional review-set id in the creation request is a possible follow-up.

**Project visibility is decided at every request**: A project may hold confidential work, so a token sees what its
owner sees and no more. A token carries no copy of the memberships: the principal holds only the user name and, for a
system administrator, the `SystemAdmin` claim, and `ProjectService` (through `IProjectAccess`) reads the user and the
memberships at every call. Taking a user out of a project, or the system administrator right away, therefore works at
once for a token that was handed out earlier. Every route that names a project, except the attestation, asks
`ProjectService.GetAsync` before it
does anything else, including before the body of a creation request is read, and answers a project that the owner may
not see exactly as a project that does not exist: the same 404 status and the same problem body, for a bearer token and
for Basic credentials. The attestation and the evidence index also go through guarded
services (`AttestationService`, `EvidenceIndexService`), so that no route to them skips the check. The list of
projects shows only the projects the owner may see.

**Authentication failures**: A refused token is answered in the same way for every reason (malformed, unknown, revoked,
expired, owner disabled or locked out, owner with a temporary password), so the answer reveals nothing about whether a
token ever existed. The reason is
recorded in the audit trail without the value of the token, as an event that is coalesced per client address, so that
a script with a wrong token does not flood the trail. Attempts are counted by the same in-memory throttle as the web
sign-in, against the limit of the client address only (20 in 300 seconds by default; there is no per-name counter), so a
flood of wrong tokens answers 429 with a `Retry-After` header. This does not lock the account of any user, but the
address counter is shared with the web sign-in from the same address, so sign-in and API calls from that address are
throttled for the rest of the window.

**Errors**: Every failure of the endpoints is `application/problem+json`, so that a program can read the status and the
message in one way. A broken domain rule is mapped to one status in one place (`ApiProblems.StatusOf`): not found
rules to 404, conflicts (including the removal of the last project admin and a change that the approval lock refuses) to
409, too many attempts to 429, an unusable
or failing git to 503, 502, or 504, a not allowed actor and a pending password change to 403, and every
other rule to 400. A request body that cannot be read, or that has none or both of the sources of a review, is a 400
that names the field. An unexpected exception is the generic 500 of the host and shows no detail.

**The description of the interface**: The OpenAPI document is generated from the endpoints, so it cannot describe a
route that does not exist. The snapshot in `docs/api/` and a test that compares the two make every change of the
interface a visible change in a review. The request schemas list as required only the members that the API requires
(the `id` and `title` of a review, and the `repositoryId` and `headRevision` of a repository source), because a client
generator would otherwise force clients to send the optional ones. The document needs no token, because tools that
generate a client fetch it
before they have one. Its two operations that a project can open list the empty security alternative, so that a
generated client knows that a call without credentials is possible there.

**Known limitations and planned follow-ups**: A token acts with the full read and create rights of its owner in the
projects of the owner; there are
no scopes. There is no paging of the lists. A request to create a review can carry at most 30 MiB.
