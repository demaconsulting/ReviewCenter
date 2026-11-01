# ReviewCenter

## Verification Approach

System-level verification uses end-to-end integration tests that invoke the tool as a real
process via the `Runner.Run` helper in `IntegrationTests.cs`. Each test exercises the full
stack — argument parsing, dispatch, execution, and output — and validates both exit code and
combined console output. The tests treat the tool as a black box and assert only on observable
outputs; no internal implementation details are assumed.

The `serve` scenarios start the tool as a long-running process through the `ServerProcess.Start`
helper, which launches `dotnet <assembly> serve --urls http://127.0.0.1:<port>` on a free
loopback port chosen by the operating system and polls the health endpoint until the server
answers. The tests then use HTTP requests against that server and stop the process when they
finish. The operating system picks the port, so no fixed port is assumed; the test classes that start server
processes or change console state belong to the serial `Sequential` xUnit collection and run one at a time, while
the in-memory test-server classes (which open no port and start no process) run in parallel.

The review integrity, signing, evidence, and access promises are verified by the `ReviewCenterSystemTests` class of the
`DemaConsulting.ReviewCenter.Puppet.Tests` project. Those tests start the real server of the tool on a free loopback
port with a temporary SQLite database and a real git repository, and drive it through a real Chromium browser with the
Puppet tool (sign-in, planning a review from the repository, naming reviewers, answering the checklist, signing) and
through the HTTP API with a personal access token. They are skipped locally when Chromium or git is missing and fail
in the CI pipeline, where `REVIEWCENTER_REQUIRE_CHROMIUM=true`, so the evidence cannot be skipped silently. The
tamper detection of the audit trail is verified by a process test that edits the SQLite database behind the tool and
runs `audit verify`.

`Runner.Run` merges stdout and stderr into a single combined output string. Per-stream assertions
(e.g., "standard error is empty") are not possible at this level; all assertions are made
against the combined output.

Platform verification additionally runs against the distributed artifact. In the CI pipeline the self-contained
single-file executable for each supported platform (win-x64 on `windows-latest`, linux-x64 on `ubuntu-latest`) is
extracted from its release archive and run with `DOTNET_ROOT` and `DOTNET_ROOT_X64` pointing to an existing, empty
folder, so no installed .NET runtime can be found by it (a missing folder would let the executable fall back to the
global installation). A negative control proves the isolation works: a tiny framework-dependent executable built in
the same job must run with the normal environment and must fail under the isolated one. A further check asserts that
the executable is larger than 20 MiB, which a
framework-dependent publish is not. The commands run are: `--version`, `--help`,
`--validate --results validation-<os>-net10.0-<rid>.trx`, and a `serve` smoke test that checks `/health`, the home
page, and the Blazor script. The self-validation results (the nine `ReviewCenter_` tests:
`GitAvailable`, `GitRoundTrip`, `ReviewWorkflow`, `Fingerprint`, `PasswordHash`, `Totp`, `AuditChain`,
`AttestationPdf`, and `HostStart`) in those TRX files are the evidence for the platform
requirements; the platform
(`windows`, `ubuntu`) and runtime (`net10.0`) in the file name are the source filters.

## Test Environment

Integration tests run on net10.0 on Windows and Linux; the published-executable checks run on the same two
platforms. All scenarios are expected to
produce identical results on all supported platforms, with one exception: the graceful-stop scenario
sends a termination signal to a child process, which Windows cannot do, so it is skipped there with a
reason. Temporary files and directories are
created and cleaned up within each test. The `serve` scenarios require permission to open
loopback TCP ports.

## Acceptance Criteria

- All integration tests pass with zero failures across all supported platforms.
- Exit code 0 is returned for all valid invocations.
- Exit code non-zero is returned for all invalid argument combinations.
- Results files are created at the specified paths when `--results` is used with `--validate`.
- Silent mode (`--silent`) produces empty combined output.
- The `serve` verb starts a web server that answers `/health` with 200 and `Healthy` and serves
  the home page at `/`.
- The running server writes a start-up log entry that names its listen address, and on Linux it exits
  with code 0 when asked to terminate.
- The `--urls` option controls the listen address, is rejected without `serve`, and a port that
  is already in use produces an error and a non-zero exit code.
- The running server serves the login page to a visitor who is not signed in and sends such a visitor who requests
  the projects page to the login page; it has created its database; stored data is served again after a restart of
  the server to a signed-in user.
- A user created with the `user add` verb takes the password from the environment variable, never prints it, and a
  second user with the same user name is refused; a missing password gives an error and exit code 1.
- A user signs in with the user name and password, uses the protected pages as that user, and signs out; only
  signed-in users reach the protected pages, and only system administrators reach the users page.
- An open page re-checks its user at a fixed interval; when the user is disabled, removed, or changed, the page
  stops showing and accepting actions and shows a message that the session ended, and a user who cannot be checked is
  treated as signed out.
- Review comments (threads, replies, and resolve state) are presented by the review page and are kept after the
  database is reopened; each is attributed to the signed-in user.
- The review checklist (answers, applicability, and the frozen snapshot) is presented by the checklist and review
  pages and is kept after the database is reopened.
- Reviewer slots and signatures are presented by the review and project pages: a review is Approved only while every
  slot has a signature of the current record, a change of the record makes a signature out of date, and slots and
  signatures are kept after the database is reopened.
- A review is named for reviewers beforehand: only members of the project are offered, one person fills one slot only,
  an API token never signs or arranges slots, a review with an unanswered check offers no way to sign, and a signed slot
  offers no way to change or remove it. While the project requires independent reviewers (the default), the author of
  a review is refused as a reviewer of it and as its signer, and an administrator can turn the requirement off and on
  with the change audited (`ReviewCenter-System-ReviewerIndependence`). The author is the user who created the review;
  git commit authors are not matched.
