## ASP.NET Core Verification

This document provides the verification evidence for the ASP.NET Core OTS software item. Requirements for this OTS
item are defined in the ASP.NET Core OTS Software Requirements document.

### Required Functionality

ASP.NET Core (the shared framework with the Kestrel web server and Blazor Server) hosts the ReviewCenter service. It
must route requests to the configured endpoints, answer them over HTTP, render and drive Razor components, and
authenticate users with a cookie, authorize requests, protect posts against forgery, hash passwords, and encrypt text
with keys that survive a restart. Passing
tests confirm the framework is functioning correctly in the way ReviewCenter uses it.

### Verification Approach

ASP.NET Core is verified by integration evidence from the project's own test suite; the framework has no
self-validation mode. Each scenario names a specific test method. Some scenarios run the application in memory
through the test host, some render a component, the system-level scenarios start a real server process with the
`serve` verb and call it over HTTP, which exercises Kestrel, and one scenario drives the live Blazor connection from a
real browser. A passing pipeline run for all scenarios constitutes evidence that all four requirements are
satisfied.

### Test Scenarios

#### ServerHost_CreateApplication_HealthRoute_Returns200Healthy

**Scenario**: The application is created with the in-memory test host and the `/health` route is requested.

**Expected**: The framework routes the request and the response is status 200 with the body `Healthy`.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Host`.

#### ReviewCenter_ServeVerb_Started_HealthEndpointReturnsHealthy

**Scenario**: The `serve` verb starts a real server process and the `/health` route is requested over HTTP.

**Expected**: Kestrel accepts the connection and the response is status 200 with the body `Healthy`.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Host`.

#### ReviewCenter_ServeVerb_Started_HomePageContainsTitleAndTagline

**Scenario**: The `serve` verb starts a real server process and the home page `/` is requested over HTTP, as is the
Blazor client script.

**Expected**: The response contains the page title and the tagline rendered by the Razor components, and the client
script is served.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Host`, `ReviewCenter-OTS-AspNetCore-Components`.

#### WebSubsystem_HomeRoute_Requested_RendersTitleAndTagline

**Scenario**: The home route is requested from the in-memory test host.

**Expected**: The framework renders the routed Razor component to HTML containing the title and the tagline.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Components`.

#### WebSubsystem_ProjectsRoute_InitialResponse_IsNotYetInteractive

**Scenario**: A signed-in client requests the projects route from the in-memory test host; no live connection exists
for a plain HTTP request.

**Expected**: The server-rendered page is marked `data-interactive="false"`, which shows that the framework renders
the page first and opens the live connection afterwards.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Components`.

#### NewProjectPage_CreateForm_ValidInput_CreatesProjectAndOpensIt

**Scenario**: The new project page component is rendered, a name is entered, and the form is submitted.

**Expected**: The component model handles the event, the project is created, and the browser is sent to the project.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-Components`.

#### PuppetEndToEnd_StartDriveStop_SignInProjectAndTheme_WorksAgainstRealServer

**Scenario**: The Puppet end-to-end test of the ReviewCenterPuppet system drives a real browser against a real
server: it signs in, waits until the page is marked interactive, clicks the new project button, fills in the form,
and submits it. The test runs on Windows and Linux in CI.

**Expected**: The click and the form submission are handled by the live Blazor connection, so the project page
appears, which proves the round trip between the browser and the component model.

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

#### CredentialProtector_ProtectUnprotect_RoundTripsAndHidesToken

**Scenario**: A token is protected with the Data Protection of the framework and unprotected again.

**Expected**: The token is returned unchanged, and the protected text does not contain it.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.

#### CredentialProtector_PersistedKeyRing_SurvivesRestart

**Scenario**: A token is protected with a key ring kept in a folder, and a new protector over the same folder, as
after a restart, unprotects it.

**Expected**: The token is returned unchanged.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.

#### CredentialProtector_Unprotect_OtherKeyRing_ThrowsCredentialsUnreadable

**Scenario**: Text protected under one key ring is unprotected under another.

**Expected**: The framework cannot decrypt it, and it is reported as unreadable without the text in the message.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.

#### CredentialProtector_Purpose_IsolatesTokensFromOtherUses

**Scenario**: Text protected for the token purpose is unprotected with another purpose on the same key ring.

**Expected**: The framework cannot decrypt it.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.

#### KeyRingProtection_CertificateRing_KeyFileIsEncryptedAndRestartReadsIt

**Scenario**: A key ring is created in a temporary folder with the certificate encryptor of the framework and an
in-memory RSA certificate, a value is protected, and a new provider over the same folder and certificate, as after a
restart, unprotects it.

**Expected**: The key file holds an encrypted secret and not the key, and the value is returned unchanged.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.

#### KeyRingProtection_CertificateRing_PreviousCertificateAndPasswordFile_StillReadTheRing

**Scenario**: A ring that a first certificate protected is read with a second certificate that lists the first one as
a previous certificate.

**Expected**: The framework decrypts the key file with the previous certificate and the value is returned unchanged.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.

#### KeyRingProtection_DpapiRing_WindowsKeyFileIsEncryptedAndRestartReadsIt

**Scenario**: On Windows only (the test is skipped elsewhere), a key ring is created in a temporary folder with the
DPAPI encryptor of the framework, a value is protected, and a new provider over the same folder unprotects it.

**Expected**: The key file holds an encrypted secret and not the key, and the value is returned unchanged.

**Requirement coverage**: `ReviewCenter-OTS-AspNetCore-DataProtection`.
