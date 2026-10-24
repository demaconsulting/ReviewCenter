## ASP.NET Core Verification

This document provides the verification evidence for the ASP.NET Core OTS software item. Requirements for this OTS
item are defined in the ASP.NET Core OTS Software Requirements document.

### Required Functionality

ASP.NET Core (the shared framework with the Kestrel web server and Blazor Server) hosts the ReviewCenter service. It
must route requests to the configured endpoints, answer them over HTTP, render and drive Razor components, and
authenticate users with a cookie, authorize requests, protect posts against forgery, and hash passwords. Passing
tests confirm the framework is functioning correctly in the way ReviewCenter uses it.

### Verification Approach

ASP.NET Core is verified by integration evidence from the project's own test suite; the framework has no
self-validation mode. Each scenario names a specific test method. Some scenarios run the application in memory
through the test host, some render a component, and the system-level scenarios start a real server process with the
`serve` verb and call it over HTTP, which exercises Kestrel. A passing pipeline run for all scenarios constitutes
evidence that all three requirements are satisfied.

### Test Scenarios

#### ServerHost_CreateApplication_HealthRoute_Returns200Healthy

**Scenario**: The application is created with the in-memory test host and the `/health` route is requested.

**Expected**: The framework routes the request and the response is status 200 with the body `Healthy`.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Host`.

#### ReviewCenter_ServeVerb_Started_HealthEndpointReturnsHealthy

**Scenario**: The `serve` verb starts a real server process and the `/health` route is requested over HTTP.

**Expected**: Kestrel accepts the connection and the response is status 200 with the body `Healthy`.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Host`.

#### ReviewCenter_ServeVerb_Started_HomePageContainsTitleAndControls

**Scenario**: The `serve` verb starts a real server process and the home page `/` is requested over HTTP.

**Expected**: The response contains the page title and the hello and counter controls rendered by the Razor
components.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Host`, `ReviewCenter-OTS-AspNetCore-Components`.

#### WebSubsystem_HomeRoute_Requested_RendersTitleAndControls

**Scenario**: The home route is requested from the in-memory test host.

**Expected**: The framework renders the routed Razor component to HTML containing the title and the controls.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Components`.

#### HomePage_HelloButton_Clicked_ShowsGreeting

**Scenario**: The home page component is rendered and the hello button is clicked.

**Expected**: The component model handles the event and re-renders with the greeting displayed.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Components`.

#### SignIn_Login_ValidCredentials_SetsSafeSessionCookieAndRedirects

**Scenario**: A user signs in through the login endpoint of the in-memory test host.

**Expected**: The cookie authentication of the framework issues a session cookie that is HTTP-only and
`SameSite=Lax`, and the response redirects to the return URL.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Authentication`.

#### SignIn_ProtectedPage_Anonymous_RedirectsToLogin

**Scenario**: Each protected page is requested by an anonymous client of the in-memory test host.

**Expected**: The authorization of the framework refuses the request and redirects it to the login page with the
requested address as the return URL.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Authentication`.

#### SignIn_PostWithoutAntiforgeryToken_IsRefusedAndSignsNobodyIn

**Scenario**: The login and logout endpoints are posted to without an antiforgery token.

**Expected**: The antiforgery protection of the framework answers status 400 and issues no cookie.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Authentication`.

#### PasswordHasher_Verify_RightAndWrongPassword_OnlyRightSucceeds

**Scenario**: A password is hashed with the password hasher of the framework, and the hash is checked against the
right and a wrong password.

**Expected**: Only the right password is accepted.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Authentication`.
