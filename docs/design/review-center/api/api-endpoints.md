### ApiEndpoints

![Api Structure](ApiView.svg)

#### Purpose

`ApiEndpoints` is the JSON interface of the system under `/api`. Its single responsibility is to turn an authenticated
request (or, for two routes that a project can open, a request without credentials) into a call of the Core services
and the result into an HTTP answer, with one way of reporting a problem. It contains no rule of its own except the
shape of a request.

#### Data Model

The unit is four files. `ApiEndpoints` is a static class with the extension method `MapApi` and the handlers of the
routes. `ApiModels` holds the types of the requests and answers, all immutable records: `ProjectDto`,
`ReviewSummaryDto`, `ReviewDetailDto` (with `SlotDto` and `ReviewCountsDto`), `CreateReviewRequest` (with
`UploadFileDto` and `RepositorySourceDto`), `CreatedReviewDto` (with `SkippedFileDto`), and `ProjectChecklistDto` (with
`ChecklistDefinitionDto`, `ChecklistSectionDto`, and `ChecklistItemDto`). The types are separate from
the Core types so that the shape of the API does not change when a Core class does. `ApiProblems` holds the mapping
from a broken rule to a status and the helpers that build a problem description and the actor of a call.

The answers carry only what a program needs: a summary has no file count and no details; the details of a review hold
the title, state, fingerprint, creator and the flag that the review was created through the API, the optional due date
(`dueDate`, the text `yyyy-MM-dd`, or null) and whether the review is overdue (`overdue`: the date is before today in
UTC and the review is not approved), the source of a review
from a repository, the approval state and record digest, the slots (the primary and alternate reviewer are user names,
and the signer is shown by display name), the counts of files, threads, and checklist answers, the blockers, and the
next
step. A summary carries `dueDate` and `overdue` as well, and the progress of the review: `approved`, `slotsRequired`,
`slotsSigned`, `slotsOutOfDate`, and `lastActivity` (the time of the newest audit entry of the review, or its creation
time). The list takes all of them from `ReviewProgressService` (the same service as the project page), which reads the
approvals and the last activity of all reviews of the project at once; the list endpoint reads the reviews of the project
once and hands that list to `ReviewProgressService.GetProgressAsync` (the overload that takes the list), so the reviews
are not read a second time; the fields were added and none was changed. The
details and the summary also carry `followsReviewId`, and the details the two counts of what was carried over.

#### Key Methods

**MapApi**: Maps the routes.

- *Parameters*: The endpoint route builder. Must not be null.
- *Returns*: The same builder.
- *Postconditions*: One route group under `/api` requires the `Api` policy of `ApiTokenAuthentication` and has a filter
  that turns a `DomainRuleException` into a problem result, so no route can be added open by accident. A second group
  under `/api`, named `open`, holds exactly two routes that a project can open to a caller without a token; it has the
  same filter, a second filter that limits the callers without a token (see below), and no group policy, because each
  of its two routes names its own policy. The routes are
  - `GET /api/projects` (the projects that the owner of the token may see),
  - `GET /api/projects/{projectId}/reviews` (the project number must be an integer),
  - `GET /api/projects/{projectId}/reviews/{reviewId}`,
  - `GET /api/projects/{projectId}/checklist` (the checklist template of the project),
  - `POST /api/projects/{projectId}/reviews`, which requires a JSON body (the OpenAPI description marks the body as
    required; the content type must be JSON, and a request with another content type or none is answered by a second
    `POST` route of lower priority with a 415 problem, before any body is read) up to a
    limit that is derived from `ReviewLimits` (the Base64 text of the largest upload, the path and JSON framing of
    every file at the largest number of files, and 64 KiB for the other fields; the services stay authoritative for the
    limits themselves), enforced by the server as a 413, and answers 201 with a `Location` header, and
  - in the `open` group, `GET /api/projects/{projectId}/reviews/{reviewId}/attestation.pdf` with the policy
    `ApiAttestation`, and
  - in the `open` group, `GET /api/projects/{projectId}/evidence/index.json` with the policy `ApiEvidenceIndex`.

  A test enumerates the endpoints under `/api` and asserts that these two, and no other, carry an open policy, so a
  third route cannot be opened by accident.

  The filter of the `open` group counts a request without an authenticated user, by client address, in
  `AnonymousRateLimit`; a request over the limit is answered with a 429 problem, a `Retry-After` header (whole seconds,
  at least 1), and `Cache-Control: no-store`, before the handler runs. A request with a valid token is not counted by
  this limiter (the token throttle of `ApiTokenAuthentication` counts refused tokens).

  A catch-all route for every standard HTTP method (`GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`) under
  `/api` is outside the group and open, so that an unknown address answers a JSON
  404 with or without a token and never falls through to the pages. It is excluded from the OpenAPI description and has
  the lowest priority, so every other route, including the 415 route, wins over it.

