package ReviewCenter {
    part def ContentSourcesSubsystem {
        doc /* Reading of the registered git repositories of the projects: a safe git child-process runner, the git implementation of the repository provider that reads a bare mirror cache without a working tree, the encryption of access tokens through Data Protection, and the reading of the review-sets of a ReviewMark configuration file. Implements the provider, protector, and parser contracts defined in the Core subsystem. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/ContentSources/ContentSourcesSubsystemTests.cs */
        comment designRef /* Design: docs/design/review-center/content-sources.md */
        comment verificationRef /* Verification: docs/verification/review-center/content-sources.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/content-sources.yaml */

        part gitRunner : GitRunner;
        part gitSource : GitSource;
        part credentialProtector : CredentialProtector;
        part reviewSetParser : ReviewSetParser;
    }
}
