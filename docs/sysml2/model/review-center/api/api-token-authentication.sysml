package ReviewCenter {
    part def ApiTokenAuthentication {
        doc /* The authentication scheme and the authorization policy of the API: turns a personal access token, sent as a bearer token or as the password of HTTP Basic credentials, into the principal of its owner (with the system administrator role claim only when the owner is a system administrator), marked as coming through a token, or into one generic 401 refusal (also for an owner who must still change a temporary password, who gets no token access) that also offers the Basic challenge; leaves requests without a token header to the other schemes; ignores the cookie on the API and the token everywhere else; throttles repeated failures per client address with the sign-in throttle; and records refused uses in the audit trail without a token value. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Api/ApiTokenAuthentication.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Api/ApiTokenAuthenticationTests.cs */
        comment designRef /* Design: docs/design/review-center/api/api-token-authentication.md */
        comment verificationRef /* Verification: docs/verification/review-center/api/api-token-authentication.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/api/api-token-authentication.yaml */
    }
}
