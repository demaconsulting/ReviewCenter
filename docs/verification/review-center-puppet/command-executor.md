## CommandExecutor

### Verification Approach

`CommandExecutor` is verified with unit tests defined in `CommandExecutorTests.cs`. The executor is created over a
`FakeBrowserPage` that records every call as readable text and can be told to fail, a fixed base URL, a session id,
and a temporary screenshot folder. Tests call `ExecuteAsync` with control requests and assert on the response and on
the calls the fake page recorded. No browser, socket, or process is involved.

### Test Environment

N/A - standard test environment. Each test that writes screenshots uses its own temporary folder created with the
`TempDirectory` helper.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A rooted path is navigated within the session's server, and a foreign URL is refused without touching the page.
- A plain selector name reaches the page as a test id selector, and `fill` and `text` act on the page.
- A screenshot is written to the requested path or to the default folder with the documented name, and the path is
  returned.
- A snapshot returns the page's snapshot text.
- A missing element, an unknown command, and a missing selector return failed responses with a message.

### Test Scenarios

**CommandExecutor_Execute_GotoRootedPath_NavigatesWithinServer**: `goto /` succeeds and the page records one
navigation to the base URL followed by `/`. This scenario is tested by
`CommandExecutor_Execute_GotoRootedPath_NavigatesWithinServer`.

**CommandExecutor_Execute_GotoForeignUrl_IsRefused**: `goto` with an address of an external site fails, the message
says the address is outside the server, and the page recorded no calls. This scenario is tested by
`CommandExecutor_Execute_GotoForeignUrl_IsRefused`.

**CommandExecutor_Execute_ClickPlainName_UsesDataTestIdSelector**: `click hello-button` succeeds and the page
records a click on `[data-testid="hello-button"]`. This scenario is tested by
`CommandExecutor_Execute_ClickPlainName_UsesDataTestIdSelector`.

**CommandExecutor_Execute_Text_ReturnsElementText**: A page that reports the text "Hello from ReviewCenter!" makes
`text` return that text as the response message. This scenario is tested by
`CommandExecutor_Execute_Text_ReturnsElementText`.

**CommandExecutor_Execute_Fill_SendsTextToPage**: `fill` with the selector `#name` and the text `Ada` succeeds and
the page records the fill with that selector and text. This scenario is tested by
`CommandExecutor_Execute_Fill_SendsTextToPage`.

**CommandExecutor_Execute_ScreenshotWithOut_WritesFileAndReturnsPath**: `screenshot` with an output path in a
temporary folder succeeds, returns that path, and the file exists. This scenario is tested by
`CommandExecutor_Execute_ScreenshotWithOut_WritesFileAndReturnsPath`.

**CommandExecutor_Execute_ScreenshotWithoutOut_UsesDefaultLocation**: `screenshot` without a path writes a `.png`
file in the default folder whose name starts with the session id, and returns its path. This scenario is tested by
`CommandExecutor_Execute_ScreenshotWithoutOut_UsesDefaultLocation`.

**CommandExecutor_Execute_Snapshot_ReturnsSnapshotText**: `snapshot` returns the text "fake snapshot" of the fake
page. This scenario is tested by `CommandExecutor_Execute_Snapshot_ReturnsSnapshotText`.

**CommandExecutor_Execute_MissingElement_ReturnsFailureMessage**: A page whose click fails with a missing-element
error makes `click` return a failed response with exit code 1 and a message that contains "No element". This
scenario is tested by `CommandExecutor_Execute_MissingElement_ReturnsFailureMessage`.

**CommandExecutor_Execute_UnknownCommandOrMissingSelector_Fails**: An unknown command and a `click` without a
selector each return a failed response, and the second message mentions the selector. This scenario is tested by
`CommandExecutor_Execute_UnknownCommandOrMissingSelector_Fails`.
