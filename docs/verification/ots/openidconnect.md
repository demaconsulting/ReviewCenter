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
verification. A passing pipeline run for all scenarios constitutes evidence that all thirteen requirements are
satisfied.

### Test Scenarios

#### OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn

**Scenario**: A person signs in through the test provider.

**Expected**: The handler accepts the valid identity token, the claims reach the application, and the person is signed
in.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`,
`ReviewCenter-OTS-OpenIdConnect-CodeFlow-Claims`.

#### OidcSignIn_Flow_NeverLogsSecretsAndUsesCodeFlowWithPkce

**Scenario**: The authorization request that the handler sent is inspected.

**Expected**: The request asks for a code with an S256 code challenge, a state, and a nonce and, for a normal sign-in,
no prompt.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow`,
`ReviewCenter-OTS-OpenIdConnect-CodeFlow-Pkce`, `ReviewCenter-OTS-OpenIdConnect-CodeFlow-RequestState`,
`ReviewCenter-OTS-OpenIdConnect-CodeFlow-RequestNonce`.

#### OidcSignIn_TamperedState_IsRefused

**Scenario**: The answer of the provider comes back with a state the handler did not issue.

**Expected**: The handler refuses the answer and no session is created.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow-State`.

#### OidcSignIn_ExpiredOrWronglySignedToken_FailsClosed

**Scenario**: The test provider issues an identity token that has expired, and one that is signed with a key it does
not serve.

**Expected**: The handler refuses each token, the person is sent to the sign-in page with an error, and no session and
no account are created.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow-Signature`,
`ReviewCenter-OTS-OpenIdConnect-CodeFlow-Lifetime`.

#### OidcSignIn_WrongIssuerAudienceOrNonce_FailsClosed

**Scenario**: The test provider issues an identity token with a wrong issuer, one with a wrong audience, and one with a
wrong nonce.

**Expected**: The handler refuses each token, the person is sent to the sign-in page with an error, and no session and
no account are created.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow-Issuer`,
`ReviewCenter-OTS-OpenIdConnect-CodeFlow-Audience`, `ReviewCenter-OTS-OpenIdConnect-CodeFlow-Nonce`.

#### OidcSignIn_ProviderRefusesTheCode_EndsWithoutSession

**Scenario**: The token endpoint of the provider refuses the code.

**Expected**: The handler reports a failure and no session is created.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow-ProviderRefusal`.

#### OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature

**Scenario**: A confirmation of identity is started for a signature.

**Expected**: The handler sends `prompt=login` and `max_age=0` that the application added, and delivers the claims
including `auth_time` that the application checks.

**Requirement coverage**: `ReviewCenter-OTS-OpenIdConnect-CodeFlow-RequestParameters`,
`ReviewCenter-OTS-OpenIdConnect-CodeFlow-Claims`.
