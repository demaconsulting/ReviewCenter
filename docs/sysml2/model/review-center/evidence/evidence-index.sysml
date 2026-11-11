package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def EvidenceIndexService {
        doc /* Builds the evidence index of a project for an actor who may see the project (a project that is hidden is refused like a missing one), or for a request without an actor only when the project has switched on the anonymous evidence index (otherwise refused the same way), in the form ReviewMark reads: lists the completed reviews that have a review-set id, one entry per review-set id and fingerprint (the latest completion, then the lowest review id), with the date of the latest signature as a UTC day, ordered by id and fingerprint. Skips reviews that are incomplete, concluded as rejected, changed since they were signed, or have no review-set id. Also holds the entry type. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Evidence/EvidenceIndexService.cs", "src/DemaConsulting.ReviewCenter/Evidence/EvidenceIndexEntry.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Evidence/EvidenceIndexServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Evidence/ReviewMarkContractTests.cs");
            designFiles = ("docs/design/review-center/evidence/evidence-index.md");
            verificationFiles = ("docs/verification/review-center/evidence/evidence-index.md");
            requirementFiles = ("docs/reqstream/review-center/evidence/evidence-index.yaml");
        }
    }
}
