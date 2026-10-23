## bUnit Verification

This document provides the verification evidence for the bUnit OTS software item. Requirements for this OTS item are
defined in the bUnit OTS Software Requirements document.

### Required Functionality

bUnit renders Blazor components in a test context and simulates user interaction, and
`Microsoft.AspNetCore.TestHost` hosts the web application in memory. Together they allow the user interface and the
hosting pipeline to be verified without a browser or a network listener. Passing tests confirm both libraries are
functioning correctly.

### Verification Approach

bUnit and the test host are verified by the project's own test suite; neither has a self-validation mode. Each
scenario names a specific test method that depends on the library under verification. A passing pipeline run for all
scenarios constitutes evidence that both requirements are satisfied.

### Test Scenarios

#### HomePage_HelloButton_Clicked_ShowsGreeting

**Scenario**: bUnit renders the home page component and clicks the hello button.

**Expected**: The component re-renders and the greeting appears in the markup.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### HomePage_CounterButton_ClickedThreeTimes_ShowsCountThree

**Scenario**: bUnit renders the home page component and clicks the counter button three times.

**Expected**: The rendered counter value is 3.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### Routes_Render_RootUrl_RendersHomePage

**Scenario**: bUnit renders the router component after navigating to the root URL.

**Expected**: The home page component is rendered by the router.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-Component`.

#### ServerHost_CreateApplication_HealthRoute_Returns200Healthy

**Scenario**: The test host serves the application in memory and the test client requests `/health`.

**Expected**: The response is status 200 with the body `Healthy`.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-TestHost`.

#### WebSubsystem_HomeRoute_Requested_RendersTitleAndControls

**Scenario**: The test host serves the application in memory and the test client requests `/`.

**Expected**: The response contains the page title and the hello and counter controls.

**Requirement coverage**: `ReviewCenter-OTS-bUnit-TestHost`.
