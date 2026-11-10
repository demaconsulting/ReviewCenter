package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ReviewPlanner {
        doc /* Plans the reviews of a head revision of a registered repository from the review-sets of its configuration file: chooses a baseline for each set (none, one given revision, or the head of the last approved review of the set when it is an ancestor), classifies each set, and creates the picked reviews one by one with generated identifiers and titles. Only an actor who may see the project can plan or create, and the repository must belong to the project. */

        metadata info : SoftwareInfo {
            description = "Plans the reviews of a head revision of a registered repository from the review-sets of its configuration file: chooses a baseline for each set (none, one given revision, or the head of the last approved review of the set when it is an ancestor), classifies each set, and creates the picked reviews one by one with generated identifiers and titles. Only an actor who may see the project can plan or create, and the repository must belong to the project.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/ReviewPlanner.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/ReviewPlannerTests.cs");
            designFiles = ("docs/design/review-center/core/review-planner.md");
            verificationFiles = ("docs/verification/review-center/core/review-planner.md");
            requirementFiles = ("docs/reqstream/review-center/core/review-planner.yaml");
        }
    }
}
