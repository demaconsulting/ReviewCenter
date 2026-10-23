## PuppetProgram

![ReviewCenterPuppet Structure](ReviewCenterPuppetView.svg)

### Purpose

`PuppetProgram` is the entry point of Puppet. It parses the command line of `puppet run`, reads the steps file, starts
the server and the browser, hands the steps to `StepRunner`, and always tears everything down again. It maps every
outcome to an exit code. `PuppetServices` bundles the server starter and the browser launcher so that tests can
replace them with fakes.

### Data Model

**Exit codes**: `ExitOk` (0, every step succeeded), `ExitStepFailed` (1), and `ExitCannotRun` (2, bad arguments, bad
steps file, missing server assembly, or a server or browser that cannot start).

**PuppetServices**: a record of two delegates, `StartServer` and `LaunchBrowser`. `PuppetServices.CreateDefault()`
builds the production set from `ServerProcess` and `PlaywrightBrowser`.

**PuppetException**: the exception used for every error that is reported to the user as a plain message.

### Key Methods

**Main**: Entry point of the process. Cancels the run on Ctrl+C and calls `RunAsync` with the production services.

**RunAsync**: Runs one command and returns the exit code.

- *Parameters*: the argument array, the output and error writers, the `PuppetServices`, and a cancellation token.
- *Postconditions*: A page and a server that were started are disposed before it returns, on every path. A failure
  to dispose is a warning on the error writer and does not change the exit code.

Options: `--server-dll <path>` or `--url <base-url>` (not both), `--out <dir>` (default `artifacts/puppet`), and
`--timeout <seconds>` (1 to 600, default 10). `--help` prints the usage text and returns 0.

**ResolveServerPath**: Chooses the server assembly: the `--server-dll` value, otherwise the path recorded in the build
metadata of the tool. A path that does not exist raises a `PuppetException` that tells the caller to build the
solution or to pass the option.

### Error Handling

A `PuppetException` is printed on the error writer and gives exit code 2, except when a step fails, which gives 1.
Cancellation also tears down before it returns.

### Dependencies

- **StepRunner**: parses the steps file and runs the steps.
- **ServerProcess** and **PlaywrightBrowser**: used through `PuppetServices`.