- A review may carry an optional due date, a UTC calendar day that is not part of the record: it is set when the review
  is created or planned and may be changed or cleared by an administrator or the creator, each change audited; the page
  shows an Overdue badge once the date has passed unless the review is concluded, and a concluded review keeps its date
  without a badge and without an edit form (`ReviewCenter-System-DueDate`).
- The project page shows a lead, for every review of the project, its stage, its signatures, what blocks it, who it
  waits on, its last activity, and its due date, with a summary by stage and overdue, filters, sorting, and paging that
  live in the address; a read-only auditor sees the same, and the API list carries the progress fields
  (`ReviewCenter-System-Oversight`).
- A project administrator or system administrator can make a member an Auditor, who reads everything of the project,
  including its audit trail and export, and changes nothing: the pages show a read-only notice without controls, the
  Auditor is not offered as a reviewer, cannot sign, and an API token of an Auditor is refused every write with 403
  while a non-member gets 404 (`ReviewCenter-System-Auditor`; the system test
  `ReviewCenter_Auditor_CanReadEverythingAndChangeNothing`).
- A review can be created in the browser or through the API as a follow-up of an earlier review of the project: the form
  previews what is carried over, the checklist marks every carried answer and shows the earlier Fail of an open check,
  the review page names the followed review and marks carried threads, the follow-up is approved on its own while the
  earlier review stays as it was, and the attestation of the follow-up names the earlier review
  (`ReviewCenter-System-FollowUpReview`; the system test
  `ReviewCenter_FollowUpReview_CarriesWhatHoldsAndIsApprovedOnItsOwn`).
- A signature needs the password of the signer again for that signature; a wrong password is refused, signs nothing,
  and is audited without the password (`ReviewCenter-System-SignatureReauthentication`). The lockout count, the second
  factor, and the provider proof are verified at the level of the child requirements.
- A signature is bound to the record of the signer: it is refused when the checklist changed since the signer's page
  was drawn, and a later comment ends the approval (`ReviewCenter-System-SignatureBinding`).
- The reviewed content, posted comments, and signatures are not offered for editing, an audit entry edited behind the
  tool is found by `audit verify`, and a change of the record after a signature, made while the review is still open,
  makes the review In review again with the signature kept. A concluded (approved or rejected) review is locked: every
  change to it is
  refused in the core services and in the database transaction, the review page shows it as Locked with its controls
  disabled, and only a project or system administrator can reopen it, with a reason that is audited together with the
  previous digest; the earlier signatures stay as history and fresh signatures are needed
  (`ReviewCenter-System-RecordImmutability`).
- A Fail answer does not block signing: when the ballots agree on a Fail, a reviewer may sign the rejected outcome once
  the review conclusion is written, and a review whose every slot holds a current signature of the rejected outcome is
  concluded as rejected and locked, has no attestation, and is never listed in the evidence index; an administrator can
  reopen it like an approved review.
- Every change of data, and every sign-in, sign-out, failed sign-in, and lockout, is recorded in an audit trail whose
  entries are chained by hashes, and the change and its entry are stored together; the entries never contain a password
  or the text of a comment or justification (verified at the level of the child requirements; the system tests show
  the signature and failed proof entries without the password).
- Every signed-in user sees the history of a review of a project the user may see, and only system administrators see
  the whole audit trail, in pages of 50, newest first (verified at the level of the child requirements).
- The audit chain is verified on request, from the audit page and with the `audit verify` verb; an intact chain gives
  exit code 0 and a broken chain gives exit code 1 and names the first broken entry.
- The limits of the chain are stated to the user: it does not detect the removal of the newest entries or a complete
  rewrite by someone with write access to the database, unless the head hash was recorded elsewhere.
- A database setting that cannot be used stops the server with an error and a non-zero exit code.
- Every interactive element of the home page carries a `data-testid`, and the same holds for every other page.
- A user creates a personal access token, sees its value once, and revokes it; a script that sends the token calls the
  API as that user until the token is revoked or expires, and a refused token is recorded in the audit trail without its
  value.
- The API lists the projects and reviews, creates a review draft flagged as created through the API, and downloads the
  attestation of a signed review; a token never signs, never changes a reviewer slot, and never opens a web page.
- A user who is not a member of a project gets the answer for a project that does not exist from every route of the
  project (for a token and for a session), a member and a system administrator are served, the project list holds
  only the projects the user may see, and a token follows the memberships of its owner at once.
- A project admin or system administrator adds an existing user by the exact user name or creates an account as part of
  adding it, the created account gets a temporary password that is shown once and must be replaced at the first
  sign-in, and the last project admin cannot be removed or demoted by a project admin.
- With sign-in with an OpenID Connect provider turned on, a new person is created as an account without any right and
  found again by issuer and subject only, a local account is never linked by name, a foreign return address is
  replaced by the home page, and a signature needs one fresh sign-in at the provider as the same person.
- A local password account may turn on a second factor with an authenticator app: the right password then leads to a
  second step that needs a code, a code works once even when many requests present it at the same time, wrong codes
  count toward the same lockout as wrong passwords, a signature needs the password and a fresh code, recovery codes
  work once for signing in but not for signing, and an administrator can reset it.

## Test Scenarios

**ReviewCenter_VersionFlag_Provided_OutputsVersion**: The `--version` flag is passed as
the sole argument; the tool outputs the version string and exits with code 0. This scenario is
tested by `ReviewCenter_VersionFlag_Provided_OutputsVersion`.

