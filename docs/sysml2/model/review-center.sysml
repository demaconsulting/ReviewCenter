package ReviewCenter {
    doc
    /*
     * ReviewCenter is an ASP.NET Core web service, with a companion .NET command-line
     * interface, for performing formal code reviews. The current implementation
     * serves a Blazor Server web interface and a health endpoint through the
     * serve verb of the command-line interface, which also provides version, help,
     * user creation, and self-validation actions. Users sign in with a local account
     * and a password. The web interface manages projects and draft
     * reviews of uploaded files, which are stored in a database together with a
     * content fingerprint of each review.
     */

    part def ReviewCenterSystem {
        doc /* The ReviewCenter system as a whole. */

        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/IntegrationTests.cs */
        comment designRef /* Design: docs/design/review-center.md */
        comment verificationRef /* Verification: docs/verification/review-center.md */
        comment reqRef /* Requirements: docs/reqstream/review-center.yaml */

        part program : Program;
        part cli : CliSubsystem;
        part selfTest : SelfTestSubsystem;
        part utilities : UtilitiesSubsystem;
        part hosting : HostingSubsystem;
        part web : WebSubsystem;
        part identity : IdentitySubsystem;
        part core : CoreSubsystem;
        part storage : StorageSubsystem;
    }
}
