## ControlChannel

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`ControlChannel` is how the commands find and talk to the daemon: the control file and a minimal request/response
protocol over loopback TCP.

### Data Model

**Control file**: `reviewcenter-puppet.json` in the temporary folder, or the path in the environment variable
`REVIEWCENTER_PUPPET_CONTROL_FILE` (or given with `--control-file`) so that separate sessions do not share one Puppet:
`{"port":N,"baseUrl":"..."}` written by the
daemon when it is ready, or `{"error":"..."}` when the daemon could not start.

**Request**: one JSON line `{"command":"click","args":["login-button"],"options":{}}`.

**Response**: one JSON line `{"ok":true,"output":"...","url":"...","title":"...","events":["..."]}`. The special
request `ping` is answered with the output `pong`.

### Key Methods

**Read / Write**: Read and write the control file. A missing, half-written, or garbage file reads as no information.

**ResolveControlFile**: Chooses the control file path: the (full) path from the `REVIEWCENTER_PUPPET_CONTROL_FILE`
variable when it is set and not blank, otherwise the well-known file in the temporary folder.

**SendAsync**: Connects to the loopback port, sends one request line, and reads one response line. One connection is
used per command.

**ServeAsync**: The daemon side: accepts one connection at a time, reads a request line, calls the handler, and writes
the response line. A client that disconnects or sends garbage does not stop the daemon.

**IsAnsweringAsync**: Sends `ping` with a 2 second limit. A control file whose port does not answer is stale.

### Error Handling

Connection and protocol failures surface as `SocketException` or `IOException` to the caller, which reports that the
control file is stale.

### Dependencies

None - uses only the .NET base class library.
