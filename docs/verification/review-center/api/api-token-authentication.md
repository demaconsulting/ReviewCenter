### ApiTokenAuthentication

#### Verification Approach

`ApiTokenAuthentication` is verified with tests defined in `ApiTokenAuthenticationTests.cs`. The tests start the real
application in process (`TestWebApp`) over a temporary database, create tokens through the real
`PersonalAccessTokenService` (`ApiTestSupport`), and send requests with the test browser or a plain HTTP client, with or
without an `Authorization` header or the session cookie. The responses are examined for the status, the challenge
header,
the content type, and the body; the state of the tokens and the audit trail are read through the services. The throttle
limit is lowered by a setting of the test, and the clock is a movable `TimeProvider` where the expiry of a token
matters.

The anonymous access of the two evidence routes (`AnonymousEvidenceAccess.cs`, `AnonymousRateLimit.cs`) is verified with
the tests of `AnonymousEvidenceAccessTests.cs` and, for the memory bound and the thread safety of the limit,
`AnonymousRateLimitTests.cs`. The first start the same in-process application, create a project with a
review that all reviewers signed, set the evidence settings of the project through the real
`EvidenceSettingsService`, and send requests with and without an `Authorization` header. The responses are examined for
the status, the body, the cache header, and `Retry-After`; the audit trail is read through the services, and the rate
limit is also called directly with a movable clock.

#### Test Environment

