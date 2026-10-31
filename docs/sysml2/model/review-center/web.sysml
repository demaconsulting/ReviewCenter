package ReviewCenter {
    part def WebSubsystem {
        doc /* Blazor Server user interface: the HTML shell, the menu bar and footer of every page, URL routing, the home page, the inbox page, the About page, the built-in Help, the sign-in (with an optional button for an OpenID Connect provider and, for an account with a second factor, a second step for the code), account (with the appearance choice and the setup of the second factor), and user administration pages (which show that such an account has no password and who has a second factor), the project, review, and checklist pages, including review comments and the pages that hold the creation forms, the plan page that creates the reviews of a repository from its review-sets, the review history and audit trail pages, and the page that administers the git repositories of one project. Every page shows a project only to a user who may see it, and a project that is hidden looks like a project that does not exist; a system administrator or a project admin additionally manages the members of a project. */

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
        part totpLoginPage : TotpLoginPage;
        part accountPage : AccountPage;
        part usersPage : UsersPage;
        part historyPage : HistoryPage;
        part auditPage : AuditPage;
        part repositoriesPage : RepositoriesPage;
        part inboxPage : InboxPage;
    }
}
