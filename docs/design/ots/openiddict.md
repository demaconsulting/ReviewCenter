## OpenIddict

This document describes the integration and usage design for the `OpenIddict` OTS software item (NuGet package
`OpenIddict.AspNetCore`).

### Purpose

OpenIddict is an OpenID Connect server library for ASP.NET Core. ReviewCenter uses it only in the test project, as a fake
identity provider, so that the sign-in with a provider can be verified against a real protocol implementation that signs
real identity tokens, without an external service. It is not referenced by the product and is not shipped.

### Features Used

- `AddOpenIddict().AddServer` with the authorization and token endpoints, the authorization code flow with a required
  proof key, a signing key, and an encryption key
- The discovery document and the signing keys that the server serves
- Degraded mode with no stored clients, authorizations, or tokens, and the ASP.NET Core pass-through to the endpoints of
  the test provider
- Identity tokens whose claims (subject, names, e-mail address, and the authentication time `auth_time`) the test sets

### Integration Pattern

The test project hosts the fake provider (`FakeIdentityProvider`) in the test process on a free loopback port. A
scenario object chooses what the next sign-in answers with: another subject, an old, future, or missing `auth_time`, an
error, or a refused code. The provider keeps nothing except the requests it has seen, so the tests can check what the
application asked for. The package has no self-validation mode: the passing tests of `OidcSignIn` that run the full flow
against it are the evidence that it behaves as a real provider.