**ReviewCenter_HelpFlag_Provided_OutputsUsageInformation**: The `--help` flag is passed
as the sole argument; the combined output contains "Usage" and "Options" and the tool exits
with code 0. This scenario is tested by
`ReviewCenter_HelpFlag_Provided_OutputsUsageInformation`.

**ReviewCenter_ValidateFlag_Provided_RunsValidation**: The `--validate` flag is passed together
with `--depth 3` and `--result <path>.xml`, as one shared real process run of the whole default
self-validation; the combined output contains "Total Tests: 9", "Passed: 9" and "Failed: 0" and
the tool exits with code 0, confirming the self-validation suite runs and completes successfully.
This scenario is tested by `ReviewCenter_ValidateFlag_Provided_RunsValidation`.

**ReviewCenter_ValidateWithXmlResults_Requested_GeneratesJUnitFile**: The shared real process run
wrote a JUnit XML file containing a `<testsuites` XML element and nine test cases and the tool
exited with code 0. The TRX format is verified at the subsystem level by
`SelfTestSubsystem_ValidationWorkflow_WithTrxFile_GeneratesResults`. This scenario is tested by
`ReviewCenter_ValidateWithXmlResults_Requested_GeneratesJUnitFile`.

**ReviewCenter_SilentFlag_Provided_SuppressesOutput**: The `--version` and `--silent`
flags are passed together; the combined output is empty or whitespace-only while the tool exits
with code 0, confirming silent mode suppresses all console output. This scenario is tested by
`ReviewCenter_SilentFlag_Provided_SuppressesOutput`.

**ReviewCenter_LogFlag_Provided_WritesOutputToFile**: The `--log <path>` flag is passed
pointing to a temporary file; the tool exits with code 0 and the log file is created containing
output that also appears in the combined console output. This scenario is tested by
`ReviewCenter_LogFlag_Provided_WritesOutputToFile`.

**ReviewCenter_UnknownArgument_Provided_ReturnsError**: An unrecognized argument
(`--unknown`) is passed; the tool exits with a non-zero code and the combined output contains
an error message identifying the unknown argument. This scenario is tested by
`ReviewCenter_UnknownArgument_Provided_ReturnsError`.

**ReviewCenter_ValidateWithDepth_DepthThree_OutputsCorrectHeadingLevel**: The shared real process
run used `--depth 3`; the combined output contains `### DEMA Consulting` (heading at depth 3)
and the tool exited with code 0. This scenario is tested by
`ReviewCenter_ValidateWithDepth_DepthThree_OutputsCorrectHeadingLevel`.

**ReviewCenter_NoArguments_Invoked_DisplaysBanner**: The tool is invoked with no
arguments; the combined output contains the tool name and copyright notice and the exit code is
0. This scenario is tested by `ReviewCenter_NoArguments_Invoked_DisplaysBanner`.

**ReviewCenter_ResultAlias_LegacyFlag_WritesResultsFile**: The shared real process run used the
legacy `--result <path>.xml` alias; a results file was created at the specified path and the tool
exited with code 0. This scenario is tested by
`ReviewCenter_ResultAlias_LegacyFlag_WritesResultsFile`.

The unsupported results extension (part of requirement `ReviewCenter-System-Qualification`) is verified
at the subsystem level by
`SelfTestSubsystem_ValidationWorkflow_WithUnsupportedExtension_EmitsErrorAndNoFile`, which runs
`Validation.Run` with stand-in checks, instead of a process run that would repeat the real
validation only to reach the same error path.

**ReviewCenter_ServeVerb_Started_HealthEndpointReturnsHealthy**: The tool is started with the
`serve` verb on a free loopback port; once the server answers, a request to `/health` returns
HTTP status 200 with the body `Healthy`. This scenario is tested by
`ReviewCenter_ServeVerb_Started_HealthEndpointReturnsHealthy`.

**ReviewCenter_ServeVerb_Started_LogsStartupEntryWithListenUrl**: The tool is started with the `serve` verb on a
free loopback port and the test waits until the server answers. The server's console output then contains a
start-up entry ("ReviewCenter server started") that names the address the server listens on. This scenario is
tested by `ReviewCenter_ServeVerb_Started_LogsStartupEntryWithListenUrl`.

**ReviewCenter_ServeVerb_TerminationRequested_StopsGracefullyWithExitCodeZero**: The tool is started with the
`serve` verb and the test waits until the server answers. The test then sends the termination signal that a process
supervisor or container runtime sends, and the server exits by itself within the time limit with exit code 0. A
graceful stop signal cannot be sent to a child process on Windows, so the test is skipped there with a reason and is
linked to the requirement for Linux only. This scenario is tested by
`ubuntu@ReviewCenter_ServeVerb_TerminationRequested_StopsGracefullyWithExitCodeZero`.

**ReviewCenter_ServeWithUrls_PortSpecified_ListensOnRequestedPort**: The tool is started with
`serve --urls` naming a specific loopback port; the server answers on exactly that address. This
scenario is tested by `ReviewCenter_ServeWithUrls_PortSpecified_ListensOnRequestedPort`.

**ReviewCenter_UrlsWithoutServe_Provided_ReturnsError**: The `--urls` option is passed without
the `serve` verb; the tool exits with a non-zero code and the combined output names the `serve`
verb as required. This scenario is tested by `ReviewCenter_UrlsWithoutServe_Provided_ReturnsError`.

**ReviewCenter_ServeVerb_PortInUse_ReturnsError**: A loopback port is occupied by a listener and
the tool is started with `serve --urls` naming that port; the tool exits with a non-zero code and
its output contains "Failed to start the server". This scenario is tested by
`ReviewCenter_ServeVerb_PortInUse_ReturnsError`.

