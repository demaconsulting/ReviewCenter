## StepRunner

### Verification Approach

`StepRunner` is verified with unit tests defined in `StepRunnerTests.cs`. Parsing is tested with steps-file text.
Execution is tested against a `FakeBrowserPage` that records its calls and writes a small PNG for screenshots.

### Test Environment

N/A - standard test environment. The tests use a temporary folder.

### Acceptance Criteria

- All unit tests pass with zero failures.
- A file with every step kind (and a comment and a trailing comma) parses, and screenshot names get `.png`.
- Invalid JSON, an empty list, an unknown kind, a missing or malformed value, and an unknown property each raise a
  `PuppetException` naming the file, the step, and the problem.
- Steps run in order with resolved selectors, and every step reports one result.
- A failing step stops the run, carries the error, and produces `failure-step-N.png`.

### Test Scenarios

**StepFile_Parse_AllStepKinds_ReturnsSteps**: One step of each kind, with a comment and a trailing comma, parses with
all fields set. Tested by `StepFile_Parse_AllStepKinds_ReturnsSteps`.

**StepFile_Parse_InvalidJson_ThrowsPuppetException**: Truncated JSON raises a message that names the file. Tested by
`StepFile_Parse_InvalidJson_ThrowsPuppetException`.

**StepFile_Parse_InvalidSteps_ThrowsPuppetExceptionNamingTheProblem**: Seven invalid inputs (unknown kind, bad
`url`, missing `selector`, missing `value`, unsafe `file`, unknown property, empty list) each raise the expected
message. Tested by `StepFile_Parse_InvalidSteps_ThrowsPuppetExceptionNamingTheProblem`.

**StepRunner_Run_AllStepKinds_ExecutesInOrderAndReportsResults**: Every kind runs against the fake page; the calls
are in order with resolved selectors, results carry the text and the screenshot path, and the PNG was written. Tested
by `StepRunner_Run_AllStepKinds_ExecutesInOrderAndReportsResults`.

**StepRunner_Run_StepFails_StopsAndCapturesFailureScreenshot**: The second step fails; the run stops there and the
result carries the error and the failure screenshot. Tested by
`StepRunner_Run_StepFails_StopsAndCapturesFailureScreenshot`.
