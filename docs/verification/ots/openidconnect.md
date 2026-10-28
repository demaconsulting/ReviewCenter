## Microsoft.AspNetCore.Authentication.OpenIdConnect Verification

This document provides the verification evidence for the Microsoft.AspNetCore.Authentication.OpenIdConnect OTS software
item. Requirements for this OTS item are defined in the OpenIdConnect OTS Software Requirements document.

### Required Functionality

Microsoft.AspNetCore.Authentication.OpenIdConnect performs the authorization code flow with a proof key, a state, and a
nonce against the provider, accepts the identity token only when it is valid and the state is the one it issued, lets the
application add `prompt` and `max_age` to the request, and gives the application the claims of the token. A passing test
confirms that the `OidcSignIn` unit gets all of this from the package.

### Verification Approach

Microsoft.AspNetCore.Authentication.OpenIdConnect has no self-validation mode and is verified by the project's own
tests, which start the real application in memory and run the whole flow against a test provider that signs real tokens
(see the OpenIddict verification). Each scenario names a specific test method that depends on the package under
verification. A passing pipeline run for the scenario constitutes evidence that the requirement is satisfied.

### Test Scenarios

#### OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn

**Scenario**: A person signs in through the test provider.

**Expected**: The handler accepts the valid identity token, the claims reach the application, and the person is signed
in.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`.

#### OidcSignIn_Flow_NeverLogsSecretsAndUsesCodeFlowWithPkce

**Scenario**: The authorization request that the handler sent is inspected.

**Expected**: The request asks for a code with an S256 code challenge and, for a normal sign-in, no prompt.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`.

#### OidcSignIn_TamperedState_IsRefused

**Scenario**: The answer of the provider comes back with a state the handler did not issue.

**Expected**: The handler refuses the answer and no session is created.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`.

#### OidcSignIn_ProviderRefusesTheCode_EndsWithoutSession

**Scenario**: The token endpoint of the provider refuses the code.

**Expected**: The handler reports a failure and no session is created.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`.

#### OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature

**Scenario**: A confirmation of identity is started for a signature.

**Expected**: The handler sends `prompt=login` and `max_age=0` that the application added, and delivers the claims
including `auth_time` that the application checks.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`.
