package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ProjectService {
        doc /* Creates projects for system administrators, lists and reads the projects an actor may see, manages the members of a project for project administrators and system administrators (add an existing user by exact user name with a failure throttle, create an account with a one-time temporary password, change a role, remove a member, and keep the last project administrator) with audited changes, switches the per-project review rules (SetReviewRuleAsync for the ReviewRule values: independent reviewers, non-author checklist answers, and non-reopener signing; all on by default; compare-and-set, audited, administrators only, never through a token; the two new rules are enforced by ChecklistService and SigningService and apply to every project, also the ones that existed before), and enforces the project rules, and defines the project and store types shared by the Core subsystem. */

        metadata info : SoftwareInfo {
            description = "Creates projects for system administrators, lists and reads the projects an actor may see, manages the members of a project for project administrators and system administrators (add an existing user by exact user name with a failure throttle, create an account with a one-time temporary password, change a role, remove a member, and keep the last project administrator) with audited changes, switches the per-project review rules (SetReviewRuleAsync for the ReviewRule values: independent reviewers, non-author checklist answers, and non-reopener signing; all on by default; compare-and-set, audited, administrators only, never through a token; the two new rules are enforced by ChecklistService and SigningService and apply to every project, also the ones that existed before), and enforces the project rules, and defines the project and store types shared by the Core subsystem.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/ProjectService.cs", "src/DemaConsulting.ReviewCenter.Core/Project.cs", "src/DemaConsulting.ReviewCenter.Core/IReviewStore.cs", "src/DemaConsulting.ReviewCenter.Core/DomainRuleException.cs", "src/DemaConsulting.ReviewCenter.Core/ReviewLimits.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/ProjectServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/ProjectMemberServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/ProjectAccessTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/MustChangePasswordTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Storage/ProjectMembersStoreTests.cs");
            designFiles = ("docs/design/review-center/core/project-service.md");
            verificationFiles = ("docs/verification/review-center/core/project-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/project-service.yaml");
        }
    }
}
