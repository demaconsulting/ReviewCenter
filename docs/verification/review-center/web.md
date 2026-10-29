## Web

### Verification Approach

The `Web` subsystem is verified by integration tests defined in `WebSubsystemTests.cs`. Each
test starts the real server pipeline on an in-memory test server, with a temporary database, through
`ServerHost.CreateApplication` and requests routes over HTTP (the pages behind the sign-in are requested by a user
signed in through the test helper `TestBrowser`), so the `App` and the page units run together with
the Core services and the Storage database exactly as they do in production. Tests assert on the returned
HTML. Behavior that needs a live browser connection (typing, selecting files, submitting forms, and the switch of
the `data-interactive` marker from `false` to `true`) is outside what plain HTTP requests can exercise. It is
verified at unit level by the component tests of the page units (see the children of the requirements in
`web.yaml`), not by these subsystem tests; the Puppet end-to-end test drives a real browser against a real server
over the live connection (sign in, open Projects from the header, create a project, switch the theme).

### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

### Acceptance Criteria

- All integration tests pass with zero failures.
- The home route answers HTTP status 200 with a document titled "ReviewCenter" that contains the product name and the
  tagline.
- The Help index, the help topic pages, and the About page are served to a signed-in user over HTTP (their content is
  verified by the component tests of `AboutPage` and `HelpPage`, and their service to an anonymous visitor by the
  system test `ReviewCenter_ServeVerb_Started_ServesHelpAndAboutPagesWithoutSignIn`).
- The projects route, which is the interactive page that the subsystem tests use for the marker, is served with the
  `data-interactive` marker set to `false`.
