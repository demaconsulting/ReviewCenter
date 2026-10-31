## ControlChannel

### Verification Approach

`ControlChannel` is verified with four unit tests defined in `ControlChannelTests.cs`, without a browser: two over a
real loopback listener (the request/response round trip and the stale-port ping) and two for the control file path
(the pure resolver, and the real environment lookup, which runs in the serial `Sequential` collection and restores
the variable).

### Test Environment

N/A - standard test environment. The tests bind a free loopback port.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A request sent with `SendAsync` reaches the handler of `ServeAsync` and the response arrives intact, including a
  multi-line result, the URL, the title, and the events.
- A port that nobody listens on does not answer the ping.
- The control file path is the configured one when given, and the well-known temporary file otherwise.

### Test Scenarios

**ControlChannel_SendAsync_RoundTrip_DeliversRequestAndResponse**: A listener whose handler echoes the command and
arguments serves one request; the response has the echoed multi-line output, the URL, the title, and the event.
Tested by `ControlChannel_SendAsync_RoundTrip_DeliversRequestAndResponse`.

**ControlChannel_IsAnsweringAsync_StaleControlFile_ReturnsFalse**: A port that was just released does not answer, so
a control file naming it is stale. Tested by `ControlChannel_IsAnsweringAsync_StaleControlFile_ReturnsFalse`.

**ControlChannel_ResolveControlFile_VariableSetOrBlank_ChoosesPath**: A configured path is used as given; a null or
blank value gives `reviewcenter-puppet.json` in the temporary folder. The test calls the pure resolver rather than
changing the process environment. Tested by `ControlChannel_ResolveControlFile_VariableSetOrBlank_ChoosesPath`.

**ControlChannel_DefaultControlFile_EnvironmentVariable_IsHonored**: With `REVIEWCENTER_PUPPET_CONTROL_FILE` set,
`DefaultControlFile` is that path; with it cleared, the well-known temporary file. The original value is restored in a
`finally` block, and the class is in the serial `Sequential` collection so no other test sees the change. Tested by
`ControlChannel_DefaultControlFile_EnvironmentVariable_IsHonored`.
