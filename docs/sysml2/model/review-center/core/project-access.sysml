package ReviewCenter {
    part def ProjectAccess {
        doc /* Decides who may see and who may administer a project from the stored user and project memberships, looked up on every call, giving a project that is not visible the same failure as a project that does not exist, and defines the membership types. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/ProjectAccess.cs, IProjectAccess.cs, and ProjectMembership.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/ProjectAccessTests.cs */
        comment designRef /* Design: docs/design/review-center/core/project-access.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/project-access.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/project-access.yaml */
    }
}
