## Api

### Verification Approach

The `Api` subsystem is verified by subsystem tests defined in `ApiSubsystemTests.cs`. The tests run the real application
in process (`TestWebApp`, the real server over a temporary SQLite database, with the real services, stores, and the
default self-signed key provider) and drive it with the test browser (`TestBrowser`, a real HTTP client), which sends the
requests with or without a bearer token or with the session cookie of a signed-in user. A token is created through the
real `PersonalAccessTokenService`, as the account page does. The subsystem exists to join its units: the token that
`PersonalAccessTokenService` creates and authenticates, the sign-in of a request by `ApiTokenAuthentication`, the routes
of `ApiEndpoints` that call the services, and the document of `ApiDocument`. The units are verified in detail by their
own unit tests: `ApiTokenAuthentication` (the accepted and refused tokens, the generic answer, the throttle, and the
audit record), `ApiEndpoints` (the routes, the creation of reviews, the problem documents, and the statuses), and
`ApiDocument` (the document and its snapshot). The token rules are verified in the `Core` subsystem, and the table in the
`Storage` subsystem.

The Puppet end-to-end test of the browser was **not** extended for this subsystem: the API has no page, and real HTTP
requests to the real server give the evidence. The token card of the account page is verified in the `Web` subsystem.

### Test Environment

Tests create temporary SQLite database files and key folders in private temporary folders, which are removed after the
test run. No network access and no external program are needed. The unit tests of `ApiEndpoints` replace the git
provider with a scripted fake.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A token that is created, used, and revoked works until it is revoked, and the same request is answered with 401 after
  the revocation.
- A draft created from files is answered with 201, is flagged as created through the API, can be read back as not
  approved, and its attestation is refused with 409; the attestation of a complete review is a PDF.
- A token can neither sign nor reach a page, and no route under `/api` signs or changes a reviewer slot; the session
  cookie is not accepted by the API.
- The document of the API is served without a token and equals the copy that is kept in the documentation.

### Test Scenarios

**ApiSubsystem_TokenJourney_CreateUseRevoke_WorksEndToEnd**: A token is created for a user through the service, and the
list of projects is requested with it as a bearer token. The answer is 200. The token is revoked through the service,
and the same request is answered with 401. This scenario is tested by
`ApiSubsystem_TokenJourney_CreateUseRevoke_WorksEndToEnd`.

**ApiSubsystem_ReviewJourney_CreateDraftReadAndDownloadAttestation_WorksEndToEnd**: With a token, a draft review is
created from uploaded files and answered with 201; the review is read back and is flagged as created through the API and
is not approved; the attestation of the draft is refused with 409; and the attestation of a complete review in another
project (made by the test helper of the attestation endpoint tests) is downloaded as `application/pdf`. This scenario is
tested by `ApiSubsystem_ReviewJourney_CreateDraftReadAndDownloadAttestation_WorksEndToEnd`.

**ApiSubsystem_TokenPrincipal_SigningAndWebPages_AreNotReachable**: A post to a signing address under `/api` is answered
with 404, because no such route exists; `SigningService.SignAsync` with an actor that came through the token is refused
with `ActorNotAllowed`; and the account page requested with the token is redirected to the sign-in page. This scenario is
tested by `ApiSubsystem_TokenPrincipal_SigningAndWebPages_AreNotReachable`.

**ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme** and **ApiDocument_Get_MatchesCommittedSnapshot**: The
document is requested without a token, describes the API with the bearer scheme, and equals the copy in the
documentation (see the `ApiDocument` verification). They are the evidence of the requirement for the document of the
subsystem. These scenarios are tested by `ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme` and
`ApiDocument_Get_MatchesCommittedSnapshot`.

**Units**: The bearer sign-in, the refusals, the throttle, and the audit record are verified in the unit tests of
`ApiTokenAuthentication`; the routes, the creation, and the problem documents in the unit tests of `ApiEndpoints`; the
document in the unit tests of `ApiDocument`. The rules of the tokens are verified in the `Core` subsystem (the unit
tests of `PersonalAccessTokenService` and the signing and creation tests of `SigningService` and `ReviewService`), the
table in the `Storage` subsystem, the registration in the `Hosting` subsystem, and the card and the help topic in the
`Web` subsystem.
