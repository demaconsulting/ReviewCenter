## ServerProcess

### Verification Approach

`ServerProcess` is verified with one test defined in `ServerProcessTests.cs` that starts the real ReviewCenter server
assembly, built next to the Puppet, through `ServerProcess.StartAsync`.

### Test Environment

The test needs permission to start a `dotnet` child process and to open a loopback port. It belongs to the serial
`Sequential` xUnit collection.

### Acceptance Criteria

- The test passes.
- The server answers its `/health` endpoint while it runs and no longer answers after it was disposed.

### Test Scenarios

**ServerProcess_Start_RealServer_BecomesHealthyAndStopsOnDispose**: The server is started, its health endpoint is
requested, and the server is disposed; afterwards the URL no longer answers. Tested by
`ServerProcess_Start_RealServer_BecomesHealthyAndStopsOnDispose`.