**ReviewCenter_ServeVerb_Started_HomePageContainsTitleAndTagline**: The tool is started with
the `serve` verb; a request to `/` returns an HTML document titled "ReviewCenter" that contains
the tagline, and the Blazor client script is served at
`/_framework/blazor.web.js`. This scenario is tested by
`ReviewCenter_ServeVerb_Started_HomePageContainsTitleAndTagline`.

**ReviewCenter_ServeVerb_Started_ServesHelpAndAboutPagesWithoutSignIn**: The tool is started with the `serve` verb as
a real process; requests without signing in to `/help`, `/help/getting-started`, and `/about` each return the page
(the Help index, a help topic page, and the About page, each recognized by its test id). This scenario is tested by
`ReviewCenter_ServeVerb_Started_ServesHelpAndAboutPagesWithoutSignIn`.

**ReviewCenter_ServeVerb_Started_RedirectsAnonymousProjectsRequestToLoginAndCreatesDatabase**: The tool is started
with the `serve` verb as a real process; the test process points its `Database` settings at a temporary file that
does not yet exist. A request to `/projects` without signing in is redirected to the login page with the return URL,
the login page contains the sign-in form, and the database file now exists, showing that the server created the
database at start-up and that the protected pages need a sign-in. This scenario is tested by
`ReviewCenter_ServeVerb_Started_RedirectsAnonymousProjectsRequestToLoginAndCreatesDatabase`.

**ReviewCenter_UserAddVerb_PasswordFromEnvironment_CreatesUserThenRejectsDuplicate**: The tool is run with `user
add`, a user name, a display name, and the `--system-admin` switch, on a new temporary database, with the password in
the
environment variable `REVIEWCENTER_PASSWORD` and not on the command line. The first run exits with code 0 and
reports "Created user 'root' (system administrator)."; the second run with the same user name exits with code 1 and
reports that the user already exists; the password appears in neither output. This scenario is tested by
`ReviewCenter_UserAddVerb_PasswordFromEnvironment_CreatesUserThenRejectsDuplicate`.

**ReviewCenter_UserAddVerb_NoPassword_ReturnsError**: The tool is run with `user add` and no password in the
environment while standard input is closed; it exits with code 1 and reports "No password was provided." instead of
waiting for input. This scenario is tested by `ReviewCenter_UserAddVerb_NoPassword_ReturnsError`.

**ReviewCenter_ReviewerSlots_NonMemberAndSamePerson_AreNotAcceptedAsReviewers**: A real server runs with a temporary
database and a git repository; a system administrator (`puppet`) creates a project, adds the user names `puppet`,
`second`, and `third` as members (a fourth user, `outsider`, is not added), and plans a review from the repository. In
the browser the list of reviewers offers the two members other than the author and not the outsider; a slot for `second`
is added; adding `second` again as primary, or as the alternate of another slot, is refused with the message that the
person is already in another slot; `third` is then added, giving exactly two slots. This scenario is tested by
`ReviewCenter_ReviewerSlots_NonMemberAndSamePerson_AreNotAcceptedAsReviewers`.

**ReviewCenter_Signing_FreshPasswordAndUnchangedRecord_ApprovesOnlyThen**: In the same setting, a review with a slot
for `second` and an unanswered checklist shows `second` its blockers and offers no form to sign. `second` answers every
checklist section as not applicable with a reason in the ballot of the own slot (with one slot the finished ballot is
handed in at once), and the form is offered. A wrong password is refused with the generic
message,
the review stays In review, and the slot stays unsigned. A second session of `second` makes the section of the
requirements applicable again while the page of the first session still shows the old record; the ballot becomes a
draft and the record changes, so signing on the old page with the right password
is refused because the record changed. After the section is marked not applicable again and the page is drawn again,
the right password signs and the review is
Approved. The audit page lists the failed proof and the signature but neither password, and its verification reports an
intact chain. This scenario is tested by `ReviewCenter_Signing_FreshPasswordAndUnchangedRecord_ApprovesOnlyThen`.

**ReviewCenter_Signing_ChangeAfterSignature_AsksToConfirmAndOnlyThenMakesItOutOfDate**: In the same setting, a review
has slots for `second` and `third`; `second` answers every checklist section as not applicable and hands the ballot in,
then signs, so the review is not approved. `third` answers differently (the requirements apply) and clicks Submit
ballot; the hand-in changes the record that the signature of `second` covers, so the page shows the
warning, which names "Slot 1" and "Second Reviewer" and stores nothing. After Cancel the slot of `second` still shows
"Signed by". `third` hands the ballot in again and clicks Confirm; the warning goes and the slot of `second` shows
"Signature
out of date". This scenario is tested by
`ReviewCenter_Signing_ChangeAfterSignature_AsksToConfirmAndOnlyThenMakesItOutOfDate`.

**ReviewCenter_ApprovedReview_EvidenceTokenAndLaterChange_BehaveAsPromised**: A comment thread is started on the
review and resolved, every checklist section is answered (by `second` in the ballot of the own slot), and a personal
access token of the signer (`second`) is created in the browser. Through the API with the token, the review is not
approved, the attestation is refused, and the evidence index is empty. After the signature in the browser the review is
approved with the signer named, the attestation is a digitally signed PDF, and the index lists the review-set with the
content fingerprint and the result. A client without credentials is refused both documents; the token does not open a
web page and a request of the token to sign or to arrange slots finds no route; the signed slot offers no control to
change or remove it. The approved review is locked, so the only later change is its reopening by the administrator with
a reason: afterwards the review is In review, its signature is out of date and kept, its approval digest differs and its
content fingerprint is unchanged, the attestation is refused and the index is empty again, a posted comment has no
control to edit it, and a link to the thread leads a signed-out user back to the thread after the sign-in. This scenario
is tested by `ReviewCenter_ApprovedReview_EvidenceTokenAndLaterChange_BehaveAsPromised`.

