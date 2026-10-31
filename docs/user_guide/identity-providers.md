# Identity Providers

This chapter helps an administrator connect ReviewCenter to an identity provider: what ReviewCenter needs from the
provider, one recipe each for Microsoft Entra ID, AD FS, Okta and Keycloak, how to test the connection, and what to do
when it fails. The general settings and the behavior of the sign-in are in the Users and Sign-in chapter (see Sign In
with an OpenID Connect Provider); this chapter adds the provider side.

**Every recipe in this chapter is unverified.** They were written from the documentation of the vendors and were not
tested against a running provider. Screens and menu names change from version to version, and a few details are
marked *unverified* where the author could not confirm them. Check each step against the documentation of your
version of the provider, and test the result as described in Testing a Provider. Please report corrections, so that
the recipes can be fixed and marked as verified.

## What ReviewCenter Needs from a Provider

A provider works with ReviewCenter when it gives the first four items below; the fifth item is optional:

1. **A discovery document** at `<Authority>/.well-known/openid-configuration`, where `<Authority>` is the value of
   `Identity:Oidc:Authority`. The `iss` claim of every identity token must be exactly the issuer named in the
   document, and ReviewCenter stores that issuer with the account.
2. **The authorization code flow with PKCE** for a confidential client. ReviewCenter always sends a PKCE challenge,
   and it always has a client secret (`Identity:Oidc:ClientSecret` or `Identity:Oidc:ClientSecretFile`); a public
   client is not supported.
3. **A stable `sub` claim** that is unique for each person at that issuer. Accounts are found by the pair of issuer
   and subject (see Why the Subject Matters).
4. **An `auth_time` claim on a fresh login.** When a person confirms a signature, ReviewCenter sends `prompt=login`
   and `max_age=0` and accepts the answer only when the identity token has an `auth_time` that is a whole number of
   seconds, not older than `Identity:Oidc:ReauthMaxAgeSeconds` and not more than 60 seconds in the future. There is
   no fall back to another claim such as `iat`: without `auth_time`, no signature can be confirmed.
5. **Names (optional).** No name claim is required: the only claims that must be present are `iss` and `sub`, and
   `auth_time` for a signature. The user name of a new account comes from the claim that `Identity:Oidc:UsernameClaim`
   names (default `preferred_username`); when that claim is missing or blank, ReviewCenter uses the part of the
   `email` claim before the `@`, and then `sub`. The display name is the `name` claim, or, when that is missing, the
   same text as the user name. Both are cleaned, and the user name is made unique. ReviewCenter does not read
   `first_name`, `last_name`, `given_name` or `family_name`. The default scopes are `openid profile email`; keep them
   so that the provider sends readable names. The claim `email_verified` is read only when
   `Identity:Oidc:RequireEmailVerified` is `true`. ReviewCenter reads claims from the identity token only and does
   not call the user info endpoint, so every claim that it uses, including `auth_time`, must be in the identity
   token itself.
6. **The redirect address** `https://<server>/signin-oidc` registered exactly as ReviewCenter sends it, where
   `<server>` is the address that people use to reach ReviewCenter.

## Providers at a Glance

| Provider      | `auth_time` default | What to set                 | `max_age=0`    | `prompt=login` | Status     |
| ------------- | ------------------- | --------------------------- | -------------- | -------------- | ---------- |
| Entra ID      | No, optional claim  | Optional claim `auth_time`  | Not documented | Documented     | Unverified |
| AD FS (2019+) | Believed yes        | Check the token, add a rule | Believed       | Believed       | Unverified |
| Okta          | Documented, ID open | Nothing expected            | Documented     | Supported      | Unverified |
| Keycloak      | Believed yes        | Keep the `basic` scope      | Documented     | Documented     | Unverified |

"Documented" means that the vendor documentation says so. "Believed" means that the author expects it but could not
confirm it. "ID open" means that the claim is documented but its presence in the identity token is not
confirmed. The column *Status* is the verification status of the recipe: all four recipes are **unverified**.

## Settings Used in Every Recipe

