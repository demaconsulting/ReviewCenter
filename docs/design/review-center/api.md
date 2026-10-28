## Api

![Api Structure](ApiView.svg)

### Overview

The `Api` subsystem is the way for a script or a pipeline to work with ReviewCenter without a browser. It is a small
JSON
interface under `/api` that reads the projects its user may see and their reviews, creates a draft review from
uploaded files or from a registered
git repository, downloads the signed attestation of a complete review, and lists the completed reviews of a project as
the
evidence index that ReviewMark reads. It holds no domain rule: every endpoint calls
the same Core services as the pages, so the limits, the validation, the audit trail, and the record digest are the same
for both. It contains three units: `ApiTokenAuthentication`, the authentication scheme and the policy that accept a
personal access token and nothing else; `ApiEndpoints`, the routes, the request and answer types, and the mapping of a
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
  OpenAPI document.
  - `GET /api/projects` returns the projects that the owner of the token may see: all of them for a system
    administrator, and for anybody else only the projects they are a member of.
  - `GET /api/projects/{projectId}/reviews` returns the review summaries of a project, 404 when the project does not
    exist or the owner of the token may not see it.
  - `GET /api/projects/{projectId}/reviews/{reviewId}` returns the details of one review, with the slots, the counts,
    the
    approval state, and what is still missing.
  - `POST /api/projects/{projectId}/reviews` creates a draft review from `files` or from a `repository` (exactly one of
    them) and answers 201 with a `Location` header and the created review.
  - `GET /api/projects/{projectId}/reviews/{reviewId}/attestation.pdf` returns the signed PDF of a complete review.
  - `GET /api/projects/{projectId}/evidence/index.json` returns `{"reviews":[{id, fingerprint, date, result, file}]}`
    for the completed reviews of a project that were planned from a review-set (the index of a ReviewMark evidence
    source), 404 when the project does not exist or the owner of the token may not see it.
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
  owner has now.

**Token authentication**: The `ReviewCenterToken` scheme and the `Api` policy.

- *Type*: ASP.NET Core authentication handler and authorization policy.
- *Role*: Provider (the identity of the caller, to the endpoints).
- *Contract*: A request is authenticated only by a personal access token in the `Authorization` header, sent as a bearer
  token or as
  the password of HTTP Basic credentials (the user name is ignored). A valid token gives a principal with the
  user name and display name of the owner, the `SystemAdmin` role claim only when the owner is a system administrator,
  and the claims `rc_auth` (value `pat`) and `rc_token_id`. A request
  with
  no token, or with a token that is refused, gets 401 with `WWW-Authenticate` headers for `Bearer` and `Basic`, always
  with the
  same answer; too many refusals from one address get 429.
- *Constraints*: The scheme is not the default scheme; the cookie stays the default for the pages. The policy applies to
  the route group of the API and to nothing else, so a token never signs a user in on a page, and a cookie never
  authenticates a request under `/api`.

**OpenAPI document**: `GET /api/openapi.json`.

- *Type*: HTTP endpoint, JSON.
- *Role*: Provider.
- *Contract*: Without a token, the generated OpenAPI description of the routes above, with the HTTP bearer and Basic
  security
  schemes on every operation. The committed snapshot `docs/api/openapi.json` is the reviewed copy of it.
- *Constraints*: The document holds no secret and lists no server address.

**Core services (consumed)**: `PersonalAccessTokenService`, `ProjectService`, `ReviewService`, `SigningService`,
`ThreadService`, `ChecklistService`, `AttestationService`, `EvidenceIndexService`, and `AuditService`.

- *Type*: In-process .NET classes.
- *Role*: Consumer.
- *Contract*: Token validation, projects, reviews, the approval and counts of a review, the attestation, the evidence
  index, and the audit
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
expired, owner disabled or locked out), so the answer reveals nothing about whether a token ever existed. The reason is
recorded in the audit trail without the value of the token, as an event that is coalesced per client address, so that
a script with a wrong token does not flood the trail. Attempts are counted by the same in-memory throttle as the web
sign-in, under a key of their own, so a flood of wrong tokens answers 429 without locking out any user and without
touching the limits of the web sign-in.

**Errors**: Every failure of the endpoints is `application/problem+json`, so that a program can read the status and the
message in one way. A broken domain rule is mapped to one status in one place (`ApiProblems.StatusOf`): not found
rules to 404, conflicts to 409, an unusable or failing git to 503, 502, or 504, a not allowed actor to 403, and every
other rule to 400. A request body that cannot be read, or that has none or both of the sources of a review, is a 400
that names the field. An unexpected exception is the generic 500 of the host and shows no detail.

**The description of the interface**: The OpenAPI document is generated from the endpoints, so it cannot describe a
route that does not exist. The snapshot in `docs/api/` and a test that compares the two make every change of the
interface a visible change in a review. The document needs no token, because tools that generate a client fetch it
before they have one.

**Known limitations and planned follow-ups**: A token acts with the full read and create rights of its owner in the
projects of the owner; there are
no scopes. There is no paging of the lists. A request to create a review can carry at most 30 MiB.