**Create a review**: The handler of the `POST` route.

- *Preconditions*: The request is authenticated as a token.
- *Postconditions*:
  - The handler checks the project first: `ProjectService.GetAsync` with the actor of the token answers nothing for a
    project that does not exist and for a project that the owner may not see, and the answer is then the 404 problem
    for a missing project, before the body is read.
  - The handler reads the body itself instead of letting the framework bind it, so that a body that is not JSON, is cut
    off, or has the wrong shape (for example `files` as a number) is a 400 problem that names the field `body`, with
    no text of the parser in it. A body over the size limit is not read and is answered 413 by the server.
  - A request with no body, or with both or neither of `files` and `repository`, is a 400 problem that names the
    field (`body` or `source`). A file whose content is not valid Base64 is a 400 that names the position
    (`files[i].contentBase64`) and never echoes the content.
  - The caller's numbers are bounded before they are used: more than `ReviewLimits.MaxFileCount` files is a 400
    validation problem for the field `files`, and a Base64 text longer than the Base64 length of
    `ReviewLimits.MaxFileSizeBytes` is a 400 validation problem that names `files[i].contentBase64`, both before
    anything is decoded or allocated for the files. The detail of the problem repeats the message. These checks only
    keep the request from sizing allocations and loops; the exact size of every decoded file and the total stay with
    the service.
  - A project that does not exist, or that the owner may not see, is a 404.
  - With `files`, `ReviewService.CreateDraftAsync` is called; with `repository`,
    `ReviewService.CreateFromRepositoryAsync` is called with a `RepositorySource` (the repository, the head and
    baseline revisions, and the selection; a selection that is missing or `null` means every changed file, stored as
    `**/*`). Every other rule (the number and
    size of files, the identifier, the title, the repository state) belongs to the service and arrives as a rule
    exception.
  - The optional `dueDate` of the request is read with `ReviewService.ParseDueDate` (the text `yyyy-MM-dd`, a calendar
    day in UTC; missing, `null`, or blank is no date) and passed to either creation call; text that is not such a date,
    or a date before today or more than two years ahead, is the rule `DueDateInvalid`, which `ApiProblems` maps to a
    400 problem, and nothing is created. The API cannot change a due date afterwards: that needs a signed-in user.
  - The optional `followsReviewId` of the request (text; missing, `null`, or blank means none) and the optional
    `acknowledgeDroppedThreads` (a boolean, default false) are passed to either creation call. The review then carries
    over what `ReviewService` says (see there); a review that does not exist in the project, does not fit, or would drop
    open threads without the acknowledgement is the rule `FollowUpInvalid`, which `ApiProblems` maps to a 400 problem
    (the message never reveals another project), and nothing is created. The details of a review show `followsReviewId`
    (null for an ordinary review), `carriedAnswerCount` and `carriedThreadCount`; the summary shows `followsReviewId`.
  - The actor is built from the claims of the token principal, so the review records the user and that the API was used.
  - The answer is 201 with the review and the files that the repository selection skipped.

**Read a review**: Checks the project first like every route (a project that the owner may not see is a 404 as for a
missing project), then builds the details from the services that own each part: the review from `ReviewService`, the
approval, blockers, and next step from `SigningService`, the thread counts from `ThreadService`, and the checklist
counts from `ChecklistService`. The checklist counts follow the screen, so only the items of applicable sections count.

**Download the attestation**: Asks `AttestationService` for the attestation of the review with the actor of the caller
(`ApiProblems.ActorOrAnonymous`: the actor of the token, or `null` when no token was presented, which the policy lets
through only when the project allows anonymous attestation PDFs) and
passes the result to the shared `AttestationEndpoint.ToResult`, with a problem 404 for a review that does not exist. The
service checks that the owner may view the project before it reads the review, so a project that the owner may not see
gives the same 404 as a review that does not exist. The
answer is therefore the same as for the page route (200 with the PDF, 409 for a review that is not complete or that
changed, 500 for an unusable key), always with `Cache-Control: no-store`, but as a problem description in JSON. A caller
without a token (anonymous) is the one exception for a review that is not complete: it gets the same 404 problem as for
a review that does not exist, so it cannot probe which review identifiers exist or whether they are complete. What the
PDF contains follows the evidence settings of the project (see `AttestationService`), whoever asks.

