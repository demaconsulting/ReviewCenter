package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def Fingerprint {
        doc /* Calculates per-file SHA-256 hashes and the order-independent content fingerprint of a set of files, as ReviewMark does. */

        metadata info : SoftwareInfo {
            description = "Calculates per-file SHA-256 hashes and the order-independent content fingerprint of a set of files, as ReviewMark does.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/Fingerprint.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/FingerprintTests.cs");
            designFiles = ("docs/design/review-center/core/fingerprint.md");
            verificationFiles = ("docs/verification/review-center/core/fingerprint.md");
            requirementFiles = ("docs/reqstream/review-center/core/fingerprint.yaml");
        }
    }
}
