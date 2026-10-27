# Puppet

> The Puppet is a minimal helper that lets an AI drive and screenshot the UI. Keep it as small and simple as
> possible: no production-grade hardening, no process-identity/race engineering, best-effort cleanup only, few
> tests. Do not grow it beyond what an agent needs for its look, decide, act loop.

Puppet keeps ONE persistent headless Chromium open and lets you drive it one command per shell call: act, look at
the screenshot or the page text, decide, act again. It is **not part of the distributed ReviewCenter package**; it is
the `ReviewCenterPuppet` software system of this repository.

## One-time setup

Puppet uses Playwright, which needs a Chromium build that is downloaded once per machine. Build Puppet, then run the
`playwright.ps1` script that the build copies next to the executable:

```powershell
dotnet build tools/puppet
pwsh tools/puppet/bin/Debug/net10.0/playwright.ps1 install chromium
```

If Chromium is missing, `puppet start` fails and prints this same command. CI installs Chromium and sets
`REVIEWCENTER_REQUIRE_CHROMIUM=true`, so the end-to-end test fails instead of skipping when the browser is missing.

In this document `puppet` stands for `dotnet tools/puppet/bin/Debug/net10.0/DemaConsulting.ReviewCenter.Puppet.dll`;
define a function or alias for it in your shell if you like.

## Agent workflow

1. Start the ReviewCenter server separately, in an async shell that stays open (the watcher also reloads on edits):

   ```powershell
   dotnet watch --project src/DemaConsulting.ReviewCenter -- serve --urls http://127.0.0.1:5199
   ```

2. Start Puppet: `puppet start --url http://127.0.0.1:5199`. It prints one line and returns; the browser keeps
   running in the background.
3. Run commands one at a time, for example `puppet goto /login`, `puppet snapshot`, `puppet click login-button`,
`puppet wait current-user`, `puppet screenshot shot.png`. Look at each result, then decide the next command.
4. Stop it when done: `puppet stop`.

`puppet start` options: `--url <base>` (paths given to `goto` are relative to it), `--viewport <WxH>` (default
1280x800), and `--headed` (show the browser window). `puppet start` while Puppet is already running fails with exit
code 1.

## Commands

- `goto <url-or-path>`, `reload`, `back`: navigate; waits until the Blazor circuit is interactive. `goto` and
  `reload` print the HTTP status (for example `HTTP 404`).
- `click <sel>`, `dblclick <sel>`, `hover <sel>`: mouse actions on an element.
- `fill <sel> <text>`, `type <text>`: set a field value; type into the focused element.
- `press <key> [sel]`: press a key such as `Enter`, optionally on an element.
- `select <sel> <value>`: choose a dropdown option.
- `check <sel>`, `uncheck <sel>`: set a checkbox.
- `upload <sel> <file>`: attach a file to a file input.
- `mouse-move <x> <y>`, `mouse-click <x> <y>`: mouse by page coordinates.
- `wait <sel or text=...> [--timeout <ms>]`: wait for an element or text (default 5000 ms).
- `text [sel]`: text of an element, or of the whole page.
- `snapshot`: visible interactive elements with tag, role, name, test id, and enabled/checked state (max 200).
- `screenshot <file.png> [--full] [--selector <sel>]`: PNG of the viewport, the full page, or one element.
- `viewport <WxH>`, `theme <light|dark>`: resize the viewport; emulate `prefers-color-scheme`.
- `eval <js-expression>`: evaluate a JavaScript expression; the result is printed as JSON.
- `console`: the most recent console errors, page errors, and failed requests.
- `reset`: new browser context; clears cookies and storage (and the hover).
- `stop`: close the browser, delete the control file, and exit the daemon.

`puppet --help` prints the same list. Selectors: `testid:foo` or a bare `foo` mean `data-testid="foo"`; `css=...`,
`text=...`, and `role=...` use Playwright selector syntax, and any other text is passed to Playwright unchanged.
A `fill` text cannot start with `--`; use `eval` for such text.

The browser reacts to Blazor Server updates asynchronously, and Puppet has no fixed sleeps. After a click that changes
the page, use `wait` (for example `puppet wait "text=Saved"`) before you read `text` or take a screenshot.

The mouse position persists between commands, so `puppet hover save-button` followed by a separate
`puppet screenshot hover.png` shows the hover state. It lasts until the mouse moves again (`hover`, `click`,
`mouse-move`) or `reset` runs.

## Output and exit codes

Every result prints the result text, then a line `@ <url>  "<title>"`, then one `! <event>` line for each new
console error, page error, or failed request since the previous command. A failed command prints one short line on
standard error, for example `click timed out after 5000ms (selector not found or not actionable): testid:foo`.
Actions fail after about 5 seconds so a wrong selector costs little.

- Exit code 0: the command worked.
- Exit code 1: the command failed, `puppet start` found Puppet already running, or the browser did not start.
- Exit code 2: usage error, Puppet is not running, or the daemon process could not be spawned.

## Visual polish loop

1. Edit the Razor or CSS source; `dotnet watch` rebuilds the server.
2. `puppet reload`, then `puppet wait <sel>` if the page loads asynchronously.
3. `puppet screenshot shot.png`, or `--selector <sel>` for one element, and look at the image: open the PNG with the
   image viewer of your tool (an agent can read the PNG file directly as an image).
3a. Apply the human-factors test to every screenshot, and fix the page whenever an answer is "no":
    (a) Can I quickly see the information I need?
    (b) Am I given so much other information that I cannot find what I am after?
    (c) Does the UI help me work out what I can or need to do next?
4. Check other sizes and themes: `puppet viewport 390x800`, `puppet theme dark`, then screenshot again.
5. Confirm details numerically with `puppet eval`, for example
   `puppet eval "getComputedStyle(document.querySelector('[data-testid=projects-link]')).color"` or
   `puppet eval "document.documentElement.scrollWidth > innerWidth"` for horizontal overflow.

## Known limitation

If the daemon or Chromium is killed or crashes, a Chromium process or a stale control file
(`reviewcenter-puppet.json` in the temporary folder) may be left behind. `puppet start` replaces a stale control
file; kill a stray process manually.
