package ReviewCenter {
    part def App {
        doc /* HTML shell and router that select the page for a requested URL, the page frame with the menu bar header (home link, primary navigation, help, the sign-in state and the administrator links) and the footer with the version and the About link, the script that applies the stored light, dark, or system appearance before the first paint, the session gate that replaces the content of an open page by a session-ended message when its user is no longer signed in, and the browser functions that scroll a page element into view and replace the address of the page without navigating (`scrollToTestId`, `replaceUrl`). */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/App.razor, Routes.razor, SessionGate.razor, and MainLayout.razor and MainLayout.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/AppTests.cs (and the header help link in HelpPageTests.cs) */
        comment designRef /* Design: docs/design/review-center/web/app.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/app.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/app.yaml */
    }
}
