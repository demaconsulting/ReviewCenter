package ReviewCenter {
    part def ReviewService {
        doc /* Creates draft reviews from uploaded files or from the changes between two revisions of a registered git repository, freezing the changed content, enforcing the review and upload rules, and reads reviews back. Records the creator of a review and whether an actor that came through a personal access token created it, naming the token id in the audit entry. Also reads one file back as a line diff between its baseline and head content (`GetDiffAsync`), computed by `LineDiff`. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ReviewService.cs, Review.cs, ReviewFile.cs, ReviewState.cs, RepositoryReviewResult.cs, and UploadedFile.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ReviewServiceTests.cs and ReviewServiceRepositoryTests.cs */
        comment designRef /* Design: docs/design/review-center/core/review-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/review-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/review-service.yaml */
    }
}
