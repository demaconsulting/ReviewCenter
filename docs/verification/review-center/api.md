## Api

### Verification Approach

The `Api` subsystem is verified by subsystem tests defined in `ApiSubsystemTests.cs`. The tests run the real application
in process (`TestWebApp`, the real server over a temporary SQLite database, with the real services, stores, and the
default self-signed key provider) and drive it with the test browser (`TestBrowser`, a real HTTP client), which sends
the
requests with or without a bearer token, with HTTP Basic credentials, or with the session cookie of a signed-in user. A
token is created through the
real `PersonalAccessTokenService`, as the account page does. The subsystem exists to join its units: the token that
`PersonalAccessTokenService` creates and authenticates, the sign-in of a request by `ApiTokenAuthentication`, the routes
of `ApiEndpoints` that call the services, and the document of `ApiDocument`. The units are verified in detail by their
own unit tests: `ApiTokenAuthentication` (the accepted and refused tokens, the generic answer, the throttle, and the
audit record), `ApiEndpoints` (the routes, the creation of reviews, the problem documents, and the statuses), and
`ApiDocument` (the document and its snapshot). The token rules are verified in the `Core` subsystem, and the table in
the
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
  approved, and its attestation is refused with 409; the attestation of a concluded review is a PDF.
- The review resource of the API shows a token no more of the standing of the reviewers than the pages show its owner
  (see the unit *ApiEndpoints*): no failed count, dispute, blocker wording, or check thread counts for a token that has
  not handed in a ballot in a review with several slots that is not concluded; `outcome` only once concluded.
- The evidence index of a project is read with HTTP Basic credentials that carry a token as the password, its
  attestation
  address can be followed with the same credentials, and the same credentials are refused with 401 after the revocation.
- A token can neither sign nor reach a page, and no route under `/api` signs or changes a reviewer slot; the session
  cookie is not accepted by the API.
- The document of the API is served without a token and equals the copy that is kept in the documentation.
- Every route of a project answers a user who is not a member of the project, with a bearer token or with Basic
  credentials, exactly like a project that does not exist (404, the same content type, and the same body apart from
  the project number); a member and a system administrator are served; the project list holds only the projects of
  the owner of the token (all of them for a system administrator); and a token follows the memberships of its owner at
  once.
- A project that has switched on the anonymous evidence index or the anonymous attestation PDFs serves that route, and
  no other, to a request without credentials; a project that has not (the default), or does not exist, answers it with
  the same 401 as a missing token; a request with a wrong token is never served as anonymous; an anonymous release is
  audited as `anonymous`; and the document of the API marks exactly those two routes as callable without credentials.
- A review that is approved, and therefore locked, stays readable through the API (the review, its attestation, and the
  evidence index), and the refusal of a change to it is a 409 problem document.

### Test Scenarios

**Anonymous access scenarios**: The tests in `AnonymousEvidenceAccessTests.cs` run the real application and set the
evidence settings of a project through the real service. They show that the answer without credentials depends only on
the switch of the route and the project (`AnonymousEvidenceAccess_SwitchOff_Anonymous_AnswersLikeAMissingProject`,
`AnonymousEvidenceAccess_Matrix_ServesOnlyWhoMay`, `AnonymousEvidenceAccess_OneSwitchOn_DoesNotOpenTheOtherRoute`,
`AnonymousEvidenceAccess_SwitchOn_OpensOnlyItsOwnProject`, and `AnonymousEvidenceAccess_SwitchTurnedOff_RefusesAtOnce`),
that an anonymous caller reads the index and downloads a signed PDF that is audited as `anonymous`
(`AnonymousEvidenceAccess_BothOn_ServesIndexEntryAndSignedPdfAndAuditsAnonymous`), that every other route and the page
route of the attestation stay closed (`AnonymousEvidenceAccess_BothOn_OtherRoutes_StayClosed`,
`AnonymousEvidenceAccess_BothOn_AttestationPageRoute_StillNeedsSignIn`, and
`AnonymousEvidenceAccess_Endpoints_OnlyTheTwoRoutesUseTheOpenPolicies`), and that the limit by address answers 429 with
`Retry-After` (`AnonymousEvidenceAccess_TooManyAnonymousRequests_Answers429WithRetryAfter` and
`AnonymousRateLimit_TryAcquire_CountsPerAddressAndWindow`; the bound of the table at 4096 addresses is verified by the
tests of `AnonymousRateLimitTests.cs`). The details are in the `ApiTokenAuthentication` verification.
These tests are the evidence of the subsystem requirements `ReviewCenter-Api-AnonymousEvidence` and its parts
`-OpenRoutes`, `-Index`, `-Pdf`, `-Audited`, `-Limit`, and `-RetryAfter`, because a criterion that is shown only by the
real application with both routes open belongs to the subsystem and not to a unit.
The two operations in the document are verified by `ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme`.

