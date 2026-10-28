### HomePage

#### Verification Approach

`HomePage` is verified with component tests defined in `HomePageTests.cs`, which use the bUnit test context to render
the real component. The signed-in user is set through the test authentication state of `PageTestBase`, so each test
chooses between an anonymous visitor, an ordinary user, and a system administrator. Tests locate elements by their
`data-testid`
attributes and by the links of the rendered markup. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The page shows the product title.
- A visitor who is not signed in sees the tagline, no link, and no "Get started" section.
- A signed-in user who is no system administrator sees the "Get started" section with exactly one link, to the
  projects page.
- A signed-in system administrator sees three links, to the projects, audit, and users pages, and no card for
  repositories, which belong to projects.

#### Test Scenarios

**HomePage_Render_Default_ShowsTitle**: The page is rendered; the heading text "ReviewCenter" is present. This
scenario is tested by `HomePage_Render_Default_ShowsTitle`.

**HomePage_Render_Anonymous_ShowsTaglineAndNoLinks**: Nobody is signed in and the page is rendered; the tagline
"Web-based code review for regulated projects." is shown, and there is no link and no "Get started" section. This
scenario is tested by `HomePage_Render_Anonymous_ShowsTaglineAndNoLinks`.

**HomePage_Render_Reviewer_ShowsGetStartedWithProjectsOnly**: An ordinary user is signed in and the page is rendered;
the projects link points to `/projects` and it is the only link on the page. This scenario is tested by
`HomePage_Render_Reviewer_ShowsGetStartedWithProjectsOnly`.

**HomePage_Render_Admin_ShowsGetStartedWithAdministrationLinks**: A system administrator is signed in and the page is
rendered; there are three links, and those to the audit and users pages point to `/audit` and `/users`, and there is no
repositories card. This scenario is tested by `HomePage_Render_Admin_ShowsGetStartedWithAdministrationLinks`.