**ReviewCenter_ApprovedReview_IsLockedUntilAnAdministratorReopensItWithAReason**: A review that the administrator
created is approved by the signature of `second`. The administrator's review page shows Approved and Locked, with the
reviewer slot selectors and the Add slot button and the checklist controls disabled, and no comment form (no new-thread
form, text box, or Start thread button); the checklist page says that it is read-only. The review page of `second`, a
member who is not an
administrator, shows the lock but offers no reopen form. The administrator's attempt to reopen without a reason is
refused with a message about the reason and leaves the review locked; with a reason the review shows In review and
no longer Locked, lists the reopening with the administrator's name and the reason, shows the earlier signature of the
slot as out of date, and enables the slot controls again. `second` is offered the form to sign again, and the fresh
signature approves and locks the review again. The history page of the review lists both signatures and the
`review.reopened` entry with its reason. This scenario is tested by
`ReviewCenter_ApprovedReview_IsLockedUntilAnAdministratorReopensItWithAReason`.

**ReviewCenter_IndependentReviewers_AuthorIsNotOfferedAndAdministratorCanSwitchTheRuleOff**: In the same setting, the
review page of its author (the administrator) does not offer the author in the list of reviewers and tells the author
that a different member has to review, while the project requires independent reviewers (the default). The
administrator switches the rule off on the project page and the page confirms it; the author is then offered, the hint
is gone, and the author is seated in a slot. After every checklist section is answered (by the author in the ballot of
the own slot) and the rule is switched on again,
the author's attempt to sign with the right password is refused with the message that the author created the review
and cannot be a reviewer of it, and the review stays In review. The audit page lists the
`project.independent-reviewers-changed` entries. This scenario is tested by
`ReviewCenter_IndependentReviewers_AuthorIsNotOfferedAndAdministratorCanSwitchTheRuleOff`.

**CoreSubsystem_ReviewRuleSwitches_ChangeIsAuditedAdminOnlyAndStale** and
**WebSubsystem_ProjectAdmin_SavesStoredReviewRuleSwitches**: These subsystem scenarios verify the system requirement of
the two further review rule switches (authors answering the checklist of their own review, and the person who reopened
a review signing it again). In the Core scenario a plain member and an administrator's token are refused, the
administrator switches both rules off with one audit entry `project.review-rule-changed` each, and a stale change is
refused. In the Web scenario the real pipeline shows both switches, on by default, only to the administrator, and shows
a rule as off after it was changed. These scenarios are tested by
`CoreSubsystem_ReviewRuleSwitches_ChangeIsAuditedAdminOnlyAndStale` and
`WebSubsystem_ProjectAdmin_SavesStoredReviewRuleSwitches`.

**CoreSubsystem_ReviewRules_EnforcedAndSwitchableOff** and
**WebSubsystem_ChecklistPage_AuthorSeesNoticeAndNonAuthorAnswers**: These subsystem scenarios verify the enforcement
of the two further rules. In the Core scenario the author's answer to a check is refused with `AuthorCannotAnswer`
and stores nothing (with the rule switched off the same call is refused only because the author holds no reviewer slot,
`NoReviewerSlot`), a reopening by the only person who may sign the slot is refused (`ReopenWouldStrandSlot`) and
leaves the review approved, and with that rule switched off the same call succeeds, so that the administrator reopens
the review and signs it again. In the Web scenario the author sees the notice and disabled controls on the
checklist page and another member, who holds a slot, answers a check. The refusal of the reopener's signature is
verified by the SigningService scenarios. These scenarios are tested by
`CoreSubsystem_ReviewRules_EnforcedAndSwitchableOff` and
`WebSubsystem_ChecklistPage_AuthorSeesNoticeAndNonAuthorAnswers`.

**ReviewCenter_DueDate_SetOnCreationAndEdit_ShowsOverdueAndIsAudited**: In the same setting, a review without a due
date says so. A review that is planned with a due date a week ahead shows that date, labeled as UTC, without an Overdue
badge. The administrator changes its date to yesterday, which is accepted on edit, and the page shows the Overdue
badge; clearing the date removes the badge. The first review is given yesterday as its date and is then approved by
the signature of `second`; it keeps its date as history, shows no Overdue badge, and offers no edit form. The audit
page lists the `review.due-date-changed` entries. This scenario is tested by
`ReviewCenter_DueDate_SetOnCreationAndEdit_ShowsOverdueAndIsAudited`.

**ReviewCenter_ProjectOverview_ShowsWhoIsBlockingAndFiltersByState**: This scenario verifies the system requirement for
the oversight of the reviews of a project. The first review is given yesterday as its due date and gets the reviewer
`second` in a slot; a second review is planned. The project page lists both with the stage (In review and Setup),
the signatures `0/1`, the name of the person the first review waits on, the Overdue badge on the first review only,
and a
summary of one overdue and one in setup. The query string `stage=setup` shows only the planned review, `overdue=1` only
the first, and `stage=approved` shows none and says so; the filter form narrows the table by the text of an identifier.
After the reviewer approves the first review, it is listed under the approved stage, waits for nobody, shows no
Overdue badge, and the summary counts one approved and no overdue review. This scenario is tested by
`ReviewCenter_ProjectOverview_ShowsWhoIsBlockingAndFiltersByState`.