Every recipe sets the same settings, with different values. They go in the section `Identity:Oidc` of the file
`appsettings.json` or, with two underscores in place of each colon, in environment variables (see Sign In with an
OpenID Connect Provider in the Users and Sign-in chapter). Placeholders are written in angle brackets and must be
replaced. Keep the client secret in the environment or in a secret store, never in the file, and restart the server
after a change.

```json
{
  "Identity": {
    "Oidc": {
      "Enabled": true,
      "Authority": "<authority>",
      "ClientId": "<client-id>",
      "DisplayName": "<name on the button>",
      "UsernameClaim": "<claim>",
      "ReauthMaxAgeSeconds": 120
    }
  }
}
```

```text
Identity__Oidc__Enabled=true
Identity__Oidc__Authority=<authority>
Identity__Oidc__ClientId=<client-id>
Identity__Oidc__ClientSecret=<client-secret>
```

`Scopes` keeps its default (`openid profile email`); it must contain `openid`. `RequireEmailVerified` stays `false`
unless your provider reliably sends `email_verified`. `ReauthMaxAgeSeconds` is the age that the login at the
provider may have when a signature is confirmed (default 120; 10 to 900).

### Keeping the Client Secret in a File

Instead of `Identity__Oidc__ClientSecret`, a recipe may name a file that holds the client secret, with the setting
`Identity:Oidc:ClientSecretFile` (environment variable `Identity__Oidc__ClientSecretFile`). This keeps the secret out
of the settings and of the environment of the process, and fits a file that the operating system or a secret store
provides. Give the secret in one form only: when both `ClientSecret` and `ClientSecretFile` are set, the server does
not start and the message names both settings.

- The file holds the secret as UTF-8 text. The server removes the line break and the spaces around it, so a file
  written with `echo` or an editor works. It may be at most 8 KB.
- The server does not start when the file is missing, cannot be read, or is empty. The message names
  `Identity:Oidc:ClientSecretFile` and the path of the file, and never the secret.
- The file is read when the server starts. After you replace the secret, restart the server.
- Let only the account of the service read the file (for example mode `0400` and the owner of the service on Linux).
  The server does not check the permissions of the file.

With systemd, store the secret in a file that only root can read, and pass it as a credential. The service then finds
it in a private folder whose path is in the variable `CREDENTIALS_DIRECTORY`:

```ini
[Service]
LoadCredential=oidc-client-secret:/etc/reviewcenter/oidc-client-secret
Environment=Identity__Oidc__ClientSecretFile=%d/oidc-client-secret
```

In a container platform that mounts a secret as a file, set the setting to the path of the mounted file, for example
`Identity__Oidc__ClientSecretFile=/run/secrets/oidc-client-secret`.

When the secret is an environment variable instead, use `Identity__Oidc__ClientSecret=<client-secret>` and no file
setting. The configuration of ASP.NET Core reads the variable into the same setting as the key `ClientSecret` of the
file `appsettings.json`.

## Microsoft Entra ID

**Prerequisites.** A tenant, the right to register applications in it, the tenant id (a GUID), and the public
`https` address of ReviewCenter.

**At the provider.**

1. In the Entra admin center, open **App registrations** and choose **New registration**. Choose a single-tenant
   account type and the platform **Web** with the redirect address `https://<server>/signin-oidc`.
2. Open **Certificates & secrets** and create a **client secret**. Copy the value at once; it is shown only once.
   A secret lasts at most 24 months and Microsoft recommends less than 12. Put the expiry date in a calendar, and
   create the next secret before the old one expires.
3. Open **Token configuration**, choose **Add optional claim**, choose the token type **ID**, and tick `auth_time`.
   The same setting in the manifest
   of the application is the list `optionalClaims.idToken` with an entry named `auth_time`; the exact form of that
   JSON is *unverified*.
4. The delegated permissions `openid`, `profile` and `email` are normally granted already; they supply the optional
   names. Do not tick implicit grant or hybrid flow; ReviewCenter does not use them.

**In ReviewCenter.**

```json
{
  "Identity": {
    "Oidc": {
      "Enabled": true,
      "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
      "ClientId": "<application-id>",
      "DisplayName": "Microsoft",
      "UsernameClaim": "preferred_username",
      "ReauthMaxAgeSeconds": 120
    }
  }
}
```

