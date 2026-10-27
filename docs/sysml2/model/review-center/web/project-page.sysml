package ReviewCenter {
    part def ProjectPage {
        doc /* Pages that show one project with its reviews as rows, each with its approval status (Approved or In review), and link to the plan page when a repository is enabled; and the separate page with the form to create a review from uploaded files or from a registered git repository (head revision, optional baseline revision, selection). */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/ProjectPage.razor, ProjectPage.razor.cs, NewReviewPage.razor, and NewReviewPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/ProjectPageTests.cs and NewReviewPageRepositoryTests.cs */
        comment designRef /* Design: docs/design/review-center/web/project-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/project-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/project-page.yaml */
    }
}
