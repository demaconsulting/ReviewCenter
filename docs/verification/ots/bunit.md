## bUnit Verification

This document provides the verification evidence for the bUnit OTS software item. Requirements for this OTS item are
defined in the bUnit OTS Software Requirements document.

### Required Functionality

bUnit renders Blazor components in a test context and simulates user interaction, and
`Microsoft.AspNetCore.TestHost` hosts the web application in memory. Together they allow the user interface, including
forms and file selection, and the hosting pipeline to be verified without a browser or a network listener. Passing
tests confirm both libraries are functioning correctly.

### Verification Approach

bUnit and the test host are verified by the project's own test suite; neither has a self-validation mode. Each
scenario names a specific test method that depends on the library under verification. A passing pipeline run for all
scenarios constitutes evidence that both requirements are satisfied.

### Test Scenarios

#### Routes_Render_RootUrl_RendersHomePage

**Scenario**: bUnit renders the router component after navigating to the root URL.

**Expected**: The home page component is rendered by the router.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### NewProjectPage_CreateForm_ValidInput_CreatesProjectAndOpensIt

**Scenario**: bUnit renders the new project page, enters a name and a description in the inputs, and submits the
form.

**Expected**: The form submission is processed, the project is created, and the navigation manager of the test
context is sent to the project page.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### NewReviewPage_FileInput_FilesSelected_ListsNamesAndSizes

**Scenario**: bUnit renders the new review page and selects two files in the file input.

**Expected**: The file selection is processed and the rendered markup lists the names and sizes of the files.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### NewReviewPage_CreateForm_ValidInput_CreatesReviewAndNavigatesToIt

**Scenario**: bUnit renders the new review page, selects files, fills in the form, and submits it.

**Expected**: The submission is processed and the navigation manager of the test context is sent to the review
page.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### ServerHost_CreateApplication_HealthRoute_Returns200Healthy

**Scenario**: The test host serves the application in memory and the test client requests `/health`.

**Expected**: The response is status 200 with the body `Healthy`.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-TestHost`.

#### WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline

**Scenario**: The test host serves the application in memory and the test client requests `/`.

**Expected**: The response contains the page title and the tagline.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-TestHost`.

#### WebSubsystem_CreatedProjectAndReview_ArePresentedByThePages

**Scenario**: The test host serves the application in memory; a project and a review are created through the
services and the test client requests the projects page, the project page, and the review page.

**Expected**: Each response is status 200 and contains the project, the review, and its fingerprint.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-TestHost`.