```text
Identity__Oidc__Enabled=true
Identity__Oidc__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0
Identity__Oidc__ClientId=<application-id>
Identity__Oidc__ClientSecret=<client-secret>
Identity__Oidc__DisplayName=Microsoft
Identity__Oidc__UsernameClaim=preferred_username
```

**Test it.** See Testing a Provider. If the confirmation fails with the message that the provider did not report a
usable sign-in time, the optional claim `auth_time` is not set up (or is not sent as a whole number of seconds).

**Pitfalls.**

- Use the address with the tenant id and `/v2.0`. The addresses with `common`, `organizations` or `consumers` name
  an issuer that differs from the issuer in the tokens, so ReviewCenter refuses the tokens.
- The older (v1.0) endpoints and tokens look different. Use the v2.0 endpoint.
- Microsoft documents that `prompt=login` forces the person to enter credentials. Single sign-on features of the
  organization (a primary refresh token, seamless sign-in, a conditional access policy) can still make the login
  quick; whether `auth_time` then shows the new login is *unverified*. If it does not, a confirmation fails as
  `stale-auth-time`.
- For a domain that is federated to AD FS, Microsoft translates `prompt=login` to a fresh-login request for the
  federation, unless the setting `PromptLoginBehavior` of the domain is changed (the documented values include
  `TranslateToFreshPasswordAuth`, `NativeSupport` and `Disabled`). Ask the owner of the tenant how the domain is set.
- Whether Entra honors `max_age` is not stated in the documents that the author read; the recipe relies on
  `prompt=login`.
- The `sub` of Entra ID is believed to be different for the same person in each application registration
  (*unverified*), so a new registration gives new accounts (see Why the Subject Matters). Guest users of another
  tenant have another subject and name claim.
- The client secret expires. After that nobody can sign in with Entra ID until a new secret is set.

**Verification status:** Unverified: written from vendor documentation, please report corrections. The
manifest form of `auth_time` and the pairwise `sub` are *unverified* as well.

## AD FS

**Prerequisites.** AD FS 2019 or later (earlier versions have no PKCE; version 2016 is not covered here), a trusted
TLS certificate, the host name of the AD FS farm, and the public `https` address of ReviewCenter.

**At the provider.**

1. In AD FS Management, open **Application Groups** and choose **Add Application Group**. Choose the template
   **Server application accessing a web API**.
2. Note the **Client Identifier**. Add the redirect address `https://<server>/signin-oidc`.
3. Choose **Generate a shared secret** and copy it at once; it is shown only once.
4. On the page for the web API, use the client identifier as the identifier of the API. Choose an access control
   policy that fits (for example permit everyone, or a group), and tick the scopes `openid`, `profile` and `email`.
5. Optionally add issuance transformation rules that send the claims for the names: send LDAP attributes as claims,
   for example **User-Principal-Name** as `upn` (the recipe uses `upn` as the `UsernameClaim`; without it,
   ReviewCenter falls back to the `email` claim and then to `sub`), **E-Mail-Addresses** as `email` and
   **Display-Name** as `name`. Which claims AD FS sends without a rule is not confirmed by the author.
6. Open the discovery document `https://<adfs-host>/adfs/.well-known/openid-configuration` in a browser and check
   that the issuer is the address that you use as the authority.

**In ReviewCenter.**

```json
{
  "Identity": {
    "Oidc": {
      "Enabled": true,
      "Authority": "https://<adfs-host>/adfs",
      "ClientId": "<client-identifier>",
      "DisplayName": "AD FS",
      "UsernameClaim": "upn",
      "ReauthMaxAgeSeconds": 120
    }
  }
}
```

```text
Identity__Oidc__Enabled=true
Identity__Oidc__Authority=https://<adfs-host>/adfs
Identity__Oidc__ClientId=<client-identifier>
Identity__Oidc__ClientSecret=<client-secret>
Identity__Oidc__DisplayName=AD FS
Identity__Oidc__UsernameClaim=upn
```

