package ReviewCenter {
    part def WebSubsystem {
        doc /* Blazor Server user interface: the HTML shell, the menu bar and footer of every page, URL routing, the home page, the About page, the built-in Help, the sign-in, account (with the appearance choice), and user administration pages, the project, review, and checklist pages, including review comments and the pages that hold the creation forms, the plan page that creates the reviews of a repository from its review-sets, the review history and audit trail pages, and the git repository administration page. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/WebSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/web.md */
        comment verificationRef /* Verification: docs/verification/review-center/web.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web.yaml */

        part app : App;
        part homePage : HomePage;
        part aboutPage : AboutPage;
        part helpPage : HelpPage;
        part projectsPage : ProjectsPage;
        part projectPage : ProjectPage;
        part planPage : PlanPage;
        part reviewPage : ReviewPage;
        part checklistPage : ChecklistPage;
        part loginPage : LoginPage;
        part accountPage : AccountPage;
        part usersPage : UsersPage;
        part historyPage : HistoryPage;
        part auditPage : AuditPage;
        part repositoriesPage : RepositoriesPage;
    }
}
