package ReviewCenter {
    part def BallotConsolidation {
        doc /* Consolidates the submitted ballots of the reviewer slots of a review into one agreed standing of every check and section (a check is agreed when every submitted ballot answers it the same way, disputed when they differ, and pending when none answers it), derives the outcome, and tests whether a ballot is complete. Also projects the ballots of a record into the consolidated checklist state that the readers of the checklist use (the digest, the approval and progress counts, the inbox, the follow-up carry, and the attestation), and into the checklist state of one ballot for its owner, a draft included (`BallotProjection`); only submitted ballots count, except that the sole ballot of a single-slot review counts before it is handed in for progress and blockers. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/BallotConsolidation.cs, src/DemaConsulting.ReviewCenter.Core/BallotConsolidationResult.cs, src/DemaConsulting.ReviewCenter.Core/BallotProjection.cs, src/DemaConsulting.ReviewCenter.Core/BallotEdits.cs, src/DemaConsulting.ReviewCenter.Core/Ballot.cs, src/DemaConsulting.ReviewCenter.Core/BallotStatus.cs, src/DemaConsulting.ReviewCenter.Core/BallotState.cs, src/DemaConsulting.ReviewCenter.Core/ReviewOutcome.cs, src/DemaConsulting.ReviewCenter.Core/ReviewConclusion.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/BallotConsolidationTests.cs and test/DemaConsulting.ReviewCenter.Tests/Core/BallotProjectionTests.cs */
        comment designRef /* Design: docs/design/review-center/core/ballot-consolidation.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/ballot-consolidation.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/ballot-consolidation.yaml */
    }
}
