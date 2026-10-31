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
using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks that the signed PDF attestation can be produced and that its signature is valid and tamper-evident.
/// </summary>
/// <remarks>
///     The check creates a throw-away self-signed key in a temporary folder, renders a small attestation with the
///     production <see cref="AttestationRenderer"/> and signs it with the production <see cref="AttestationSigner"/>.
///     It then verifies the result the way an independent reader does, with
///     <see cref="System.Security.Cryptography.Pkcs.SignedCms"/>: the CMS signature over the byte ranges, and the
///     DocMDP certification that forbids later changes. Finally it changes one signed byte and requires the
///     verification to fail. The class is stateless and thread-safe.
/// </remarks>
internal static class AttestationPdfCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_AttestationPdf";

    /// <summary>
    ///     The time limit of each regular expression.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of what was verified.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the signature, the DocMDP permission or the tamper check is wrong.</exception>
    public static Task<string> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Declared first, so it is disposed last: the key provider (which may hold its key file) goes first
        using var folder = new TemporaryDirectory();
        using var keys = new SelfSignedKeyProvider(
            new AttestationOptions { SelfSignedFolder = Path.Combine(folder.DirectoryPath, "attestation") },
            TimeProvider.System,
            NullLogger.Instance,
            new EphemeralDataProtectionProvider());
        var key = keys.GetSigningKey();

        // Production rendering and signing
        var pdf = new AttestationRenderer().Render(CreateDocument(key.Info));
        var signed = new AttestationSigner().Sign(pdf, key, DateTimeOffset.UtcNow, "ReviewCenter self-validation");

        // Independent verification, then the same with one covered byte changed
        var range = ReadByteRange(signed);
        ValidationFailedException.ThrowUnless(
            SignatureVerifies(signed, range), "The CMS signature of the attestation does not verify.");
        ValidationFailedException.ThrowUnless(
            ReadDocMdpPermission(signed) == 1, "The attestation is not certified with DocMDP permission 1 (no changes).");

        var tampered = (byte[])signed.Clone();
        tampered[range[1] / 2] ^= 0x01;
        ValidationFailedException.ThrowUnless(
            !SignatureVerifies(tampered, range), "A changed byte of the attestation was not detected by the signature.");

        return Task.FromResult($"CMS signature and DocMDP verified; changed byte detected ({signed.Length} bytes)");
    }

    /// <summary>
    ///     Creates a minimal but complete attestation document.
    /// </summary>
    /// <param name="key">The description of the signing key.</param>
    /// <returns>The document.</returns>
    private static AttestationDocument CreateDocument(AttestationKeyInfo key)
    {
        var digest = new string('d', 64);
        var signedAt = DateTimeOffset.UtcNow;
        return new AttestationDocument(
            1,
            "Self-test project",
            "selftest-review",
            "Self-test review",
            "selftest-repository",
            new string('b', 40),
            new string('a', 40),
            "core",
            new string('f', 64),
            digest,
            "ReviewCenter record v1",
            [new AttestationFile("src/a.txt", "Modified", new string('0', 64), 4)],
            "1.0",
            [
                new AttestationSection(
                    "correctness",
                    "Correctness",
                    true,
                    null,
                    [new AttestationCheck("C-1", "Does it work?", "Pass", null, "Self Test")])
            ],
            0,
            [],
            [
                new AttestationSlot(
                    1,
                    "Self Test (selftest)",
                    null,
                    new AttestationSignature(
                        "Self Test (selftest)", "Primary", signedAt, "ReviewCenter record v1", digest, "Self-validation"))
            ],
            new string('c', 64),
            1,
            true,
            key,
            Program.Version,
            signedAt,
            "Self Test (selftest)",
            EvidenceSettings.Default,
            false);
    }

    /// <summary>
    ///     Reads the byte range of the signature from the PDF.
    /// </summary>
    /// <param name="pdf">The signed PDF.</param>
    /// <returns>The four numbers of <c>/ByteRange</c>.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the PDF has no byte range.</exception>
    private static int[] ReadByteRange(byte[] pdf)
    {
        var match = Regex.Match(
            Encoding.Latin1.GetString(pdf),
            @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]",
            RegexOptions.CultureInvariant,
            RegexTimeout);
        ValidationFailedException.ThrowUnless(match.Success, "The attestation has no signature byte range.");
        return [.. match.Groups.Cast<Group>().Skip(1).Select(g => int.Parse(g.Value, CultureInfo.InvariantCulture))];
    }

    /// <summary>
    ///     Reads the DocMDP permission of the certification signature.
    /// </summary>
    /// <param name="pdf">The signed PDF.</param>
    /// <returns>The permission number, or 0 when the PDF is not certified.</returns>
    private static int ReadDocMdpPermission(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(
            text,
            @"/TransformMethod /DocMDP /TransformParams << ([^>]*?)/P (\d)\b",
            RegexOptions.CultureInvariant,
            RegexTimeout);
        var perms = Regex.IsMatch(text, @"/Perms << /DocMDP \d+ 0 R >>", RegexOptions.CultureInvariant, RegexTimeout);
        return match.Success && perms ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
    }

    /// <summary>
    ///     Verifies the CMS signature of a PDF over the bytes its byte range covers.
    /// </summary>
    /// <param name="pdf">The PDF bytes.</param>
    /// <param name="range">The byte range of the signature.</param>
    /// <returns><c>true</c> when the signature verifies; <c>false</c> when it does not.</returns>
    private static bool SignatureVerifies(byte[] pdf, int[] range)
    {
        // The signed bytes are the two ranges either side of the signature placeholder
        var covered = new byte[range[1] + range[3]];
        Buffer.BlockCopy(pdf, range[0], covered, 0, range[1]);
        Buffer.BlockCopy(pdf, range[2], covered, range[1], range[3]);

        // The placeholder holds the DER signature followed by zero padding
        var text = Encoding.Latin1.GetString(pdf);
        var hex = Regex.Match(text, @"/Contents <([0-9A-Fa-f]+)>", RegexOptions.CultureInvariant, RegexTimeout).Groups[1].Value;
        var all = Convert.FromHexString(hex);
        try
        {
            var cms = new SignedCms(new ContentInfo(covered), detached: true);
            cms.Decode(all[..DerLength(all)]);
            cms.CheckSignature(verifySignatureOnly: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
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
