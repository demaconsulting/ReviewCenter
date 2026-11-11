package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def App {
        doc /* HTML shell and router that select the page for a requested URL, the not-found page (`/not-found`, also the answer with status 404 to a page request that no page handles), the page frame with the menu bar header (home link, primary navigation including an Inbox tab with a badge of the number of reviews that need the user's action, cached per user for 30 seconds and refreshed on navigation, help, the sign-in state and the system administrator links) that confines a user with a temporary password to the account, login, help, and About pages by a redirect to the account page and hides the navigation tabs for that user, and the footer with the version and the About link, the script that applies the stored light, dark, or system appearance before the first paint, the session gate that replaces the content of an open page by a session-ended message when its user is no longer signed in, and the browser functions that scroll a page element into view, replace the address of the page without navigating, and copy text to the clipboard (`scrollToTestId`, `replaceUrl`, `copyText`). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Web/App.razor", "src/DemaConsulting.ReviewCenter/Web/Routes.razor", "src/DemaConsulting.ReviewCenter/Web/NotFoundPage.razor", "src/DemaConsulting.ReviewCenter/Web/SessionGate.razor", "src/DemaConsulting.ReviewCenter/Web/MainLayout.razor", "src/DemaConsulting.ReviewCenter/Web/MainLayout.razor.cs", "src/DemaConsulting.ReviewCenter/Hosting/ServerHost.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Web/AppTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Web/HelpPageTests.cs");
            designFiles = ("docs/design/review-center/web/app.md");
            verificationFiles = ("docs/verification/review-center/web/app.md");
            requirementFiles = ("docs/reqstream/review-center/web/app.yaml");
        }
    }
}
