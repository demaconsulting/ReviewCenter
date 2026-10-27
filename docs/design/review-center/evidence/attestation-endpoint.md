### AttestationEndpoint

![Evidence Structure](EvidenceView.svg)

#### Purpose

`AttestationEndpoint` is the HTTP address at which a user downloads the signed attestation of a review. Its single
responsibility is to turn a request into a call of `AttestationService` and the result into an HTTP answer.

#### Data Model

The unit is a static class with one extension method that maps the route
`GET /projects/{projectId}/reviews/{reviewId}/attestation.pdf` (the project number is an integer), and one internal
static method, `ToResult`, that turns the outcome of `AttestationService` into the HTTP answer. The route requires an
authenticated user, like every page except the home page and the login page; a personal access token does not
authenticate this page route (the API has its own route that uses the same `ToResult`, see `ApiEndpoints`). The actor
passed to the service is built
from the user name and the display name claim of the signed-in user.

#### Key Methods

**MapAttestation**: Maps the endpoint.

- *Parameters*: The endpoint route builder.
- *Returns*: The same builder.
- *Postconditions*: The route answers as follows. Every answer of the handler carries `Cache-Control: no-store`, because
  the document is made for the moment of the request.
  - A visitor who is not signed in is redirected to `/login` by the authentication scheme.
  - 200 with the PDF as a file download (`application/pdf`, and the file name from `AttestationService.FileNameFor`)
    for a completed review.
  - 404 for a review that does not exist.
  - 409 with a problem description titled "The review is not complete" for a review that is not approved.
  - 409 with a problem description titled "The review changed during generation" when the review changed while the
    attestation was being prepared (nothing was signed or recorded; the user can ask again).
  - 500 with a problem description titled "Attestation signing key unavailable" and the reason, when the key cannot be
    used. The reason names the setting to correct and never contains a secret.

**ToResult**: Maps the outcome of the service to an answer, for the page route and for the API route.

- *Parameters*: The `AttestationResult`, the request context, and a function that creates the answer for a review that
  does not exist.
- *Returns*: The answer: the file for a completed review, a 409 problem description for a review that is not complete or
  changed, a 500 problem description for an unusable key, and the answer of the function for a missing review.
- *Postconditions*: `Cache-Control: no-store` is set on the response in every case. The page route passes an empty 404
  (`Results.NotFound`) and the API passes a JSON problem 404, so the two cannot differ in any other status, header, or
  title.

#### Error Handling

The unit has no error handling of its own: the statuses of the service are the whole set of expected outcomes, and
the status maps one to one to an answer. An unexpected exception (for example an audit trail that cannot be written)
is the generic error of the host. The authorization is a requirement of the route and not a check in the handler, so it
cannot be forgotten.

#### Dependencies

- **AttestationService**, **AttestationResult** — the work.
- **SignIn** — the name of the display name claim.
- **Actor** — Core.

#### Callers

- **ServerHost** — maps the endpoint when it builds the application.
- **ApiEndpoints** — its attestation route calls `ToResult`.
- **ReviewPage** — links to the address (through `ReviewLinks`) in the download button.
