package ReviewCenter {
    part def RevalidatingAuthenticationStateProvider {
        doc /* The authentication state of an open page: it starts from the signed-in user of the request and, on a timer set by the Auth:RevalidationSeconds setting, re-reads the user and applies the same rule as the cookie check, so that a page that stays open and makes no request stops being signed in when its user is disabled, removed, or changed (including a change of the system administrator state). A user who cannot be read is treated as signed out (fail closed), a cancelled read ends the check cleanly, and the check stops when the page connection closes. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/RevalidatingAuthenticationStateProvider.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/RevalidatingAuthenticationStateProviderTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/revalidating-authentication-state-provider.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/revalidating-authentication-state-provider.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/revalidating-authentication-state-provider.yaml */
    }
}
