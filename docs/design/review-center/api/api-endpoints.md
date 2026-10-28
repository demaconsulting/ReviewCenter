### ApiEndpoints

![Api Structure](ApiView.svg)

#### Purpose

`ApiEndpoints` is the JSON interface of the system under `/api`. Its single responsibility is to turn an authenticated
request (or, for two routes that a project can open, a request without credentials) into a call of the Core services
and the result into an HTTP answer, with one way of reporting a problem. It contains no rule of its own except the
shape of a request.

#### Data Model

The unit is three files. `ApiEndpoints` is a static class with the extension method `MapApi` and the handlers of the
routes. `ApiModels` holds the types of the requests and answers, all immutable records: `ProjectDto`,
`ReviewSummaryDto`, `ReviewDetailDto` (with `SlotDto` and `ReviewCountsDto`), `CreateReviewRequest` (with
`UploadFileDto` and `RepositorySourceDto`), and `CreatedReviewDto` (with `SkippedFileDto`). The types are separate from
the Core types so that the shape of the API does not change when a Core class does. `ApiProblems` holds the mapping
from a broken rule to a status and the helpers that build a problem description and the actor of a call.

The answers carry only what a program needs: a summary has no file count and no details; the details of a review hold
the title, state, fingerprint, creator and the flag that the review was created through the API, the source of a review
from a repository, the approval state and record digest, the slots (the primary and alternate reviewer are user names,
and the signer is shown by display name), the counts of files, threads, and checklist answers, the blockers, and the
next
step.

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
  - A project that does not exist, or that the owner may not see, is a 404.
  - With `files`, `ReviewService.CreateDraftAsync` is called; with `repository`,
    `ReviewService.CreateFromRepositoryAsync`
    is called with the repository, the head and baseline revisions, and the selection. Every other rule (the number and
    size of files, the identifier, the title, the repository state) belongs to the service and arrives as a rule
    exception.
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
changed, 500 for an unusable key), always with `Cache-Control: no-store`, but as a problem description in JSON. What the
PDF contains follows the evidence settings of the project (see `AttestationService`), whoever asks.

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

**ApiProblems.StatusOf**: Maps a rule to a status: the not-found rules (including a missing token) to 404; a duplicate
review identifier, an existing membership, the removal or demotion of the last project admin, a disabled repository,
and a changed record to 409; too many attempts to 429; an unavailable git to 503, a git access failure
to
502, and a git timeout to 504; a not allowed actor, a signing that is not allowed, and a token action of a user who
must change the password first to 403; unreadable credentials to
500; and every other rule to 400. **ToResult** builds the problem description with the message of the rule as the
detail;
**NotFound** builds a 404; **ActorOf** builds the actor from a token principal (the user name, the display name, and the
display id of the token, which is "unknown" when the claim is missing); **ActorOrAnonymous** returns that actor for an
authenticated principal and `null` for a request without a token.

#### Error Handling

A `DomainRuleException` becomes a problem description through the filter of the group. A problem description is
`application/problem+json` with a status, a title, and a detail that never holds a secret (a token value, a
credential, or file content). Other exceptions are not caught: they are the generic 500 of the host. The authentication
failures are answered before a handler runs, by `ApiTokenAuthentication`; a request to one of the two open routes
without credentials, for a project that does not allow it, gets the same generic 401 as a missing token.

#### Dependencies

- **ProjectService**, **ReviewService**, **SigningService**, **ThreadService**, **ChecklistService** — Core; the work.
- **AttestationService**, **AttestationEndpoint** — Evidence; the attestation and the shared mapping of its result.
- **EvidenceIndexService** — Evidence; the entries of the evidence index.
- **ApiTokenAuthentication**, **AnonymousRateLimit** — the policies of the groups and the limiter of anonymous callers.
- **Actor**, **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **ServerHost** — maps the routes when it builds the application.
- **ApiDocument** — the generator reads the metadata of the routes.