**Read the checklist template**: Checks the project first like every route (`ProjectService.GetAsync` with the actor of
the token; a project that does not exist or that the owner may not see is the same 404 problem), then asks
`ProjectChecklistService.GetAsync` and copies the result into `ProjectChecklistDto`: the version label, whether the
built-in default is used, the SHA-256 hash of the definition text (the value the web page sends back when it replaces the
template), the number of sections and checks, and the definition with the sections and checks in order. A stored
definition that cannot be read is a `ChecklistDefinitionInvalid` rule exception, which `ApiProblems.StatusOf` maps
explicitly to a 400 problem. The route only reads: no route under `/api` changes the template, and no API token can.

**Read the evidence index**: For a caller with a token, answers 404 for a project that does not exist or that the owner
may not see. A caller without a token (let through by the policy only when the project allows an anonymous evidence
index) skips that check and is checked again by `EvidenceIndexService`, which refuses it with the not-found rule when
the project does not allow it. Otherwise it
asks `EvidenceIndexService` (with the actor, which checks the project again) for
the
entries of the project and writes them as `{"reviews":[{id, fingerprint, date, result, file}]}`, the document that
ReviewMark reads as the `index.json` of a `url` evidence source. The result of every entry is `pass`. The `file` of an
entry is
the absolute address of the attestation of the review that the entry stands for, built by `AttestationUrl` from the
scheme, the host (the `Host` header), and the path base of the request, so the address is the one the client used to
reach
the server; the scheme follows the forwarded protocol when the forwarded headers are enabled, and a reverse proxy must
pass the `Host` header on. Every answer of the handler, including the 404 of an unknown project, has
`Cache-Control: no-store`. An empty list is a normal answer.

**MapAuditExport** (`AuditExportEndpoints.cs`): Maps the two routes of the audit export on the application. They are
*not* part of `MapApi`: they are for a signed-in person, they need the session cookie, not a token, and they are not in
the OpenAPI document (which describes the token API only; a test checks that the routes are absent). Both require
authorization with the default (cookie) scheme, so a request without a session is redirected to the sign-in page, like
the attestation route of the web. Neither collides with the pages `/audit` and `/projects/{id}/audit`, because the
path has one more segment (`/export`).

- `GET /projects/{projectId:int}/audit/export`: whoever passes `RequireAuditAsync` of the project; reads `actor`,
  `action`, `review`, `from`, and `to`.
- `GET /audit/export`: system administrators; reads the same and `project`.

The project number of the project route always wins: a `project` value in its query is ignored. The filter values are
read with `AuditFilterState.FromQuery`, the parser of the pages, so an unusable value (a bad date, an unknown action, a
huge text) falls back exactly as it does on the page and never fails; `size`, `before`, and `after` are not read. The
query value `format` is `csv` (the default, also when it is empty) or `json`, without regard to case; any other value is
a 400 problem.

*Access answers*, decided before anything is read or written and before the format is checked:

- No session: 302 to `/login` (cookie scheme).
- A project that does not exist, or one the user may not see: 404 with an empty body (the page for a missing address
  is shown).
- A project the user may see but not read the audit trail of (a plain member): 403 problem.
- A project admin, a system administrator, or an Auditor on a project: 200.
- The whole trail, not a system administrator (also a project admin): 403 problem.
- The whole trail, a system administrator: 200.
- An unknown `format`, after the access check: 400 problem.

The project export mirrors the project page (a user who may not even see the project cannot tell it from a missing
one), and the whole-trail export answers 403 where the page shows an access denied message, because an export is a
request for data and not a page: the user is signed in, so a refusal is the honest answer.

*Streaming*: the handler calls `AuditService.PrepareExportAsync` first (the snapshot and the single verification of the
chain), then writes the response headers, then calls `ExportAsync` with the response body and `RequestAborted`, so the
body is streamed in batches and an aborted download stops the read. An `OperationCanceledException` or an
`IOException` (a write to a closed connection) is swallowed when `RequestAborted` is set, after the service has
recorded the incomplete export; the same exceptions without an abort propagate. A cancellation during the
verification of `PrepareExportAsync` also ends the request quietly and records nothing, because no data was sent. The
response headers are:

