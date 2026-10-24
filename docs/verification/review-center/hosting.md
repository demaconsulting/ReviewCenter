## Hosting

### Verification Approach

The `Hosting` subsystem is verified by integration tests defined in `HostingSubsystemTests.cs`.
Each test starts the real server pipeline, built by `ServerHost.CreateApplication`, on an
in-memory test server and exercises it over HTTP, so no port is bound and nothing is mocked.
A capturing logger provider, attached through `ServerHostOptions`, makes logged entries
observable. The `Web` subsystem components are real, so the tests also confirm that `Hosting`
and `Web` work together.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All integration tests pass with zero failures.
- The health endpoint answers HTTP status 200 with the body `Healthy`.
- The health route and the home route are both registered and answer.
- Start-up writes a structured log entry with a named property, and framework logging reaches
  the same logger providers.
- The database is created for a configured location, the stored data is served again by a second host on the
  same database (to a signed-in user), and an unsupported database provider ends the run with exit code 1.

### Test Scenarios

**HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy**: The host is started in memory and
`/health` is requested; the response has a success status and the body `Healthy`. This scenario
is tested by `HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy`.

**HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry**: The host is started with a
capturing logger provider and one request is made; the application's start-up entry carries a
named property, and entries from framework components reach the same provider. This scenario is
tested by `HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry`.

**HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer**: The host is started in memory
and both `/` and `/health` are requested; both answer HTTP status 200. This scenario is tested
by `HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer`.

**HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart**: A host is started on a new
database file and a project is stored through its services; the host is stopped and a second host is started on
the same file. A user signed in to the second host requests `/projects`, which lists the stored project. This scenario
is tested by
`HostingSubsystem_Startup_DatabaseConfigured_ServesStoredProjectsAfterRestart`.

**HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne**: The server is run through
`ServerHost.RunAsync` with an unsupported database provider; it returns 1 and the context records the failure,
and no unhandled exception escapes. This scenario is tested by
`HostingSubsystem_Startup_UnsupportedDatabaseProvider_FailsWithExitCodeOne`.
