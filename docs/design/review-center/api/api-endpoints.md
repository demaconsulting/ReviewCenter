### ApiEndpoints

![Api Structure](ApiView.svg)

#### Purpose

`ApiEndpoints` is the JSON interface of the system under `/api`. Its single responsibility is to turn an authenticated
request into a call of the Core services and the result into an HTTP answer, with one way of reporting a problem. It
contains no rule of its own except the shape of a request.

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
and the signer is shown by display name), the counts of files, threads, and checklist answers, the blockers, and the next
step.

#### Key Methods

**MapApi**: Maps the routes.

- *Parameters*: The endpoint route builder. Must not be null.
- *Returns*: The same builder.
- *Postconditions*: One route group under `/api` requires the `Api` policy of `ApiTokenAuthentication` and has a filter
  that turns a `DomainRuleException` into a problem result, so no route can be added open by accident. The routes are
  - `GET /api/projects`,
  - `GET /api/projects/{projectId}/reviews` (the project number must be an integer),
  - `GET /api/projects/{projectId}/reviews/{reviewId}`,
  - `POST /api/projects/{projectId}/reviews`, which accepts a body of up to 30 MiB (the largest upload as Base64 text
    with the JSON around it) and answers 201 with a `Location` header, and
  - `GET /api/projects/{projectId}/reviews/{reviewId}/attestation.pdf`.

  A catch-all route for every method under `/api` is outside the group and open, so that an unknown address answers a JSON
  404 with or without a token and never falls through to the pages. It is excluded from the OpenAPI description. A more
  specific route wins over it.

**Create a review**: The handler of the `POST` route.

- *Preconditions*: The request is authenticated as a token.
- *Postconditions*:
  - A request with no body, or with both or neither of `files` and `repository`, is a 400 problem that names the
    field (`body` or `source`). A file whose content is not valid Base64 is a 400 that names the position
    (`files[i].contentBase64`) and never echoes the content.
  - A project that does not exist is a 404.
  - With `files`, `ReviewService.CreateDraftAsync` is called; with `repository`, `ReviewService.CreateFromRepositoryAsync`
    is called with the repository, the head and baseline revisions, and the selection. Every other rule (the number and
    size of files, the identifier, the title, the repository state) belongs to the service and arrives as a rule
    exception.
  - The actor is built from the claims of the token principal, so the review records the user and that the API was used.
  - The answer is 201 with the review and the files that the repository selection skipped.

**Read a review**: Builds the details from the services that own each part: the review from `ReviewService`, the
approval, blockers, and next step from `SigningService`, the thread counts from `ThreadService`, and the checklist
counts from `ChecklistService`. The checklist counts follow the screen, so only the items of applicable sections count.

**Download the attestation**: Asks `AttestationService` for the attestation of the review with the actor of the token and
passes the result to the shared `AttestationEndpoint.ToResult`, with a problem 404 for a review that does not exist. The
answer is therefore the same as for the page route (200 with the PDF, 409 for a review that is not complete or that
changed, 500 for an unusable key), always with `Cache-Control: no-store`, but as a problem description in JSON.

**ApiProblems.StatusOf**: Maps a rule to a status: the not-found rules (including a missing token) to 404; a duplicate
review identifier, a disabled repository, and a changed record to 409; an unavailable git to 503, a git access failure to
502, and a git timeout to 504; a not allowed actor and a signing that is not allowed to 403; unreadable credentials to
500; and every other rule to 400. **ToResult** builds the problem description with the message of the rule as the detail;
**NotFound** builds a 404; **ActorOf** builds the actor from a token principal (the user name, the display name, and the
display id of the token, which is "unknown" when the claim is missing).

#### Error Handling

A `DomainRuleException` becomes a problem description through the filter of the group. A problem description is
`application/problem+json` with a status, a title, and a detail that never holds a secret (a token value, a
credential, or file content). Other exceptions are not caught: they are the generic 500 of the host. The authentication
failures are answered before a handler runs, by `ApiTokenAuthentication`.

#### Dependencies

- **ProjectService**, **ReviewService**, **SigningService**, **ThreadService**, **ChecklistService** — Core; the work.
- **AttestationService**, **AttestationEndpoint** — Evidence; the attestation and the shared mapping of its result.
- **ApiTokenAuthentication** — the policy of the group.
- **Actor**, **DomainRuleException**, **DomainRule** — Core.

#### Callers

- **ServerHost** — maps the routes when it builds the application.
- **ApiDocument** — the generator reads the metadata of the routes.
