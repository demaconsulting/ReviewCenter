package ReviewCenter {
    part def ProjectAccess {
        doc /* Decides who may see, work in, administer, and audit a project from the stored user and project memberships, looked up on every call (an undefined stored role fails closed as read-only, and the declared but not yet assignable Auditor role is read-only), giving a project that is not visible the same failure as a project that does not exist, treats a user with a temporary password as having no access, and defines the membership types. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ProjectAccess.cs, IProjectAccess.cs, and ProjectMembership.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ProjectAccessTests.cs and ProjectAccessGuardTests.cs */
        comment designRef /* Design: docs/design/review-center/core/project-access.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/project-access.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/project-access.yaml */
    }
}
