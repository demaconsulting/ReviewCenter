package ReviewCenter {
    part def ProjectService {
        doc /* Creates projects for system administrators, lists and reads the projects an actor may see, manages the members of a project for project administrators and system administrators (add an existing user by exact user name with a failure throttle, create an account with a one-time temporary password, change a role, remove a member, and keep the last project administrator) with audited changes, switches the per-project review rules (SetReviewRuleAsync for the ReviewRule values: independent reviewers, non-author checklist answers, and non-reopener signing; all on by default; compare-and-set, audited, administrators only, never through a token; the two new rules are stored but not enforced yet), and enforces the project rules, and defines the project and store types shared by the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ProjectService.cs, Project.cs, IReviewStore.cs, DomainRuleException.cs, and ReviewLimits.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ProjectServiceTests.cs, ProjectMemberServiceTests.cs, ProjectAccessTests.cs, and MustChangePasswordTests.cs, and Storage/ProjectMembersStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/core/project-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/project-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/project-service.yaml */
    }
}
