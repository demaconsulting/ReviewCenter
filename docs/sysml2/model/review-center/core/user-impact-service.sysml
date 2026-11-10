package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def UserImpactService {
        doc /* Answers for a user who may leave which reviewer slots of unlocked reviews still wait for the user (as primary or alternate, not signed or out of date, by the signature rule of the inbox) and in which projects the user is the only enabled project administrator, to a system administrator for every project and to a project administrator for the projects the asker administers, and refuses every other actor. */

        metadata info : SoftwareInfo {
            description = "Answers for a user who may leave which reviewer slots of unlocked reviews still wait for the user (as primary or alternate, not signed or out of date, by the signature rule of the inbox) and in which projects the user is the only enabled project administrator, to a system administrator for every project and to a project administrator for the projects the asker administers, and refuses every other actor.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/UserImpactService.cs", "src/DemaConsulting.ReviewCenter.Core/UserImpact.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/UserImpactServiceTests.cs");
            designFiles = ("docs/design/review-center/core/user-impact-service.md");
            verificationFiles = ("docs/verification/review-center/core/user-impact-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/user-impact-service.yaml");
        }
    }
}
