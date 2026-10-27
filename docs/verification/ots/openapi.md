## Microsoft.AspNetCore.OpenApi Verification

This document provides the verification evidence for the Microsoft.AspNetCore.OpenApi OTS software item.
Requirements for this OTS item are defined in the OpenApi OTS Software Requirements document.

### Required Functionality

Microsoft.AspNetCore.OpenApi generates an OpenAPI document from the endpoints of the application: it lists the routes
with their methods, bodies, and declared answers, honors a filter that limits the routes, and writes the security scheme
and the fixed values that a document transformer sets. The output must be the same on every run. A passing test confirms
that the `ApiDocument` unit gets all of this from the package.

### Verification Approach

Microsoft.AspNetCore.OpenApi has no self-validation mode and is verified by the project's own tests, which start the real
application in memory, fetch the document through HTTP, and compare it with the snapshot that is kept in the
documentation. Each scenario names a specific test method that depends on the package under verification. A passing
pipeline run for the scenario constitutes evidence that the requirement is satisfied.

### Test Scenarios

#### ApiDocument_Get_NoToken_DescribesTheApiWithBearerScheme

**Scenario**: The document is requested without a token and parsed.

**Expected**: The generated document lists only paths under `/api/`, does not list the address of the document itself,
has the fixed version, no servers, an HTTP bearer security scheme, and one security requirement on every operation.

**Requirement coverage**: `ReviewCenter-OTS-OpenApi-Document`.

#### ApiDocument_Get_MatchesCommittedSnapshot

**Scenario**: The generated document is compared with `docs/api/openapi.json`.

**Expected**: The two are the same after the line endings are normalized, so the output of the package for the current
routes is the reviewed one.

**Requirement coverage**: `ReviewCenter-OTS-OpenApi-Document`.

#### ApiDocument_Get_TwoApplications_AreIdentical

**Scenario**: The document is fetched from two separately started applications.

**Expected**: The two documents are byte for byte the same, so the output is stable.

**Requirement coverage**: `ReviewCenter-OTS-OpenApi-Document`.