**ReviewCenter_FollowUpReview_CarriesWhatHoldsAndIsApprovedOnItsOwn**: In a browser, the reviewer `second` of a review
fails a check, passes the rest of its section and marks the other sections as not applicable, which hands the ballot of
the single slot in; another user starts a thread. A second review is
created through the form of a new review from the same repository, with the first review chosen in the follow-up
choice and the suggested title kept. The new review names the first one with the carried counts and marks the carried
thread; its checklist marks the carried Pass and section and shows the earlier Fail with its justification for the check
that is open again. The reviewer of the follow-up (`second` again, in the slot that is added to it) decides the check
again and the carried thread is resolved, the reviewer signs, and the review is
Approved while the first review is still In review and lists the follow-up. The attestation PDF of the follow-up
contains "Follows review" and the identifier of the first review. This scenario is tested by
`ReviewCenter_FollowUpReview_CarriesWhatHoldsAndIsApprovedOnItsOwn`.

**ReviewCenter_AuditTrail_FiltersByActionAndActor**: This scenario verifies the filter of the audit page of the system
requirement for the audit trail on the audit page. The user `puppet` has signed in and created a review, so the trail
holds both. The whole trail shows no Clear link. Choosing the action `review.created` in the filter form and applying
it leaves only entries of that action, fewer than the whole trail, puts `action=review.created` in the address and
shows the Clear link. The address with the actor `puppet` and the group `auth.` shows only sign-in entries of that user,
and an actor nobody has shows the no-match state without a row. The Clear link returns to the whole trail. This
scenario is tested by `ReviewCenter_AuditTrail_FiltersByActionAndActor`.

**ReviewCenter_AuditTrail_Export_ReportsChainStatus**: This scenario verifies the export of the system requirement for
the audit trail. The user `puppet` has signed in and created a review, and the audit page is filtered by the action
`review.created`. The CSV export link of the page carries `format=csv` and the action. Fetched from the page, so that
the cookies of the session are used, it answers 200 with the type `text/csv`, an attachment file name that ends in
`chain-intact.csv`, the header `X-ReviewCenter-Audit-Chain` with the value `intact`, and a body that holds the header
row and the entry of the created review. This scenario is tested by `ReviewCenter_AuditTrail_Export_ReportsChainStatus`.

**ReviewCenter_AuditVerifyVerb_HistoryEditedInDatabase_ReportsFirstBrokenEntryWithExitCodeOne**: Three users are created
with the `user add` verb on a temporary database, and `audit verify` reports an intact chain with exit code 0. One audit
entry is then edited directly in the SQLite file, and `audit verify` reports that the chain is broken at that entry with
exit code 1. This scenario is tested by
`ReviewCenter_AuditVerifyVerb_HistoryEditedInDatabase_ReportsFirstBrokenEntryWithExitCodeOne`.

The system requirements for reviews, restart persistence, and database failure are verified at subsystem level,
where the same production pipeline can be exercised with real databases more precisely than through a child
process: creating a review needs a live browser connection for the file upload, which the in-memory component
tests provide. The following scenarios belong to the system verification through the subsystem tests.

**Follow-up review**: A review is created that follows an earlier review of the same files through the domain services
over the fake store, so the answers that passed and the open threads are carried with their original authors and times,
the digest binds the follow-up, and the creator stays the author for the independence rule; through the API over the
real pipeline, the review is created with `followsReviewId`, shows it in the details and the list, and a source that
cannot be followed is a 400 problem that creates nothing; with the real database the carry is stored in the one
transaction of the review; and the attestation names the followed review. These scenarios are tested by
`ReviewService_CreateDraftAsync_FollowUp_CarriesAnswersAndSections`,
`ApiEndpoints_CreateReview_FollowsReviewId_IsCreatedShownAndRefusedWhenInvalid`,
`ReviewStore_AddReviewAsync_FollowUp_RoundTripsEveryNewColumn`, and
`AttestationService_GenerateAsync_FollowUpReview_NamesFollowedReviewAndCarriedAnswers`. The page for choosing the
earlier review and the carried badges are verified by the component tests of the Web units and by the system test
`ReviewCenter_FollowUpReview_CarriesWhatHoldsAndIsApprovedOnItsOwn`.

**Review creation and display**: A project and a review are created through the domain services over a real
database, and the review and project pages are requested over HTTP; the pages show the fingerprint, the files,
and the project. This scenario is tested by `WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages`.

**Review comments**: A review with an open and a resolved thread is stored through the domain services over a
real database, and the review page is requested over HTTP; the page shows the open and resolved thread counts and
the open-thread count of the file. This scenario is tested by
`WebSubsystem_ReviewWithThreads_ArePresentedByThePage`. In a second scenario a thread is started on a line of a
file, replied to, and resolved; the database is then closed and opened again, and the thread keeps its anchor, its
resolved state, and both comments in posting order. This scenario is tested by
`StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsThreadsAndComments`.

**Sign-in journey**: A user is created through the user service over the real pipeline; a request for the projects
page is refused and redirected to the login page, the user signs in with the return URL and reaches the page, the
page shows the display name of the user, and after signing out the page is refused again. This scenario is tested
by `IdentitySubsystem_SignInJourney_LoginUseAndLogout_WorksEndToEnd`. In a second scenario every protected page
redirects an anonymous request to the login page, and the users page is refused to an ordinary user and served to a
system administrator. These scenarios are tested by `SignIn_ProtectedPage_Anonymous_RedirectsToLogin` and
`SignIn_UsersPage_ReviewerAndAdmin_OnlyAdminIsAllowed`.

