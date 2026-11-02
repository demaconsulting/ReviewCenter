package ReviewCenter {
    part def ReviewProgressService {
        doc /* Answers for every review of a project which stage it is in (derived from the approval status, never the stored state), how many signatures it has of the number needed and how many are out of date, how many ballots are handed in of the number needed and who has not handed in (progress that every viewer sees, while a viewer who may not see the other ballots yet is flagged as masked and gets no dispute count), whom it waits on, what blocks its approval (unanswered, failed, and disputed checks of the consolidated checklist of the submitted ballots, open threads) with counts that equal the blocker sentences, when it last changed according to the audit trail, and whether it is overdue, for everyone who may see the project, with a number of reads that does not grow with the number of reviews. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ReviewProgressService.cs and ReviewProgress.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ReviewProgressServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/review-progress-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/review-progress-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/review-progress-service.yaml */
    }
}
