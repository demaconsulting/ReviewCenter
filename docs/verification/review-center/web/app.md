### App

#### Verification Approach

`App` is verified with tests defined in `AppTests.cs`. The shell is verified by starting the real
server pipeline on an in-memory server and requesting the root document. The router (`Routes`)
and the layout (`MainLayout`) are verified with component tests that set the navigation address and the signed-in
user (through the test authentication state of `PageTestBase`) and render the component in a test context. No
mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- The root document contains the shell elements: head outlet content, the router output, and
  the client script reference.
- The router renders the home page for the root URL.
- The router renders the not-found message for a URL that no page handles.
- The router applies the authorization rules of a page: a reviewer opening the users route sees the access denied
  message and not the page, an administrator sees the page, and a signed-out user opening a protected route sees
  a link to the login page (returning to the requested page) and not the page.
- The layout header shows the display name of the signed-in user with the links to the account page and the logout
  button, and the link to the users page only for an administrator; for nobody signed in it shows only the link to
  the login page.

#### Test Scenarios

**App_Render_ServedAtRoot_ContainsShellElements**: The pipeline is started in memory and the
root document is requested; the response contains the shell elements. This scenario is tested by
`App_Render_ServedAtRoot_ContainsShellElements`.

**Routes_Render_RootUrl_RendersHomePage**: The navigation address is set to the root URL and
`Routes` is rendered; the home page is shown. This scenario is tested by
`Routes_Render_RootUrl_RendersHomePage`.

**Routes_Render_UnknownUrl_ShowsNotFoundMessage**: The navigation address is set to a URL that
no page handles and `Routes` is rendered; the not-found message is shown. This scenario is
tested by `Routes_Render_UnknownUrl_ShowsNotFoundMessage`.

**Routes_Render_ReviewerOpensUsersRoute_ShowsAccessDeniedNotUserList**: A reviewer is signed in, a user exists, the
navigation address is `/users` and `Routes` is rendered; the access denied message is shown, and neither the
creation form nor the user's name is rendered. This scenario is tested by
`Routes_Render_ReviewerOpensUsersRoute_ShowsAccessDeniedNotUserList`.

**Routes_Render_AdminOpensUsersRoute_ShowsUsersPage**: An administrator is signed in and `Routes` is rendered at
`/users`; the creation form is shown and there is no access denied message. This scenario is tested by
`Routes_Render_AdminOpensUsersRoute_ShowsUsersPage`.

**Routes_Render_AnonymousOpensProjectsRoute_ShowsLoginLinkNotPage**: Nobody is signed in and `Routes` is rendered at
`/projects`; the projects page is not rendered, and a link to the login page that returns to `/projects` is
shown. This scenario is tested by `Routes_Render_AnonymousOpensProjectsRoute_ShowsLoginLinkNotPage`.

**MainLayout_Render_SignedInUser_ShowsNameAccountAndLogout**: `MainLayout` is rendered for a signed-in reviewer; the
header shows the display name, the link to the account page, and the logout button, and no link to the users page
and no sign-in link. After the user signs in as an administrator, the link to the users page appears. This scenario
is tested by `MainLayout_Render_SignedInUser_ShowsNameAccountAndLogout`.

**MainLayout_Render_NobodySignedIn_ShowsSignInLink**: `MainLayout` is rendered with nobody signed in; the header
shows the sign-in link to `/login` and no user name and no logout button. This scenario is tested by
`MainLayout_Render_NobodySignedIn_ShowsSignInLink`.
