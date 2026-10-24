## ControlChannel

### Verification Approach

`ControlChannel` is verified with two unit tests defined in `ControlChannelTests.cs` over a real loopback listener,
without a browser: one for the request/response round trip and one for the stale-port ping.

### Test Environment

N/A - standard test environment. The tests bind a free loopback port.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A request sent with `SendAsync` reaches the handler of `ServeAsync` and the response arrives intact, including a
  multi-line result, the URL, the title, and the events.
- A port that nobody listens on does not answer the ping.

### Test Scenarios

**ControlChannel_SendAsync_RoundTrip_DeliversRequestAndResponse**: A listener whose handler echoes the command and
arguments serves one request; the response has the echoed multi-line output, the URL, the title, and the event.
Tested by `ControlChannel_SendAsync_RoundTrip_DeliversRequestAndResponse`.

**ControlChannel_IsAnsweringAsync_StaleControlFile_ReturnsFalse**: A port that was just released does not answer, so
a control file naming it is stale. Tested by `ControlChannel_IsAnsweringAsync_StaleControlFile_ReturnsFalse`.
