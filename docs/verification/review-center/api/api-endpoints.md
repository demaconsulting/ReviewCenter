### ApiEndpoints

#### Verification Approach

`ApiEndpoints` is verified with tests defined in `ApiEndpointsTests.cs`. The tests start the real application in process
(`TestWebApp`) over a temporary database, with the git provider replaced by a scripted fake for the repository case,
create projects and reviews (a complete one with `AttestationEndpointTests.CreateReviewAsync`) and a token through the
real `PersonalAccessTokenService` (`ApiTestSupport`), and call the routes with an HTTP client that sends the token. The
responses are examined for the status, the content type, the headers, and the JSON; the data and the audit trail are
read
through the services, and the routes are read from the endpoint data source of the application.

#### Test Environment

N/A - standard test environment (a temporary database and key folder are created by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- The routes return the projects, the review summaries, and the details of a review as JSON, and a project or a review
  that does not exist is a 404 problem document.
- Every route refuses a request without a token with 401 while the anonymous evidence settings of the project are off,
  which is the default; the two routes that a project can open are verified by `AnonymousEvidenceAccessTests.cs` (see
  the `ApiTokenAuthentication` unit).
- A review is created from uploaded files or from the changes of a repository, answered with 201, flagged as created
  through the API, and the audit entry names the token.
- An invalid body is a 400 problem that names the field, a body that is not JSON of the right shape is a 400 problem, a
  body that is not declared as JSON is a 415 problem, and the rules of the services answer with their status; the rule
  for the last project administrator is 409, too many attempts 429, a required password change 403, and the rules for a
  locked review (`ReviewLocked`) and for a review that is not locked (`ReviewNotLocked`) are 409.
- A file count, file size or total size beyond the review limits is a 400 problem, a body over the limit of the endpoint
  is a 413, and no review is created in these cases. A file count above the limit, or a Base64 text longer than that of
  the largest file, is a 400 validation problem that is answered before anything is decoded.
- The attestation of a complete review is a PDF, of a review that is not complete a 409 problem (for a request without a
  token a 404 problem, as for a review that does not exist).
- The evidence index of a project is a JSON document with the complete reviews that have a review-set id, each with an
  absolute attestation address built from the request; it is empty when no review can be listed, and a 404 problem for
  an
  unknown project.
- An unknown address under `/api` is a JSON 404 for every standard HTTP method (`GET`, `HEAD`, `POST`, `PUT`, `PATCH`,
  `DELETE`, `OPTIONS`), and there is no route that signs or changes a slot.
- A route of a project answers a non-member like a missing project, and the project list holds only the projects the
  owner of the token may see; these are verified over HTTP by `ApiProjectAccessTests.cs` (see the `Api` subsystem).
- The checklist template of a project is a JSON document with the definition, the version label, the default flag, the
  hash, and the counts; a project that cannot be seen is a 404 problem, an unreadable stored definition a 400 problem,
  and no route changes the template.
- The evidence index and the attestation PDF are the only two routes that serve a request without a token, when the
  project allows it, and the only two that are limited per address; the endpoint grouping, the policies, and the limit
  are verified by `AnonymousEvidenceAccessTests.cs` (see the `ApiTokenAuthentication` unit).

#### Test Scenarios

**ApiEndpoints_ListProjects_Token_ReturnsProjects**: Two projects are listed with the token of a system
administrator. The answer is 200 `application/json` with a list whose items have camel-case names, the identity,
the description, and the creation time. This scenario is tested by
`ApiEndpoints_ListProjects_Token_ReturnsProjects`.

**ApiEndpoints_ListReviews_Project_ReturnsSummariesOrNotFound**: The reviews of a project and of a project that does not
exist are requested. The first is 200 with the summaries; the second is a 404 problem document. This scenario is tested
by `ApiEndpoints_ListReviews_Project_ReturnsSummariesOrNotFound`.

**ApiEndpoints_GetReview_CompleteReview_ReturnsDetails**: A complete review and a review that does not exist are
requested. The details show the identity, that it is approved, the approval digest, the counts, the next step, and the
slot with its reviewers and who signed; the other is 404. This scenario is tested by
`ApiEndpoints_GetReview_CompleteReview_ReturnsDetails`.

**ApiEndpoints_Request_NoToken_Answers401**: Each route (the three reads, the creation, and the attestation) is
requested without a token, while the anonymous evidence settings of the project are off. Each answer is 401. This
scenario is tested by `ApiEndpoints_Request_NoToken_Answers401`.

**ApiEndpoints_CreateReview_NoTokenOddBody_Answers401**: A creation request without a token and with a body that is
not JSON (plain text, or malformed JSON) is refused with 401, never with 415 or 400, because authentication comes
before the body is read. This scenario is tested by `ApiEndpoints_CreateReview_NoTokenOddBody_Answers401`.

**ApiEndpoints_CreateReview_Upload_CreatesDraftFlaggedAsApi**: A review is created from two uploaded files. The answer
is
201 with the address of the review, the details, and no skipped files; the review is flagged as created through the API,
and the audit entry names the display id of the token. This scenario is tested by
`ApiEndpoints_CreateReview_Upload_CreatesDraftFlaggedAsApi`.

**ApiEndpoints_CreateReview_Repository_CreatesReviewFromChanges**: A review is created from a registered repository
with a
head revision, a baseline revision, and a selection. The review is from the repository, flagged, shows both commits and
the two changed files, and the audit entry names the token. This scenario is tested by
`ApiEndpoints_CreateReview_Repository_CreatesReviewFromChanges`.

**ApiEndpoints_CreateReview_Repository_NoSelection_ReviewsEveryChange**: A review is created from a registered
repository with a request whose repository source leaves out the selection, and with a request whose selection is
`null`. Both are answered with 201, the review has the two changed files, and its selection is `**/*`. This scenario is
tested by `ApiEndpoints_CreateReview_Repository_NoSelection_ReviewsEveryChange`.

**ApiEndpoints_CreateReview_BadRequestBody_Answers400NamingTheField**: Bodies with no source, with two sources, with a
file that is not Base64, and with no body are sent. Each answer is a 400 problem document that names the field
(`source`, `files[0].contentBase64`, or `body`), and no review is created. This scenario is tested by
`ApiEndpoints_CreateReview_BadRequestBody_Answers400NamingTheField`.

**ApiEndpoints_CreateReview_MalformedJson_Answers400Problem**: Bodies that are cut off (`{"id":`), that are not JSON
(`not json`), and that are JSON of the wrong shape (`{"files": 5}`) are sent. Each answer is a 400 problem document with
the status in it, with no exception name, namespace, or stack trace in the text, and no review is created. This scenario
is tested by `ApiEndpoints_CreateReview_MalformedJson_Answers400Problem`.

**ApiEndpoints_CreateReview_NotJsonContentType_Answers415Problem**: A valid review body in JSON is sent with the content
type `text/plain` and with no content type. Each answer is a 415 problem document, no review is created, and no audit
entry is added. This scenario is tested by `ApiEndpoints_CreateReview_NotJsonContentType_Answers415Problem`.

**ApiEndpoints_CreateReview_FileLimits_Answer400Problem**: A review with one file more than the limit, a review with one
file one byte over the size limit of a file, and a review of files that together are one byte over the total limit are
sent; the sizes are built from `ReviewLimits`. Each answer is a 400 problem document whose detail names the limit, and
no review is created. This scenario is tested by `ApiEndpoints_CreateReview_FileLimits_Answer400Problem`.

**ApiEndpoints_CreateReview_DecodeBounds_Answer400NamingTheField**: A review with one file more than the limit, and a
review whose second file has a Base64 text four characters longer than the Base64 text of the largest file, are sent.
Each answer is a 400 validation problem that names the field (`files`, or `files[1].contentBase64`), the text of the
content is not in the answer, and no review is created. This scenario is tested by
`ApiEndpoints_CreateReview_DecodeBounds_Answer400NamingTheField`.

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

**ApiProblems_StatusOf_MemberRules_MapsToDocumentedStatus**: The status for each of the rules `LastProjectAdmin`,
`TooManyAttempts`, and `PasswordChangeRequired` is asked of `ApiProblems`; the answers are 409, 429, and 403. This
scenario is tested by `ApiProblems_StatusOf_MemberRules_MapsToDocumentedStatus`.

**ApiProblems_StatusOf_ReviewLockedAndNotLocked_Is409**: The status for each of the rules `ReviewLocked` and
`ReviewNotLocked` is asked of `ApiProblems`; both answers are 409, while `ReviewerNotIndependent` and
`ReopenReasonInvalid` keep the default 400. This scenario is tested by
`ApiProblems_StatusOf_ReviewLockedAndNotLocked_Is409`.

**ApiEndpoints_GetAttestation_Token_ReturnsPdfOrProblem**: The attestation of a complete review and of a review that
does
not exist are requested. The first is 200 `application/pdf` with the PDF header; the second is a 404 problem document.
This scenario is tested by `ApiEndpoints_GetAttestation_Token_ReturnsPdfOrProblem`.

**ApiEndpoints_GetAttestation_ReviewNotSigned_Answers409Problem**: The attestation of a review nobody signed is
requested.
The answer is a 409 problem document. A signed-in member is told that the review is not complete; an anonymous caller is
not (see `AnonymousEvidenceAccess_AttestationPdf_IncompleteReview_AnswersLikeMissingReview`). This scenario is tested by
`ApiEndpoints_GetAttestation_ReviewNotSigned_Answers409Problem`.

**ApiEndpoints_GetEvidenceIndex_CompleteReviewWithSetId_ReturnsEntryWithAbsoluteFileUrl**: A project has a complete
review
without a review-set id and one with a review-set id. The index is requested with a bearer token and with Basic
credentials. Each answer is 200 JSON with `Cache-Control: no-store`, a `reviews` list with the one entry of the review
that has
a set id (its id, a 64-character fingerprint, the date of the day, the result `pass`), and the absolute address of its
attestation built from the request. This scenario is tested by
`ApiEndpoints_GetEvidenceIndex_CompleteReviewWithSetId_ReturnsEntryWithAbsoluteFileUrl`.

**ApiEndpoints_GetEvidenceIndex_NoListableReview_ReturnsEmptyList**: The index of a project whose only complete review
has
no review-set id is requested. The answer is 200 with an empty list. This scenario is tested by
`ApiEndpoints_GetEvidenceIndex_NoListableReview_ReturnsEmptyList`.

**ApiEndpoints_GetEvidenceIndex_UnknownProject_Answers404Problem**: The index of a project that does not exist is
requested. The answer is a 404 problem document with `Cache-Control: no-store`. This scenario is tested by
`ApiEndpoints_GetEvidenceIndex_UnknownProject_Answers404Problem`.

**ApiEndpoints_GetEvidenceIndex_SchemeHostPathBaseAndId_BuildsAbsoluteUrl**: The attestation address is built for a
request with the scheme `https`, the host `reviews.example.com:8443` and the path base `/center`, for a request with
the default scheme, host and an empty path base, and for a review identifier with special characters. The address is
absolute, follows the request, and has the identifier escaped (`A%20B%2Fc%3Fd`). This scenario is tested by
`ApiEndpoints_GetEvidenceIndex_SchemeHostPathBaseAndId_BuildsAbsoluteUrl`.

**ApiEndpoints_GetEvidenceIndex_OtherHost_BuildsUrlFromRequestHost**: The index is requested with another `Host`
header. The attestation address of the entry names that host. This scenario is tested by
`ApiEndpoints_GetEvidenceIndex_OtherHost_BuildsUrlFromRequestHost`.

**ApiEndpoints_GetChecklist_Token_ReturnsDefaultThenCustomDefinition**: A member and a system administrator read the
checklist of a project with a bearer token and with Basic credentials. At first the answer is 200 `application/json`
with the built-in default (version `default-1`, `isDefault` true, 14 sections, 58 checks, the hash of the default, and
the sections with their titles, hints, and checks); after the template is replaced the answer shows the custom version
label, `isDefault` false, another 64-character hash, and the counts and checks of the custom definition. This scenario is
tested by `ApiEndpoints_GetChecklist_Token_ReturnsDefaultThenCustomDefinition`.

**ApiEndpoints_GetChecklist_SectionWithoutHint_ReturnsEmptyHint**: A project's own checklist has a section without the
hint member (which the validator accepts); the answer has the field `applicabilityHint` as an empty string, not null.
This scenario is tested by `ApiEndpoints_GetChecklist_SectionWithoutHint_ReturnsEmptyHint`.

**ApiEndpoints_GetChecklist_NonMemberUnknownProjectAndUnreadableDefinition_AnswerProblems**: A user who is no member
and a project that does not exist both get a 404 problem document; a stored definition that cannot be read is a 400
problem whose detail names the checklist definition. This scenario is tested by
`ApiEndpoints_GetChecklist_NonMemberUnknownProjectAndUnreadableDefinition_AnswerProblems`.

**ApiProblems_StatusOf_ChecklistDefinitionInvalid_Is400WithTheMessage**: The rule `ChecklistDefinitionInvalid` is mapped
to status 400 and its exception becomes a problem result with that status and the message as the detail. This scenario
is tested by `ApiProblems_StatusOf_ChecklistDefinitionInvalid_Is400WithTheMessage`.

**ApiEndpoints_Request_UnknownRoute_AnswersJsonNotFound**: An address under `/api` that does not exist is requested with
`GET`, `POST`, and `DELETE`, with and without a token. Each answer is a 404 problem document, not a page. This scenario
is tested by `ApiEndpoints_Request_UnknownRoute_AnswersJsonNotFound`.

**ApiEndpoints_Routes_NoSigningOrSlotRoutes**: The routes under `/api` are listed from the endpoint data source, and a
slot route and a signing route are posted with a token. No route pattern mentions slots, signing, or approval, and each
post is a 404. This scenario is tested by `ApiEndpoints_Routes_NoSigningOrSlotRoutes`.
