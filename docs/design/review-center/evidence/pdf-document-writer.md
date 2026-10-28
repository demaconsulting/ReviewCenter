### PdfDocumentWriter

![Evidence Structure](EvidenceView.svg)

#### Purpose

`PdfDocumentWriter` writes a small, plain PDF 1.5 document. Its single responsibility is to lay out titles, headings,
paragraphs, and label and value rows on A4 pages, with a header and a footer that carries the page number, and to
return the bytes of the file. It has no dependency, so the attestation does not need a PDF library.

#### Data Model

The writer is created with a `PdfDocumentInfo` (title, subject, author, keywords, and creation time, which become the
document properties) and the text of the header and the footer. It keeps the pages built so far: for each page the
drawing instructions of the text, and a cursor that moves down the page. The page is A4 (595 by 842 points) with side
margins of 54 points and top and bottom margins of 64 points. Five fonts are used, all of the standard fonts that every
reader has (Helvetica, Helvetica-Bold, Helvetica-Oblique, Courier, and Courier-Bold; `PdfFont`), so no font is embedded.
The width of a character is taken from fixed tables, because the writer must wrap text without a font library. The
file has a classic cross-reference table, no compression, and no object streams, which keeps the layout simple and
lets `AttestationSigner` add a signature as an incremental update. The same calls always give the same bytes: there is
no random value and no use of the clock beyond the creation time that the caller supplies.

#### Key Methods

**Title, Heading, Paragraph, Row, Rule, Space**: Add content at the cursor.

- *Parameters*: The text, and for a paragraph the font, size, and spacing; for a row a label and a value, which wraps
  beside the label.
- *Returns*: Nothing.
- *Preconditions*: The text is not null.
- *Postconditions*: The content is on the current page; when it does not fit, a new page is started. Lines break at
  word boundaries within the width of the text; a word wider than a line is broken between characters; a line feed starts
  a new line.

**Build**: Finishes the document.

- *Parameters*: None.
- *Returns*: `byte[]` — the PDF file, with at least one page.
- *Postconditions*: Every page has the header, the footer, and "Page n of m".

**ToWinAnsi and EncodeTextString**: Convert text.

- *Parameters*: `string text`.
- *Returns*: `ToWinAnsi` keeps the characters of the WinAnsi encoding of the standard fonts and replaces every other
  character by a question mark; `EncodeTextString` encodes a document property for the information dictionary.
- *Postconditions*: Parentheses and the backslash are escaped where text is written, so a text reads back unchanged.

#### Error Handling

A null text raises `ArgumentNullException`. A character that the encoding does not have is not an error: it is shown as
a
question mark, so a reviewer name or a file name never corrupts the file. The writer does not validate the layout it
makes beyond that; it is tested by reading the output back.

#### Dependencies

- **PdfFont**, **PdfDocumentInfo** — the fonts and the document properties.
- No other unit and no library.

#### Callers

- **AttestationRenderer** — builds the attestation with it.
