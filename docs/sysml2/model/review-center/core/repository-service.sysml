package ReviewCenter {
    part def RepositoryService {
        doc /* Rules for the registry of git repositories: registration, editing, enabling and disabling, and removal by administrators, validation of names, locations, and patterns, and protection of access tokens. Also defines the repository store, token protector, and git repository provider contracts and the repository types. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/RepositoryService.cs, SourceRepository.cs, IRepositoryStore.cs, ICredentialProtector.cs, and IGitRepository.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/RepositoryServiceTests.cs */
        comment designRef /* Design: docs/design/review-center/core/repository-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/repository-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/repository-service.yaml */
    }
}
