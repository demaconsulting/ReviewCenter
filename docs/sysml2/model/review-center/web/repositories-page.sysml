package ReviewCenter {
    part def RepositoriesPage {
        doc /* Page, limited to administrators, that lists the registered git repositories and lets an administrator register, edit, enable, disable, and remove them. The access token is write-only and is never shown. The administrator role is checked again on load and on every action. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/RepositoriesPage.razor and RepositoriesPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/RepositoriesPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/repositories-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/repositories-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/repositories-page.yaml */
    }
}
