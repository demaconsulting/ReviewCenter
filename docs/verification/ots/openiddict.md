## OpenIddict Verification

This document provides the verification evidence for the OpenIddict OTS software item. Requirements for this OTS item
are defined in the OpenIddict OTS Software Requirements document.

### Required Functionality

OpenIddict is used only by the tests, as a fake identity provider. It serves the discovery document and the signing keys
and issues identity tokens, with the authentication time of the login, that are signed with its keys in the
authorization code flow with a proof key. A passing test confirms that the tests that rely on it test the product against
a real provider and not against a mock.

### Verification Approach

OpenIddict has no self-validation mode and is verified by the project's own tests, which run the full sign-in flow
against it and read the time of the login from the tokens it issues. Each scenario names a specific test method that
depends on the package under verification. A passing pipeline run for the scenario constitutes evidence that the
requirement is satisfied.

### Test Scenarios

#### OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn

**Scenario**: A person signs in through the fake provider in the authorization code flow with a proof key.

**Expected**: The application finds the discovery document and the keys, and accepts the identity token, so the token is
signed with the keys that the provider serves.

**Requirement coverage**: `ReviewCenter-OTS-OpenIddict-TestProvider`.

#### OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature

**Scenario**: A fresh sign-in is made for a confirmation of identity.

**Expected**: The identity token carries an `auth_time` that the application accepts as recent.

**Requirement coverage**: `ReviewCenter-OTS-OpenIddict-TestProvider`.

#### OidcReauth_AuthTimeNotInWindow_IsRefusedFailClosed

**Scenario**: The provider is set to issue tokens with an old, a future, or no authentication time.

**Expected**: The tokens carry exactly that time (or none), so the application refuses them.

**Requirement coverage**: `ReviewCenter-OTS-OpenIddict-TestProvider`.
