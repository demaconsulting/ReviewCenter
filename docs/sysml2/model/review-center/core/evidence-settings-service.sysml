package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def EvidenceSettingsService {
        doc /* Reads and changes the evidence settings of a project (anonymous access to the evidence index and the attestation PDFs, and the sections the PDFs contain, private and complete by default) for system administrators and project administrators of the project, with a compare-and-set against the settings the caller started from, an audit entry of the changed keys, and a read without an actor for the services that apply the settings to a request, and defines the settings types shared by the Core subsystem. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/EvidenceSettingsService.cs", "src/DemaConsulting.ReviewCenter.Core/EvidenceSettings.cs", "src/DemaConsulting.ReviewCenter.Core/Project.cs", "src/DemaConsulting.ReviewCenter.Core/IReviewStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/EvidenceSettingsServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Storage/EvidenceSettingsStoreTests.cs");
            designFiles = ("docs/design/review-center/core/evidence-settings-service.md");
            verificationFiles = ("docs/verification/review-center/core/evidence-settings-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/evidence-settings-service.yaml");
        }
    }
}
