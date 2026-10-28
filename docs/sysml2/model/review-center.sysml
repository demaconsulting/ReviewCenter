package ReviewCenter {
    doc
    /*
     * ReviewCenter is an ASP.NET Core web service, with a companion .NET command-line
     * interface, for performing formal code reviews. The current implementation
     * serves a Blazor Server web interface and a health endpoint through the
     * serve verb of the command-line interface, which also provides version, help,
     * user creation, audit trail verification, and self-validation actions. Users
     * sign in with a local account and a password. The web interface manages projects
     * and draft reviews of uploaded files, which are stored in a database together
     * with a content fingerprint of each review. Every change is recorded in a
     * hash-chained audit trail that makes later edits of the history evident.
     * Access is project based: a user sees a project only as a member, a system
     * administrator sees every project, and a hidden project looks like a project
     * that does not exist. The administrators of a project register its git
     * repositories, with access tokens that are stored encrypted, as the sources of
     * review content. A complete review can be
     * downloaded as a digitally signed PDF attestation, signed with a key of the
     * server. Users create personal access tokens, with which scripts call a JSON
     * API under /api, described by an OpenAPI document, to read the projects they
     * may see and their reviews, create draft reviews, and download attestations; a
     * token follows the memberships of its owner and never signs.
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
        part contentSources : ContentSourcesSubsystem;
        part evidence : EvidenceSubsystem;
        part api : ApiSubsystem;
    }
}
