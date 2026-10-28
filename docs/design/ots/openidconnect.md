## Microsoft.AspNetCore.Authentication.OpenIdConnect

This document describes the integration and usage design for the `Microsoft.AspNetCore.Authentication.OpenIdConnect`
OTS software item (NuGet package `Microsoft.AspNetCore.Authentication.OpenIdConnect`).

### Purpose

Microsoft.AspNetCore.Authentication.OpenIdConnect is the package of Microsoft that signs people in with an OpenID
Connect provider. It speaks the protocol (discovery, the authorization request, the code exchange) and validates the
identity token (signature, issuer, audience, lifetime, state, and nonce). ReviewCenter uses it so that people can sign in
with a generic provider and so that a user of such an account can confirm their identity for a signature. The package is
not part of the shared framework of ASP.NET Core, so it is a reference of the application project.

### Features Used

- `AddOpenIdConnect` with the options `Authority`, `ClientId`, `ClientSecret`, `ResponseType` (`code`), `UsePkce`,
  `Scope`, `CallbackPath`, `SignInScheme`, `SaveTokens` (off), `MapInboundClaims` (off), and the correlation and nonce
  cookie settings
- The events `OnRedirectToIdentityProvider` (to add `prompt` and `max_age`), `OnMessageReceived`, `OnTicketReceived`, and
  `OnRemoteFailure`
- The claims of the validated identity token, read as sent (including `iss`, `sub`, and `auth_time`)
- The authentication properties, which carry the binding of a confirmation in the protected state of the request

### Integration Pattern

Only the `OidcSignIn` unit uses the package. The unit registers the scheme when `Identity:Oidc:Enabled` is set, and
configures the handler from the validated `OidcOptions` when the scheme is first used. The handler is not allowed to
create the session: it signs in to a short-lived hand-over cookie, and `OidcSignIn` turns the claims into an account and
the normal session cookie, or, for a confirmation of identity, checks them and issues a proof. The handler does not check
`auth_time`, so `OidcSignIn` does. The package has no self-validation mode: the passing tests of `OidcSignIn`, which run
the whole flow against a test provider that signs real tokens, are the integration evidence. An upgrade of the package
that changes the protocol behavior shows as a failing test.
