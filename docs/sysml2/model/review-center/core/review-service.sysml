package ReviewCenter {
    part def ReviewService {
        doc /* Creates draft reviews from uploaded files, enforcing the review and upload rules, and reads reviews back. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ReviewService.cs, Review.cs, ReviewFile.cs, ReviewState.cs, and UploadedFile.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ReviewServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/review-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/review-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/review-service.yaml */
    }
}