**ApiProjectAccess scenarios**: The tests in `ApiProjectAccessTests.cs` run the real application and call every route
of a project with a token (the list of reviews, a review, the creation of a review, the attestation, the evidence
index, and the checklist template). `ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`
(a theory over the bearer scheme
and the Basic credentials of ReviewMark) sends each route for a project the owner of the token is no member of and for
a project that does not exist, and compares the status, the content type, and the body, with the project number
replaced. `ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused` shows that a member and a system
administrator who is no member are served (no 404, 403, or 401) by every route.
`ApiProjectAccess_ListProjects_ShowsOnlyMemberProjectsExceptToSystemAdmins` shows that the list holds the project of a
member, none for an outsider, and both projects for a system administrator.
`ApiProjectAccess_AttestationPage_NonMemberSession_Answers404` shows that the web route of the attestation answers a
signed-in non-member like a missing project. `ApiProjectAccess_Token_OwnerRemovedFromProject_Gets404AtOnce` shows that
the token of a member is served, and that the same token gets 404 after the member is removed from the project. These
tests are the evidence of the subsystem requirement `ReviewCenter-Api-ProjectAccess` and of its parts `-SameAnswer`,
`-Served`, `-ListProjects`, and `-Removed`, one test each.

**ApiSubsystem_TokenJourney_CreateUseRevoke_WorksEndToEnd**: A token is created for a user through the service, and the
list of projects is requested with it as a bearer token. The answer is 200. The token is revoked through the service,
and the same request is answered with 401. This scenario is tested by
`ApiSubsystem_TokenJourney_CreateUseRevoke_WorksEndToEnd`.

**ApiSubsystem_ReviewJourney_CreateDraftReadAndDownloadAttestation_WorksEndToEnd**: With a token, a draft review is
created from uploaded files and answered with 201; the review is read back and is flagged as created through the API and
is not approved; the attestation of the draft is refused with 409; and the attestation of a complete review in another
project (made by the test helper of the attestation endpoint tests) is downloaded as `application/pdf`. This scenario is
tested by `ApiSubsystem_ReviewJourney_CreateDraftReadAndDownloadAttestation_WorksEndToEnd`.

**ApiSubsystem_EvidenceJourney_BasicCredentials_ReadIndexAndFollowLink_WorksEndToEnd**: A project has a complete review
with a
review-set id, and a token is created. With Basic credentials that carry the token as the password, the evidence index
is
requested and lists the review; the attestation address of the entry is followed with the same credentials and answers a
PDF;
the token is revoked and the index is requested again, which is answered with 401. This scenario is tested by
`ApiSubsystem_EvidenceJourney_BasicCredentials_ReadIndexAndFollowLink_WorksEndToEnd`.

**ApiSubsystem_LockedReview_StaysReadableAndMutationIsProblem409**: A review is approved through the Core services. With
a token, the review is read (it is approved), its attestation is downloaded with status 200, and the evidence index
is read. A change to the review through `ThreadService` is refused with `ReviewLocked`, and `ApiProblems` maps that
refusal to a 409 problem document. After an administrator reopens the review through `SigningService`, the API reports
it as not approved. This scenario is tested by `ApiSubsystem_LockedReview_StaysReadableAndMutationIsProblem409`.

**ApiSubsystem_LockedReview_TokenCannotReopen**: An approved review and a token exist. A post to
`.../reviews/REV-1/reopen` is answered with 404, because the API has no reopen route. `SigningService.ReopenReviewAsync`
for an actor that came through the token is refused with `ActorNotAllowed`. The review read afterwards is still
approved. This scenario is tested by `ApiSubsystem_LockedReview_TokenCannotReopen`.

