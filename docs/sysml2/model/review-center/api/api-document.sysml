package ReviewCenter {
    part def ApiDocument {
        doc /* The OpenAPI description of the API: generated from the real routes with the Microsoft.AspNetCore.OpenApi generator, served without a token at /api/openapi.json, identical on every machine and run, and compared in a test with the committed snapshot docs/api/openapi.json so that a change of the contract is visible in review. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Api/ApiDocument.cs and the snapshot docs/api/openapi.json */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Api/ApiDocumentTests.cs */
        comment designRef /* Design: docs/design/review-center/api/api-document.md */
        comment verificationRef /* Verification: docs/verification/review-center/api/api-document.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/api/api-document.yaml */
    }
}
