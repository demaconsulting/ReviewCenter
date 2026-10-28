package ReviewCenter {
    part def EvidenceIndexService {
        doc /* Builds the evidence index of a project for an actor who may see the project (a project that is hidden is refused like a missing one) in the form ReviewMark reads: lists the completed reviews that have a review-set id, one entry per review-set id and fingerprint (the latest completion, then the lowest review id), with the date of the latest signature as a UTC day, ordered by id and fingerprint. Skips reviews that are incomplete, changed since they were signed, or have no review-set id. Also holds the entry type. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/EvidenceIndexService.cs, EvidenceIndexEntry.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/EvidenceIndexServiceTests.cs, ReviewMarkContractTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/evidence-index.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/evidence-index.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/evidence-index.yaml */
    }
}
