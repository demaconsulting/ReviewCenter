# Puppet

Puppet is a developer tool that lets a person, or a coding agent, look at and drive the ReviewCenter web UI from the
command line. It starts the ReviewCenter server on a random local port, opens it in headless Chromium, and then
accepts short commands such as `click`, `text`, and `screenshot` against that browser.

Puppet is **not part of the distributed ReviewCenter package**. It lives in the repository as its own software
system (`ReviewCenterPuppet`) so that its behavior, tests, and reviews stay separate from the product.

## One-time setup

Puppet uses Playwright, which needs a Chromium build that is downloaded once per machine. Build the solution, then run
the `playwright.ps1` script that the build copies next to the Puppet executable:

```powershell
dotnet build tools/puppet
pwsh tools/puppet/bin/Debug/net10.0/playwright.ps1 install chromium
```

If Chromium is missing, `start` fails with exit code 4 and prints this same command.

## Usage

Run Puppet from the repository root (replace `Debug` with `Release` if you built that configuration):

```powershell
$puppet = "tools/puppet/bin/Debug/net10.0/DemaConsulting.ReviewCenter.Puppet.dll"
```

In the examples below, run Puppet as `dotnet $puppet <command>`. The `$puppet` variable holds only the path of the
DLL: PowerShell does not split a string such as `"dotnet <path>"` into a program and its argument when it is invoked
with `&`, so the program name stays outside the variable.

| Command                                  | What it does                                                    |
|------------------------------------------|-----------------------------------------------------------------|
| `start [--session <id>]`                 | Starts the server and browser; prints the session id and URL    |
| `goto <path>`                            | Navigates inside the session's server, for example `goto /`     |
| `click <selector>`                       | Clicks an element                                               |
| `fill <selector> <text>`                 | Types text into an input                                        |
| `text <selector>`                        | Prints the text of an element                                   |
| `screenshot [--out <file>] [--full-page]`| Saves a PNG and prints its absolute path                        |
| `snapshot`                               | Prints an accessibility snapshot, title, URL, and test ids      |
| `stop`                                   | Stops the session: browser, server, and background daemon       |
| `list`                                   | Lists sessions; leftovers of dead sessions are cleaned up       |

Options that apply to every command except `start`, `stop`, and `list` are `--session <id>` and
`--timeout <seconds>` (1 to 600, default 10). `start` also accepts `--server <path>` to run a specific
`DemaConsulting.ReviewCenter.dll` instead of the one built next to the Puppet project.

### Selectors

A selector made only of letters, digits, hyphens, and underscores is a **test id**: `hello-button` means
`[data-testid="hello-button"]`. Anything else, such as `#main`, `text=Hello`, or `button >> nth=0`, is passed to
Playwright unchanged.

### Sessions

The session to use is chosen in this order: `--session`, the `REVIEWCENTER_PUPPET_SESSION` environment variable, or the
only running session. A session id is 1 to 32 letters, digits, hyphens, or underscores; `start` generates one when you do
not give it.

### Example

```powershell
dotnet $puppet start --session demo
dotnet $puppet goto / --session demo
dotnet $puppet screenshot --out demo-before.png --session demo
dotnet $puppet click hello-button --session demo
dotnet $puppet text hello-message --session demo
dotnet $puppet click counter-button --session demo
dotnet $puppet text counter-value --session demo
dotnet $puppet stop --session demo
```

## Exit codes

| Code | Meaning                                                                                          |
|------|--------------------------------------------------------------------------------------------------|
| 0    | The command succeeded                                                                            |
| 1    | Bad arguments, or the command failed (element not found, timeout, navigation outside the server) |
| 2    | The session does not exist                                                                       |
| 3    | The session was stale (its daemon had died); leftovers were cleaned up                           |
| 4    | The session could not be started (server did not start, or Chromium is not installed)            |

## How it works

`start` does not stay running. It launches a detached background **daemon** (the same executable with a hidden
`__daemon` verb) and waits until the daemon reports that the server and browser are ready. The daemon:

- starts the ReviewCenter server with `dotnet DemaConsulting.ReviewCenter.dll serve --urls http://127.0.0.1:<port>`
  on a free port and waits for its `/health` endpoint to answer `200` with the exact body `Healthy` (any other
  successful answer means another service took the port, and the start fails);
- launches headless Chromium and opens one page;
- listens for commands on a free port of the loopback interface, protected by a random token;
- shuts everything down on `stop`, or after 30 minutes without a command.

Every other command is a separate short process: it reads the session file, sends one request to the daemon, prints
the answer, and exits.

Session files live in `%TEMP%\reviewcenter-puppet\<id>.json` (the system temp folder on other platforms). They hold the
control port, the token, the server URL, and the process ids and start times of the daemon, the server, and the
browser; they are deleted on `stop`. On Linux and macOS the folder and the files are readable by their owner only; on
Windows the per-user temp folder already restricts them to your account. Screenshots without `--out` are saved in the
same folder as `<id>-<timestamp>.png`.

Only one `start` can win for a session id: simultaneous starts of the same id fail with exit code 1. A `start` that
times out or is cancelled ends the daemon it launched, and a stale session or `stop` also ends the server and browser
it recorded. A process is only ended when its id and start time both match what the session recorded, so a recycled
process id is never ended.

## Known limitations

- `puppet stop` cannot interrupt a slow `start`. The control loop only runs commands once startup has finished, so
  a `stop` sent while `start` is still waiting for the server or the browser is answered with "The session is still
  starting; try again shortly." and does not stop anything. Startup is bounded by a timeout, after which `start` ends
  the daemon and cleans up. To abandon a slow start sooner, cancel the `start` command itself (for example with
  Ctrl+C), which also ends the daemon it launched.

## Design notes

- Puppet only navigates inside the server that belongs to the session; `goto` to another site is refused.
- The control port accepts connections from the local machine only and rejects requests without the session token.
- Documentation of the requirements, design, and verification of Puppet is in `docs/` under `review-center-puppet`.
