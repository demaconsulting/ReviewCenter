# Puppet

> The Puppet is a minimal helper that lets an AI drive and screenshot the UI. Keep it as small and simple as
> possible: no production-grade hardening, no process-identity/race engineering, best-effort cleanup only, few
> tests. Do not grow it beyond what an agent needs to run steps and see screenshots.

Puppet runs a list of steps (go to a page, click, fill, read text, screenshot) against the ReviewCenter web UI in
headless Chromium. It is **not part of the distributed ReviewCenter package**; it is the `ReviewCenterPuppet` software
system of this repository.

## Agent workflow

1. Write a `steps.json` file.
2. Run `dotnet tools/puppet/bin/Debug/net10.0/DemaConsulting.ReviewCenter.Puppet.dll run steps.json` (see Usage).
   It starts the server on a free port, runs the steps, and stops the server and browser.
3. Read the JSON result lines on standard output and view the PNG files it wrote.

## One-time setup

Puppet uses Playwright, which needs a Chromium build that is downloaded once per machine. Build Puppet, then run the
`playwright.ps1` script that the build copies next to the executable:

```powershell
dotnet build tools/puppet
pwsh tools/puppet/bin/Debug/net10.0/playwright.ps1 install chromium
```

If Chromium is missing, `run` fails with exit code 2 and prints this same command. CI installs Chromium and sets
`REVIEWCENTER_REQUIRE_CHROMIUM=true`, so the end-to-end test fails instead of skipping when the browser is missing.

## Usage

```powershell
dotnet tools/puppet/bin/Debug/net10.0/DemaConsulting.ReviewCenter.Puppet.dll run steps.json
```

| Option                | Meaning                                                                           |
|-----------------------|-----------------------------------------------------------------------------------|
| `--server-dll <path>` | ReviewCenter server assembly to start (default: the one built next to the Puppet) |
| `--url <base-url>`    | Drive an already running server instead of starting one                           |
| `--out <dir>`         | Folder for screenshots (default `artifacts/puppet`)                               |
| `--timeout <seconds>` | Wait limit for each step, 1 to 600 (default 10)                                   |

## Steps file

A JSON array of steps, run in order. Comments and trailing commas are allowed; unknown properties are errors.

```json
[
  { "kind": "goto", "url": "/" },
  { "kind": "click", "selector": "hello-button" },
  { "kind": "text", "selector": "hello-message" },
  { "kind": "click", "selector": "counter-button" },
  { "kind": "text", "selector": "counter-value" },
  { "kind": "screenshot", "file": "after-click" }
]
```

| Kind         | Properties                                                                           |
|--------------|--------------------------------------------------------------------------------------|
| `goto`       | `url`: a path starting with `/`                                                      |
| `click`      | `selector`                                                                           |
| `fill`       | `selector`, `value`                                                                  |
| `text`       | `selector`: prints the element text                                                  |
| `screenshot` | `file`: optional plain name (`.png` is added; default `step-N`), `fullPage`: boolean |

A selector made only of letters, digits, hyphens, and underscores is a `data-testid`, so `hello-button` means
`[data-testid="hello-button"]`. Any other selector, such as `#main` or `text=Hello`, is passed to Playwright unchanged.

## Output and exit codes

One JSON line per step on standard output, for example `{"step":4,"kind":"text","ok":true,"text":"Hello!"}`. A
screenshot step reports the absolute `file` path. On the first failing step the run stops, the line has
`"ok":false` and an `error`, and a `failure-step-N.png` screenshot is saved when possible.

| Code | Meaning                                                                                       |
|------|-----------------------------------------------------------------------------------------------|
| 0    | Every step succeeded                                                                          |
| 1    | A step failed                                                                                 |
| 2    | The run could not start (bad arguments or steps file, no Chromium, server did not start)      |

## Known limitation

The server and browser are stopped in a `finally` block, best effort. If the Puppet process itself is hard-killed,
a server or Chromium process may linger. This is accepted and not engineered around.
