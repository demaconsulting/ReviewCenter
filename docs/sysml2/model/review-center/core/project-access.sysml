package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ProjectAccess {
        doc /* Decides who may see, work in, administer, and audit a project from the stored user and project memberships, looked up on every call (an undefined stored role fails closed as no access, and the Auditor role is read-only and can read the audit trail), giving a project that is not visible the same failure as a project that does not exist, treats a user with a temporary password as having no access, and defines the membership types. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/ProjectAccess.cs", "src/DemaConsulting.ReviewCenter.Core/IProjectAccess.cs", "src/DemaConsulting.ReviewCenter.Core/ProjectMembership.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/ProjectAccessTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/ProjectAccessGuardTests.cs");
            designFiles = ("docs/design/review-center/core/project-access.md");
            verificationFiles = ("docs/verification/review-center/core/project-access.md");
            requirementFiles = ("docs/reqstream/review-center/core/project-access.yaml");
        }
    }
}
