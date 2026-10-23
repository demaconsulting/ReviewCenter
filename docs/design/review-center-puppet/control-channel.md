## ControlChannel

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`ControlChannel` carries commands from the short-lived client invocations to the session daemon. It is a loopback-only
TCP connection that carries one JSON document per line, and every request must present the random session token. The
unit consists of the message types and wire format (`ControlProtocol`, `ControlRequest`, `ControlResponse`), the
client (`ControlClient`), the server (`ControlServer`), and the `PuppetException` that reports expected failures.

### Data Model

**ControlRequest**: a record with `Token`, `Command`, `Selector`, `Text`, `OutputPath`, `FullPage`, and `TimeoutMs`.
`Text` holds the path of `goto` or the value of `fill`.

**ControlResponse**: a record with `Ok`, `ExitCode`, and `Message`. The message is the command output on success and
the reason on failure.

**PuppetException**: the exception type for failures that the user can understand and act on. Its message is shown to
the user as it is. Its internal helper `DescribeUnexpected` formats any other exception as a one-line message that
names the exception type, so unanticipated failures stay visible without a stack trace.

**MaxLineLength**: 8 MiB, the longest line either side accepts, which bounds memory use when a peer misbehaves.

**Idle timeout**: supplied by the caller of `ControlServer.RunAsync`; the daemon uses 30 minutes.

### Key Methods

**ControlProtocol.Serialize / Deserialize**: Convert a message to and from one line of JSON. `Deserialize` raises a
`PuppetException` when the text is not a valid message.

**ControlProtocol.ReadLineAsync**: Reads one newline-terminated line with the length limit. Returns null when the peer
closes the connection before a complete line arrives.

**ControlProtocol.ParseResponse**: Parses the line a daemon answered with and accepts only a JSON object with exactly
a boolean `Ok`, an integer `ExitCode`, and a string `Message`. Anything else (`{}`, another JSON value, a missing,
extra, or mistyped property, or text that is not JSON) raises a `PuppetException` that names the port and says the
service did not answer like a Puppet session daemon. The strictness mirrors the server readiness probe: once a daemon
is gone its port can be taken by an unrelated service, which may answer with anything, and a lenient parse would turn
that into a "failed command" with no message. There are no redirects to follow on this channel (it is a raw line
exchange over TCP, not HTTP). The token that the client sends belongs to the dead daemon, so a foreign listener that
receives it learns nothing of value.

**ControlClient.SendAsync**: Opens a connection to the loopback address and the given port, sends the request line,
reads the response line, and returns the response parsed with `ParseResponse`. One connection is used per command.
The whole exchange is bounded by the timeout the caller supplies.

**ControlServer constructor**: Starts listening on the loopback address with a port chosen by the operating system.
`Port` exposes the port.

**ControlServer.CreateToken**: Returns a random 256-bit value as hexadecimal text.

**ControlServer.RunAsync**: Accepts and serves one client at a time until the cancellation token is cancelled or no
connection arrives within the idle timeout.

- *Returns*: true when the loop ended because of the idle timeout and false when it was cancelled.

For each client it reads the request line within 10 seconds, deserializes it, compares the presented token with the
session token using a fixed-time comparison, and passes an authenticated request to the handler. A request with the
wrong token receives a failed response and the handler is not called.

### Error Handling

The server converts a malformed message, an oversized line, a request that takes too long to arrive, and a rejected
token into failed responses with exit code 1 and never stops the loop. An unexpected exception raised by the handler, for
example from a browser that was killed, is also converted into a failed response with exit code 1 whose message
comes from `PuppetException.DescribeUnexpected` (`Unexpected error (<type>): <first line>`); the loop keeps serving
later requests, so one bad command cannot end the session without an answer. Only an out-of-memory condition is not
caught. A client that disconnects before reading the
answer is ignored. The client raises a `PuppetException` when it cannot connect, when the daemon closes the
connection without answering, when the daemon does not answer in time, and when the answer is malformed or is not
exactly a control response. Cancellation
by the caller is not converted.

### Dependencies

- **.NET base class library**: sockets, JSON serialization, cryptographic random numbers, and fixed-time comparison.

### Callers

- **PuppetProgram**: uses `ControlClient.SendAsync` to send every browser command and `stop`.
- **DaemonHost**: creates the `ControlServer` and runs it.
- **CommandExecutor**: consumes `ControlRequest` and produces `ControlResponse`.
