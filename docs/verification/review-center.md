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
page, and the Blazor script. The self-validation results (`ReviewCenter_VersionDisplay` and
`ReviewCenter_HelpDisplay`) in those TRX files are the evidence for the platform requirements; the platform
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
  signed-in users reach the protected pages, and only administrators reach the users page.
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
- Every change of data, and every sign-in, sign-out, failed sign-in, and lockout, is recorded in an audit trail whose
  entries are chained by hashes, and the change and its entry are stored together; the entries never contain a password
  or the text of a comment or justification.
- Every signed-in user sees the history of a review, and only administrators see the whole audit trail, in pages of 50,
  newest first.
- The audit chain is verified on request, from the audit page and with the `audit verify` verb; an intact chain gives
  exit code 0 and a broken chain, or an unreadable database, exit code 1.
- The limits of the chain are stated to the user: it does not detect the removal of the newest entries or a complete
  rewrite by someone with write access to the database, unless the head hash was recorded elsewhere.
- A database setting that cannot be used stops the server with an error and a non-zero exit code.
- Every interactive element of the home page carries a `data-testid`, and the same holds for every other page.

## Test Scenarios

**ReviewCenter_VersionFlag_Provided_OutputsVersion**: The `--version` flag is passed as
the sole argument; the tool outputs the version string and exits with code 0. This scenario is
tested by `ReviewCenter_VersionFlag_Provided_OutputsVersion`.

**ReviewCenter_HelpFlag_Provided_OutputsUsageInformation**: The `--help` flag is passed
as the sole argument; the combined output contains "Usage" and "Options" and the tool exits
with code 0. This scenario is tested by
`ReviewCenter_HelpFlag_Provided_OutputsUsageInformation`.

**ReviewCenter_ValidateFlag_Provided_RunsValidation**: The `--validate` flag is passed as
the sole argument; the combined output contains "Total Tests:" and the tool exits with code 0,
confirming the self-validation suite runs and completes successfully. This scenario is tested by
`ReviewCenter_ValidateFlag_Provided_RunsValidation`.

**ReviewCenter_ValidateWithTrxResults_Requested_GeneratesTrxFile**: The `--validate` flag
is combined with `--results <path>.trx`; a TRX file containing a `<TestRun` XML element is
created at the specified path and the tool exits with code 0. This scenario is tested by
`ReviewCenter_ValidateWithTrxResults_Requested_GeneratesTrxFile`.

**ReviewCenter_ValidateWithXmlResults_Requested_GeneratesJUnitFile**: The `--validate` flag
is combined with `--results <path>.xml`; a JUnit XML file containing a `<testsuites` XML element
is created at the specified path and the tool exits with code 0. This scenario is tested by
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

**ReviewCenter_ValidateWithDepth_DepthThree_OutputsCorrectHeadingLevel**: The `--validate`
flag is combined with `--depth 3`; the combined output contains `###` (heading at depth 3) and
the tool exits with code 0. This scenario is tested by
`ReviewCenter_ValidateWithDepth_DepthThree_OutputsCorrectHeadingLevel`.

**ReviewCenter_NoArguments_Invoked_DisplaysBanner**: The tool is invoked with no
arguments; the combined output contains the tool name and copyright notice and the exit code is
0. This scenario is tested by `ReviewCenter_NoArguments_Invoked_DisplaysBanner`.

**ReviewCenter_ResultAlias_LegacyFlag_WritesResultsFile**: The `--validate` flag is
combined with `--result <path>.trx` (the legacy alias); a TRX file is created at the specified
path and the tool exits with code 0. This scenario is tested by
`ReviewCenter_ResultAlias_LegacyFlag_WritesResultsFile`.

