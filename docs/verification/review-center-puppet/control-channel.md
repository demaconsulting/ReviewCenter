## ControlChannel

### Verification Approach

`ControlChannel` is verified with unit tests defined in `ControlChannelTests.cs`. The tests create a real
`ControlServer` with a handler that is a test delegate, run its loop on a background task, and talk to it through the
real `ControlClient` over the loopback interface. This exercises the wire format, the authentication, and the
timeouts end to end without any other Puppet unit. The message functions are verified directly.

### Test Environment

N/A - standard test environment. The tests require permission to open loopback TCP ports; they use ports chosen by
the operating system and run in the serial `Sequential` xUnit collection of the Puppet test project, so they never
compete with other port- or process-using tests for CPU or sockets.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A request with the right token reaches the handler and the handler's response comes back.
- A request with a wrong token is rejected with a message about the token, and the handler is never called.
- The server accepts a loopback connection on a valid port.
- The server loop returns true after the idle timeout and false when cancelled.
- The client reports a `PuppetException` when nothing listens, when the handler is too slow, and when a message is
  malformed.
- A handler that throws an unexpected exception produces a failed response (exit code 1) that names the exception
  type and the first line of its message, and later requests are still served.
- A request survives a serialize and deserialize round trip with every field.

### Test Scenarios

**ControlChannel_Send_ValidToken_ReturnsHandlerResponse**: A server whose handler echoes the command runs in the
background; a request with the right token returns a successful response with the message "handled click". This
scenario is tested by `ControlChannel_Send_ValidToken_ReturnsHandlerResponse`.

**ControlChannel_Send_WrongToken_IsRejectedWithoutCallingHandler**: A request with another token returns a failed
response whose message mentions the token, and the handler call counter stays at 0. This scenario is tested by
`ControlChannel_Send_WrongToken_IsRejectedWithoutCallingHandler`.

**ControlChannel_Server_Listening_BindsToLoopbackOnly**: A new server reports a port from 1 to 65535 and a client
can connect to it through the loopback address. The server is created with the loopback address only, so a
connection through that address is the supported route. This scenario is tested by
`ControlChannel_Server_Listening_BindsToLoopbackOnly`.

**ControlChannel_Run_NoRequests_ReturnsTrueAfterIdleTimeout**: A server with a very short idle timeout and no
callers ends its loop and returns true. This scenario is tested by
`ControlChannel_Run_NoRequests_ReturnsTrueAfterIdleTimeout`.

**ControlChannel_Run_Cancelled_ReturnsFalse**: A server whose cancellation token is already cancelled returns false.
This scenario is tested by `ControlChannel_Run_Cancelled_ReturnsFalse`.

**ControlChannel_Send_NothingListening_ThrowsPuppetException**: A client sends to a port that was free and released;
a `PuppetException` is thrown and its message mentions the daemon. This scenario is tested by
`ControlChannel_Send_NothingListening_ThrowsPuppetException`.

**ControlChannel_Send_HandlerTooSlow_ThrowsPuppetExceptionOnTimeout**: A handler that waits for the test to finish
and a short client timeout lead to a `PuppetException` whose message says that the daemon did not answer. This
scenario is tested by `ControlChannel_Send_HandlerTooSlow_ThrowsPuppetExceptionOnTimeout`.

**ControlChannel_Send_HandlerThrowsUnexpected_ReturnsFailedResponseAndKeepsServing**: A handler throws an
`InvalidOperationException` with a two-line message on its first call and answers normally afterwards. The first
request returns a failed response with exit code 1 whose message contains "Unexpected error", the exception type, and
only the first line of the message; the second request returns the handler's normal answer, which proves that the
serving loop survived. This scenario is tested by
`ControlChannel_Send_HandlerThrowsUnexpected_ReturnsFailedResponseAndKeepsServing`.

**ControlChannel_Deserialize_MalformedJson_ThrowsPuppetException**: Deserializing the text `{ nope` as a response
throws a `PuppetException`. This scenario is tested by `ControlChannel_Deserialize_MalformedJson_ThrowsPuppetException`.

**ControlChannel_SerializeDeserialize_Request_RoundTrips**: A request with every field set is serialized and
deserialized and the copy equals the original. This scenario is tested by
`ControlChannel_SerializeDeserialize_Request_RoundTrips`.
