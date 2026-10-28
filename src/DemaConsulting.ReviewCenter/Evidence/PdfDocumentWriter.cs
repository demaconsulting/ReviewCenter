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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     A minimal PDF 1.5 writer: A4 portrait pages of wrapped text in standard fonts, with a header, a footer with
///     page numbers, and document information.
/// </summary>
/// <remarks>
///     The writer exists because an attestation needs only text, rules and page numbers; a third-party PDF library
///     would be a large dependency for that. The file uses a classic cross-reference table, no compression, no object
///     streams and no embedded fonts, which keeps it easy to read, to verify and to extend with an incremental
///     update (see <see cref="AttestationSigner"/>, which relies on exactly this layout). Text is limited to the
///     Windows-1252 ("WinAnsi") character set that the standard fonts support; any other character becomes
///     <c>?</c>. The output is a pure function of the information, the header, the footer and the calls made: no
///     clock, random value or environment is read, so the same input yields the same bytes. Instances are not
///     thread-safe; use one per document.
/// </remarks>
internal sealed class PdfDocumentWriter
{
    /// <summary>The page width in points (A4).</summary>
    public const double PageWidth = 595;

    /// <summary>The page height in points (A4).</summary>
    public const double PageHeight = 842;

    /// <summary>The left and right margin in points.</summary>
    public const double SideMargin = 54;

    /// <summary>The distance in points from the top edge to the first line of the body.</summary>
    private const double TopMargin = 64;

    /// <summary>The distance in points from the bottom edge to the lowest line of the body.</summary>
    private const double BottomMargin = 64;

    /// <summary>The font size of header and footer text.</summary>
    private const double SmallSize = 8;

    /// <summary>The line height as a multiple of the font size.</summary>
    private const double LineFactor = 1.3;

    /// <summary>The width, in thousandths of the font size, that is assumed for a character outside ASCII.</summary>
    private const int FallbackWidth = 600;

    /// <summary>The width, in thousandths of the font size, of every Courier character.</summary>
    private const int CourierWidth = 600;

    /// <summary>The number of fonts, and so of font objects in the file.</summary>
    private static readonly int FontCount = Enum.GetValues<PdfFont>().Length;

    /// <summary>The PostScript names of the fonts, in the order of <see cref="PdfFont"/>.</summary>
    private static readonly string[] BaseFonts =
        ["Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Courier", "Courier-Bold"];

