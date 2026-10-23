package ReviewCenterPuppet {
    part def SessionStore {
        doc /* Persists session files and detects and cleans up stale sessions whose daemon has died. */

        comment sourceRef /* Source: tools/puppet/SessionStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Puppet.Tests/SessionStoreTests.cs */
        comment designRef /* Design: docs/design/review-center-puppet/session-store.md */
        comment verificationRef /* Verification: docs/verification/review-center-puppet/session-store.md */
        comment reqRef /* Requirements: docs/reqstream/review-center-puppet/session-store.yaml */
    }
}
