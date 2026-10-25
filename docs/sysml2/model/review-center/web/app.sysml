package ReviewCenter {
    part def App {
        doc /* HTML shell and router that select the page for a requested URL, the page header with the sign-in state and the administrator links (users, repositories, and audit), the session gate that replaces the content of an open page by a session-ended message when its user is no longer signed in, and the browser function that scrolls a page element into view. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/App.razor, Routes.razor, SessionGate.razor, and MainLayout.razor and MainLayout.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AppTests.cs */
        comment designRef /* Design: docs/design/review-center/web/app.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/app.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/app.yaml */
    }
}
