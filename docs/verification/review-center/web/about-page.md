### AboutPage

#### Verification Approach

`AboutPage` is verified with component tests defined in `AboutPageTests.cs`, which use the bUnit test context to render
the real component. The signed-in state is set through the test authentication state of `PageTestBase`; one test renders
the page with nobody signed in. Tests locate elements by their `data-testid` attributes and assert on the rendered
text and links. Two tests check that a visitor who is not signed in is served the page: one renders the router at
`/about` with nobody signed in, and one requests the page through the real pipeline in memory.
No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The page, rendered with nobody signed in, shows the full version of the program, names DEMA Consulting, and links to
  the project page on GitHub.
- A visitor who is not signed in is shown the page by the router at `/about`, and is not asked to sign in; that the
  running server serves the page is verified for the `Web` subsystem.
- The page names the main features: threaded comments, the checklist, and the audit trail.

#### Test Scenarios

**AboutPage_Render_Anonymous_ShowsVersionCopyrightAndProjectLink**: Nobody is signed in and the page is rendered; the
version element holds exactly the full version of the program, the page text contains "DEMA Consulting", and the
link to the project points to `https://github.com/demaconsulting/ReviewCenter`. This scenario is tested by
`AboutPage_Render_Anonymous_ShowsVersionCopyrightAndProjectLink`.

**AboutPage_Render_AnonymousThroughRouter_ShowsPageAndNoSignInPrompt**: Nobody is signed in and the router, which
applies the authorization rules of a page, is rendered at `/about`; the version element of the page is shown, and
neither the access denied message nor the link to the login page is. This scenario is tested by
`AboutPage_Render_AnonymousThroughRouter_ShowsPageAndNoSignInPrompt`.

**AboutPage_Request_Anonymous_IsServedWithoutSignIn**: The real pipeline is started in memory and a visitor who is not
signed in requests `/about`; the answer is the page itself (status 200 with the version element), not a redirect to
the sign-in page. This scenario is tested by `AboutPage_Request_Anonymous_IsServedWithoutSignIn`, which is the
evidence of the subsystem requirement `ReviewCenter-Web-Help-About`.

**AboutPage_Render_Default_DescribesWhatReviewCenterDoes**: The page is rendered; the text contains "threaded
comments", "checklist", and "audit trail". This scenario is tested by
`AboutPage_Render_Default_DescribesWhatReviewCenterDoes`.
