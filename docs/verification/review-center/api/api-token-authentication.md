### ApiTokenAuthentication

#### Verification Approach

`ApiTokenAuthentication` is verified with tests defined in `ApiTokenAuthenticationTests.cs`. The tests start the real
application in process (`TestWebApp`) over a temporary database, create tokens through the real
`PersonalAccessTokenService` (`ApiTestSupport`), and send requests with the test browser or a plain HTTP client, with or
without an `Authorization` header or the session cookie. The responses are examined for the status, the challenge header,
the content type, and the body; the state of the tokens and the audit trail are read through the services. The throttle
limit is lowered by a setting of the test, and the clock is a movable `TimeProvider` where the expiry of a token matters.

#### Test Environment

N/A - standard test environment (a temporary database is created by each test).

#### Acceptance Criteria

- All tests pass with zero failures.
- A valid token is accepted and its last use is recorded, and the request acts as the owner of the token.
- A request with no token, another kind of credential, or an incomplete bearer header is answered with 401, the bearer
  challenge, and a problem document.
- An unknown, malformed, revoked, expired, or disabled-owner token is answered with the same 401 body.
- The session cookie does not authenticate the API, and a token does not sign in a web page, the sign-in and sign-out
  endpoints or the web socket negotiation.
- Refused tokens are throttled per address with 429, without stopping people signing in with a password.
- A refused token is audited once for each address and minute and the value is never recorded.
- No token value, and no text with the token prefix, is written to the application log for any outcome.

#### Test Scenarios

**ApiTokenAuthentication_Request_ValidToken_AuthenticatesAndRecordsLastUsed**: A token that was never used is presented
to the list of projects. The answer is 200 and the token has a last-used time afterwards. This scenario is tested by
`ApiTokenAuthentication_Request_ValidToken_AuthenticatesAndRecordsLastUsed`.

**ApiTokenAuthentication_Request_ValidToken_ActsAsTheOwner**: A review is created with a token of a user. The audit
entry of the creation names the user and the display id of the token. This scenario is tested by
`ApiTokenAuthentication_Request_ValidToken_ActsAsTheOwner`.

**ApiTokenAuthentication_Request_NoBearerToken_Answers401WithChallenge**: The list of projects is requested with no
header, with a `Basic` header, and with a bearer header without a value. Each answer is 401 with the `Bearer` challenge
and a problem document. This scenario is tested by
`ApiTokenAuthentication_Request_NoBearerToken_Answers401WithChallenge`.

**ApiTokenAuthentication_Request_RefusedTokens_AllGetTheSameGenericAnswer**: A revoked token, the token of a disabled
owner, a well-formed unknown token, a malformed value, and a good token with a changed ending are presented. Each answer
is 401 and all the bodies are the same; a good token still works. This scenario is tested by
`ApiTokenAuthentication_Request_RefusedTokens_AllGetTheSameGenericAnswer`.

**ApiTokenAuthentication_Request_ExpiredToken_Answers401**: A token of 30 days is accepted, the clock is moved by 30
days, and the same token is answered with 401. This scenario is tested by
`ApiTokenAuthentication_Request_ExpiredToken_Answers401`.

**ApiTokenAuthentication_Request_SessionCookie_IsNotAccepted**: A browser that is signed in requests the API with its
cookie only. The answer is 401 with the bearer challenge. This scenario is tested by
`ApiTokenAuthentication_Request_SessionCookie_IsNotAccepted`.

**ApiTokenAuthentication_Request_TokenOnWebPage_IsNotSignedIn**: The account page and the attestation page route are
requested with a valid token. Each answer is a redirect to the sign-in page, as for a signed-out request. This scenario
is tested by `ApiTokenAuthentication_Request_TokenOnWebPage_IsNotSignedIn`.

**ApiTokenAuthentication_Request_TooManyRefusals_AnswersTooManyRequests**: With a limit of three, three refused tokens
are answered with 401 and then a good token from the same address is answered with 429 as a problem document; a person
can still sign in with a password. This scenario is tested by
`ApiTokenAuthentication_Request_TooManyRefusals_AnswersTooManyRequests`.

**ApiTokenAuthentication_Request_RefusedToken_IsAuditedOnceWithoutTheValue**: A revoked token is presented three times.
The audit trail has one `token.use-failed` entry that names the reason and the display id, and no entry holds the token
value. This scenario is tested by `ApiTokenAuthentication_Request_RefusedToken_IsAuditedOnceWithoutTheValue`.

**ApiTokenAuthentication_Request_TokenOnAuthAndBlazorRoutes_IsNotSignedIn**: The web socket negotiation of the pages
(`/_blazor/negotiate`) and the sign-out and sign-in endpoints (`/auth/logout`, `/auth/login`) are posted to once with a
valid token and once without. The status and the redirect address are the same in both, no challenge is sent, and the
audit trail has no new entry, so the token changed nothing and did not act as its owner. This scenario is tested by
`ApiTokenAuthentication_Request_TokenOnAuthAndBlazorRoutes_IsNotSignedIn`.

**ApiTokenAuthentication_Request_AnyOutcome_NeverLogsTheToken**: The application keeps its log in a capturing provider.
A token is created, then presented while valid (200), a well-formed unknown value is presented, and a revoked token is
presented (both 401). The log is not empty, and no message or structured property of any entry contains a token value,
its secret part, or the token prefix. This scenario is tested by
`ApiTokenAuthentication_Request_AnyOutcome_NeverLogsTheToken`.
