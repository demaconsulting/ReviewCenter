package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def ApiDocument {
        doc /* The OpenAPI description of the API: generated from the real routes with the Microsoft.AspNetCore.OpenApi generator, served without a token at /api/openapi.json, identical on every machine and run, names the bearer and Basic schemes with an additional empty (anonymous) security alternative on exactly the evidence index and the attestation operations, and is compared in a test with the committed snapshot docs/api/openapi.json so that a change of the contract is visible in review. */

        // Data artifact: the tests compare the generated document with the committed snapshot docs/api/openapi.json.
        metadata info : SoftwareInfo {
            description = "The OpenAPI description of the API: generated from the real routes with the Microsoft.AspNetCore.OpenApi generator, served without a token at /api/openapi.json, identical on every machine and run, names the bearer and Basic schemes with an additional empty (anonymous) security alternative on exactly the evidence index and the attestation operations, and is compared in a test with the committed snapshot docs/api/openapi.json so that a change of the contract is visible in review.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Api/ApiDocument.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Api/ApiDocumentTests.cs");
            designFiles = ("docs/design/review-center/api/api-document.md");
            verificationFiles = ("docs/verification/review-center/api/api-document.md");
            requirementFiles = ("docs/reqstream/review-center/api/api-document.yaml");
        }
    }
}