N/A - standard test environment (a temporary database is created by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- A valid token is accepted and its last use is recorded, and the request acts as the owner of the token, whether it is
  sent as a bearer token or as the password of HTTP Basic credentials with any user name.
- A request with no token, another kind of credential, an incomplete bearer header, or Basic credentials that cannot be
  read or have no password is answered with 401, the bearer and Basic challenges, and a problem document.
- A wrong Basic password is answered, throttled, and audited exactly like a wrong bearer token.
- An unknown, malformed, revoked, expired, disabled-owner, or temporary-password-owner token is answered with the same
  401 body.
- The session cookie does not authenticate the API, and a token, sent either way, does not sign in a web page, the
  sign-in and sign-out
  endpoints or the web socket negotiation.
- Refused tokens are throttled per address with 429, without stopping people signing in with a password.
- A refused token is audited once for each address and minute and the value is never recorded.
- No token value, and no text with the token prefix, is written to the application log for any outcome.
- A request without credentials is served by the evidence index route and the attestation PDF route only when the
  project has switched on that route, and is otherwise answered with the same generic 401 as for a project that does
  not exist; a wrong token, a disabled owner, or a non-member is never served as anonymous (401, 401, 404).
- Turning a switch on opens only its own route and only its own project, and turning it off refuses at once.
- Every other route of the API, and the page route of the attestation, stay closed to a request without credentials
  when both switches are on.
- An anonymous release of an attestation is audited with the actor `anonymous`, and a served response is never cached.
- A client address is limited to 30 anonymous requests per minute with 429 and `Retry-After`, and a request with a
  token is not limited.
- An anonymous request for the attestation of a review that is not complete is answered like one for a review that does
  not exist (404, same body); a member still gets 409.
- The table of addresses never holds more than 4096 addresses: ended windows are removed first, then the oldest
  ones; a renewed window is not lost to pruning; the limit is exact under parallel requests.
- Exactly two endpoints use the open policies.

#### Test Scenarios

**ApiTokenAuthentication_Request_ValidToken_AuthenticatesAndRecordsLastUsed**: A token that was never used is presented
to the list of projects. The answer is 200 and the token has a last-used time afterwards. This scenario is tested by
`ApiTokenAuthentication_Request_ValidToken_AuthenticatesAndRecordsLastUsed`.

**ApiTokenAuthentication_Request_ValidToken_ActsAsTheOwner**: A review is created with a token of a user. The audit
entry of the creation names the user and the display id of the token. This scenario is tested by
`ApiTokenAuthentication_Request_ValidToken_ActsAsTheOwner`.

**ApiTokenAuthentication_Request_NoUsableToken_Answers401WithChallenge**: The list of projects is requested with no
header, with a Basic header that is not Base64 text, has no colon, or has an empty password, with a Digest header, and
with a
bearer header without a value. Each answer is 401 with the Bearer and the Basic challenge and a problem document. This
scenario is tested by `ApiTokenAuthentication_Request_NoUsableToken_Answers401WithChallenge`.

**ApiTokenAuthentication_Request_BasicWithTokenAsPassword_AuthenticatesAsOwner**: A token that was never used is sent as
the password of Basic credentials, with a user name and with an empty user name. The answer is 200 and the token has a
last-used time afterwards. This scenario is tested by
`ApiTokenAuthentication_Request_BasicWithTokenAsPassword_AuthenticatesAsOwner`.

**ApiTokenAuthentication_Request_BasicWithWrongPassword_GetsTheSameGeneric401AndIsThrottledAndAudited**: With a limit of
three, a wrong token is sent once as a bearer token and once as a Basic password; the status, the challenge, and the
body
are the same. After one more refused attempt a good token sent as Basic is answered with 429. The audit trail has the
refusal and the throttling and no entry holds the wrong token. This scenario is tested by
`ApiTokenAuthentication_Request_BasicWithWrongPassword_GetsTheSameGeneric401AndIsThrottledAndAudited`.

**ApiTokenAuthentication_Request_RefusedTokens_AllGetTheSameGenericAnswer**: A revoked token, the token of a disabled
owner, a well-formed unknown token, a malformed value, and a good token with a changed ending are presented. Each answer
is 401 and all the bodies are the same; a good token still works. This scenario is tested by
`ApiTokenAuthentication_Request_RefusedTokens_AllGetTheSameGenericAnswer`.

**ApiTokenAuthentication_Request_ExpiredToken_Answers401**: A token of 30 days is accepted, the clock is moved by 30
days, and the same token is answered with 401. This scenario is tested by
`ApiTokenAuthentication_Request_ExpiredToken_Answers401`.

**ApiTokenAuthentication_Request_OwnerMustChangePassword_GetsTheGenericAnswerUntilChanged**: The owner of a good token
is flagged as having to change a temporary password; the token is answered with 401 and a body that equals the body for
an unknown token. After the flag is cleared the same token is answered with 200. This scenario is tested by
`ApiTokenAuthentication_Request_OwnerMustChangePassword_GetsTheGenericAnswerUntilChanged`.

**ApiTokenAuthentication_Request_SessionCookie_IsNotAccepted**: A browser that is signed in requests the API with its
cookie only, and with its cookie and Basic credentials that are not a token. Each answer is 401 with both the
`Bearer` and the `Basic` challenge. This scenario is tested by
`ApiTokenAuthentication_Request_SessionCookie_IsNotAccepted`.

**ApiTokenAuthentication_Request_TokenOnWebPage_IsNotSignedIn**: The account page and the attestation page route are
requested with a valid token, sent as a bearer token and as a Basic password. Each answer is a redirect to the sign-in
page, as
for a signed-out request. This scenario
is tested by `ApiTokenAuthentication_Request_TokenOnWebPage_IsNotSignedIn`.

**ApiTokenAuthentication_Request_TooManyRefusals_AnswersTooManyRequests**: With an address limit of three and a window
of 300 seconds, three refused tokens are answered with 401 and then a good token from the same address is answered with
429 as a problem document with a `Retry-After` header of `300`. This scenario is tested by
`ApiTokenAuthentication_Request_TooManyRefusals_AnswersTooManyRequests`.

**ApiTokenAuthentication_Request_RefusalsBelowAddressLimit_AreNotThrottledByNameLimit**: With a name limit of two but an
address limit of five, four refused tokens are all answered with 401 and a good token from the same address is then
accepted, so only the address limit counts. This scenario is tested by
`ApiTokenAuthentication_Request_RefusalsBelowAddressLimit_AreNotThrottledByNameLimit`.

**ApiTokenAuthentication_Request_RefusedToken_IsAuditedOnceWithoutTheValue**: A revoked token is presented three times.
The audit trail has one `token.use-failed` entry that names the reason and the display id, and no entry holds the token
value. This scenario is tested by `ApiTokenAuthentication_Request_RefusedToken_IsAuditedOnceWithoutTheValue`.

**ApiTokenAuthentication_Request_TokenOnAuthAndBlazorRoutes_IsNotSignedIn**: The web socket negotiation of the pages
(`/_blazor/negotiate`) and the sign-out and sign-in endpoints (`/auth/logout`, `/auth/login`) are posted to once with a
valid token (sent as a bearer token and as a Basic password) and once without. The status and the redirect address are
the same in both, no challenge is sent, and the
audit trail has no new entry, so the token changed nothing and did not act as its owner. This scenario is tested by
`ApiTokenAuthentication_Request_TokenOnAuthAndBlazorRoutes_IsNotSignedIn`.

**ApiTokenAuthentication_Request_AnyOutcome_NeverLogsTheToken**: The application keeps its log in a capturing provider.
A token is created, then presented while valid (200), a well-formed unknown value is presented, and a revoked token is
presented (both 401), each as a bearer token and as a Basic password. The log is not empty, and no message or structured
property of any entry contains a token value, its secret part, its Base64 form in Basic credentials, or the token
prefix. This scenario is tested by
`ApiTokenAuthentication_Request_AnyOutcome_NeverLogsTheToken`.

**AnonymousEvidenceAccess_SwitchOff_Anonymous_AnswersLikeAMissingProject**: With both switches off, which is the
default, both routes are requested without credentials for a project with a complete review and for a project number
that does not exist. Each answer is 401 with the bearer challenge, and the bodies of the two answers are the same. This
scenario is tested by `AnonymousEvidenceAccess_SwitchOff_Anonymous_AnswersLikeAMissingProject`.

**AnonymousEvidenceAccess_Matrix_ServesOnlyWhoMay**: For each route, with its own switch off and on, an anonymous
caller, a member with a bearer token and with Basic credentials, a non-member, a disabled member, and a caller with a
wrong token request the route. An anonymous caller is served only with the switch on, a member always, a non-member
gets 404, and the disabled member and the wrong token get 401 also with the switch on. The answer served to an
anonymous caller carries `Cache-Control: no-store`. This scenario is tested by
`AnonymousEvidenceAccess_Matrix_ServesOnlyWhoMay`.

**AnonymousEvidenceAccess_OneSwitchOn_DoesNotOpenTheOtherRoute**: With only the index open the PDF is refused to an
anonymous caller, and with only the PDF open the index is refused. This scenario is tested by
`AnonymousEvidenceAccess_OneSwitchOn_DoesNotOpenTheOtherRoute`.

**AnonymousEvidenceAccess_SwitchOn_OpensOnlyItsOwnProject**: With the index of one project open, an anonymous request
for the index of a second project is answered with 401. This scenario is tested by
`AnonymousEvidenceAccess_SwitchOn_OpensOnlyItsOwnProject`.

**AnonymousEvidenceAccess_BothOn_ServesIndexEntryAndSignedPdfAndAuditsAnonymous**: With both switches on, an anonymous
caller reads the index, follows the file address of its single entry, and receives a PDF with a valid signature that is
not cached. The audit trail has one attestation event with the actor `anonymous` and the project. This scenario is
tested by `AnonymousEvidenceAccess_BothOn_ServesIndexEntryAndSignedPdfAndAuditsAnonymous`.

**AnonymousEvidenceAccess_BothOn_OtherRoutes_StayClosed**: With both switches on, an anonymous caller requests the other
routes of the API. The list of projects, the reviews, one review, and the creation of a review are answered with 401,
and a path or method that is not a route is answered with 404. This scenario is tested by
`AnonymousEvidenceAccess_BothOn_OtherRoutes_StayClosed`.

**AnonymousEvidenceAccess_BothOn_AttestationPageRoute_StillNeedsSignIn**: With both switches on, the attestation page
route of the web pages is requested without signing in and answers with a redirect to the sign-in page. This scenario
is tested by `AnonymousEvidenceAccess_BothOn_AttestationPageRoute_StillNeedsSignIn`.

**AnonymousEvidenceAccess_SwitchTurnedOff_RefusesAtOnce**: An open index is read, the switch is turned off, and the next
request from the same client is answered with 401 without a restart. This scenario is tested by
`AnonymousEvidenceAccess_SwitchTurnedOff_RefusesAtOnce`.

**AnonymousEvidenceAccess_TooManyAnonymousRequests_Answers429WithRetryAfter**: An anonymous client makes one request
more than the limit allows. The last answer is 429 with `Retry-After` and `Cache-Control: no-store`, and a request with
a token from the same address is still answered with 200. This scenario is tested by
`AnonymousEvidenceAccess_TooManyAnonymousRequests_Answers429WithRetryAfter`.

**AnonymousEvidenceAccess_AttestationPdf_IncompleteReview_AnswersLikeMissingReview**: A project opens the attestation
PDFs and holds a review that nobody signed. An anonymous request for that review and one for a review that does not
exist are both answered 404 with identical bodies and `Cache-Control: no-store`, and no `attestation.generated` event is
recorded; a member with a token gets 409 for the incomplete review and 404 for the unknown one. This scenario is tested
by `AnonymousEvidenceAccess_AttestationPdf_IncompleteReview_AnswersLikeMissingReview`.

**AnonymousRateLimit_TryAcquire_CountsPerAddressAndWindow**: With a movable clock, one address uses up its budget and
is refused with a wait that is longer than zero and at most the window; another address and an unknown address are
still served, and after the window ended the first address is served again. This scenario is tested by
`AnonymousRateLimit_TryAcquire_CountsPerAddressAndWindow`.

**AnonymousRateLimit_TryAcquire_ManyDistinctAddresses_NeverExceedsCapacity**: Three times the capacity of distinct
addresses ask once each inside one window; every request is granted and the table never holds more than 4096 addresses
(it holds exactly 4096 at the end). This scenario is tested by
`AnonymousRateLimit_TryAcquire_ManyDistinctAddresses_NeverExceedsCapacity`.

**AnonymousRateLimit_TryAcquire_FullTable_RemovesExpiredBeforeEvictingLive**: A table that is full of two halves, one
ended and one live with an address that is used up, takes a new address: the ended half is removed, the live half is
kept and the used-up address is still refused. This scenario is tested by
`AnonymousRateLimit_TryAcquire_FullTable_RemovesExpiredBeforeEvictingLive`.

**AnonymousRateLimit_TryAcquire_FullOfLiveWindows_EvictsOldestFirst**: A table full of live windows takes a new
address: the new address is granted, the table stays at 4096, the oldest window is dropped (its address starts again)
and the next oldest is still there (still refused). This scenario is tested by
`AnonymousRateLimit_TryAcquire_FullOfLiveWindows_EvictsOldestFirst`.

**AnonymousRateLimit_TryAcquire_RenewedWindow_IsNotRemovedByPruning**: Regression of the pruning race: an address
renews its window after the first one ended and is then over its limit; new addresses make the table prune the ended
windows of the others, and the renewed address is still refused. This scenario is tested by
`AnonymousRateLimit_TryAcquire_RenewedWindow_IsNotRemovedByPruning`.

**AnonymousRateLimit_TryAcquire_ClockStepsBack_StillBoundedAndConsistent**: After the clock steps back, ended windows
that sit behind a live window are still found by the full scan, the wait is not negative, and a flood of addresses
keeps the table at its capacity without an error. This scenario is tested by
`AnonymousRateLimit_TryAcquire_ClockStepsBack_StillBoundedAndConsistent`.

**AnonymousRateLimit_TryAcquire_Concurrent_GrantsExactlyTheLimitPerAddress**: Eight workers make 1600 parallel
requests of one address with a fixed clock; exactly 30 are granted. This scenario is tested by
`AnonymousRateLimit_TryAcquire_Concurrent_GrantsExactlyTheLimitPerAddress`.

**AnonymousRateLimit_TryAcquire_ConcurrentDistinctAddresses_StaysWithinCapacity**: Parallel requests of many distinct
addresses next to four hot addresses: while the table is not full each hot address is granted exactly 30, and after a
parallel flood of three times the capacity the table holds exactly 4096 addresses. This scenario is tested by
`AnonymousRateLimit_TryAcquire_ConcurrentDistinctAddresses_StaysWithinCapacity`.

**AnonymousEvidenceAccess_Endpoints_OnlyTheTwoRoutesUseTheOpenPolicies**: The endpoints of the application are
enumerated. Exactly the evidence index and the attestation PDF use the open policies, and every other endpoint of the
API, except the OpenAPI document and the answer for an unknown address, requires the token policy. This scenario is
tested by
`AnonymousEvidenceAccess_Endpoints_OnlyTheTwoRoutesUseTheOpenPolicies`.