    /// <summary>
    ///     The Helvetica widths of the characters 32 to 126 in thousandths of the font size (Adobe font metrics).
    ///     Helvetica-Oblique has the same widths.
    /// </summary>
    private static readonly int[] HelveticaWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    /// <summary>
    ///     The Helvetica-Bold widths of the characters 32 to 126 in thousandths of the font size (Adobe font metrics).
    /// </summary>
    private static readonly int[] HelveticaBoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    ];

    /// <summary>
    ///     The Windows-1252 code of each character in the 0x80-0x9F range that is not the same in Unicode.
    /// </summary>
    private static readonly Dictionary<int, byte> WinAnsiSpecials = new()
    {
        [0x20AC] = 0x80,
        [0x201A] = 0x82,
        [0x0192] = 0x83,
        [0x201E] = 0x84,
        [0x2026] = 0x85,
        [0x2020] = 0x86,
        [0x2021] = 0x87,
        [0x02C6] = 0x88,
        [0x2030] = 0x89,
        [0x0160] = 0x8A,
        [0x2039] = 0x8B,
        [0x0152] = 0x8C,
        [0x017D] = 0x8E,
        [0x2018] = 0x91,
        [0x2019] = 0x92,
        [0x201C] = 0x93,
        [0x201D] = 0x94,
        [0x2022] = 0x95,
        [0x2013] = 0x96,
        [0x2014] = 0x97,
        [0x02DC] = 0x98,
        [0x2122] = 0x99,
        [0x0161] = 0x9A,
        [0x203A] = 0x9B,
        [0x0153] = 0x9C,
        [0x017E] = 0x9E,
        [0x0178] = 0x9F
    };

    /// <summary>The document information written to the Info dictionary.</summary>
    private readonly PdfDocumentInfo _info;

    /// <summary>The header text, already restricted to WinAnsi.</summary>
    private readonly string _header;

    /// <summary>The footer text on the left, already restricted to WinAnsi.</summary>
    private readonly string _footer;

    /// <summary>The body lines in order; pagination happens in <see cref="Build"/>.</summary>
    private readonly List<Line> _lines = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="PdfDocumentWriter"/> class.
    /// </summary>
    /// <param name="info">The document information. Must not be null.</param>
    /// <param name="header">The text at the top of every page; clipped to the page width. Must not be null.</param>
    /// <param name="footer">The text at the left of the footer of every page. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public PdfDocumentWriter(PdfDocumentInfo info, string header, string footer)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(footer);
        _info = info;
        _header = ToWinAnsi(header);
        _footer = ToWinAnsi(footer);
    }

    /// <summary>
    ///     Restricts text to what the standard fonts can show, one character per byte of Windows-1252.
    /// </summary>
    /// <param name="text">The text. Must not be null.</param>
    /// <returns>
    ///     The text where each character that Windows-1252 has is replaced by its one-byte code (as a char of the same
    ///     value), each control character is a space and every other character is <c>?</c>; a character outside the
    ///     Basic Multilingual Plane (a surrogate pair) gives a single <c>?</c>. Every character of the result is in
    ///     the range 32 to 255, so it can be written as one byte.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    public static string ToWinAnsi(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        foreach (var value in text.EnumerateRunes().Select(rune => rune.Value))
        {
            if (value is >= 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF)
            {
                result.Append((char)value);
            }
            else if (WinAnsiSpecials.TryGetValue(value, out var code))
            {
                result.Append((char)code);
            }
            else if (value < 0x20 || value is 0x7F or >= 0x80 and <= 0x9F)
            {
                // Tabs and other control characters have no glyph; a space keeps the words apart
                result.Append(' ');
            }
            else
            {
                result.Append('?');
            }
        }

        return result.ToString();
    }

    /// <summary>
    ///     Encodes text as a PDF text string: a literal string for plain ASCII, otherwise UTF-16BE with a byte order mark.
    /// </summary>
    /// <param name="text">The text. Must not be null.</param>
    /// <returns>The string including its delimiters, safe to place in a PDF dictionary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    public static string EncodeTextString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.All(c => c is >= ' ' and <= '~'))
        {
            return "(" + Escape(text) + ")";
        }

        var hex = new StringBuilder("<FEFF");
        foreach (var unit in text)
        {
            hex.Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
        }

        return hex.Append('>').ToString();
    }

    /// <summary>
    ///     Adds a large bold title line.
    /// </summary>
    /// <param name="text">The title text.</param>
    public void Title(string text) => Paragraph(text, PdfFont.HelveticaBold, 18, spaceBefore: 0);

    /// <summary>
    ///     Adds a section heading with some space above it and a thin rule under it.
    /// </summary>
    /// <param name="text">The heading text.</param>
    public void Heading(string text)
    {
        Paragraph(text, PdfFont.HelveticaBold, 12, spaceBefore: 14);
        Rule();
    }

    /// <summary>
    ///     Adds a paragraph that is wrapped to the page width; a line feed starts a new line.
    /// </summary>
    /// <param name="text">The text. Must not be null.</param>
    /// <param name="font">The font; regular Helvetica by default.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="indent">The distance from the left margin in points.</param>
    /// <param name="spaceBefore">The extra space above the paragraph in points.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
    public void Paragraph(
        string text,
        PdfFont font = PdfFont.Helvetica,
        double size = 10,
        double indent = 0,
        double spaceBefore = 3)
    {
        ArgumentNullException.ThrowIfNull(text);
        var available = PageWidth - (2 * SideMargin) - indent;
        var first = true;
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            foreach (var line in Wrap(ToWinAnsi(paragraph), font, size, available))
            {
                _lines.Add(new Line(
                    first ? spaceBefore : 0,
                    size * LineFactor,
                    false,
                    [new Run(SideMargin + indent, font, size, line)]));
                first = false;
            }
        }
    }

    /// <summary>
    ///     Adds a label with a value: the bold label on the left, and the value, wrapped, in a column to its right.
    /// </summary>
    /// <param name="label">The label text.</param>
    /// <param name="value">The value text; a line feed starts a new line.</param>
    /// <param name="valueFont">The font of the value; regular Helvetica by default.</param>
    /// <param name="indent">The distance from the left margin in points.</param>
    /// <param name="labelWidth">The width of the label column in points.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="label"/> or <paramref name="value"/> is null.</exception>
    public void Row(
        string label,
        string value,
        PdfFont valueFont = PdfFont.Helvetica,
        double indent = 0,
        double labelWidth = 140)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(value);
        const double size = 10;
        var labelLines = Wrap(ToWinAnsi(label), PdfFont.HelveticaBold, size, labelWidth - 6);
        var valueSize = valueFont is PdfFont.Courier or PdfFont.CourierBold ? 9 : size;
        var available = PageWidth - (2 * SideMargin) - indent - labelWidth;
        var valueLines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .SelectMany(part => Wrap(ToWinAnsi(part), valueFont, valueSize, available))
            .ToList();
        var count = Math.Max(labelLines.Count, valueLines.Count);
        for (var index = 0; index < count; index++)
        {
            var runs = new List<Run>();
            if (index < labelLines.Count)
            {
                runs.Add(new Run(SideMargin + indent, PdfFont.HelveticaBold, size, labelLines[index]));
            }

            if (index < valueLines.Count)
            {
                runs.Add(new Run(SideMargin + indent + labelWidth, valueFont, valueSize, valueLines[index]));
            }

            _lines.Add(new Line(index == 0 ? 3 : 0, size * LineFactor, false, runs));
        }
    }

    /// <summary>
    ///     Adds a thin horizontal rule across the text area.
    /// </summary>
    public void Rule()
    {
        _lines.Add(new Line(2, 4, true, []));
    }

    /// <summary>
    ///     Adds empty vertical space.
    /// </summary>
    /// <param name="points">The height in points.</param>
    public void Space(double points)
    {
        _lines.Add(new Line(points, 0, false, []));
    }

    /// <summary>
    ///     Lays out the pages and writes the PDF file.
    /// </summary>
    /// <returns>The bytes of the file: a complete PDF with one revision and a classic cross-reference table.</returns>
    /// <remarks>
    ///     The call may be repeated; it does not change the writer. A document with no body still has one page.
    /// </remarks>
    public byte[] Build()
    {
        var pages = Paginate();

        // Object numbers: 1 catalog, 2 page tree, 3 info, then the fonts, then a page and its content per page
        var firstFont = 4;
        var firstPage = firstFont + FontCount;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count " + pages.Count.ToString(CultureInfo.InvariantCulture) + " /Kids ["
                + string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + (2 * i)} 0 R")) + "] >>",
            InfoDictionary()
        };
        for (var font = 0; font < FontCount; font++)
        {
            objects.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{BaseFonts[font]} /Encoding /WinAnsiEncoding >>");
        }

        var fonts = string.Join(' ', Enumerable.Range(0, FontCount).Select(i => $"/F{i + 1} {firstFont + i} 0 R"));
        for (var index = 0; index < pages.Count; index++)
        {
            var content = RenderPage(pages[index], index + 1, pages.Count);
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(PageWidth)} {Number(PageHeight)}] "
                + $"/Resources << /Font << {fonts} >> >> /Contents {firstPage + (2 * index) + 1} 0 R >>");
            objects.Add($"<< /Length {content.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{content}\nendstream");
        }

        // The file: header with a binary comment (marks the file as binary for transfer tools), the objects, the
        // cross-reference table and the trailer
        var file = new StringBuilder("%PDF-1.5\n%\u00E2\u00E3\u00CF\u00D3\n");
        var offsets = new List<int>();
        for (var number = 1; number <= objects.Count; number++)
        {
            offsets.Add(file.Length);
            file.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(objects[number - 1]).Append("\nendobj\n");
        }

        // The file identifier is derived from the content so that it is stable for the same input
        var identifier = Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(file.ToString())), 0, 16);
        var xref = file.Length;
        file.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        file.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            file.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        file.Append("trailer\n<< /Size ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append(" /Root 1 0 R /Info 3 0 R /ID [<").Append(identifier).Append("> <").Append(identifier).Append(">] >>\n");
        file.Append("startxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(file.ToString());
    }

    /// <summary>
    ///     Measures a text in points.
    /// </summary>
    /// <param name="text">The text, restricted to WinAnsi (see <see cref="ToWinAnsi"/>).</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The font size in points.</param>
    /// <returns>The width of the text. Characters outside ASCII use a conservative average width.</returns>
    internal static double Measure(string text, PdfFont font, double size)
    {
        var total = 0;
        foreach (var character in text)
        {
            total += Width(character, font);
        }

        return total * size / 1000.0;
    }

    /// <summary>
    ///     Writes a number without exponent or culture-specific separators, with at most two decimals.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    private static string Number(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Gets the width of a character in thousandths of the font size.
    /// </summary>
    /// <param name="character">The character, restricted to WinAnsi.</param>
    /// <param name="font">The font.</param>
    /// <returns>The width.</returns>
    private static int Width(char character, PdfFont font)
    {
        if (font is PdfFont.Courier or PdfFont.CourierBold)
        {
            return CourierWidth;
        }

        if (character is < ' ' or > '~')
        {
            return FallbackWidth;
        }

        var table = font == PdfFont.HelveticaBold ? HelveticaBoldWidths : HelveticaWidths;
        return table[character - ' '];
    }

    /// <summary>
    ///     Escapes the characters that end or start something inside a literal PDF string.
    /// </summary>
    /// <param name="text">The text, restricted to WinAnsi.</param>
    /// <returns>The text with <c>\</c>, <c>(</c> and <c>)</c> escaped and every non-ASCII character as an octal code.</returns>
    private static string Escape(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        foreach (var character in text)
        {
            if (character is '\\' or '(' or ')')
            {
                result.Append('\\').Append(character);
            }
            else if (character is > '~' or < ' ')
            {
                result.Append('\\').Append(Convert.ToString(character & 0xFF, 8).PadLeft(3, '0'));
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    /// <summary>
    ///     Breaks a text into lines that fit a width.
    /// </summary>
    /// <param name="text">The text without line feeds, restricted to WinAnsi.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The font size in points.</param>
    /// <param name="width">The available width in points.</param>
    /// <returns>
    ///     The lines. Words are kept whole where possible; a word wider than a line is broken between characters.
    ///     An empty text gives one empty line so that a blank line is kept.
    /// </returns>
    private static List<string> Wrap(string text, PdfFont font, double size, double width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Measure(candidate, font, size) <= width)
            {
                current.Clear().Append(candidate);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            // A word wider than a line is cut where the line is full
            var rest = word;
            while (Measure(rest, font, size) > width && rest.Length > 1)
            {
                var length = 1;
                while (length < rest.Length && Measure(rest[..(length + 1)], font, size) <= width)
                {
                    length++;
                }

                lines.Add(rest[..length]);
                rest = rest[length..];
            }

            current.Append(rest);
        }

        lines.Add(current.ToString());
        return lines;
    }

    /// <summary>
    ///     Builds the Info dictionary.
    /// </summary>
    /// <returns>The dictionary text.</returns>
    private string InfoDictionary()
    {
        var created = "(D:" + _info.Created.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00')";
        return $"<< /Title {EncodeTextString(_info.Title)} /Author {EncodeTextString(_info.Author)} "
            + $"/Subject {EncodeTextString(_info.Subject)} /Keywords {EncodeTextString(_info.Keywords)} "
            + $"/Creator {EncodeTextString(_info.Creator)} /Producer {EncodeTextString(_info.Producer)} "
            + $"/CreationDate {created} /ModDate {created} >>";
    }

    /// <summary>
    ///     Distributes the body lines over pages.
    /// </summary>
    /// <returns>The lines of each page, at least one page.</returns>
    private List<List<Placed>> Paginate()
    {
        var top = PageHeight - TopMargin;
        var pages = new List<List<Placed>> { new() };
        var cursor = top;
        foreach (var line in _lines)
        {
            // Space above a line is dropped at the top of a page; a line that does not fit starts the next page
            var atTop = cursor >= top;
            var need = (atTop ? 0 : line.SpaceBefore) + line.Height;
            if (!atTop && cursor - need < BottomMargin)
            {
                pages.Add([]);
                cursor = top;
                need = line.Height;
            }

            var y = cursor - (cursor >= top ? 0 : line.SpaceBefore);
            pages[^1].Add(new Placed(line, y - line.Height));
            cursor -= need;
        }

        return pages;
    }

    /// <summary>
    ///     Writes the content stream of a page: header, body, footer.
    /// </summary>
    /// <param name="lines">The placed body lines.</param>
    /// <param name="number">The 1-based page number.</param>
    /// <param name="count">The number of pages.</param>
    /// <returns>The content stream text.</returns>
    private string RenderPage(List<Placed> lines, int number, int count)
    {
        var text = new StringBuilder();
        var width = PageWidth - (2 * SideMargin);

        // Header and footer, each with a thin rule between it and the body
        text.Append("q 0.5 w 0.6 G ")
            .Append(Segment(SideMargin, PageHeight - 44, width)).Append(Segment(SideMargin, 48, width)).Append("Q\n");
        Show(text, SideMargin, PageHeight - 38, PdfFont.Helvetica, SmallSize, Clip(_header, PdfFont.Helvetica, SmallSize, width));
        Show(text, SideMargin, 36, PdfFont.Helvetica, SmallSize, Clip(_footer, PdfFont.Helvetica, SmallSize, width - 90));
        var pageText = $"Page {number.ToString(CultureInfo.InvariantCulture)} of {count.ToString(CultureInfo.InvariantCulture)}";
        Show(text, SideMargin + width - Measure(pageText, PdfFont.Helvetica, SmallSize), 36, PdfFont.Helvetica, SmallSize, pageText);

        foreach (var placed in lines)
        {
            if (placed.Line.IsRule)
            {
                text.Append("q 0.5 w 0.6 G ").Append(Segment(SideMargin, placed.Y + 2, width)).Append("Q\n");
                continue;
            }

            foreach (var run in placed.Line.Runs)
            {
                Show(text, run.X, placed.Y + (placed.Line.Height * 0.25), run.Font, run.Size, run.Text);
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>
    ///     Writes a horizontal line path.
    /// </summary>
    /// <param name="x">The start.</param>
    /// <param name="y">The height.</param>
    /// <param name="length">The length.</param>
    /// <returns>The path operators.</returns>
    private static string Segment(double x, double y, double length)
    {
        return $"{Number(x)} {Number(y)} m {Number(x + length)} {Number(y)} l S ";
    }

    /// <summary>
    ///     Writes one text object.
    /// </summary>
    /// <param name="target">Where to append.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The baseline.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size.</param>
    /// <param name="text">The text, restricted to WinAnsi.</param>
    private static void Show(StringBuilder target, double x, double y, PdfFont font, double size, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        target.Append("BT /F").Append((int)font + 1).Append(' ').Append(Number(size)).Append(" Tf ")
            .Append(Number(x)).Append(' ').Append(Number(y)).Append(" Td (").Append(Escape(text)).Append(") Tj ET\n");
    }

    /// <summary>
    ///     Shortens a text until it fits a width.
    /// </summary>
    /// <param name="text">The text, restricted to WinAnsi.</param>
    /// <param name="font">The font.</param>
    /// <param name="size">The size.</param>
    /// <param name="width">The available width.</param>
    /// <returns>The text, cut at the end when it is too wide.</returns>
    private static string Clip(string text, PdfFont font, double size, double width)
    {
        while (text.Length > 0 && Measure(text, font, size) > width)
        {
            text = text[..^1];
        }

        return text;
    }

    /// <summary>One piece of text at a horizontal position of a line.</summary>
    /// <param name="X">The left edge in points from the page edge.</param>
    /// <param name="Font">The font.</param>
    /// <param name="Size">The size in points.</param>
    /// <param name="Text">The text, restricted to WinAnsi.</param>
    private sealed record Run(double X, PdfFont Font, double Size, string Text);

    /// <summary>One line of the body: space above it, its height, and either a rule or text runs.</summary>
    /// <param name="SpaceBefore">The space above in points.</param>
    /// <param name="Height">The height in points.</param>
    /// <param name="IsRule">Whether the line is a horizontal rule.</param>
    /// <param name="Runs">The text runs of the line.</param>
    private sealed record Line(double SpaceBefore, double Height, bool IsRule, IReadOnlyList<Run> Runs);

    /// <summary>A line placed on a page: the bottom of its box in points from the page bottom.</summary>
    /// <param name="Line">The line.</param>
    /// <param name="Y">The bottom of the line.</param>
    private sealed record Placed(Line Line, double Y);
}
