package ReviewCenter {
    part def ServerHost {
        doc /* Composition root that builds and runs the ASP.NET Core web application behind the serve verb, including the database, the domain services (including the signing service, the repository service, and the project access rules, which are one shared instance), the git source with validated settings (`Git:TimeoutSeconds` 1 to 3600), the persisted Data Protection key ring (its protection and checks are delegated to KeyRingProtection), the signed attestation (its settings, key provider, services, and endpoint, with a start-up check of the signing key), the authentication pipeline, the start-up validation of the `Identity:Oidc` settings (a failure names the setting, never a value) with the OpenID Connect sign-in registered only when enabled and `/signin-oidc` reserved, the start-up validation of the `Identity:Totp` settings with the services of the second factor registered and the endpoint of its second step mapped only when enabled, the single shared store of the sign-in proofs, the personal access token service, the API routes under /api, and the OpenAPI document. With `ServerHostOptions.IsolatedSettings` it is built from an empty builder that reads nothing but the given settings (used by the self-validation host check). */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/ServerHost.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/ServerHostTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/server-host.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/server-host.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/server-host.yaml */
    }
}
