## Web

### Verification Approach

The `Web` subsystem is verified by integration tests defined in `WebSubsystemTests.cs`. Each
test starts the real server pipeline on an in-memory test server through
`ServerHost.CreateApplication` and requests the home route over HTTP, so the `App` and
`HomePage` units run together exactly as they do in production. Tests assert on the returned
HTML. Behavior that needs a live browser connection is verified at unit level with component
tests in the `HomePage` unit and by the system-level scenarios.

### Test Environment

N/A - standard test environment.

### Acceptance Criteria

- All integration tests pass with zero failures.
- The home route answers HTTP status 200 with a document titled "ReviewCenter" that contains
  every control the user interface tests drive.
- The server-rendered response is marked as not yet interactive, so that drivers wait for the
  live connection.

### Test Scenarios

**WebSubsystem_HomeRoute_Requested_RendersTitleAndControls**: The pipeline is started in memory
and the home route is requested; the response is 200 and contains the document title, the Hello
button, the counter button, and the counter value. This scenario is tested by
`WebSubsystem_HomeRoute_Requested_RendersTitleAndControls`.

**WebSubsystem_HomeRoute_InitialResponse_IsNotYetInteractive**: The home route is requested
over plain HTTP, where no live connection exists; the `data-interactive` marker in the response
is `false`. This scenario is tested by
`WebSubsystem_HomeRoute_InitialResponse_IsNotYetInteractive`.