- `Content-Type`: `text/csv; charset=utf-8` or `application/json`.
- `Content-Disposition`: `attachment; filename="audit-<scope>-<yyyyMMddTHHmmssZ>-chain-<status>.<ext>"`, where the
  scope is `project-N` or `all` and the status is `intact` or `broken`.
- `Cache-Control`: `no-store`; `X-Content-Type-Options`: `nosniff`.
- `X-ReviewCenter-Audit-Chain`: `intact` or `broken`.
- `X-ReviewCenter-Audit-Chain-Broken-At`: the first broken sequence, only when the chain is broken.

The status of the chain is in the headers (and in the file name) because the body is streamed; the headers are sent
before the first byte, and the chain was verified once for it. The chain is global, so the headers of a project export
say only whether the whole chain is sound and where it first breaks, never a count. The export is recorded in the audit
trail by the service (`audit.exported`, for the user, scoped to the project of the filter).

**ApiProblems.StatusOf**: Maps a rule to a status: the not-found rules (including a missing token) to 404; a duplicate
review identifier, an existing membership, the removal or demotion of the last project admin, a member who holds open
review work (`MemberHasOpenReviewWork`), a disabled repository,
a change that the approval lock refuses (`ReviewLocked`), a reopen of a review that is not locked (`ReviewNotLocked`),
a change that would make a signature out of date without acknowledgement (`SignaturesWouldBecomeOutOfDate`),
and a changed record to 409; too many attempts to 429; an unavailable git to 503, a git access failure
to
502, and a git timeout to 504; a not allowed actor, a signing that is not allowed, and a token action of a user who
must change the password first to 403; unreadable credentials to
500; and every other rule to 400. **ToResult** builds the problem description with the message of the rule as the
detail;
**NotFound** builds a 404; **ActorOf** builds the actor from a token principal (the user name, the display name, and the
display id of the token, which is "unknown" when the claim is missing); **ActorOrAnonymous** returns that actor for an
authenticated principal and `null` for a request without a token.

**Read-only role and the API**: an API token acts as its user, so a token of an Auditor reads everything the user may
read (projects, reviews, progress, checklist and thread counts, attestations, and the audit export of the project) and
every write is refused in Core with `ActorNotAllowed`, which is a 403; a user who is not a member still gets 404. The
API has no endpoint that assigns roles, so a role is only changed in the web pages (project administrators and system
administrators). The progress overview and the review list stay on the view access, which an Auditor has.

**Approval lock and the API**: The API has no endpoint that changes the content of an existing review, its threads, its
checklist answers, or its reviewer slots, and none that signs or reopens a review (reviewers and signatures are made in
the web pages with a re-authentication, and an API token is refused for them in Core). The mapping of `ReviewLocked` and
`ReviewNotLocked` to 409 is therefore defense in depth for a future endpoint, and no wire shape of the interface
changed. The read endpoints (the review, the checklist and thread counts, the attestation, and the evidence index) are
not affected by the lock; a reopened review simply reads as not approved.

**Signature impact and the API**: the same holds for `SignaturesWouldBecomeOutOfDate`: no endpoint calls the methods
that raise it, so the 409 mapping is defense in depth and the problem body is still the message of the rule. When an
endpoint that changes the record is added, the contract is that its problem document carries the affected slots as an
`affectedSlots` list (slot number and signer name) and that the request can acknowledge them; that is not built yet.

#### Error Handling

A `DomainRuleException` becomes a problem description through the filter of the group. A problem description is
`application/problem+json` with a status, a title, and a detail that never holds a secret (a token value, a
credential, or file content). Other exceptions are not caught: they are the generic 500 of the host. The authentication
failures are answered before a handler runs, by `ApiTokenAuthentication`; a request to one of the two open routes
without credentials, for a project that does not allow it, gets the same generic 401 as a missing token.

#### Dependencies

- **ProjectService**, **ReviewService**, **SigningService**, **ThreadService**, **ChecklistService**,
  **ProjectChecklistService**, **ReviewProgressService** — Core; the work.
- **AttestationService**, **AttestationEndpoint** — Evidence; the attestation and the shared mapping of its result.
- **EvidenceIndexService** — Evidence; the entries of the evidence index.
- **AuditService**, **IProjectAccess**, **AuditFilterState** — the export routes: the export, the access answers, and
  the parsing of the filter.
- **ApiTokenAuthentication**, **AnonymousRateLimit** — the policies of the groups and the limiter of anonymous callers.
- **Actor**, **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **ServerHost** — maps the routes when it builds the application.
- **ApiDocument** — the generator reads the metadata of the routes.