**Project access**: Every route of the API that belongs to a project is called with the token of a user who is no
member of the project (as a bearer token and as the password of Basic credentials); each answers 404 with the content
type and the body of the answer for a project that does not exist. A member and a system administrator who is no member
are served by every route, the project list holds only the projects of a member (all of them for a system
administrator), the attestation page answers a signed-in non-member with 404, and the token of a member gets 404 at
once after the member is removed from the project. These scenarios are tested by
`ApiProjectAccess_EveryProjectRoute_NonMemberToken_AnswersLikeAMissingProject`,
`ApiProjectAccess_EveryProjectRoute_MemberAndSystemAdmin_AreNotRefused`,
`ApiProjectAccess_ListProjects_ShowsOnlyMemberProjectsExceptToSystemAdmins`,
`ApiProjectAccess_AttestationPage_NonMemberSession_Answers404`, and
`ApiProjectAccess_Token_OwnerRemovedFromProject_Gets404AtOnce`. The memberships themselves are stored and audited as
verified by `ReviewStore_AddMembershipAsync_Membership_RoundTripsAndIsListed` and
`ReviewStore_Membership_Changes_AreAuditedWithTheProject`; the management of members (the exact user name, the
temporary password, and the rule of the last project admin) is verified in the `Core`, `Storage`, and `Web` subsystems;
the pages that hide a project from a non-member are
verified in the `Web` subsystem.

**Open session ends**: A user is signed in on the real pipeline and the authentication state provider of a new scope
(a circuit) is checked; the user is valid. The user is then disabled through the user service and the check is
repeated; the user is no longer valid, which is what makes an open page show the "Session ended" message. This
scenario is tested by `IdentitySubsystem_Revalidation_UserDisabledAfterSignIn_OpenSessionIsNoLongerValid`.

**Users and passwords**: Users are created, signed in with in any case of the user name, locked after repeated
failures, and protected against the loss of the last system administrator through the domain service, and users and
password changes are kept after the database is reopened. These scenarios are tested by
`CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin` and `StorageSubsystem_Users_PersistAcrossReopen`.

**Sign-in with an OpenID Connect provider**: The real pipeline is started with a fake identity provider (the test-only
OpenIddict server) and the settings turned on. A new person signs in at the provider and gets a plain enabled account
without membership or administrator right, which is found again at the second sign-in. This scenario is tested by
`OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn`. A person who proposes the name of a local administrator gets a
new account and never the local one, which is tested by `OidcSignIn_NameOfLocalAccount_NeverLinksToIt`; a return
address that is not on this server is replaced by the home page, which is tested by
`OidcSignIn_ForeignReturnUrl_IsReplacedByHome`. A signature of a user of the provider needs a fresh sign-in of the
same person at the provider, and one such sign-in allows exactly one signature. This scenario is tested by
`OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature`. The settings, the services, the store, and the pages
are verified in the `Identity`, `Core`, `Storage`, `Hosting`, and `Web` subsystems.

**Second factor with an authenticator app**: A user with a turned-on second factor enters the right password, gets a
challenge and no session, and signs in and is sent on with the right code; this scenario is tested by
`TotpSignIn_CodeStep_RightCode_SignsInAndRedirects`. A signature of such a user needs the password and a fresh code,
which is tested by `SigningService_SignAsync_EnrolledUserWithPasswordAndFreshCode_Signs`. Thirty-two parallel requests
with the same code give exactly one success, which is tested by
`TotpService_CompleteSignInAsync_ConcurrentSameCode_ExactlyOneSucceeds`. The algorithm, the secret protection, the
rules, the store, the settings, and the pages are verified in the `Identity`, `Core`, `Storage`, `Hosting`, and `Web`
subsystems.

**Inbox**: A reviewer who is named in a slot finds the review in the inbox as a signature to give; the home page counts
it, the item is gone once the reviewer has signed, and reading and signing leave no inbox or read-state event in the
audit trail. This scenario is tested by the system test
`ReviewCenter_Inbox_AssignedReviewer_SeesToSignItemAndHomeCount_UntilSigned`. The service, the store, the route, and the
pages are verified in the `Core`, `Storage`, `Hosting`, and `Web` subsystems.

**Review checklist**: Two checks of a new review are answered through the domain service over the real pipeline (in the
ballot of the reviewer who is seated in its only slot), one
Pass and one Fail with a justification; the checklist page, requested twice, shows the progress, the pressed Pass
button, and the justification, and the review page shows the progress summary, the link to the checklist page, and
the unanswered checks as the next step. This scenario is tested by
`WebSubsystem_ChecklistPage_AnsweredChecksSurviveReload`. In a second scenario the snapshot, the answers, and the
applicability of a section are stored over a real database; the database is closed and opened again, and all of it
is read back. This scenario is tested by
`StorageSubsystem_Checklist_PersistsSnapshotAnswersAndSectionsAcrossReopen`.

**Review signatures**: A reviewer slot and a signature are created through the domain services over the real
pipeline; the review page presents the review as Approved after the signing, and as In review with a slot whose
signature is out of date once the record changes, and the project page shows the same status. This scenario is tested
by `WebSubsystem_SignedReview_ApprovalIsPresentedAndGoesOutOfDateWhenRecordChanges`. In a second scenario a slot and
its signature are stored over a real database; the database is closed and opened again, the signature is still
current, and the review is approved. This scenario is tested by
`StorageSubsystem_Signatures_PersistAcrossReopenAndStayCurrent`.

**Approval lock and reopen**: On the running host with a real SQLite database, a review is created by one
administrator and signed by a separate reviewer; the review is then approved and locked, a change to it is refused
with `ReviewLocked`, and its attestation, downloaded with an API token, has no reopen history. An administrator
reopens it with a reason; the review returns to In review under a new digest, and the reviewer signs the new digest.
The review is approved and locked once more, the reopen is recorded with the reason and the previous digest, and the
attestation now holds the reopen history. This scenario is tested by
`IntegrationTests_ApprovalLockReopenResign_EndToEnd`.

