## FileSystemGlobbing Verification

This document provides the verification evidence for the FileSystemGlobbing OTS software item. Requirements for this
OTS item are defined in the FileSystemGlobbing OTS Software Requirements document.

### Required Functionality

FileSystemGlobbing matches paths against glob patterns for the common syntax, so that it can serve as an independent
reference for the selection matcher of the tool. A passing test confirms that it does so and that the matcher agrees
with it.

### Verification Approach

FileSystemGlobbing has no self-validation mode and is verified by the project's own tests, which run the real library
in memory. The scenario names a specific test method that depends on the library under verification. A passing
pipeline run for the scenario constitutes evidence that the requirement is satisfied.

### Test Scenarios

#### SelectionMatcher_IsMatch_CommonSyntax_AgreesWithFileSystemGlobbing

**Scenario**: Include patterns followed by exclusions with the common syntax (literals, star, and double star) are
matched against a set of paths with the selection matcher and with the library.

**Expected**: Both give the same answer for every pair.

**Requirement coverage**: `ReviewCenter-OTS-FileSystemGlobbing-Reference`.