- A project and a review created through the services are presented by the pages with the correct fingerprint.
  (The creation forms and uploads are verified by the page units' component tests.)
- Threads created through the Core service are presented by the review page: the next-step line, the thread index,
  the summary, and the per-file open count are correct, and the Details section is collapsed.
- The review page links the checklist page and shows its progress; the checklist page presents the answers stored
  through the Core service, also when requested again.
- A review with a reviewer slot that was signed through the Core service is presented as Approved by the review page
  and the project page, with the signer and the record digest on the review page; when the record changes
  afterwards, both pages present it as In review and the review page shows the signature as out of date.
- The `Web` subsystem's pages are presented to a signed-in user; the sign-in itself is verified in the `Identity`
  subsystem.
- When the sign-in state of an open page ends, the page is replaced by the "Session ended" message and offers no
  actions. This needs the live state of a page, which plain HTTP requests do not have, so it is verified at unit level
  by the component tests of `SessionGate`, `ReviewPage`, `UsersPage`, and `RepositoriesPage` (see the children of the requirement
  `ReviewCenter-Web-SessionEnd` in `web.yaml`), together with the `Identity` subsystem test of the provider that ends
  the state.
- A system administrator who opens `/projects/{id}/repositories` is shown the repositories of that project; a user who
  is no member of it receives the same not-found page as for a project that does not exist.
- Every interactive element of every page has a `data-testid`, no identifier repeats within a page, and the breadcrumb links
  have their own identifiers (`breadcrumb-projects-link`, `breadcrumb-users-link`), distinct from the header tabs.
- The creation of a review from a repository (source choice, repository form, refusals, and skipped entries) and the
  display of the change kinds, the source, and the Diff / Head / Baseline views need a live connection, so they are
  verified at unit level by the component tests of `ProjectPage` and `ReviewPage` (see the children of the requirement
  `ReviewCenter-Web-RepositoryReviews` in `web.yaml`).
- The diff view of a review page (collapsed unchanged lines, line comments on both sides, threads under their lines,
  and the too-large state) needs a live connection, so it is verified at unit level by the component tests of
  `ReviewPage`; the subsystem test reads the stylesheet that keeps the code area scrolling inside itself on a phone
  (see the requirement `ReviewCenter-Web-DiffReview` in `web.yaml`).
- The plan page and the planning links need a live connection for the form and the creation, so they are verified at
  unit level by the component tests of `PlanPage`, `ProjectPage`, and `ReviewPage` (see the children of the requirement
  `ReviewCenter-Web-PlanReviews` in `web.yaml`); the subsystem test requests the plan route of a project that has a
  repository.
- Links to files, lines and threads (the routes, the highlight, the replacement of the address, the thread links, the
  messages for addresses that name nothing, and the return to a link after signing in over HTTP) are verified by the
  component tests of `ReviewPage` in `ReviewDeepLinkTests.cs`, which render the real router, and by the tests of
  `SignIn`; the subsystem test renders the router for a signed-out visitor (see the requirement
  `ReviewCenter-Web-DeepLinks` in `web.yaml`).
- The token card of the account page (the form, the value shown once with the copy button, the list with the state of
  each token, and the revocation) and the help topic on the API tokens need a live connection for the actions, so they
  are verified at unit level by the component tests of `AccountPage` and `HelpPage` (see the children of the requirement
  `ReviewCenter-Web-AccessTokens` in `web.yaml`).
- The management of project members (the member list and its actions, the pages for a new member and a new account,
  the temporary password and its confinement, the audit page of a project, and the help topic on project members) needs
  a live connection, so it is verified at unit level by the component tests of `ProjectPage`, `AuditPage`,
  `AccountPage`, `LoginPage`, `App`, and `HelpPage` (see the children of the requirements
  `ReviewCenter-Web-ProjectAccess`, `ReviewCenter-Web-SignIn`, and `ReviewCenter-Web-AuditPage` in `web.yaml`).
- The evidence settings page (who gets it, the switches and their defaults, the warnings, the save with its audit
  entry, a change by someone else, and a lost right) and the help topic on the evidence settings need a live connection,
  so they are verified at unit level by the component tests of `ProjectPage` (`EvidenceSettingsPageTests.cs`) and
  `HelpPage` (see the children of the requirement `ReviewCenter-Web-EvidenceSettings` in `web.yaml`). What the stored
  settings do to the API and the PDFs is verified in the `Api` and `Evidence` subsystems.
- Optional sign-in with an OpenID Connect provider: the login page offers the button of the provider beside the local
  form only while it is enabled, the users and account pages show that such an account has no password, the review
  page offers the confirmation at the provider instead of the password field, and the help topics mention it; the
  pages are verified at unit level by their component tests (`OidcPageTests.cs`) and the review page by the
  integration test of the `Identity` subsystem (see the children of the requirement `ReviewCenter-Web-OidcSignIn` in
  `web.yaml`).
- Optional second factor: the code is asked for in a page of its own after the password and shows no user name, the
  account page sets up, confirms (with ten recovery codes shown once and never the secret), and turns off the second
  factor, the users page shows who has it and lets a system administrator reset it, the sign form of a review asks for
  the code beside the password, and the help topic explains it; the pages are verified at unit level by their
  component tests (see the children of the requirement `ReviewCenter-Web-TotpSecondFactor` in `web.yaml`).

### Test Scenarios

**WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline**: The pipeline is started in memory
and the home route is requested; the response is 200 and contains the document title and the tagline. This
scenario is tested by
`WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline`.

**WebSubsystem_ProjectsRoute_InitialResponse_IsNotYetInteractive**: The projects route is requested
over plain HTTP by a signed-in user, where no live connection exists; the `data-interactive` marker in the response
is `false`. This scenario is tested by
`WebSubsystem_ProjectsRoute_InitialResponse_IsNotYetInteractive`.

**WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages**: The pipeline is started in memory on a temporary
database, a project with a review of three files (one of them binary) is created through the Core services, and
the projects page, the project page, and the review page are requested. The project and review are listed and
linked, and the review page shows the title, the Draft state, the fingerprint, and the path and hash of each
file, equal to the known values. This scenario is tested by
`WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages`.

**WebSubsystem_ReviewWithThreads_ArePresentedByThePage**: A review with one open whole-file thread and one resolved
line thread is created through the Core services of the real pipeline, and the review page is requested. The
summary shows one open and one resolved thread, the file shows "1 open thread", the next-step line names the open
thread, the index lists the open thread with an activity time read back from the database, and the Details section
is collapsed. This scenario is tested by
`WebSubsystem_ReviewWithThreads_ArePresentedByThePage`.

**WebSubsystem_Stylesheet_DiffRules_KeepCodeScrollingInsideItself**: The stylesheet source is read and checked for the
rules that keep the diff inside the page width: each row is a grid of two equal columns, long lines wrap inside their
cell, the code area still scrolls inside itself as a safety net, empty placeholders are muted, inline threads and
collapsed-run buttons stick to the left edge, below 40 rem the cells stack and the placeholders and the repeated
baseline copy of an unchanged line are hidden, and a narrower line-number gutter applies on a phone. This scenario
is tested by
`WebSubsystem_Stylesheet_DiffRules_KeepCodeScrollingInsideItself`. The measured width of the page at 390 by 844
pixels is checked in a real browser by the acceptance walk-through.

**WebSubsystem_Stylesheet_ThreadLink_IsButtonStyled**: The stylesheet source is read and checked: no `a.thread-link`
rule makes
the link look like plain text, and the thread header sizes the link like the buttons beside it, so the link and the Copy
link button of a thread look alike. This scenario is tested by `WebSubsystem_Stylesheet_ThreadLink_IsButtonStyled`.

**WebSubsystem_Stylesheet_DiffCommentControl_ShowsOnHoverFocusAndTouch**: The stylesheet source is read and the rules of
the `+` control of a diff row are checked: it is hidden with `opacity: 0`, shown on row hover, focus within the row, a
selected row, and keyboard focus, always shown under `@media (hover: none)`, and never taken out of the layout or the
tab order with `display: none` or `visibility: hidden`. This scenario is tested by
`WebSubsystem_Stylesheet_DiffCommentControl_ShowsOnHoverFocusAndTouch`.

**Routes_DeepLink_SignedOut_LoginLinkKeepsEscapesAndQuery**: The router is rendered for a signed-out visitor at the
address of a line link of a file whose name has a space and a hash, with a baseline side. The "Sign in required" link
is `/login?ReturnUrl=` followed by the whole address escaped once more, and no review page is drawn. This scenario is
tested by `Routes_DeepLink_SignedOut_LoginLinkKeepsEscapesAndQuery`.

**WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload**: A review is created through the Core services of the
real pipeline and two checks are answered, one Pass and one Fail with a justification. The checklist page is
requested twice and the review page once. Both loads of the checklist page say "56 checks unanswered.", show the
section progress "2 of 8", the pressed Pass button, and the saved justification; the review page shows "2 of 58
checks answered", a link to the checklist page, and "56 checklist checks unanswered, 1 check answered Fail." as the
next step. This
scenario is tested by `WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload`.

**WebSubsystem_SignedReview_ApprovalIsPresentedAndGoesOutOfDateWhenRecordChanges**: The pipeline is started in memory
on a temporary database, and a second user, a project, and a review with every check answered Pass are created
through the real Core services, with one reviewer slot of the signed-in user (primary) and the second user
(alternate). The review page shows the badge "In review" and the sign form of the slot. The second user signs
through `SigningService` with the digest read from the service; the review page then shows "Approved", the status
"Signed by Bob on ..." and the full record digest in the title of the Details element, and the project page shows
"Approved" for the review. A thread is then started, which changes the record; both pages show "In review" again
and the review page shows "Signature out of date". This scenario is tested by
`WebSubsystem_SignedReview_ApprovalIsPresentedAndGoesOutOfDateWhenRecordChanges`.

**WebSubsystem_AllPages_EveryInteractiveElementHasTestId**: A system administrator is signed in, and every route
(home, login, about, the help index, a help topic, an unknown help topic, account, users, the new user page,
projects, the new project page, a project, the new review page of a project, the plan page of a project that has a
repository, the repositories page and the new repository page of the project, the
plan page and the project page of a missing project, a review, a missing review, a checklist) is
requested over the in-memory server; every link, button, visible input, select, and text area in each response has a
`data-testid` in kebab-case. This scenario is tested by
`WebSubsystem_AllPages_EveryInteractiveElementHasTestId`.

**WebSubsystem_AllPages_TestIdsAreUniqueAndBreadcrumbsDistinct**: A system administrator is signed in and the project
page, the new review page, the plan page, the review page, its checklist page, its history page, the repositories page
and the new repository page of the project, and the new user page are requested. In each response no `data-testid`
value occurs twice, the breadcrumb link has its prefixed identifier (`breadcrumb-projects-link` or
`breadcrumb-users-link`), and the
header tab of the same name occurs exactly once. This scenario is tested by
`WebSubsystem_AllPages_TestIdsAreUniqueAndBreadcrumbsDistinct`.

**Repositories page**: The page of the repositories of a project is verified by its unit tests, which render it with
the component test host. It lists only the repositories of its project with the token state only and no delete
control, which is tested by `RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete` and
`RepositoriesPage_Render_OtherProjectsRepositories_AreNotListed`, and it stores a token without ever showing it, which
is tested by `NewRepositoryPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields`. A member who is no project
administrator, and a non-member, receive only the not-found message, which is tested by
`RepositoriesPage_Render_MemberOrNonMember_ShowsNotFoundOnly`; a non-member gets a not-found page body and a system
administrator receives the page, which is tested by `SignIn_RepositoriesPage_NonMemberAndSystemAdmin_OnlyAdminIsServed`
in the `Identity` subsystem tests.

**Project access**: Which projects a user sees, and who may administer them, is verified by the unit tests of
`ProjectAccessPageTests`, which render the pages with the component test host over the real services. The projects list
shows each user only the projects the user may see, which is tested by
`ProjectsPage_Render_MemberAndSystemAdmin_ListOnlyWhatEachMaySee`, and explains an empty list without a create button,
which is tested by `ProjectsPage_Render_UserWithoutMembership_ExplainsTheEmptyListAndOffersNoCreateButton`. A member who
is no system administrator is refused when creating a project, which is tested by
`NewProjectPage_CreateForm_MemberWhoIsNoSystemAdmin_IsRefusedAndCreatesNothing`. A non-member sees the project as a
missing one, which is tested by `ProjectPage_Render_NonMember_LooksLikeAMissingProject`, a member sees the reviews but
no Members section, which is tested by `ProjectPage_Render_Member_SeesReviewsButNoMembersSection`, and a system
administrator sees the members with their roles and actions, which is tested by
`ProjectPage_Render_SystemAdmin_ListsMembersWithRolesAndActions`; a project admin sees the same, with the audit trail
button, which is tested by `ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons`. Removing and changing the role of a
member are tested by `ProjectPage_MemberActions_ToggleRoleAndRemove_ChangeTheMembership`, the refusal to remove or
demote the last project admin by `ProjectPage_MemberChange_LastProjectAdminSelf_ShowsExplanationAndChangesNothing`. A
member is added on the pages for a new member, by the exact user name with no list of users, which is tested by
`NewMemberPage_Submit_ExistingUser_AddsWithRoleAndReturnsToTheProject`, or as a new account with a temporary password
that is shown once, which is tested by
`NewMemberAccountPage_Submit_ValidNames_CreatesAccountShowsPasswordOnceAndDoneHidesIt`; both pages show the not-found
message to anybody who does not administer the project, which is tested by
`NewMemberPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm` and
`NewMemberAccountPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm`. The review creation form offers only the
repositories of its project, which is tested by `NewReviewPage_RepositorySelect_OffersOnlyRepositoriesOfThisProject`.

**Access on an open page**: The pages of a project check the view access again at the start of every action, through
`ProjectViewGuard`. The guard is tested directly by `ProjectViewGuard_GuardView_Member_ReturnsTrueWithoutCallback`,
`ProjectViewGuard_GuardView_NonMember_ReturnsFalseAndCallsBackOnce`,
`ProjectViewGuard_GuardView_MembershipRemovedBetweenChecks_SecondCheckReturnsFalse` and
`ProjectViewGuard_GuardView_NobodySignedIn_ReturnsFalseAndCallsBackOnce`. The administer and system administrator
levels of the same guard are tested by `ProjectViewGuard_GuardAdminister_ProjectAdmin_ReturnsTrueWithoutCallback`,
`ProjectViewGuard_GuardAdminister_PlainMember_ReturnsFalseAndCallsBackOnce`,
`ProjectViewGuard_GuardAdminister_AdminDemotedBetweenChecks_SecondCheckReturnsFalse`,
`ProjectViewGuard_GuardAdminister_NobodySignedIn_ReturnsFalseAndCallsBackOnce`,
`ProjectViewGuard_GuardSystemAdmin_SystemAdmin_ReturnsTrueWithoutCallback`,
`ProjectViewGuard_GuardSystemAdmin_DemotedOrDisabled_ReturnsFalseAndCallsBackOnce` and
`ProjectViewGuard_GuardSystemAdmin_NobodySignedIn_ReturnsFalseAndCallsBackOnce`. On the pages, a member opens the page,
the
membership is removed in the store, and an action is triggered; the page then shows the page for a missing project and
none of the data it had loaded, which is tested by `ReviewPage_SelectFile_MembershipRevoked_ShowsNotFoundAndNoData`,
`ChecklistPage_Choose_MembershipRevoked_ShowsNotFoundAndNoData`,
`HistoryPage_Reparameterize_MembershipRevoked_ShowsNotFoundAndNoEntries`,
`PlanPage_ShowPlan_MembershipRevoked_ShowsNotFoundAndNoPlan` and
`NewReviewPage_CreateForm_MembershipRevoked_ShowsNotFoundAndCreatesNothing`; the other actions are covered in the
verification of each page. The pages that show data for administrators only clear it in the same way: the repositories
pages, the members section and the pages for a new member, and the users page. A revocation that lands inside a single
action,
after its check, and data that is already in
the browser until the next action are not covered; the rule is checked per action, not pushed to the browser.

**Audit pages**: The history and audit pages are verified by their unit tests, which render them with the component
test host. The history page shows only the entries of its review, newest first, which is tested by
`HistoryPage_Render_ReviewWithEntries_ShowsOnlyItsEntriesNewestFirst`; a visitor who is not signed in does not get
the entries, which is tested by `Routes_Render_NobodySignedInOpensHistoryRoute_DoesNotShowEntries`. The audit page
shows the entry count and the full head hash of an intact chain, which is tested by
`AuditPage_Verify_IntactChain_ShowsCountAndFullHeadHash`, and a user who is no system administrator and opens the
audit route gets an access
denied message and no entries, which is tested by `Routes_Render_ReviewerOpensAuditRoute_ShowsAccessDeniedNotTrail`.
The audit page of a project shows a project admin the events of that project only, which is tested by
`ProjectAuditPage_Render_ProjectAdmin_ShowsOnlyThisProjectsEvents`, and anybody who does not administer the project
the not-found message and no entries, which is tested by
`ProjectAuditPage_Render_NoAdministerRight_ShowsNotFoundAndNoEntries`.
The subsystem test of all pages does not include the two new routes; their `data-testid` attributes are checked in
the unit tests.

**Attestation download and help**: The download button, its note, the problem shown instead of it, and the absence of
all three for a review that is not approved are verified by the `ReviewPage` unit tests
(`ReviewPage_Render_ReviewNotApproved_OffersNoAttestation`,
`ReviewPage_Render_ReviewApprovedWithSelfSignedKey_OffersAttestationDownloadWithKeyNote`,
`ReviewPage_Render_ReviewApprovedWithOrganizationKey_NoteNamesTheCertificate`,
`ReviewPage_Render_ReviewApprovedWithKeyProblem_ShowsProblemInsteadOfDownload`, and
`ReviewPage_Sign_CorrectPassword_OffersAttestationDownload`), the address by
`ReviewLinks_ForAttestation_ProjectAndReview_BuildsAddress`, and the help topic by
`HelpAttestationPage_Render_ExplainsWhatTheSignatureProvesAndTheKey`. The download itself, over HTTP, is verified in the
`Evidence` subsystem.

**Personal access tokens on the account page**: The token card, the creation that shows the value once, and the
revocation are verified by the `AccountPage` unit tests, which render the page with the component test host and the real
`PersonalAccessTokenService` over a fake store: `AccountPage_CreateToken_ValidInput_ShowsValueOnceAndListsToken` and
`AccountPage_RevokeToken_ActiveToken_MarksRevokedAndRemovesButton` trace the subsystem requirement, and the help topic is
verified by `HelpApiTokensPage_Render_ExplainsTokensCurlEndpointsAndErrors`. The end-to-end journey from the card
to a call of the API is verified in the `Api` subsystem.

**Evidence settings page and help**: The page, its defaults, warnings, save, audit entry, conflict, and lost right are
verified by the `EvidenceSettingsPageTests` unit tests, which render the page with the component test host and the real
`EvidenceSettingsService` over a fake store: `EvidenceSettingsPage_Render_Administrator_ShowsDefaultsWithoutWarnings`,
`EvidenceSettingsPage_Render_NoAdministerRight_ShowsNotFoundAndNoSwitches`, and
`EvidenceSettingsPage_Save_Change_StoresConfirmsAndAudits` trace the subsystem requirement, and the help topic is
verified by `HelpEvidenceSettingsPage_Render_ExplainsAnonymousAccessContentSwitchesAndScope`. The button on the project
page is verified by `ProjectPage_Render_ProjectAdmin_ShowsMembersAndButtons` and
`ProjectPage_Render_PlainMember_ShowsNoMembersAndNoAuditLink`. The effect of the settings on the API and the PDFs is
verified in the `Api` and `Evidence` subsystems.

**Temporary password**: A user who signed in with a temporary password is confined to the account page. The layout
shows no navigation tabs and sends the browser to `/account` from every page except the account, help, and about pages,
which is tested by `MainLayout_Render_TemporaryPassword_ConfinesUserToAccountPage`; the account page shows a banner and
the password form only, which is tested by `AccountPage_Render_TemporaryPassword_ShowsBannerAndOnlyPasswordForm`;
replacing the password sends the browser to the login page with the notice, which is tested by
`AccountPage_ChangePassword_TemporaryPassword_RedirectsToLoginWithNotice` and
`LoginPage_Render_ChangedQuery_ShowsNoticeOnlyAfterPasswordChange`. The refusals on the server side (signatures, access
tokens, project access) are verified in the `Core` and `Api` subsystems.

**Sign-in with an OpenID Connect provider**: With the provider on and nobody signed in, the login page shows the button
beside the local form, which is tested by `LoginPage_Render_ProviderEnabled_ShowsButtonBesideLocalForm`. A reviewer
who signs in at the provider sees, on the review page, the button to confirm the identity and no password field, and
after the confirmation the Sign button, which is tested by `OidcReauth_ReviewPage_OffersConfirmationThenSigning`. The
other states of the pages (the failure message, the missing button, the account and user pages, and the help topics)
are tested in the unit tests of `LoginPage`, `UsersPage`, `AccountPage`, and `HelpPage`.

**Second factor**: A pending sign-in shows the code form and no user name, which is tested by
`TotpLoginPage_Render_PendingSignIn_ShowsCodeFormAndNoUserName`, and a right code on the account page shows ten recovery
codes once and no secret, which is tested by `AccountPage_Confirm_RightCode_ShowsTenRecoveryCodesOnceAndNoSecret`. The
other states of the pages are tested in the unit tests of `TotpLoginPage`, `LoginPage`, `AccountPage`, `UsersPage`,
`ReviewPage`, and `HelpPage`.
