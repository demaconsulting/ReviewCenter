package ReviewCenter {
    part def RepositoryService {
        doc /* Rules for the registry of git repositories of a project: registration, editing, and enabling and disabling by the administrators of the project (a repository belongs to exactly one project and is never deleted), validation of names, locations, user names, and tokens, and protection of access tokens. Also defines the repository store, token protector, and git repository provider contracts and the repository types. File selection patterns are validated by SelectionMatcher, not here. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/RepositoryService.cs, SourceRepository.cs, IRepositoryStore.cs, ICredentialProtector.cs, and IGitRepository.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/RepositoryServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/repository-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/repository-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/repository-service.yaml */
    }
}
