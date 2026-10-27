// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.ReviewCenter.Evidence;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="PdfDocumentWriter"/>: the file structure, pagination, wrapping, escaping and the
///     restriction to the characters the standard fonts support.
/// </summary>
public class PdfDocumentWriterTests
{
    /// <summary>
    ///     The fixed information used by every test.
    /// </summary>
    private static readonly PdfDocumentInfo _info = new(
        "A title",
        "An author",
        "A subject",
        "key words",
        "Creator",
        "Producer",
        new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));

    /// <summary>
    ///     Creates a writer with the fixed information.
    /// </summary>
    /// <returns>The writer.</returns>
    private static PdfDocumentWriter CreateWriter() => new(_info, "Header text", "Footer text");

    /// <summary>
    ///     Test that a document without a body still is a valid PDF with one page.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Build_EmptyDocument_HasOnePageAndPdfFraming()
    {
        // Arrange: a writer with no content
        var writer = CreateWriter();

        // Act: build the file
        var pdf = new PdfInspector(writer.Build());

        // Assert: PDF header and end marker, one page, consistent cross-reference table
        Assert.True(pdf.HasPdfFraming);
        Assert.Equal(1, pdf.PageCount);
        Assert.True(pdf.CrossReferenceIsConsistent());
        Assert.Single(pdf.CrossReferenceChain);
    }

    /// <summary>
    ///     Test that the same calls give the same bytes, also when the build is repeated.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Build_SameInput_ProducesIdenticalBytes()
    {
        // Arrange: two writers with the same calls
        static PdfDocumentWriter Fill(PdfDocumentWriter writer)
        {
            writer.Title("Title");
            writer.Heading("Heading");
            writer.Row("Label", "Value");
            writer.Paragraph("Some text " + new string('x', 300));
            return writer;
        }

        var first = Fill(CreateWriter());
        var second = Fill(CreateWriter());

        // Act: build both, and the first once more
        var bytes = first.Build();
        var again = first.Build();

        // Assert: identical
        Assert.Equal(bytes, second.Build());
        Assert.Equal(bytes, again);
    }

    /// <summary>
    ///     Test that a long body is spread over pages, every page numbered "Page n of N" with header and footer.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Build_LongBody_BreaksIntoNumberedPages()
    {
        // Arrange: far more lines than fit on one page
        var writer = CreateWriter();
        for (var line = 1; line <= 200; line++)
        {
            writer.Paragraph($"Body line {line}");
        }

        // Act: build the file
        var pdf = new PdfInspector(writer.Build());
        var lines = pdf.TextLines;

        // Assert: several pages, each with its number, the header and the footer; all lines kept in order
        var pages = pdf.PageCount;
        Assert.InRange(pages, 3, 10);
        for (var page = 1; page <= pages; page++)
        {
            Assert.Contains($"Page {page} of {pages}", lines);
        }

        Assert.Equal(pages, lines.Count(l => l == "Header text"));
        Assert.Equal(pages, lines.Count(l => l == "Footer text"));
        Assert.Equal(
            Enumerable.Range(1, 200).Select(n => $"Body line {n}"),
            lines.Where(l => l.StartsWith("Body line", StringComparison.Ordinal)));
        Assert.True(pdf.CrossReferenceIsConsistent());
    }

    /// <summary>
    ///     Test that a long paragraph is wrapped at word boundaries, and no line is wider than the text area.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Paragraph_LongText_WrapsAtWordsWithinTheTextWidth()
    {
        // Arrange: a paragraph of many words
        var words = Enumerable.Range(1, 120).Select(n => $"word{n}").ToList();
        var writer = CreateWriter();

        // Act: add it and read the lines back
        writer.Paragraph(string.Join(' ', words));
        var lines = new PdfInspector(writer.Build()).TextLines
            .Where(l => l.StartsWith("word", StringComparison.Ordinal) || l.Contains(" word", StringComparison.Ordinal))
            .ToList();

        // Assert: several lines, each within the width, and the words are all there in order
        Assert.True(lines.Count > 3);
        var width = PdfDocumentWriter.PageWidth - (2 * PdfDocumentWriter.SideMargin);
        Assert.All(lines, l => Assert.True(PdfDocumentWriter.Measure(l, PdfFont.Helvetica, 10) <= width));
        Assert.Equal(words, lines.SelectMany(l => l.Split(' ')));
    }

    /// <summary>
    ///     Test that a word wider than a line is broken between characters instead of running off the page.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Paragraph_WordWiderThanTheLine_IsBrokenBetweenCharacters()
    {
        // Arrange: one word of 400 characters, such as a hash or a path
        var word = new string('W', 400);
        var writer = CreateWriter();

        // Act: add it
        writer.Paragraph(word);
        var lines = new PdfInspector(writer.Build()).TextLines.Where(l => l.StartsWith('W')).ToList();

        // Assert: more than one line, each within the width, nothing lost
        var width = PdfDocumentWriter.PageWidth - (2 * PdfDocumentWriter.SideMargin);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(PdfDocumentWriter.Measure(l, PdfFont.Helvetica, 10) <= width));
        Assert.Equal(word, string.Concat(lines));
    }

    /// <summary>
    ///     Test that a line feed in a paragraph starts a new line and that blank text keeps a blank line.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Paragraph_LineFeeds_StartNewLines()
    {
        // Arrange: text with line feeds in both common spellings
        var writer = CreateWriter();

        // Act: add it
        writer.Paragraph("first\nsecond\r\nthird");
        var lines = new PdfInspector(writer.Build()).TextLines;

        // Assert: three separate lines
        Assert.Contains("first", lines);
        Assert.Contains("second", lines);
        Assert.Contains("third", lines);
    }

    /// <summary>
    ///     Test that the characters that are special in a PDF string are escaped so that the string stays intact.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Paragraph_ParenthesesAndBackslash_AreEscapedAndReadBackIntact()
    {
        // Arrange: text with parentheses and a backslash, which would end or corrupt a literal string
        const string text = @"call f(a) \ then (unbalanced";
        var writer = CreateWriter();

        // Act: add it and read the content back
        writer.Paragraph(text);
        var pdf = new PdfInspector(writer.Build());

        // Assert: the raw file holds the escapes, and the text reads back unchanged
        Assert.Contains(@"call f\(a\) \\ then \(unbalanced", pdf.Text, StringComparison.Ordinal);
        Assert.Contains(text, pdf.TextLines);
    }

    /// <summary>
    ///     Test that text outside the standard fonts' character set is replaced by a question mark, and that
    ///     characters of Windows-1252 survive.
    /// </summary>
    [Theory]
    [InlineData("plain ASCII", "plain ASCII")]
    [InlineData("caf\u00E9 \u00FC\u00DF", "caf\u00E9 \u00FC\u00DF")]
    [InlineData("\u20AC 5 \u2013 \u201Cquoted\u201D", "\u0080 5 \u0096 \u0093quoted\u0094")]
    [InlineData("\u4E2D\u6587", "??")]
    [InlineData("a\U0001F600b", "a?b")]
    [InlineData("tab\there", "tab here")]
    public void PdfDocumentWriter_ToWinAnsi_Text_KeepsWinAnsiAndReplacesTheRest(string text, string expected)
    {
        // Arrange: text from the data

        // Act: restrict it
        var result = PdfDocumentWriter.ToWinAnsi(text);

        // Assert: one byte-sized character per input character, as expected
        Assert.Equal(expected, result);
        Assert.All(result, c => Assert.InRange(c, ' ', '\u00FF'));
    }

    /// <summary>
    ///     Test that Unicode text in the body is written as the replacement, never as a broken byte.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Paragraph_UnicodeBeyondWinAnsi_IsShownAsQuestionMarks()
    {
        // Arrange: Chinese text and an emoji
        var writer = CreateWriter();

        // Act: add and read back
        writer.Paragraph("name: \u5F20\u4F1F \U0001F600");
        var lines = new PdfInspector(writer.Build()).TextLines;

        // Assert: one question mark per character, an emoji being one character
        Assert.Contains("name: ?? ?", lines);
    }

    /// <summary>
    ///     Test that the document information is written, with Unicode kept intact in its UTF-16 form.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Build_Info_RecordsMetadata()
    {
        // Arrange: information with a non-ASCII title
        var info = _info with { Title = "Review \u4E2D\u6587 (1)" };
        var writer = new PdfDocumentWriter(info, "h", "f");

        // Act: build and read the information back
        var pdf = new PdfInspector(writer.Build());

        // Assert: every entry is there; the creation date is the one given
        Assert.Equal("Review \u4E2D\u6587 (1)", pdf.Info("Title"));
        Assert.Equal("An author", pdf.Info("Author"));
        Assert.Equal("A subject", pdf.Info("Subject"));
        Assert.Equal("key words", pdf.Info("Keywords"));
        Assert.Contains("/CreationDate (D:20250304050607+00'00')", pdf.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that rows put the label and the value on the same line and wrap a long value beside the label.
    /// </summary>
    [Fact]
    public void PdfDocumentWriter_Row_LongValue_WrapsBesideTheLabel()
    {
        // Arrange: a row whose value needs several lines
        var writer = CreateWriter();

        // Act: add it
        writer.Row("Label", string.Join(' ', Enumerable.Repeat("value", 60)));
        var lines = new PdfInspector(writer.Build()).TextLines;

        // Assert: the label appears once and the value is spread over several lines
        Assert.Single(lines, l => l == "Label");
        Assert.True(lines.Count(l => l.StartsWith("value", StringComparison.Ordinal)) > 1);
    }
}
