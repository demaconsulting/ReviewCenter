### ApiEndpoints

#### Verification Approach

`ApiEndpoints` is verified with tests defined in `ApiEndpointsTests.cs`. The tests start the real application in process
(`TestWebApp`) over a temporary database, with the git provider replaced by a scripted fake for the repository case,
create projects and reviews (a complete one with `AttestationEndpointTests.CreateReviewAsync`) and a token through the
real `PersonalAccessTokenService` (`ApiTestSupport`), and call the routes with an HTTP client that sends the token. The
responses are examined for the status, the content type, the headers, and the JSON; the data and the audit trail are read
through the services, and the routes are read from the endpoint data source of the application.

#### Test Environment

N/A - standard test environment (a temporary database and key folder are created by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- The routes return the projects, the review summaries, and the details of a review as JSON, and a project or a review
  that does not exist is a 404 problem document.
- Every route refuses a request without a token with 401.
- A review is created from uploaded files or from the changes of a repository, answered with 201, flagged as created
  through the API, and the audit entry names the token.
- An invalid body is a 400 problem that names the field, a body that is not JSON of the right shape is a 400 problem, a
  body that is not declared as JSON is a 415 problem, and the rules of the services answer with their status.
- A file count, file size or total size beyond the review limits is a 400 problem, a body over the limit of the endpoint
  is a 413, and no review is created in these cases.
- The attestation of a complete review is a PDF, of a review that is not complete a 409 problem.
- An unknown address under `/api` is a JSON 404 for every method, and there is no route that signs or changes a slot.

#### Test Scenarios

**ApiEndpoints_ListProjects_Token_ReturnsProjects**: Two projects are listed with a token. The answer is 200
`application/json` with a list whose items have camel-case names, the identity, the description, and the creation time.
This scenario is tested by
`ApiEndpoints_ListProjects_Token_ReturnsProjects`.

**ApiEndpoints_ListReviews_Project_ReturnsSummariesOrNotFound**: The reviews of a project and of a project that does not
exist are requested. The first is 200 with the summaries; the second is a 404 problem document. This scenario is tested
by `ApiEndpoints_ListReviews_Project_ReturnsSummariesOrNotFound`.

**ApiEndpoints_GetReview_CompleteReview_ReturnsDetails**: A complete review and a review that does not exist are
requested. The details show the identity, that it is approved, the approval digest, the counts, the next step, and the
slot with its reviewers and who signed; the other is 404. This scenario is tested by
`ApiEndpoints_GetReview_CompleteReview_ReturnsDetails`.

**ApiEndpoints_Request_NoToken_Answers401**: Each route (the three reads, the creation, and the attestation) is requested
without a token. Each answer is 401. This scenario is tested by `ApiEndpoints_Request_NoToken_Answers401`.

**ApiEndpoints_CreateReview_Upload_CreatesDraftFlaggedAsApi**: A review is created from two uploaded files. The answer is
201 with the address of the review, the details, and no skipped files; the review is flagged as created through the API,
and the audit entry names the display id of the token. This scenario is tested by
`ApiEndpoints_CreateReview_Upload_CreatesDraftFlaggedAsApi`.

**ApiEndpoints_CreateReview_Repository_CreatesReviewFromChanges**: A review is created from a registered repository
with a
head revision, a baseline revision, and a selection. The review is from the repository, flagged, shows both commits and
the two changed files, and the audit entry names the token. This scenario is tested by
`ApiEndpoints_CreateReview_Repository_CreatesReviewFromChanges`.

**ApiEndpoints_CreateReview_BadRequestBody_Answers400NamingTheField**: Bodies with no source, with two sources, with a
file that is not Base64, and with no body are sent. Each answer is a 400 problem document that names the field
(`source`, `files[0].contentBase64`, or `body`), and no review is created. This scenario is tested by
`ApiEndpoints_CreateReview_BadRequestBody_Answers400NamingTheField`.

**ApiEndpoints_CreateReview_MalformedJson_Answers400Problem**: Bodies that are cut off (`{"id":`), that are not JSON
(`not json`), and that are JSON of the wrong shape (`{"files": 5}`) are sent. Each answer is a 400 problem document with
the status in it, with no exception name, namespace, or stack trace in the text, and no review is created. This scenario
is tested by `ApiEndpoints_CreateReview_MalformedJson_Answers400Problem`.

**ApiEndpoints_CreateReview_NotJsonContentType_Answers415Problem**: A body is sent with the content type `text/plain`
and with no content type. Each answer is a 415 problem document, and no review is created. This scenario is tested by
`ApiEndpoints_CreateReview_NotJsonContentType_Answers415Problem`.

**ApiEndpoints_CreateReview_FileLimits_Answer400Problem**: A review with one file more than the limit, a review with one
file one byte over the size limit of a file, and a review of files that together are one byte over the total limit are
sent; the sizes are built from `ReviewLimits`. Each answer is a 400 problem document whose detail names the limit, and
no review is created. This scenario is tested by `ApiEndpoints_CreateReview_FileLimits_Answer400Problem`.

**ApiEndpoints_CreateReview_Endpoint_HasDerivedBodySizeLimit**: The endpoint that creates a review is found in the
endpoint data source. Its request size limit is the derived limit of `ApiEndpoints`, above the Base64 size of the
largest upload and below twice the largest upload. This scenario is tested by
`ApiEndpoints_CreateReview_Endpoint_HasDerivedBodySizeLimit`.

**ApiEndpoints_CreateReview_BodyOverLimit_Answers413**: The application is started on a real Kestrel server (the
in-memory test server does not enforce the limit), and a body one byte over the limit of the endpoint is announced with
`Expect: 100-continue`. The answer is 413, and no review is created. This scenario is tested by
`ApiEndpoints_CreateReview_BodyOverLimit_Answers413`.

**ApiEndpoints_CreateReview_ServiceRules_AnswerTheirStatus**: A duplicate review id, an invalid id, an empty title,
and an
unknown project are sent. The answers are 409, 400, 400, and 404, each a problem document with the status and a detail.
This scenario is tested by `ApiEndpoints_CreateReview_ServiceRules_AnswerTheirStatus`.

**ApiEndpoints_GetAttestation_Token_ReturnsPdfOrProblem**: The attestation of a complete review and of a review that does
not exist are requested. The first is 200 `application/pdf` with the PDF header; the second is a 404 problem document.
This scenario is tested by `ApiEndpoints_GetAttestation_Token_ReturnsPdfOrProblem`.

**ApiEndpoints_GetAttestation_ReviewNotSigned_Answers409Problem**: The attestation of a review nobody signed is requested.
The answer is a 409 problem document. This scenario is tested by
`ApiEndpoints_GetAttestation_ReviewNotSigned_Answers409Problem`.

**ApiEndpoints_Request_UnknownRoute_AnswersJsonNotFound**: An address under `/api` that does not exist is requested with
`GET`, `POST`, and `DELETE`, with and without a token. Each answer is a 404 problem document, not a page. This scenario
is tested by `ApiEndpoints_Request_UnknownRoute_AnswersJsonNotFound`.

**ApiEndpoints_Routes_NoSigningOrSlotRoutes**: The routes under `/api` are listed from the endpoint data source, and a
slot route and a signing route are posted with a token. No route pattern mentions slots, signing, or approval, and each
post is a 404. This scenario is tested by `ApiEndpoints_Routes_NoSigningOrSlotRoutes`.
