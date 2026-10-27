package ReviewCenter {
    part def ServerHost {
        doc /* Composition root that builds and runs the ASP.NET Core web application behind the serve verb, including the database, the domain services (including the signing service and the repository registry), the git source, the persisted Data Protection key ring, the signed attestation (its settings, key provider, services, and endpoint, with a start-up check of the signing key), and the authentication pipeline. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Hosting/ServerHost.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Hosting/ServerHostTests.cs */
        comment designRef /* Design: docs/design/review-center/hosting/server-host.md */
        comment verificationRef /* Verification: docs/verification/review-center/hosting/server-host.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/hosting/server-host.yaml */
    }
}
