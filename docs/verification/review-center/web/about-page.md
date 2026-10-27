### AboutPage

#### Verification Approach

`AboutPage` is verified with component tests defined in `AboutPageTests.cs`, which use the bUnit test context to render
the real component. The signed-in state is set through the test authentication state of `PageTestBase`; one test renders
the page with nobody signed in. Tests locate elements by their `data-testid` attributes and assert on the rendered
text and links. No mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All component tests pass with zero failures.
- The page, rendered with nobody signed in, shows the full version of the program, names DEMA Consulting, and links to
  the project page on GitHub.
- The page names the main features: threaded comments, the checklist, and the audit trail.

#### Test Scenarios

**AboutPage_Render_Anonymous_ShowsVersionCopyrightAndProjectLink**: Nobody is signed in and the page is rendered; the
version element holds exactly the full version of the program, the page text contains "DEMA Consulting", and the
link to the project points to `https://github.com/demaconsulting/ReviewCenter`. This scenario is tested by
`AboutPage_Render_Anonymous_ShowsVersionCopyrightAndProjectLink`.

**AboutPage_Render_Default_DescribesWhatReviewCenterDoes**: The page is rendered; the text contains "threaded
comments", "checklist", and "audit trail". This scenario is tested by
`AboutPage_Render_Default_DescribesWhatReviewCenterDoes`.