**Audit trail chain**: Changes are made at the same moment through the three stores (review, user, and audit), which
share the process-wide gate; the resulting trail has consecutive sequence numbers and an intact chain, so the changes
were serialized and none was lost. This scenario is tested by
`AuditStore_ConcurrentChangesFromDifferentStores_ProduceLinearChain`. In a second scenario a user signs in with the
wrong password, signs in, and signs out over the real pipeline; the trail contains the three events in order and its
chain is intact. This scenario is tested by `SignIn_Flow_LoginFailureAndLogout_FormAnIntactChain`.

**Audit history and audit page**: The history page of a review with a long history shows 50 entries, newest first,
with Older and Newer links; the audit page refuses a user who is not a system administrator and shows no entries. These
scenarios are tested by `HistoryPage_Render_LongHistory_PagesByFiftyWithOlderAndNewerLinks` and
`AuditPage_Render_NonAdmin_IsRefusedAndShowsNoEntries`.

**Audit verification**: The `audit verify` verb reports an intact trail with its entry count and exit code 0, and the
Verify button of the audit page reports the first broken entry of a tampered chain. These scenarios are tested by
`AuditCommand_Run_IntactTrail_ReportsEntryCountWithExitCodeZero` and
`AuditPage_Verify_TamperedChain_ShowsFirstBadEntry`. No test starts the tool as a process with the `audit verify`
verb; the dispatch of `Program` to the command is verified by inspection and by the tests of the command.

**Key rotation**: The `keys reprotect` verb is verified by the unit tests of `KeysCommand` over a real database and a
real key folder: every stored repository token and second-factor secret is still readable by freshly started production
services afterwards, which is tested by `KeysCommand_Run_SecretsOfTheRing_AreStillReadableAfterwards`, and an
unreadable secret makes the command refuse with exit code 1 and no change at all, which is tested by
`KeysCommand_Run_UnreadableSecret_RefusesAndChangesNothing`. No test starts the tool as a process with the verb; the
dispatch of `Program` to the command is verified by inspection and by the tests of the command.

**Registered git repositories**: A private https repository and a local repository are registered through the
repository service over a real database. The stored token text differs from the token, the access data holds the
token, and the real git source resolves a branch and reads a changed file. This scenario is tested by
`ContentSourcesSubsystem_RegisteredRepository_IsReadThroughTheRealGitSource`. In a second scenario a repository is
disabled; it is no longer listed as enabled and its access data is refused. This scenario is tested by
`ContentSourcesSubsystem_DisabledRepository_IsNotAvailableForReading`. A registration is kept after the database is
reopened, which is tested by `StorageSubsystem_Repositories_PersistAcrossReopen`, and a token stays readable after a
restart of the server, which is tested by `ServerHost_Restart_KeyRingPersisted_TokenStaysReadable`.

**Review from a git repository**: A review is created from a real temporary git repository (a baseline commit and a
head commit with an added, a modified, and a deleted file, and a symbolic link that is skipped); it holds both commits
and the frozen head and baseline content; a further commit is then made in the repository, and the review, its file
content, and its record digest are unchanged, while a review created afterwards sees the new commit. This scenario is
tested by `ReviewService_CreateFromRepositoryAsync_RealGit_FreezesChangesAgainstLaterCommits`. A selection that
matches no changed file is refused with the nothing-to-review message and nothing is created, which is tested by
`NewReviewPage_CreateForm_EmptySelection_ShowsNothingToReviewAndCreatesNothing`. The digest that binds to the source,
the selection, and each change is tested by `RecordDigest_Compute_SourceChange_ChangesDigest`.

**Signed attestation**: A review is completed by the signature of its reviewer, and a signed-in user opens the review
page, which offers the download of the attestation; the download is a PDF whose signature verifies and that carries the
notice of the self-signed key, and the audit trail records one `attestation.generated` event. A review that is not
complete is not offered and is refused. These scenarios are tested by
`EvidenceSubsystem_CompletedReview_IsOfferedAndDownloadedAsSignedPdf` and
`EvidenceSubsystem_ReviewNotComplete_IsNotOfferedAndRefused`. The system requirement is also traced to the HTTP test of
the endpoint, `AttestationEndpoint_Get_CompleteReview_ReturnsSignedPdfDownloadAndRecordsEvent`. The Puppet end-to-end
test was not extended for this slice (see the verification of the `Evidence` subsystem).

**Personal access tokens**: A user creates a token, a script uses it to call the API, and after the token is revoked the
same request is refused with 401. This
scenario is tested by `ApiSubsystem_TokenJourney_CreateUseRevoke_WorksEndToEnd`. The rules of the tokens (the hash, the
expiry, the limit, and the revocation) are verified in the `Core` subsystem, the table in the `Storage` subsystem, and
the card on the account page in the `Web` subsystem.

**API under /api**: With a token, a script creates a draft review from files, reads it back (it is flagged as created
through the API and is not approved), finds that the attestation of a draft is refused with 409, and downloads the
attestation PDF of a complete review. This scenario is tested by
`ApiSubsystem_ReviewJourney_CreateDraftReadAndDownloadAttestation_WorksEndToEnd`. A token cannot sign or change a
reviewer slot, and cannot open a web page; this is tested by
`ApiSubsystem_TokenPrincipal_SigningAndWebPages_AreNotReachable`. The Puppet end-to-end test was not extended, because
the API has no page (see the verification of the `Api` subsystem).

**Persistence across restart**: A project is stored, the server is stopped and started again on the same
database file, and a signed-in user sees the project on the projects page. This scenario is tested by
`HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart`.

**Database failure**: The server is run with an unsupported database provider; it reports an error and returns
exit code 1 without serving. This scenario is tested by
`HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne`.

**Test identifiers on every page**: Every interactive element of every page carries a `data-testid`. This
scenario is tested by `WebSubsystem_AllPages_EveryInteractiveElementHasTestId`.
