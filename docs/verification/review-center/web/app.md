### App

#### Verification Approach

`App` is verified with tests defined in `AppTests.cs` and, for the help link of the header, in `HelpPageTests.cs`. The
shell is verified by starting the real server pipeline on an in-memory server and requesting the root document. The
router (`Routes`) and the layout (`MainLayout`) are verified with component tests that set the navigation address and
the signed-in user (through the test authentication state of `PageTestBase`) and render the component in a test
context. The Inbox badge is verified over the real inbox service and fake stores; the only wrapper is a user store that
counts, and can fail, the user look-up that starts every count, and the cache age is moved with the test clock, so
no test waits for time to pass. Otherwise no mocking is applied.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- The root document contains the shell elements: the base address, the page title, the router output, and the client
  script reference.
- The root document contains the script that applies the stored appearance, and the script comes before the style
  sheet link.
- The router renders the home page for the root URL.
- The router renders the not-found page, with a link back to the projects page, for a URL that no page handles.
- The not-found page is rendered inside the page frame, so the header link to the home page and the footer are present.
- A request to the real pipeline for an address that no page handles is answered with status 404 and the not-found page
  with the header and the footer. A request under `/api`, a request for an address that ends in a file extension, and a
  POST keep a plain 404 without the page.
- The router shows the creation form, and no review, for the address of the new-review form of a project.
- The router applies the authorization rules of a page: an ordinary user opening the users route sees the access
  denied
  message and not the page, a system administrator sees the page, and a signed-out user opening a protected route sees
  a link to the login page (returning to the requested page) and not the page.
- The layout header shows the display name of the signed-in user with the links to the projects page and the account
  page and the logout button, and the links to the audit and users pages only for a system administrator (there is no
  repositories link, because repositories belong to projects); for
  nobody signed in it shows the link to the login page and no projects link, no name, and no logout button.
- The header shows the help link to everyone, and for nobody signed in it comes before the sign-in link.
- The header has exactly one link to the home page.
- While the session carries the must-change-password claim, the header has no projects and users tabs, and the browser
  is sent to `/account` from the projects and review pages but not from the account, help, and about pages.
- The footer shows the product name, the copyright holder, a version that begins with the version of the program and is
  no longer than it, and a link to the About page, also when nobody is signed in.
- A signed-in user sees the Inbox tab, a link to `/inbox`, with a badge of the number of reviews that need the user's
  action (accessible label with the exact number, singular for one); the badge is absent at zero, shows `99+` above
  ninety-nine, and the tab itself stays.
- Nobody signed in and a user with a temporary password see no Inbox tab and no badge, and the inbox service is not
  asked for a count (the user look-up that starts every count is not made).
- The count is read once per user in 30 seconds however many times the layout is rendered (the badge may be that much
  behind), again once the injected clock has moved past 30 seconds, separately for each user, and again on a
  navigation after it has expired but not on one inside the 30 seconds; a layout that was disposed no longer reads it.
- A failure to read the count hides the badge, keeps the rest of the page (name, tab, footer), and is not cached.
- The page frame component `SessionGate` shows its content while somebody is signed in, shows the "Session ended"
  message with a link to the login page that returns to the page, and none of the content, when nobody is, and
  replaces the content by the message when the sign-in state becomes anonymous while the page is open.
- Every protected page tested (projects, project, plan, review, checklist, account, and users) is wrapped in the gate,
  so none shows data or actions to a user who is signed out.

#### Test Scenarios

**App_Render_ServedAtRoot_ContainsShellElements**: The pipeline is started in memory and the root document is
requested; the response contains the base address, the page title, and the client script reference. This scenario is
tested by `App_Render_ServedAtRoot_ContainsShellElements`.

**App_Render_ServedAtRoot_AppliesStoredThemeBeforeStyleSheet**: The pipeline is started in memory and the root
document is requested; the appearance script (found by the storage key of the choice) and the `data-theme` attribute are
in the document, and the script comes before the style sheet link. This scenario is tested by
`App_Render_ServedAtRoot_AppliesStoredThemeBeforeStyleSheet`.

**App_Render_ServedAtRoot_DefinesCopyTextHelper**: The pipeline is started in memory and the root document is requested;
the document defines `copyText`, uses `navigator.clipboard`, and has the fallback that creates a text area and calls
`document.execCommand('copy')`. This scenario is tested by `App_Render_ServedAtRoot_DefinesCopyTextHelper`.

**App_Render_ServedAtRoot_DefinesFocusHelperAndCtrlEnterListener**: The pipeline is started in memory and the root
document is requested; the document defines `focusTestId` and a `keydown` listener that reads the attribute
`data-ctrl-enter-submits` and checks Ctrl or Meta with Enter. The browser behavior itself is not run here; it is
checked by hand in the browser. This scenario is tested by `App_Render_ServedAtRoot_DefinesFocusHelperAndCtrlEnterListener`.

**Routes_Render_RootUrl_RendersHomePage**: The navigation address is set to the root URL and `Routes` is rendered; the
home page is shown. This scenario is tested by `Routes_Render_RootUrl_RendersHomePage`.