**Test it.** See Testing a Provider. Open the identity token of a test login (the audit trail never contains tokens,
so use a tool of your own) and check that it has `auth_time`.

**Pitfalls.**

- Whether AD FS puts `auth_time` in the identity token by default is *unverified*. When it does not, a confirmation
  fails as `missing-auth-time`, and a rule for the claim, or a newer AD FS version, is needed.
- With Windows integrated authentication, AD FS can sign a person in silently, so the login shows a fresh
  `auth_time` without the person typing anything. For this application, require forms-based authentication
  (for example with an access control policy or the authentication methods for access from the internet), so that the
  person
  really proves who they are again.
- Whether AD FS honors `max_age` and `prompt=login` the way ReviewCenter needs is believed from the documentation of
  the protocol (*unverified*).
- Keep the clocks of the AD FS servers and of the ReviewCenter server in sync with a time service (see
  Troubleshooting).
- The shared secret has the lifetime that you chose at the provider. Note the expiry date.

**Verification status:** Unverified: written from vendor documentation, please report corrections. The default
`auth_time` of AD FS is *unverified*.

## Okta

**Prerequisites.** An Okta organization, the right to create applications, and the public `https` address of
ReviewCenter.

**At the provider.**

1. In the Admin Console, open **Applications**, choose **Create App Integration**, choose **OIDC - OpenID Connect**
   and the application type **Web Application**.
2. Keep the grant type **Authorization Code**. Add the sign-in redirect address `https://<server>/signin-oidc`.
   Choose client authentication with a client secret. If the console offers **Require PKCE as additional
   verification**, you may tick it; ReviewCenter always sends PKCE.
3. Under **Assignments**, assign the people or groups who may sign in. A person who is not assigned cannot sign in.
4. Copy the **Client ID** and the **Client secret**.
5. Decide which authorization server issues the tokens. The org authorization server has the issuer
   `https://<okta-domain>`; a custom one has `https://<okta-domain>/oauth2/default` or
   `https://<okta-domain>/oauth2/<server-id>`. Open the `.well-known/openid-configuration` document of the server and
   copy its `issuer` exactly.

**In ReviewCenter.**

```json
{
  "Identity": {
    "Oidc": {
      "Enabled": true,
      "Authority": "https://<okta-domain>/oauth2/default",
      "ClientId": "<client-id>",
      "DisplayName": "Okta",
      "UsernameClaim": "preferred_username",
      "ReauthMaxAgeSeconds": 120
    }
  }
}
```

```text
Identity__Oidc__Enabled=true
Identity__Oidc__Authority=https://<okta-domain>/oauth2/default
Identity__Oidc__ClientId=<client-id>
Identity__Oidc__ClientSecret=<client-secret>
Identity__Oidc__DisplayName=Okta
Identity__Oidc__UsernameClaim=preferred_username
```

**Test it.** See Testing a Provider.

**Pitfalls.**

- The authority must be exactly the issuer of the authorization server that you chose: the org server and a custom
  server have different issuers, and changing from one to the other gives new accounts (see Why the Subject Matters).
- Okta documents `auth_time` as a claim; whether it is in the **identity token** (and not only in the access token)
  is *unverified*. If a confirmation fails as `missing-auth-time`, check the token that Okta sends.
- Okta documents `max_age=0` for step-up authentication and notes that the Classic Engine needs a small positive
  value such as `max_age=1`. ReviewCenter sends `max_age=0` and `prompt=login`; whether that forces a new login in a
  Classic Engine organization is *unverified*.
- A session policy of the organization can decide how the new login is done. Test with the policy that people will
  have.
- A person who is not assigned to the application gets an error at the provider, and ReviewCenter records only
  `provider-error`.

**Verification status:** Unverified: written from vendor documentation, please report corrections. The `auth_time`
claim in the identity token of Okta is *unverified*.

## Keycloak

**Prerequisites.** A running Keycloak with a realm for the people, a public host name that is configured in Keycloak
(see the pitfalls), and the public `https` address of ReviewCenter. ReviewCenter does not need an e-mail address, a
first name or a last name; the user name of the realm is sent as `preferred_username`. Keycloak is believed to build
`name` from the first and last name when the person has them (*unverified*); without them, the display name falls
back to the user name.

