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
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Reads the structure of a PDF that the attestation code wrote, independently of the code that wrote it: the
///     cross-reference chain, the pages, the text, the information dictionary and the signature.
/// </summary>
/// <remarks>
///     The inspector understands only what the product writes (classic cross-reference tables, uncompressed
///     content, literal strings). It is a test tool: it parses with plain regular expressions and checks what a
///     PDF reader would check, so that a test does not merely compare the product with itself.
/// </remarks>
internal sealed class PdfInspector
{
    /// <summary>
    ///     The time limit of each regular expression.
    /// </summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The file as text, one character per byte.
    /// </summary>
    private readonly string _text;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PdfInspector"/> class.
    /// </summary>
    /// <param name="bytes">The bytes of the PDF file.</param>
    public PdfInspector(byte[] bytes)
    {
        Bytes = bytes;
        _text = Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    ///     Gets the bytes of the file.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    ///     Gets the file as text, one character per byte.
    /// </summary>
    public string Text => _text;

    /// <summary>
    ///     Gets a value indicating whether the file starts with the PDF header and ends with the end-of-file marker.
    /// </summary>
    public bool HasPdfFraming => _text.StartsWith("%PDF-1.", StringComparison.Ordinal) && _text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal);

    /// <summary>
    ///     Gets the number of pages, from the page tree.
    /// </summary>
    public int PageCount
    {
        get
        {
            var matches = Regex.Matches(_text, @"/Type /Pages /Count (\d+)", RegexOptions.None, _timeout);
            return int.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    ///     Gets every text string that the page content streams show, in file order, with the escapes resolved.
    /// </summary>
    public IReadOnlyList<string> TextLines =>
        Regex.Matches(_text, @"\(((?:\\.|[^\\)])*)\) Tj", RegexOptions.None, _timeout)
            .Select(m => Unescape(m.Groups[1].Value))
            .ToList();

    /// <summary>
    ///     Gets the offsets of the cross-reference sections, from the last one back to the first.
    /// </summary>
    public IReadOnlyList<int> CrossReferenceChain
    {
        get
        {
            var chain = new List<int>();
            var at = int.Parse(Match(_text[_text.LastIndexOf("startxref", StringComparison.Ordinal)..], @"startxref\n(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            while (true)
            {
                chain.Add(at);
                var trailer = _text[_text.IndexOf("trailer", at, StringComparison.Ordinal)..];
                trailer = trailer[..trailer.IndexOf("startxref", StringComparison.Ordinal)];
                var previous = Regex.Match(trailer, @"/Prev (\d+)", RegexOptions.None, _timeout);
                if (!previous.Success)
                {
                    return chain;
                }

                at = int.Parse(previous.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>
    ///     Gets the offset of every object as the cross-reference chain says; a newer section overrides an older one.
    /// </summary>
    /// <returns>The offset by object number, without the free entry 0.</returns>
    public IReadOnlyDictionary<int, int> ObjectOffsets()
    {
        var offsets = new Dictionary<int, int>();
        foreach (var at in CrossReferenceChain)
        {
            var end = _text.IndexOf("trailer", at, StringComparison.Ordinal);
            var section = _text[(at + "xref\n".Length)..end];
            var position = 0;
            while (position < section.Length)
            {
                var header = Regex.Match(section[position..], @"^(\d+) (\d+)\n", RegexOptions.None, _timeout);
                position += header.Length;
                var first = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                var count = int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture);
                for (var index = 0; index < count; index++)
                {
                    var entry = section.Substring(position, 20);
                    position += 20;
                    var number = first + index;
                    if (number != 0 && entry.EndsWith("n \n", StringComparison.Ordinal))
                    {
                        offsets.TryAdd(number, int.Parse(entry[..10], CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        return offsets;
    }

    /// <summary>
    ///     Checks that every object that the cross-reference chain lists starts at its offset.
    /// </summary>
    /// <returns><c>true</c> when each offset points at <c>n 0 obj</c>.</returns>
    public bool CrossReferenceIsConsistent()
    {
        return ObjectOffsets().All(pair => string.CompareOrdinal(_text, pair.Value, $"{pair.Key} 0 obj\n", 0, $"{pair.Key} 0 obj\n".Length) == 0);
    }

    /// <summary>
    ///     Gets the byte range of the signature, or <c>null</c> when the file is not signed.
    /// </summary>
    public int[]? ByteRange
    {
        get
        {
            var match = Regex.Match(_text, @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]", RegexOptions.None, _timeout);
            return match.Success ? [.. match.Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture))] : null;
        }
    }

    /// <summary>
    ///     Gets the bytes that the signature covers: the two ranges of the byte range.
    /// </summary>
    public byte[] SignedBytes
    {
        get
        {
            var range = ByteRange ?? throw new InvalidOperationException("The file is not signed.");
            return [.. Bytes.Skip(range[0]).Take(range[1]), .. Bytes.Skip(range[2]).Take(range[3])];
        }
    }

    /// <summary>
    ///     Gets the hexadecimal text of the signature as written between the angle brackets of <c>/Contents</c>.
    /// </summary>
    public string ContentsHex => Match(_text, @"/Contents <([0-9A-Fa-f]+)>").Groups[1].Value;

    /// <summary>
    ///     Gets the DER encoding of the signature, without the zero padding of the placeholder.
    /// </summary>
    public byte[] SignatureBytes
    {
        get
        {
            var all = Convert.FromHexString(ContentsHex);
            return all[..DerLength(all)];
        }
    }

    /// <summary>
    ///     Checks that the byte range covers every byte of the file except the signature placeholder.
    /// </summary>
    /// <returns><c>true</c> when the first range starts at zero, the second ends at the end of the file, and the gap is exactly the hexadecimal contents with its angle brackets.</returns>
    public bool ByteRangeCoversEverythingButContents()
    {
        var range = ByteRange;
        if (range is null)
        {
            return false;
        }

        var contents = _text.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents ".Length;
        var end = contents + 1 + ContentsHex.Length + 1;
        return range[0] == 0 && range[1] == contents && range[2] == end && range[2] + range[3] == Bytes.Length;
    }

    /// <summary>
    ///     Verifies the signature the way a reader does: decodes the CMS structure, checks the signature over the
    ///     signed bytes against the embedded certificate.
    /// </summary>
    /// <returns>The decoded structure; the call throws when the signature does not verify.</returns>
    public SignedCms VerifySignature()
    {
        var cms = new SignedCms(new ContentInfo(SignedBytes), detached: true);
        cms.Decode(SignatureBytes);

        // checkOnly: the certificate chain is not evaluated; trust is a matter of the verifier's policy
        cms.CheckSignature(verifySignatureOnly: true);
        return cms;
    }

    /// <summary>
    ///     Gets the value of a document information entry.
    /// </summary>
    /// <param name="key">The entry name, such as <c>Title</c>.</param>
    /// <returns>The decoded text.</returns>
    public string Info(string key)
    {
        var info = Match(_text, @"3 0 obj\n<<(.*?)>>\nendobj").Groups[1].Value;
        var match = Regex.Match(info, "/" + key + @" (\((?:\\.|[^\\)])*\)|<[0-9A-F]+>)", RegexOptions.None, _timeout);
        if (!match.Success)
        {
            throw new InvalidOperationException($"The information dictionary has no /{key}.");
        }

        var value = match.Groups[1].Value;
        if (value[0] == '(')
        {
            return Unescape(value[1..^1]);
        }

        var bytes = Convert.FromHexString(value[1..^1]);
        return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
    }

    /// <summary>
    ///     Parses a literal-string body: resolves <c>\\</c>, <c>\(</c>, <c>\)</c> and octal escapes.
    /// </summary>
    /// <param name="body">The text between the parentheses.</param>
    /// <returns>The characters, one per byte.</returns>
    private static string Unescape(string body)
    {
        return Regex.Replace(
            body,
            @"\\(\d{3}|.)",
            m => m.Groups[1].Value.Length == 3
                ? ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString()
                : m.Groups[1].Value,
            RegexOptions.None,
            _timeout);
    }

    /// <summary>
    ///     Matches a pattern that must be found.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The match.</returns>
    private static Match Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.Singleline, _timeout);
        return match.Success ? match : throw new InvalidOperationException($"The PDF does not contain '{pattern}'.");
    }

    /// <summary>
    ///     Reads the total length of a DER element from its header.
    /// </summary>
    /// <param name="der">The bytes, starting with the element.</param>
    /// <returns>The length of the element including its header.</returns>
    private static int DerLength(byte[] der)
    {
        var first = der[1];
        if (first < 0x80)
        {
            return 2 + first;
        }

        var count = first & 0x7F;
        var length = 0;
        for (var index = 0; index < count; index++)
        {
            length = (length << 8) | der[2 + index];
        }

        return 2 + count + length;
    }
}
