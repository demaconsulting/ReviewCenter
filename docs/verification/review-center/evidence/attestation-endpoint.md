### AttestationEndpoint

#### Verification Approach

`AttestationEndpoint` is verified with tests defined in `AttestationEndpointTests.cs`. The tests start the real
application in process (`TestWebApp`) over a temporary database, with the default self-signed key or a key provider of
the tests that fails, create a project and a review that is complete or not, and call the address with the test browser,
which signs in as a user or stays signed out. The responses are examined for the status, the headers, and the body, and
the signature of the downloaded PDF is verified with `PdfInspector`.

#### Test Environment

N/A - standard test environment (a temporary database and key folder are created by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- A visitor who is not signed in is redirected to the sign-in page, also when the project has switched on the
  anonymous attestation PDFs; that setting opens only the API route.
- A completed review is downloaded as a signed PDF with the right content type, a file name, and no caching, and the
  download is recorded in the audit trail; with the default configuration it is signed by the key that the server created.
- An unknown review is 404, a review that is not complete is 409, a review that changed during generation is 409, and
  an unusable key is 500 without an audit event.
- A signed-in user who is not a member of the project gets an empty 404, like a project that does not exist; the
  tests of this guard are `ApiProjectAccess_AttestationPage_NonMemberSession_Answers404` and the route tests of
  `ApiProjectAccessTests.cs`, which are described in the `Api` subsystem.

#### Test Scenarios

**AttestationEndpoint_Get_SignedOut_RedirectsToLogin**: The address is requested without signing in. The answer is a
redirect to the sign-in page. This scenario is tested by `AttestationEndpoint_Get_SignedOut_RedirectsToLogin`, and, with
both anonymous settings on, by `AnonymousEvidenceAccess_BothOn_AttestationPageRoute_StillNeedsSignIn`.

**AttestationEndpoint_Get_UnknownReview_Answers404**: The address of a review that does not exist is requested. The
answer is 404. This scenario is tested by `AttestationEndpoint_Get_UnknownReview_Answers404`.

**AttestationEndpoint_Get_ReviewNotSigned_Answers409**: The address of a review that is not complete is requested. The
answer is 409 with a problem description that says the review is not complete. This scenario is tested by
`AttestationEndpoint_Get_ReviewNotSigned_Answers409`.

**AttestationEndpoint_Get_ReviewChangesDuringGeneration_Answers409AndRecordsNothing**: The application is started with an
attestation service whose test step changes the checklist answers after the document is rendered. The answer is 409
with a problem description that says the review changed, no PDF is sent, and no event is recorded. This scenario is
tested by `AttestationEndpoint_Get_ReviewChangesDuringGeneration_Answers409AndRecordsNothing`.

**AttestationEndpoint_Get_CompleteReview_ReturnsSignedPdfDownloadAndRecordsEvent**: The address of a complete review is
requested. The answer is 200 with `application/pdf`, a download file name, and no caching; the PDF is signed; and the
audit trail has the event of the user. This scenario is tested by
`AttestationEndpoint_Get_CompleteReview_ReturnsSignedPdfDownloadAndRecordsEvent`.

**AttestationEndpoint_Get_DefaultConfiguration_SignsWithTheServerGeneratedSelfSignedKey**: The server is started without
any attestation setting. The download is signed with a self-signed certificate that the server created. This scenario is
tested by `AttestationEndpoint_Get_DefaultConfiguration_SignsWithTheServerGeneratedSelfSignedKey`.

**AttestationEndpoint_Get_KeyBecomesUnavailable_Answers500AndRecordsNothing**: The application starts with a key
provider of
the tests that works at the start and fails afterwards, and the key then stops working. The answer is 500 with a problem
description that carries the reason of the key, no PDF is sent, and no event is recorded. This scenario is tested by
`AttestationEndpoint_Get_KeyBecomesUnavailable_Answers500AndRecordsNothing`.

**Shared result mapping**: `ToResult`, which turns the outcome of the attestation service into the answer, is shared by
the page address and by the API address of the attestation. The mapping is exercised through this unit's tests above and
through the attestation tests of the API (`ApiEndpoints_GetAttestation_*` in `ApiEndpointsTests.cs`), which check the same
statuses and the same PDF; see the verification of the `ApiEndpoints` unit.
