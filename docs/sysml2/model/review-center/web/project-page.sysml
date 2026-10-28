package ReviewCenter {
    part def ProjectPage {
        doc /* Pages that show one project to a user who may see it (a hidden project looks like a project that does not exist) with its reviews as rows, each with its approval status (Approved or In review), a link to the plan page when a repository of the project is enabled, Repositories and Audit trail buttons for whoever administers the project, and, for those administrators only (project admins and system administrators), the members of the project with their roles to change or remove and a button to add one; the separate pages to add an existing user by the exact user name and to create a local account for the project, which shows the generated temporary password once; and the separate page with the form to create a review from uploaded files or from an enabled git repository of the project (head revision, optional baseline revision, selection). Every handler re-checks access through the shared guard and clears loaded data when it is gone. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/ProjectPage.razor, ProjectPage.razor.cs, NewReviewPage.razor, NewReviewPage.razor.cs, NewMemberPage.razor, NewMemberPage.razor.cs, NewMemberAccountPage.razor, and NewMemberAccountPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/ProjectPageTests.cs, NewReviewPageRepositoryTests.cs, NewMemberPageTests.cs, and NewMemberAccountPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/project-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/project-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/project-page.yaml */
    }
}
