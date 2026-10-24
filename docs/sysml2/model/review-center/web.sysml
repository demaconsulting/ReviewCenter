package ReviewCenter {
    part def WebSubsystem {
        doc /* Blazor Server user interface: the HTML shell, URL routing, the home page, and the project and review pages, including review comments. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/WebSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/web.md */
        comment verificationRef /* Verification: docs/verification/review-center/web.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web.yaml */

        part app : App;
        part homePage : HomePage;
        part projectsPage : ProjectsPage;
        part projectPage : ProjectPage;
        part reviewPage : ReviewPage;
    }
}
