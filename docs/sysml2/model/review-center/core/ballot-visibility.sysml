package ReviewCenter {
    part def BallotVisibility {
        doc /* Decides whether a viewer may see what the other reviewers of a review with several reviewer slots answered (only after the viewer has handed in their own ballot, or when the viewer leads the review, administers the project, is a system administrator or an auditor, or the review is concluded), and keeps the answers, the disputed and failed counts, the provisional outcome, the conclusion text, the discussions about checks, and the audit details of answers from a viewer who may not. Gives the checklist page its ballot view (the differing answers side by side, the counts of handed-in ballots, and the Assign me and Submit and sign offers) and tells which discussions a viewer may start. Masking is applied when the data is read; the stored record, the audit chain, and the signature checks are untouched. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/BallotVisibility.cs, src/DemaConsulting.ReviewCenter.Core/BallotView.cs, src/DemaConsulting.ReviewCenter.Core/ThreadCapabilities.cs, src/DemaConsulting.ReviewCenter.Core/AuditMasking.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/BallotVisibilityTests.cs, test/DemaConsulting.ReviewCenter.Tests/Core/AuditMaskingTests.cs, and test/DemaConsulting.ReviewCenter.Tests/Core/BallotMaskingServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/ballot-visibility.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/ballot-visibility.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/ballot-visibility.yaml */
    }
}
