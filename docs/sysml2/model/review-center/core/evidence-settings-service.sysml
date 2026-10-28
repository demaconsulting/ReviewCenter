package ReviewCenter {
    part def EvidenceSettingsService {
        doc /* Reads and changes the evidence settings of a project (anonymous access to the evidence index and the attestation PDFs, and the sections the PDFs contain, private and complete by default) for system administrators and project administrators of the project, with a compare-and-set against the settings the caller started from, an audit entry of the changed keys, and a read without an actor for the services that apply the settings to a request, and defines the settings types shared by the Core subsystem. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/EvidenceSettingsService.cs, EvidenceSettings.cs, Project.cs, and IReviewStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/EvidenceSettingsServiceTests.cs and Storage/EvidenceSettingsStoreTests.cs */
        comment designRef /* Design: docs/design/review-center/core/evidence-settings-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/evidence-settings-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/evidence-settings-service.yaml */
    }
}
