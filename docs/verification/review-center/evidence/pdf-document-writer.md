### PdfDocumentWriter

#### Verification Approach

`PdfDocumentWriter` is verified with tests defined in `PdfDocumentWriterTests.cs`. The unit has no dependency, so the
tests use the real class and read the bytes it writes back with `PdfInspector`, a small reader of the PDF structure that
is a helper of the tests: it finds the objects, the page count, the metadata, and the text that is drawn.

#### Test Environment

N/A - standard test environment (everything runs in memory).

#### Acceptance Criteria

- All tests pass with zero failures.
- An empty document is a valid one-page PDF 1.5 file, and a long body continues onto numbered pages.
- The same input gives the same bytes.
- Text is wrapped at word boundaries, an over-wide word is broken, line feeds start lines, and the value of a row wraps
  beside its label.
- Parentheses and the backslash survive, and characters outside WinAnsi become question marks.

#### Test Scenarios

**PdfDocumentWriter_Build_EmptyDocument_HasOnePageAndPdfFraming**: A document without content is built. It starts with the
PDF header, ends with the end-of-file marker, has a cross-reference table, and has one page with the header and the
footer. This scenario is tested by `PdfDocumentWriter_Build_EmptyDocument_HasOnePageAndPdfFraming`.

**PdfDocumentWriter_Build_SameInput_ProducesIdenticalBytes**: The same calls are made twice. The bytes are equal. This
scenario is tested by `PdfDocumentWriter_Build_SameInput_ProducesIdenticalBytes`.

**PdfDocumentWriter_Build_LongBody_BreaksIntoNumberedPages**: A body that is longer than a page is written. There are
several pages and each footer shows its number and the total. This scenario is tested by
`PdfDocumentWriter_Build_LongBody_BreaksIntoNumberedPages`.

**PdfDocumentWriter_Paragraph_LongText_WrapsAtWordsWithinTheTextWidth**: A long text is written as a paragraph. No
line is
wider than the text width and the lines end at words. This scenario is tested by
`PdfDocumentWriter_Paragraph_LongText_WrapsAtWordsWithinTheTextWidth`.

**PdfDocumentWriter_Paragraph_WordWiderThanTheLine_IsBrokenBetweenCharacters**: A word without spaces that is wider
than a
line is written. It is broken into lines that fit, and no character is lost. This scenario is tested by
`PdfDocumentWriter_Paragraph_WordWiderThanTheLine_IsBrokenBetweenCharacters`.

**PdfDocumentWriter_Paragraph_LineFeeds_StartNewLines**: A text with line feeds is written. Each line feed starts a new
line. This scenario is tested by `PdfDocumentWriter_Paragraph_LineFeeds_StartNewLines`.

**PdfDocumentWriter_Paragraph_ParenthesesAndBackslash_AreEscapedAndReadBackIntact**: A text with parentheses and a
backslash is written. The file stays well formed and the reader returns the text unchanged. This scenario is tested by
`PdfDocumentWriter_Paragraph_ParenthesesAndBackslash_AreEscapedAndReadBackIntact`.

**PdfDocumentWriter_ToWinAnsi_Text_KeepsWinAnsiAndReplacesTheRest**: Text with letters of the WinAnsi encoding and other
characters is converted. The first are kept and the others become question marks. This scenario is tested by
`PdfDocumentWriter_ToWinAnsi_Text_KeepsWinAnsiAndReplacesTheRest`.

**PdfDocumentWriter_Paragraph_UnicodeBeyondWinAnsi_IsShownAsQuestionMarks**: A paragraph with characters beyond
WinAnsi is
written. The document shows question marks for them. This scenario is tested by
`PdfDocumentWriter_Paragraph_UnicodeBeyondWinAnsi_IsShownAsQuestionMarks`.

**PdfDocumentWriter_Build_Info_RecordsMetadata**: A document is built with title, subject, author, keywords, and a
creation time. The information dictionary holds them. This scenario is tested by
`PdfDocumentWriter_Build_Info_RecordsMetadata`.

**PdfDocumentWriter_Row_LongValue_WrapsBesideTheLabel**: A row with a long value is written. The value wraps in its
column beside the label and does not run into it. This scenario is tested by
`PdfDocumentWriter_Row_LongValue_WrapsBesideTheLabel`.