**ApiSubsystem_TokenPrincipal_SigningAndWebPages_AreNotReachable**: A post to a signing address under `/api` is answered
with 404, because no such route exists; `SigningService.SignAsync` with an actor that came through the token is refused
with `ActorNotAllowed`; and the account page requested with the token is redirected to the sign-in page. This scenario
is
tested by `ApiSubsystem_TokenPrincipal_SigningAndWebPages_AreNotReachable`.

**ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme** and **ApiDocument_Get_MatchesCommittedSnapshot**: The
document is requested without a token, describes the API with the bearer scheme, and equals the copy in the
documentation (see the `ApiDocument` verification). They are the evidence of the requirement for the document of the
subsystem. These scenarios are tested by `ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme` and
`ApiDocument_Get_MatchesCommittedSnapshot`.

**Review due date**: The unit tests of `ApiEndpoints` verify the subsystem requirement for the optional due date: a
review created with a `dueDate` shows `dueDate` and `overdue` in the answer, the list, and the details, and a date that
is not valid is a 400 problem that creates nothing. They are tested by
`ApiEndpoints_CreateReview_DueDate_IsCreatedAndShownWithOverdueFlag` and
`ApiEndpoints_CreateReview_InvalidDueDate_Answers400AndCreatesNothing`.

**Review progress**: The unit tests of `ApiEndpoints` verify the subsystem requirement for the progress fields: the list
of the reviews of a project shows `approved`, `slotsRequired`, `slotsSigned`, `slotsOutOfDate`, and `lastActivity`
beside `dueDate` and `overdue`, and the committed copy of the document of the API contains them. The list of a project
is built from one read of the review list, as the page of the project does. They are tested by
`ApiEndpoints_ListReviews_Progress_ShowsApprovalSignaturesAndLastActivity`,
`ApiEndpoints_ListReviews_Project_ReadsTheReviewListOnce`, and `ApiDocument_Get_MatchesCommittedSnapshot`.

**Read-only role (Auditor)**: The tests of `ApiAuditor` verify the subsystem requirement for the Auditor: the token of
an Auditor member reads the project, the review list, the progress overview, and the review, and is refused a write
(the creation of a review) with 403, while the token of a user who is no member of the project gets 404 and nothing is
stored. They are tested by `ApiAuditor_Reads_Token_ServeTheProject` and
`ApiAuditor_CreateReview_AuditorIs403AndNonMemberIs404`.

**Audit export**: The tests of `AuditExportEndpoints` verify the subsystem requirement for the audit export through the
real application: a project administrator gets the CSV of the project with its headers, a plain member gets 403 and a
user who is no member or a missing project gets 404 from the project export, and the whole-trail export is 403 for a
project administrator, a member, and an outsider, that is, for everybody who is not a system administrator. They are
tested by `AuditExportEndpoints_ProjectExport_ProjectAdmin_ServesCsvWithHeaders`,
`AuditExportEndpoints_ProjectExport_NotAllowed_Answers403Or404`, and
`AuditExportEndpoints_GlobalExport_NotSystemAdmin_Answers403`.

**Follow-up review**: The unit tests of `ApiEndpoints` verify the subsystem requirement for the follow-up review: a
review created with a `followsReviewId` shows it in the answer, the list, and the details, a review that follows
nothing shows null, and a source that does not exist or has no file in common is a 400 problem that creates nothing.
It is tested by `ApiEndpoints_CreateReview_FollowsReviewId_IsCreatedShownAndRefusedWhenInvalid`; the rule row is
verified by `ApiProblems_StatusOf_MemberRules_MapsToDocumentedStatus`.

**Units**: The bearer sign-in, the refusals, the throttle, and the audit record are verified in the unit tests of
`ApiTokenAuthentication`; the routes, the creation, and the problem documents in the unit tests of `ApiEndpoints`; the
document in the unit tests of `ApiDocument`. The rules of the tokens are verified in the `Core` subsystem (the unit
tests of `PersonalAccessTokenService` and the signing and creation tests of `SigningService` and `ReviewService`), the
table in the `Storage` subsystem, the registration in the `Hosting` subsystem, and the card and the help topic in the
`Web` subsystem.
