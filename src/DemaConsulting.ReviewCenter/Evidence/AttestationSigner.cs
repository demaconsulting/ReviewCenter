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
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Signs a PDF with a detached CMS signature (<c>adbe.pkcs7.detached</c>) added as an incremental update, so
///     that PDF readers show the document as signed and report any later change.
/// </summary>
/// <remarks>
///     The original bytes are never changed. The update appends a signature dictionary with a fixed-size
///     <c>/Contents</c> placeholder and a <c>/ByteRange</c> that covers every byte of the file except that
///     placeholder, a signature field (widget) on the first page, the rewritten page and catalog objects that
///     reference it, a new cross-reference section and a trailer that points back to the old one. The CMS signature
///     is then computed over exactly the covered bytes and written into the placeholder. The signer understands only
///     the layout that <see cref="PdfDocumentWriter"/> writes (a classic cross-reference table and one revision)
///     and refuses any other PDF. It is stateless and thread-safe.
/// </remarks>
internal sealed class AttestationSigner : IAttestationSigner
{
    /// <summary>
    ///     The number of bytes that the signature may take; large enough for a signature with a certificate chain.
    /// </summary>
    private const int SignatureCapacity = 32768;

    /// <summary>
    ///     The object identifier of SHA-256.
    /// </summary>
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>
    ///     The text that stands in the byte range until the final values are known; it has the same length as the final text.
    /// </summary>
    private const string ByteRangePlaceholder = "[0 0000000000 0000000000 0000000000]";

    /// <summary>
    ///     The time limit of each pattern search in the file.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public byte[] Sign(byte[] pdf, AttestationSigningKey key, DateTimeOffset signedAt, string reason)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reason);

        // Read just enough of the file to extend it
        var text = Encoding.Latin1.GetString(pdf);
        Layout layout;
        try
        {
            layout = ReadLayout(text);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException("The document is not a PDF that the attestation signer can extend.", ex);
        }

        // Append the signature, its field and the rewritten page and catalog
        var update = BuildUpdate(text, layout, key, signedAt, reason);
        var signed = new byte[pdf.Length + update.Length];
        pdf.CopyTo(signed, 0);
        update.CopyTo(signed, pdf.Length);

