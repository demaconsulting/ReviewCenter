## SelfTest

### Verification Approach

The `SelfTest` subsystem is verified by integration tests defined in
`SelfTestSubsystemTests.cs`. The workflows that must see the real checks read the outcome of one
shared in-process run of the real default validation (`RealValidationRun`), which goes through
`Program.Run` with a real `Context`, `--validate`, `--depth 3`, a TRX results file, and a log
file; the suite pays for the real checks once there, and once more in the process-level system
test. The workflows that only need results files (JUnit, both formats, unsupported extension)
use `Validation.Run` with instant stand-in checks. No mocking is applied; `Context`, `Program`,
and `PathHelpers` all execute their real implementations. Temporary directories are used for
result file output to keep tests isolated and leave no permanent file-system side-effects.

### Test Environment

N/A - standard test environment. The real run needs `git` on the path, as the tool itself does.

### Acceptance Criteria

- All integration tests pass with zero failures.
- The real validation completes with exit code 0, eight tests passed, none skipped or failed, each
  line showing its elapsed time, within a generous time bound that catches a hung check.
- The real validation never creates the configured database and never runs or records the opt-in
  `ReviewCenter_ConfiguredDatabase` check.
- TRX and JUnit XML result files are generated with the correct XML root elements when valid
  file paths are supplied, and a skipped test is recorded as not executed.
- An unsupported result file extension produces exit code 1 and no file is created.

### Test Scenarios

**SelfTestSubsystem_ValidationWorkflow_NoResultFiles_CompletesSuccessfully**:
The shared real run completes without error, exit code is 0, the validate flag is set, the
summary shows eight tests passed, and each of the eight `ReviewCenter_` tests has a passed line
with its elapsed time. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_NoResultFiles_CompletesSuccessfully`.

**SelfTestSubsystem_ValidationWorkflow_WithTrxFile_GeneratesResults**: The shared real run
wrote a TRX file containing the `<TestRun` XML element and eight passed results with the names
of the eight tests, and none failed or not executed. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_WithTrxFile_GeneratesResults`.

**SelfTestSubsystem_ValidationWorkflow_DefaultRun_IsHermetic**: The real run, in a process whose
environment configures a database file that does not exist, does not create that file, and
neither the log nor the TRX mention the configured-database check. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_DefaultRun_IsHermetic`.

**SelfTestSubsystem_ValidationWorkflow_RealRun_FinishesWithinBound**: The real run completes in
well under the 60 s limit of a single check. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_RealRun_FinishesWithinBound`.

**SelfTestSubsystem_ValidationWorkflow_WithJUnitFile_GeneratesResults**: `Validation.Run` is
called with stand-in checks and a context whose `ResultsFile` points to a temporary `.xml` path;
a JUnit XML file containing the `<testsuites` XML element and the check names is created and
exit code is 0. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_WithJUnitFile_GeneratesResults`.

**SelfTestSubsystem_ValidationWorkflow_WithBothResultFiles_GeneratesBothResults**: Two separate
`Validation.Run` calls with a passing and a skipped stand-in check are made, one targeting a
`.trx` path and one targeting a `.xml` path; both files are created with the correct root XML
elements, the TRX records `NotExecuted`, the JUnit file records `<skipped`, and exit code is 0
for each run. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_WithBothResultFiles_GeneratesBothResults`.

**SelfTestSubsystem_ValidationWorkflow_WithUnsupportedExtension_EmitsErrorAndNoFile**:
`Validation.Run` is called with a context whose `ResultsFile` has a `.bad` extension; no file
is created at the specified path, `context.ExitCode` is 1, and an error message identifying the
unsupported format is written via `context.WriteError`. This scenario is tested by
`SelfTestSubsystem_ValidationWorkflow_WithUnsupportedExtension_EmitsErrorAndNoFile`.
