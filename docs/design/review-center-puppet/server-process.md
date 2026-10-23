## ServerProcess

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`ServerProcess` starts the ReviewCenter server as a child process and stops it again. It is the only place that
starts a process.

### Data Model

**IRunningServer**: an `IAsyncDisposable` with the `Url` of the server.

### Key Methods

**StartAsync**: Picks a free loopback port, runs `dotnet <assembly> serve --urls http://127.0.0.1:<port>`, and polls
`/health` until it answers 200 `Healthy` (limit 30 seconds). It keeps the last 20 lines of the server output so that
a start failure message can show them.

**DisposeAsync**: Kills the process tree of the server and waits up to 10 seconds for it to exit.

### Error Handling

A server that exits early or is not healthy in time raises a `PuppetException` with the output tail, after the
process was killed.

### Dependencies

- **System.Diagnostics.Process** and loopback sockets from the .NET base class library.
