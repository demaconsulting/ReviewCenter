package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def PdfDocumentWriter {
        doc /* Writes a plain PDF 1.5 document of A4 pages with wrapped text in standard fonts (none embedded), a header, a footer with page numbers, and document information, using a classic cross-reference table and no compression so that the signer can append an incremental update; the output is deterministic for a fixed input. Also holds the document information and font types it uses. */

        metadata info : SoftwareInfo {
            description = "Writes a plain PDF 1.5 document of A4 pages with wrapped text in standard fonts (none embedded), a header, a footer with page numbers, and document information, using a classic cross-reference table and no compression so that the signer can append an incremental update; the output is deterministic for a fixed input. Also holds the document information and font types it uses.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Evidence/PdfDocumentWriter.cs", "src/DemaConsulting.ReviewCenter/Evidence/PdfDocumentInfo.cs", "src/DemaConsulting.ReviewCenter/Evidence/PdfFont.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Evidence/PdfDocumentWriterTests.cs");
            designFiles = ("docs/design/review-center/evidence/pdf-document-writer.md");
            verificationFiles = ("docs/verification/review-center/evidence/pdf-document-writer.md");
            requirementFiles = ("docs/reqstream/review-center/evidence/pdf-document-writer.yaml");
        }
    }
}
