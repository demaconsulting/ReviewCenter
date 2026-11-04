# ReviewCenterPuppet

## Verification Approach

Puppet is verified by unit tests of its units plus two real end-to-end tests; there are no fakes and no test-only
abstractions. The unit tests call the units directly: argument parsing and result formatting in `PuppetProgram`,
the request/response protocol and the stale-file ping in `ControlChannel`, selector resolution, and the browser-free
helpers of `PlaywrightBrowser`.

The first end-to-end test, `PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer`, starts the real
ReviewCenter server on a free port with one seeded administrator, runs `puppet start`, signs in and creates a project
in real headless Chromium with separate commands, and stops Puppet again. It is requirement-linked evidence for the
system requirements of the lifecycle, the interaction commands (all except what the second test adds), and the visual
checks, and for `ReviewCenterPuppet-OTS-Playwright-Chromium` on both platforms. The unit requirements of the
`BrowserDaemon` that need a browser are shown here, not by a unit test; only its start-up failure has a unit test.
The CI pipeline installs Chromium and sets `REVIEWCENTER_REQUIRE_CHROMIUM=true`, so a missing or broken browser
fails the test instead of skipping it. Without that variable, as on a developer machine without Chromium, the test
skips itself with a clear reason.

The second end-to-end test,
`PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer`, proves in real
Chromium that a control that is revealed by hovering can be reached with separate commands. It registers a local git
repository, creates a review of one changed line, and drives the diff of the review page. It is requirement-linked
evidence for `ReviewCenterPuppet-System-Interact-Click`, `-Hover`, and `-HoverPersists` on both platforms and is
skipped locally, like the first test,
when Chromium (or `git`) is not available.

## Test Environment

The tests run on net10.0 on Windows and Linux. The end-to-end test starts the real server and the real daemon on
free loopback ports and uses a private control file in a temporary folder, so it never touches the control file of a
Puppet that a developer has running. Tests that bind ports or start processes belong to the serial `Sequential`
xUnit collection and run one at a time. In CI the two test assemblies also run one after the other
(`--max-parallel-test-modules 1`). The second end-to-end test also needs the `git` command line (CI runners have it);
it creates its repository and the server's clone cache in a temporary folder that it removes afterwards.

## Acceptance Criteria

- All tests pass with zero failures on all supported platforms.
- `puppet start` returns with the browser ready: the first command after it works.
- `puppet start` while Puppet is already running (the same control file) fails with exit code 1; `puppet stop` exits
  with 0 and removes the control file, and a second `puppet stop` exits with code 2 because nothing is running.
- `puppet start` while no Chromium is installed (the test points `PLAYWRIGHT_BROWSERS_PATH` at an empty folder for
  that one start) fails with exit code 1, prints the reason with the install command and leaves no control file; the
  normal start that follows works.
- A hover stays in effect for later commands: after `hover`, a separate `screenshot` and `eval` still see the
  element hovered, until `mouse-move` moves the mouse away.
- `goto` reports the HTTP status and every result ends with the `@ <url>  "<title>"` line of the current page.
- A console error that `eval` raises is reported once, in the result of the command that raised it, and is not
  repeated by the commands after that. Page errors and failed requests are reported the same way by the code but are
  not claimed by a requirement, because their timing is not deterministic.
- A command that fails (for example an unknown element) exits with code 1 and a one-line message naming the selector.
- The end-to-end run signs in (the password is submitted with `press Enter`), creates a project over the live
  connection (the project page appears after a `wait` for the asynchronous update, and for text), reads page text,
  switches the theme on the account page, and produces PNG screenshots of the page and of one element.
- `viewport` changes the inner width of the page, and `theme light` and `theme dark` change the emulated color
  scheme (checked with `eval`).
- On a real review diff, the **+** of a line is hidden while the mouse is elsewhere, `hover` on its row reveals it
  (computed opacity `1`), and a `click` on it opens the comment composer under the line.

## Test Scenarios

**PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer**: Starts Puppet, goes to the home page,
checks the HTTP status and the `@ <url>  "<title>"` line of the result, and checks that `snapshot` lists the sign-in
link. After `hover` on that link, a `screenshot` and an `eval` of
`matches(':hover')` show it hovered, and after `mouse-move 0 0` it is not. On the login page a failed sign-in
(`fill` and `click` of the login button) shows the login error, and the right user name and password, submitted with
`press Enter` on the password field, sign in and show
the display name `Puppet Admin`. A `click` on the projects link of the header, a `wait` for the page marked
interactive, a `click` on the new project button, a `fill` of the project name, and a `click` on the create button
create a project over the live connection, and `wait` (for an element and for text) and `text` (of an element and of
the whole page) show the project page. On the account page a
`click` on the dark theme button makes `eval` report the `dark` theme attribute on the page and in the stored
setting. An `eval` of `console.error('probe')` is reported once, by the `! console.error: probe` line of the
same command, and not by the two commands after it. A click on a missing element exits with 1 and names the selector;
`screenshot` writes a PNG of the page and of one element (`--selector`); `viewport` sets the inner width and
`theme light` and `theme dark` set the emulated color scheme (checked with `eval`); `console` succeeds; a `start`
with a missing Chromium (an empty browser folder for that one start) exits with 1, prints the reason and leaves no
control file; a `start` while running exits with 1; `stop` exits with 0
and removes the control file; a second `stop` exits with 2. It is the evidence for the lifecycle, the interaction
commands, and the visual-check system requirements. Tested by
`windows@PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer` and
`ubuntu@PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer`.

**PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer**: Creates a local git
repository with a `base` tag and one changed line, starts the real server with a private clone cache and one seeded
administrator, starts Puppet and signs in. Through the pages, `fill` and `click` register the repository, create a
project, and create a review of `main` against the `base` tag (`check`, `select`, `fill`); a `wait` for the page marked
interactive shows the review. A `click` on the file's **View** and on **Diff** shows the diff. After `mouse-move 0 0`
an `eval` of the computed opacity of `diff-comment-2` (the **+** of the added line) reports `0`; after `hover` on
`diff-row-2` it reports `1`; a `click` on `diff-comment-2` and a `wait` show the `new-thread-form` inside the
`diff-composer`. Tested by
`windows@PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer` and
`ubuntu@PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer`.

**Unit-level coverage**: Argument handling and result formatting, the control channel, selector resolution, and the
launch failure message are verified by the unit tests of the five units.
