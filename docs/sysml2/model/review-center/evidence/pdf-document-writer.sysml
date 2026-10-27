package ReviewCenter {
    part def PdfDocumentWriter {
        doc /* Writes a plain PDF 1.4 document of A4 pages with wrapped text in standard fonts (none embedded), a header, a footer with page numbers, and document information, using a classic cross-reference table and no compression so that the signer can append an incremental update; the output is deterministic for a fixed input. Also holds the document information and font types it uses. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Evidence/PdfDocumentWriter.cs, PdfDocumentInfo.cs, PdfFont.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Evidence/PdfDocumentWriterTests.cs */
        comment designRef /* Design: docs/design/review-center/evidence/pdf-document-writer.md */
        comment verificationRef /* Verification: docs/verification/review-center/evidence/pdf-document-writer.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/evidence/pdf-document-writer.yaml */
    }
}