**At the provider.**

1. In the admin console, choose the realm, then **Clients** and **Create client**. Choose the client type **OpenID
   Connect** and give a client id.
2. Turn **Client authentication** on and keep only **Standard flow** enabled.
3. Set the valid redirect address to `https://<server>/signin-oidc`.
4. On the tab **Credentials**, copy the **Client secret**.
5. Optionally set **Proof Key for Code Exchange Code Challenge Method** to `S256` under the advanced settings;
   ReviewCenter always sends PKCE.
6. Keep the client scope `basic` among the default scopes of the client. It holds the mapper for `sub`, and
   the mapper for `auth_time` is believed to be there too (*unverified*).

**In ReviewCenter.**

```json
{
  "Identity": {
    "Oidc": {
      "Enabled": true,
      "Authority": "https://<keycloak-host>/realms/<realm>",
      "ClientId": "<client-id>",
      "DisplayName": "Keycloak",
      "UsernameClaim": "preferred_username",
      "ReauthMaxAgeSeconds": 120
    }
  }
}
```

```text
Identity__Oidc__Enabled=true
Identity__Oidc__Authority=https://<keycloak-host>/realms/<realm>
Identity__Oidc__ClientId=<client-id>
Identity__Oidc__ClientSecret=<client-secret>
Identity__Oidc__DisplayName=Keycloak
Identity__Oidc__UsernameClaim=preferred_username
```

**Test it.** See Testing a Provider.

**Pitfalls.**

- The issuer in the tokens comes from the host name that Keycloak believes it has. Behind a reverse proxy, configure
  the host name (or the proxy headers) of Keycloak so that the issuer is the same address every time. An issuer that
  changes makes every existing account fail as `issuer-mismatch`.
- A client whose default scopes do not include `basic`, or a realm with changed mappers, may send no `auth_time` or no
  `sub`. The documentation that the author read does not name the `auth_time` mapper, so it is *unverified*.
- Keycloak documents that it honors `max_age` and `prompt=login`. The idle time and lifetime of the single sign-on
  session of the realm should not matter for a login that is forced, but this is *unverified*.
- The realm name and the client are part of the identity. Moving people to another realm gives new accounts (see Why
  the Subject Matters).

**Verification status:** Unverified: written from vendor documentation, please report corrections. The mapper for
`auth_time` is *unverified*.

## Testing a Provider

The same test works for every provider and proves the part that is most often wrong, `auth_time`:

1. Sign in with the button of the provider. ReviewCenter creates an account at the first sign-in, and a project
   administrator adds it to a project (see Managing Project Members in the Administration chapter).
2. Open a review of that project in which the account has a reviewer slot, and choose **Confirm identity with**
   followed by the name that you set as `DisplayName`.
3. The provider must ask for the credentials again, even though the person has just signed in. If it does not, the
   provider has reused its session, and the confirmation may fail as `stale-auth-time`.
4. After the person returns, the review page says "Identity confirmed. Review the record, then sign." The page
   then offers the signature. Sign the slot. This proves that the login was fresh and that `auth_time` arrived.
5. If the page shows a failure instead, read the message and see Troubleshooting. The cause is also in the audit
   trail (see The Audit Page in the Administration chapter) as the action `signature.reauth-failed`, with a detail
   such as `oidc: missing-auth-time`.

Test with a review that you can afford to sign, or use a test project. A signature is a record that cannot be taken
back.

## Troubleshooting

These are the reasons that ReviewCenter records. A failed confirmation of a signature is recorded as
`signature.reauth-failed` with the detail `oidc: <reason>`; a failed sign-in is recorded as
`auth.oidc-sign-in-failed` with the reason. The review page tells the person the cause only for the first two
reasons below; for every other reason it shows a general message so that it does not help an attacker.

