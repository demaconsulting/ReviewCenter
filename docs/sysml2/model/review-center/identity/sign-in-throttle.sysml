package ReviewCenter {
    part def SignInThrottle {
        doc /* In-memory limit on sign-in attempts: counters per client address and lower-cased typed user name and per client address over all names, in a fixed window, counted before the password is checked and given back on success only to the window that was counted (or, for the password step of an account with a second factor, only the address slot), the same outcome for known and unknown names, a bounded table that removes expired counters and then evicts its oldest counters at the cap, and the settings under Identity:Throttle with their defaults and limits. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Identity/SignInThrottle.cs and SignInThrottleOptions.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Identity/SignInThrottleTests.cs */
        comment designRef /* Design: docs/design/review-center/identity/sign-in-throttle.md */
        comment verificationRef /* Verification: docs/verification/review-center/identity/sign-in-throttle.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/identity/sign-in-throttle.yaml */
    }
}