        // The signature covers everything except the placeholder for the signature itself
        var signedText = Encoding.Latin1.GetString(signed, pdf.Length, update.Length);
        var contentsStart = pdf.Length + signedText.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents ".Length;
        var contentsEnd = contentsStart + 1 + (SignatureCapacity * 2) + 1;
        var rangeAt = pdf.Length + signedText.IndexOf(ByteRangePlaceholder, StringComparison.Ordinal);
        var range = Encoding.Latin1.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"[0 {contentsStart:D10} {contentsEnd:D10} {signed.Length - contentsEnd:D10}]"));
        range.CopyTo(signed, rangeAt);

        var covered = new byte[contentsStart + (signed.Length - contentsEnd)];
        Array.Copy(signed, 0, covered, 0, contentsStart);
        Array.Copy(signed, contentsEnd, covered, contentsStart, signed.Length - contentsEnd);

        // The signature is written into the placeholder between the angle brackets
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(ComputeSignature(covered, key, signedAt)));
        if (hex.Length > SignatureCapacity * 2)
        {
            throw new AttestationKeyException("The signature, with its certificate chain, is too large for the attestation.");
        }

        hex.CopyTo(signed, contentsStart + 1);
        return signed;
    }

    /// <summary>
    ///     Reads the entry that the incremental update needs: the trailer values and the page and catalog objects.
    /// </summary>
    /// <param name="text">The file as Latin-1 text, one character per byte.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file is not a layout that the signer can extend.</exception>
    private static Layout ReadLayout(string text)
    {
        var start = text.LastIndexOf("startxref", StringComparison.Ordinal);
        if (!text.StartsWith("%PDF-", StringComparison.Ordinal) || start < 0)
        {
            throw new InvalidOperationException("The document is not a PDF that the attestation signer can extend.");
        }

        var xref = int.Parse(RequiredMatch(text[(start + "startxref".Length)..], @"(\d+)"), CultureInfo.InvariantCulture);
        var trailer = text[text.LastIndexOf("trailer", StringComparison.Ordinal)..];
        var size = int.Parse(RequiredMatch(trailer, @"/Size (\d+)"), CultureInfo.InvariantCulture);
        var root = int.Parse(RequiredMatch(trailer, @"/Root (\d+) 0 R"), CultureInfo.InvariantCulture);
        var info = Match(trailer, @"/Info (\d+) 0 R");
        var id = Match(trailer, @"/ID \[[^\]]*\]");

        // The first subsection of the classic table lists every object of a single-revision file
        var table = Match(text[xref..], @"^xref\n0 (\d+)\n");
        if (!table.Success || int.Parse(table.Groups[1].Value, CultureInfo.InvariantCulture) != size)
        {
            throw new InvalidOperationException("The document does not have the cross-reference layout of the attestation writer.");
        }

        var offsets = new int[size];
        var position = xref + table.Length;
        for (var number = 0; number < size; number++)
        {
            offsets[number] = int.Parse(text.AsSpan(position, 10), NumberStyles.None, CultureInfo.InvariantCulture);
            position += 20;
        }

        var catalog = ReadObject(text, offsets, root);
        if (catalog.Contains("/AcroForm", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The document already has a form or a signature.");
        }

        // The signature field goes on the first page
        var pagesNumber = int.Parse(RequiredMatch(catalog, @"/Pages (\d+) 0 R"), CultureInfo.InvariantCulture);
        var pages = ReadObject(text, offsets, pagesNumber);
        var page = int.Parse(RequiredMatch(pages, @"/Kids \[(\d+) 0 R"), CultureInfo.InvariantCulture);
        var pageBody = ReadObject(text, offsets, page);
        if (pageBody.Contains("/Annots", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The first page already has annotations.");
        }

        return new Layout(
            xref,
            size,
            root,
            catalog,
            page,
            pageBody,
            info.Success ? int.Parse(info.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            id.Success ? id.Value : null);
    }

    /// <summary>
    ///     Gets the first capture group of a pattern that must match.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="pattern">The pattern, with one group.</param>
    /// <returns>The captured text.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pattern does not match.</exception>
    private static string RequiredMatch(string text, string pattern)
    {
        var match = Match(text, pattern);
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException("The document does not have the structure of the attestation writer.");
    }

    /// <summary>
    ///     Searches a text for a pattern, with a time limit.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The match.</returns>
    private static Match Match(string text, string pattern)
    {
        return Regex.Match(text, pattern, RegexOptions.None, MatchTimeout);
    }

    /// <summary>
    ///     Reads the dictionary of an object.
    /// </summary>
    /// <param name="text">The file as Latin-1 text.</param>
    /// <param name="offsets">The offset of each object.</param>
    /// <param name="number">The object number.</param>
    /// <returns>The text between <c>obj</c> and <c>endobj</c>, without the surrounding line breaks.</returns>
    private static string ReadObject(string text, int[] offsets, int number)
    {
        var header = $"{number.ToString(CultureInfo.InvariantCulture)} 0 obj\n";
        var at = offsets[number];
        if (string.CompareOrdinal(text, at, header, 0, header.Length) != 0)
        {
            throw new InvalidOperationException("The document's cross-reference table does not match its objects.");
        }

        var end = text.IndexOf("\nendobj", at, StringComparison.Ordinal);
        return text[(at + header.Length)..end];
    }

    /// <summary>
    ///     Builds the appended bytes: the signature, the field, the rewritten page and catalog, the new
    ///     cross-reference section and the trailer.
    /// </summary>
    /// <param name="text">The original file as Latin-1 text.</param>
    /// <param name="layout">The layout of the original.</param>
    /// <param name="key">The signing key; its certificate names the signer.</param>
    /// <param name="signedAt">The signing time.</param>
    /// <param name="reason">The reason for the signature.</param>
    /// <returns>The bytes to append, with the byte range and signature still as placeholders.</returns>
    private static byte[] BuildUpdate(string text, Layout layout, AttestationSigningKey key, DateTimeOffset signedAt, string reason)
    {
        var signatureObject = layout.Size;
        var widgetObject = layout.Size + 1;
        var name = key.Certificate.GetNameInfo(X509NameType.SimpleName, false);
        var when = "(D:" + signedAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "+00'00')";

        // Each object is written with its offset recorded for the cross-reference section
        var update = new StringBuilder();
        var written = new SortedDictionary<int, int>();
        void WriteObject(int number, string body)
        {
            written[number] = text.Length + update.Length;
            update.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        WriteObject(
            signatureObject,
            "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange " + ByteRangePlaceholder
            + " /Contents <" + new string('0', SignatureCapacity * 2) + ">"
            + " /Name " + PdfDocumentWriter.EncodeTextString(name)
            + " /Reason " + PdfDocumentWriter.EncodeTextString(reason)
            + " /M " + when + " >>");
        WriteObject(
            widgetObject,
            $"<< /Type /Annot /Subtype /Widget /FT /Sig /Rect [0 0 0 0] /F 132 /T (Signature1) /V {signatureObject} 0 R /P {layout.Page} 0 R >>");
        WriteObject(layout.Page, InsertBeforeEnd(layout.PageBody, $" /Annots [{widgetObject} 0 R]"));
        WriteObject(layout.Root, InsertBeforeEnd(layout.Catalog, $" /AcroForm << /Fields [{widgetObject} 0 R] /SigFlags 3 >>"));

        // The new cross-reference section lists only the objects that were added or rewritten
        var xref = text.Length + update.Length;
        update.Append("xref\n");
        foreach (var (number, offset) in written)
        {
            update.Append(number.ToString(CultureInfo.InvariantCulture)).Append(" 1\n")
                .Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        update.Append("trailer\n<< /Size ").Append((layout.Size + 2).ToString(CultureInfo.InvariantCulture))
            .Append(" /Root ").Append(layout.Root.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        if (layout.Info is not null)
        {
            update.Append(" /Info ").Append(layout.Info.Value.ToString(CultureInfo.InvariantCulture)).Append(" 0 R");
        }

        if (layout.Id is not null)
        {
            update.Append(' ').Append(layout.Id);
        }

        update.Append(" /Prev ").Append(layout.Xref.ToString(CultureInfo.InvariantCulture)).Append(" >>\n");
        update.Append("startxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(update.ToString());
    }

    /// <summary>
    ///     Adds entries at the end of a dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary text, ending with <c>&gt;&gt;</c>.</param>
    /// <param name="entries">The entries to add.</param>
    /// <returns>The dictionary with the entries.</returns>
    private static string InsertBeforeEnd(string dictionary, string entries)
    {
        var end = dictionary.LastIndexOf(">>", StringComparison.Ordinal);
        return dictionary[..end].TrimEnd() + entries + " >>";
    }

    /// <summary>
    ///     Computes the detached CMS signature of the covered bytes.
    /// </summary>
    /// <param name="covered">The bytes that the signature covers.</param>
    /// <param name="key">The signing key.</param>
    /// <param name="signedAt">The signing time to record as a signed attribute.</param>
    /// <returns>The DER encoding of the CMS signature.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the key cannot sign.</exception>
    private static byte[] ComputeSignature(byte[] covered, AttestationSigningKey key, DateTimeOffset signedAt)
    {
        try
        {
            // The key handle signs; the key material is never exported
            var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, key.Certificate, key.PrivateKey)
            {
                DigestAlgorithm = new Oid(Sha256Oid),
                IncludeOption = X509IncludeOption.EndCertOnly
            };
            foreach (var certificate in key.Chain)
            {
                signer.Certificates.Add(certificate);
            }

            signer.SignedAttributes.Add(new Pkcs9SigningTime(signedAt.UtcDateTime));
            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.ComputeSignature(signer);
            return cms.Encode();
        }
        catch (CryptographicException ex)
        {
            throw new AttestationKeyException("The signing key could not sign the attestation: " + ex.Message, ex);
        }
    }

    /// <summary>
    ///     What the update needs to know about the original file.
    /// </summary>
    /// <param name="Xref">The offset of the original cross-reference section.</param>
    /// <param name="Size">The object count of the original, so the first free object number.</param>
    /// <param name="Root">The catalog object number.</param>
    /// <param name="Catalog">The catalog dictionary.</param>
    /// <param name="Page">The first page object number.</param>
    /// <param name="PageBody">The first page dictionary.</param>
    /// <param name="Info">The info object number, or <c>null</c>.</param>
    /// <param name="Id">The file identifier entry of the trailer, or <c>null</c>.</param>
    private sealed record Layout(
        int Xref,
        int Size,
        int Root,
        string Catalog,
        int Page,
        string PageBody,
        int? Info,
        string? Id);
}
