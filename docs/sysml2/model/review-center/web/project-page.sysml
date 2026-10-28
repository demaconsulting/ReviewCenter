package ReviewCenter {
    part def ProjectPage {
        doc /* Pages that show one project to a user who may see it (a hidden project looks like a project that does not exist) with its reviews as rows, each with its approval status (Approved or In review), a link to the plan page when a repository of the project is enabled, a Repositories button for whoever manages the project, and, for a system administrator only, the members of the project with their roles, to add, remove, or change; and the separate page with the form to create a review from uploaded files or from an enabled git repository of the project (head revision, optional baseline revision, selection). */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/ProjectPage.razor, ProjectPage.razor.cs, NewReviewPage.razor, and NewReviewPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/ProjectPageTests.cs and NewReviewPageRepositoryTests.cs */
        comment designRef /* Design: docs/design/review-center/web/project-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/project-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/project-page.yaml */
    }
}
