package ReviewCenter {
    doc
    /*
     * ReviewCenter is an ASP.NET Core web service, with a companion .NET command-line
     * interface, for performing formal code reviews. The current implementation
     * provides the command-line scaffold.
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
    }
}
