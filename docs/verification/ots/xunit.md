## xUnit Verification

This document provides the verification evidence for the xUnit OTS software item. Requirements
for this OTS item are defined in the xUnit OTS Software Requirements document.

### Required Functionality

xUnit is the unit-testing framework used by the project. It discovers and runs all
test methods on the Microsoft Testing Platform. The Microsoft Testing Platform TRX report extension
(Microsoft.Testing.Extensions.TrxReport) writes the TRX result files that feed into coverage
reporting and requirements traceability when `--report-trx` is requested, as the CI pipeline does.
Passing tests confirm the framework is functioning correctly.

### Verification Approach

The evidence is the TRX result file produced by the normal test run and consumed by ReqStream; no dedicated test
exercises xUnit or the TRX extension. Every test of the project runs under xUnit on the Microsoft Testing Platform,
so a passing test shows that xUnit discovered and executed it. The test-run configuration (`--report-trx` in the CI
build, which activates the TRX report extension referenced by the test projects) writes the TRX files, and the
ReqStream trace matrix is satisfied only when those files contain results for the tests linked to the requirements.
The scenarios below name tests the project already has; any of them being present in the TRX satisfies the
requirements. A local `build.ps1` run does not request TRX files and so evidences execution only; the TRX is
produced by the CI pipeline.

### Test Scenarios

#### Context_Create_NoArguments_ReturnsDefaultContext

**Scenario**: xUnit discovers and runs this test; the test verifies Context default construction.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### Context_Create_VersionFlag_SetsVersionTrue

**Scenario**: xUnit discovers and runs this test; the test verifies that passing the --version flag
sets the Version property to true.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### Context_Create_SilentFlag_SetsSilentTrue

**Scenario**: xUnit discovers and runs this test; the test verifies that passing the --silent flag
sets the Silent property to true.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### Context_Create_LogFlag_OpensLogFile

**Scenario**: xUnit discovers and runs this test; the test verifies that passing the --log flag
opens a log file.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### Context_Create_UnknownArgument_ThrowsArgumentException

**Scenario**: xUnit discovers and runs this test; the test verifies that an unrecognized argument
raises an exception.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### PathHelpers_SafePathCombine_ValidPaths_CombinesCorrectly

**Scenario**: xUnit discovers and runs this test; the test verifies that SafePathCombine correctly
joins valid path segments.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### Program_Run_WithVersionFlag_DisplaysVersionOnly

**Scenario**: xUnit discovers and runs this test; the test verifies that the program prints only
version information when invoked with the --version flag.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### Validation_Run_WithSilentContext_PrintsSummary

**Scenario**: xUnit discovers and runs this test; the test verifies that Validation.Run prints a
summary even when the context is configured for silent operation.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.

#### SelectorResolver_Resolve_AllForms_ReturnsPlaywrightSelector

**Scenario**: xUnit discovers and runs this test from the Puppet test project; the test verifies that a plain
selector name and the prefixed selector forms are converted to Playwright selectors.

**Expected**: xUnit executes the test, the test passes, and the result is recorded by name in the TRX output.

**Requirement coverage**: `ReviewCenter-OTS-xUnit-Execute`, `ReviewCenter-OTS-xUnit-Report`.