**ReviewCenter_ValidateWithBadExtension_ExtensionInvalid_ReturnsNonZero**: The `--validate`
flag is combined with `--results <path>.bad` (unsupported extension); the tool exits with a
non-zero code and no file is created at the specified path. This scenario is tested by
`ReviewCenter_ValidateWithBadExtension_ExtensionInvalid_ReturnsNonZero`.

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
add`, a user name, a display name, and the role Admin, on a new temporary database, with the password in the
environment variable `REVIEWCENTER_PASSWORD` and not on the command line. The first run exits with code 0 and
reports "Created user 'root' with the role Admin."; the second run with the same user name exits with code 1 and
reports that the user already exists; the password appears in neither output. This scenario is tested by
`ReviewCenter_UserAddVerb_PasswordFromEnvironment_CreatesUserThenRejectsDuplicate`.

**ReviewCenter_UserAddVerb_NoPassword_ReturnsError**: The tool is run with `user add` and no password in the
environment while standard input is closed; it exits with code 1 and reports "No password was provided." instead of
waiting for input. This scenario is tested by `ReviewCenter_UserAddVerb_NoPassword_ReturnsError`.

The system requirements for reviews, restart persistence, and database failure are verified at subsystem level,
where the same production pipeline can be exercised with real databases more precisely than through a child
process: creating a review needs a live browser connection for the file upload, which the in-memory component
tests provide. The following scenarios belong to the system verification through the subsystem tests.

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
redirects an anonymous request to the login page, and the users page is refused to a reviewer and served to an
administrator. These scenarios are tested by `SignIn_ProtectedPage_Anonymous_RedirectsToLogin` and
`SignIn_UsersPage_ReviewerAndAdmin_OnlyAdminIsAllowed`.

**Open session ends**: A user is signed in on the real pipeline and the authentication state provider of a new scope
(a circuit) is checked; the user is valid. The user is then disabled through the user service and the check is
repeated; the user is no longer valid, which is what makes an open page show the "Session ended" message. This
scenario is tested by `IdentitySubsystem_Revalidation_UserDisabledAfterSignIn_OpenSessionIsNoLongerValid`.

**Users and passwords**: Users are created, signed in with in any case of the user name, locked after repeated
failures, and protected against the loss of the last administrator through the domain service, and users and
password changes are kept after the database is reopened. These scenarios are tested by
`CoreSubsystem_UsersWorkflow_CreateSignInLockoutAndLastAdmin` and `StorageSubsystem_Users_PersistAcrossReopen`.

**Review checklist**: Two checks of a new review are answered through the domain service over the real pipeline, one
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

**Audit trail chain**: Changes are made at the same moment through the three stores (review, user, and audit), which
share the process-wide gate; the resulting trail has consecutive sequence numbers and an intact chain, so the changes
were serialized and none was lost. This scenario is tested by
`AuditStore_ConcurrentChangesFromDifferentStores_ProduceLinearChain`. In a second scenario a user signs in with the
wrong password, signs in, and signs out over the real pipeline; the trail contains the three events in order and its
chain is intact. This scenario is tested by `SignIn_Flow_LoginFailureAndLogout_FormAnIntactChain`.

**Audit history and audit page**: The history page of a review with a long history shows 50 entries, newest first,
with Older and Newer links; the audit page refuses a user who is not an administrator and shows no entries. These
scenarios are tested by `HistoryPage_Render_LongHistory_PagesByFiftyWithOlderAndNewerLinks` and
`AuditPage_Render_NonAdmin_IsRefusedAndShowsNoEntries`.

**Audit verification**: The `audit verify` verb reports an intact trail with its entry count and exit code 0, and the
Verify button of the audit page reports the first broken entry of a tampered chain. These scenarios are tested by
`AuditCommand_Run_IntactTrail_ReportsEntryCountWithExitCodeZero` and
`AuditPage_Verify_TamperedChain_ShowsFirstBadEntry`. No test starts the tool as a process with the `audit verify`
verb; the dispatch of `Program` to the command is verified by inspection and by the tests of the command.

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

**Persistence across restart**: A project is stored, the server is stopped and started again on the same
database file, and a signed-in user sees the project on the projects page. This scenario is tested by
`HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart`.

**Database failure**: The server is run with an unsupported database provider; it reports an error and returns
exit code 1 without serving. This scenario is tested by
`HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne`.

**Test identifiers on every page**: Every interactive element of every page carries a `data-testid`. This
scenario is tested by `WebSubsystem_AllPages_EveryInteractiveElementHasTestId`.