- **`missing-auth-time`.** The identity token has no `auth_time`, or a value that is not a whole number of seconds
  since 1970 that a date can hold. The review page says that the provider did not report a usable sign-in time
  and asks an administrator to check that `auth_time` is sent as a whole number of seconds since 1970. The cause
  is in the settings of the provider: add the optional claim (Entra), check the claims and rules (AD FS), the token
  (Okta), or the mappers and scopes (Keycloak). ReviewCenter does not guess the time from another claim.
- **`stale-auth-time`.** The login is older than `ReauthMaxAgeSeconds`, or more than 60 seconds in the future.
  Causes: the provider reused its session instead of asking again (so the login is old); the clock of the provider
  and the clock of the ReviewCenter server differ; the person took longer than the allowed age at the provider.
  Synchronize the clocks of the provider and of the ReviewCenter server with a time service, force a new login at
  the provider, or raise `ReauthMaxAgeSeconds` within 10 to 900.
- **`subject-mismatch`.** The person signed in at the provider as someone else than the account that asked. Sign out
  at the provider and confirm with the right account.
- **`issuer-mismatch`.** The issuer of the token is not the issuer stored with the account. The authority, the
  tenant, the realm, the authorization server or the public host name of the provider changed. Restore the old
  address, or see Why the Subject Matters. Behind a reverse proxy, check that the provider reports one fixed address
  as its issuer.
- **`not-external`, `other`.** The account is a local account, or the confirmation does not belong to the request
  (for example the browser was changed between the start and the return). Start the confirmation again from the
  review page.
- **`provider-error`, `state-or-nonce`, `token-invalid`.** The provider answered with an error (for example the
  person is not assigned to the application), the answer does not belong to the sign-in that was started (often a
  lost cookie, see the next item), or the identity token was refused (wrong issuer, audience or lifetime, or a large
  clock difference between the provider and the ReviewCenter server).
- **A wrong redirect address.** The provider shows its own error page and ReviewCenter never sees the request, so
  there is no audit entry. The address registered at the provider must be exactly
  `https://<server>/signin-oidc`, with the same scheme, host name and port that people use. Behind a reverse proxy
  that ends TLS, ReviewCenter must know the original scheme; see Secure Cookie Behind a Reverse Proxy in the
  Deployment chapter. Otherwise it sends an `http` address, or the cookie is lost, and the sign-in fails.
- **An expired or wrong client secret.** The provider refuses the exchange of the code, and the sign-in fails
  (recorded as `provider-error` or `other`). Create a new secret at the provider, set `Identity__Oidc__ClientSecret`
  (or replace the contents of the file named by `Identity__Oidc__ClientSecretFile`) and restart the server.
- **The server does not start.** A setting is not valid. The message names the setting and never shows its value. For
  the client secret this includes a secret set both inline and in a file, and a secret file that is missing, empty,
  unreadable, or larger than 8 KB.
- **Clock skew.** ReviewCenter compares the `auth_time` of the provider and the lifetime of the identity token with
  the clock of its own server. The clock of the person's computer or browser is not used. A large difference between
  the clock of the provider and the clock of the ReviewCenter server causes `stale-auth-time` or `token-invalid`.
  Keep both synchronized with a time service (NTP, or Windows Time on Windows).

## Why the Subject Matters

ReviewCenter finds an account only by the **issuer and the subject** (`iss` and `sub`) of the identity token, never by
a name or an e-mail address. This protects the accounts: someone who gets the same name at the provider does not get
the account.

Everything that changes the issuer or the subject therefore gives the same people **new identities**, and so new
accounts: a new application registration (some providers give each application its own subject), another tenant,
realm, domain or authorization server, or a changed authority. The new accounts are empty. They are in no project
and hold no slot. The previous accounts remain with their audit history and their signatures, but nobody can sign in
to them any more.

Plan the registration before people use it, and avoid changing it. If a change cannot be avoided, a system
administrator disables the previous accounts, and a project administrator adds the new accounts to the projects and
to the open reviews (see Managing Project Members in the Administration chapter).

## Reporting Corrections

The recipes are unverified. When a step, a name or a value is wrong for your version of a provider, or when you have
tested a provider end to end, please report it as an issue of the ReviewCenter project, with the provider, its
version, and what you did.