**Routes_Render_UnknownUrl_ShowsNotFoundMessage**: The navigation address is set to a URL that no page handles and
`Routes` is rendered; the not-found page is shown with its "Page not found" heading and the link back to the projects
page. This scenario is tested by `Routes_Render_UnknownUrl_ShowsNotFoundMessage`.

**Routes_Render_UnknownUrl_ShowsNotFoundInsidePageFrameWithHeaderAndFooter**: Nobody is signed in, the navigation
address is set to a URL that no page handles and `Routes` is rendered; the not-found page, the single header link to the
home page, and the footer with the link to the About page are all present. This scenario is tested by
`Routes_Render_UnknownUrl_ShowsNotFoundInsidePageFrameWithHeaderAndFooter`.

**App_Request_UnknownPath_Returns404WithFramedNotFoundPage**: The pipeline is started in memory and an address that no
page handles is requested; the response status is 404 and the document contains the not-found page, the header link to
the home page, and the footer. This scenario is tested by `App_Request_UnknownPath_Returns404WithFramedNotFoundPage`.

**App_Request_UnknownApiPath_Returns404WithoutPageFrame**: An unknown address under `/api` is requested; the status is
404 and the body has neither the page frame nor the not-found text. This scenario is tested by
`App_Request_UnknownApiPath_Returns404WithoutPageFrame`.

**App_Request_UnknownFilePath_Returns404WithoutPageFrame**: A missing style sheet (`/missing.css`) is requested; the
status is 404 and the body has no not-found text. This scenario is tested by
`App_Request_UnknownFilePath_Returns404WithoutPageFrame`.

**App_Request_PostToUnknownPath_Returns404WithoutPageFrame**: A POST to an unknown address is sent; the status is 404
and the body has no not-found text. This scenario is tested by `App_Request_PostToUnknownPath_Returns404WithoutPageFrame`.

**Routes_Render_NewReviewRoute_ShowsFormNotReview**: A user is signed in, a project exists, the navigation address is
the new-review address of the project and `Routes` is rendered; the new-review form is shown and the review page is
not. This scenario is tested by `Routes_Render_NewReviewRoute_ShowsFormNotReview`.

**Routes_Render_ReviewerOpensUsersRoute_ShowsAccessDeniedNotUserList**: An ordinary user is signed in, a user exists, the
navigation address is `/users` and `Routes` is rendered; the access denied message is shown, and neither the
creation form nor the user's name is rendered. This scenario is tested by
`Routes_Render_ReviewerOpensUsersRoute_ShowsAccessDeniedNotUserList`.

**Routes_Render_AdminOpensUsersRoute_ShowsUsersPage**: A system administrator is signed in and `Routes` is rendered at
`/users`; the New user button is shown and there is no access denied message. This scenario is tested by
`Routes_Render_AdminOpensUsersRoute_ShowsUsersPage`.

**Routes_Render_AnonymousOpensProjectsRoute_ShowsLoginLinkNotPage**: Nobody is signed in and `Routes` is rendered at
`/projects`; the projects page is not rendered, and a link to the login page that returns to `/projects` is
shown. This scenario is tested by `Routes_Render_AnonymousOpensProjectsRoute_ShowsLoginLinkNotPage`.

**MainLayout_Render_SignedInUser_ShowsNameAccountAndLogout**: `MainLayout` is rendered for a signed-in ordinary user;
the header shows the link to the projects page, the display name, the link to the account page, and the logout button,
and no audit or users link and no sign-in link. After the user signs in as a system administrator, the links to the
users page and the audit page (`/audit`) appear, and there is still no repositories link, because repositories belong to
projects. This scenario is tested by
`MainLayout_Render_SignedInUser_ShowsNameAccountAndLogout`.

**MainLayout_Render_NobodySignedIn_ShowsSignInLink**: `MainLayout` is rendered with nobody signed in; the header shows
the sign-in link to `/login`, no projects link, and no user name and no logout button. This scenario is tested by
`MainLayout_Render_NobodySignedIn_ShowsSignInLink`.

**MainLayout_Render_NobodySignedIn_ShowsHelpLinkBeforeSignIn**: `MainLayout` is rendered with nobody signed in; the
help link points to `/help`, and the links of the right-hand group of the header are the help link and then the
sign-in link, in that order. This scenario is tested by `MainLayout_Render_NobodySignedIn_ShowsHelpLinkBeforeSignIn`.

**MainLayout_Render_NobodySignedIn_ShowsFooterWithVersionAndAboutLink**: `MainLayout` is rendered with nobody signed in;
the footer names the product and the copyright holder, the version text begins with the version of the program and is
not longer than it, the About link points to `/about`, and there is exactly one link to the home page, to `/`. This
scenario is tested by `MainLayout_Render_NobodySignedIn_ShowsFooterWithVersionAndAboutLink`.

**MainLayout_Render_TemporaryPassword_ConfinesUserToAccountPage**: A theory over the pages `projects`, `reviews/1`,
`account`, `help/project-members`, and `about`, with a session that carries the must-change-password claim; `MainLayout`
is rendered with no projects and no users tab, and the browser is sent to `/account` from the first two pages only. This
scenario is tested by `MainLayout_Render_TemporaryPassword_ConfinesUserToAccountPage`.

**MainLayout_Navigate_TemporaryPassword_RedirectsLaterNavigationAndStopsAfterDispose**: A flagged session renders
`MainLayout` on `/help`, then navigates (the layout stays alive) to `/account` and `/help/project-members`, which are
not redirected, and to `/projects`, which is sent to `/account`. After the layout instance is disposed, navigating to
`/projects` is no longer redirected, so the handler was removed. This scenario is tested by
`MainLayout_Navigate_TemporaryPassword_RedirectsLaterNavigationAndStopsAfterDispose`.

**MainLayout_Navigate_OrdinaryUser_IsNotRedirected**: A signed-in user without the claim navigates from `/help` to
`/projects` and stays there. This scenario is tested by `MainLayout_Navigate_OrdinaryUser_IsNotRedirected`.

**MainLayout_Render_UserWithActionItems_ShowsInboxTabWithCount**: A theory over one and two reviews in which an
ordinary member has to sign; `MainLayout` is rendered for the member and the Inbox tab links to `/inbox` and holds a
badge with the number and the label "1 review needs your action" or "2 reviews need your action". This scenario is
tested by `MainLayout_Render_UserWithActionItems_ShowsInboxTabWithCount`.

**MainLayout_Render_NoActionItems_ShowsInboxTabWithoutBadge**: A member has nothing to do; the Inbox tab is shown and
the badge is not. This scenario is tested by `MainLayout_Render_NoActionItems_ShowsInboxTabWithoutBadge`.

**MainLayout_Render_ManyActionItems_CapsBadgeAt99Plus**: A hundred reviews wait for the member; the badge shows `99+`
and its label says "100 reviews need your action". This scenario is tested by
`MainLayout_Render_ManyActionItems_CapsBadgeAt99Plus`.

**MainLayout_Render_NobodyOrTemporaryPassword_ShowsNoInboxTabAndReadsNoCount**: A review waits for a member; the layout
is rendered with nobody signed in and then for the member with a temporary password; neither shows the Inbox tab or the
badge, and the counting user store, which the count would call first, records no look-up. This scenario is tested by
`MainLayout_Render_NobodyOrTemporaryPassword_ShowsNoInboxTabAndReadsNoCount`.

**MainLayout_Render_WithinThirtySeconds_ReadsCountOnceThenAgainAfterwards**: The layout is rendered twice, a second
review is assigned, and it is rendered after 29 seconds of the test clock and after 31 seconds; the first three show 1
and made one look-up, the last shows 2 after a second look-up. This scenario is tested by
`MainLayout_Render_WithinThirtySeconds_ReadsCountOnceThenAgainAfterwards`.

**MainLayout_Render_TwoUsers_KeepsTheirCountsApart**: The layout is rendered for a member with a review to sign and then
for another member with none; the first shows 1, the second no badge, and each caused a look-up. This scenario is tested
by `MainLayout_Render_TwoUsers_KeepsTheirCountsApart`.

**MainLayout_Navigate_ChangedCount_RefreshesBadgeAndStopsAfterDispose**: The layout shows 1, a review is assigned, and
the user navigates within 30 seconds (no new look-up, still 1); after the clock moves 31 seconds a navigation makes the
badge show 2 with one more look-up; after the layout is disposed and the clock has moved again, a navigation makes no
look-up. This scenario is tested by `MainLayout_Navigate_ChangedCount_RefreshesBadgeAndStopsAfterDispose`.

**MainLayout_Render_CountFails_HidesBadgeAndStillRendersPage**: The user store throws on the look-up; the layout shows
the name, the Inbox tab, and the footer and no badge; when the store works again within 30 seconds the next render shows
the badge, so the failure was not cached. This scenario is tested by
`MainLayout_Render_CountFails_HidesBadgeAndStillRendersPage`.

**SessionGate_Render_SignedInUser_ShowsContent**: `SessionGate` is rendered around some content for a signed-in user;
the content is shown and no session-ended message. This scenario is tested by
`SessionGate_Render_SignedInUser_ShowsContent`.

**SessionGate_Render_Anonymous_ShowsSessionEndedWithLoginLinkAndNoContent**: Nobody is signed in and the page is at
`/projects/1`; the gate shows the "Session ended" heading and a login link that returns to `/projects/1`, and none of
its content. This scenario is tested by `SessionGate_Render_Anonymous_ShowsSessionEndedWithLoginLinkAndNoContent`.

**SessionGate_Render_StateBecomesAnonymous_ReplacesContentWithMessage**: A gate shows its content; the sign-in state
then ends; the content is replaced by the message. This scenario is tested by
`SessionGate_Render_StateBecomesAnonymous_ReplacesContentWithMessage`.

**SessionGate_Render_EveryProtectedPageSignedOut_ShowsSessionEndedAndNoPage**: A theory over the projects, project,
plan, review, checklist, account, and users pages; each is rendered with nobody signed in, and each shows the message
and not its own root element. This scenario is tested by
`SessionGate_Render_EveryProtectedPageSignedOut_ShowsSessionEndedAndNoPage`.
