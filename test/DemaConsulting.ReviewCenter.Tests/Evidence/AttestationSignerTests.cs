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

using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DemaConsulting.ReviewCenter.Evidence;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="AttestationSigner"/>: the incremental update, the byte range, and the CMS signature,
///     checked with <see cref="PdfInspector"/> and <see cref="SignedCms"/> instead of the signer's own code.
/// </summary>
public class AttestationSignerTests
{
    /// <summary>
    ///     The signing time used by every test.
    /// </summary>
    private static readonly DateTimeOffset _signedAt = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Creates an unsigned PDF of a few pages.
    /// </summary>
    /// <param name="lines">The number of body lines.</param>
    /// <returns>The bytes of the unsigned PDF.</returns>
    private static byte[] CreatePdf(int lines = 120)
    {
        var writer = new PdfDocumentWriter(
            new PdfDocumentInfo("Title", "Author", "Subject", "Keywords", "Creator", "Producer", _signedAt),
            "Header",
            "Footer");
        writer.Title("A document");
        for (var line = 1; line <= lines; line++)
        {
            writer.Paragraph($"Line {line}");
        }

        return writer.Build();
    }

    /// <summary>
    ///     Signs a PDF and checks everything that does not depend on the kind of key.
    /// </summary>
    /// <param name="key">The signing key.</param>
    /// <returns>The unsigned bytes, the signed bytes and the decoded signature.</returns>
    private static (byte[] Unsigned, PdfInspector Signed, SignedCms Cms) SignAndVerify(AttestationSigningKey key)
    {
        var unsigned = CreatePdf();
        var signed = new PdfInspector(new AttestationSigner().Sign(unsigned, key, _signedAt, "Reason"));
        var cms = signed.VerifySignature();
        Assert.Equal(key.Info.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
        return (unsigned, signed, cms);
    }

    /// <summary>
    ///     Test that an RSA signature is a detached CMS that verifies over exactly the byte range.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_RsaKey_ProducesVerifiableDetachedSignature()
    {
        // Arrange: a PDF and a self-signed RSA key
        using var key = TestKeys.SelfSignedRsa();

        // Act: sign, then verify the way a reader does
        var (_, signed, cms) = SignAndVerify(key);

        // Assert: the signature is SHA-256 with the signer's certificate, and its time is the signing time
        Assert.True(cms.Detached);
        Assert.Equal("2.16.840.1.101.3.4.2.1", cms.SignerInfos[0].DigestAlgorithm.Value);
        Assert.Contains(
            cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().SelectMany(a => a.Values.OfType<Pkcs9SigningTime>()),
            t => t.SigningTime == _signedAt.UtcDateTime);
        Assert.True(signed.ByteRangeCoversEverythingButContents());
    }

    /// <summary>
    ///     Test that an ECDSA key signs the same way.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_EcdsaKey_ProducesVerifiableDetachedSignature()
    {
        // Arrange: a self-signed ECDSA key
        using var key = TestKeys.SelfSignedEcdsa();

        // Act: sign and verify
        var (_, signed, cms) = SignAndVerify(key);

        // Assert: verifies and covers the whole file
        Assert.True(cms.Detached);
        Assert.True(signed.ByteRangeCoversEverythingButContents());
    }

    /// <summary>
    ///     Test that a key that cannot be exported still signs: the signer uses the key handle and never the key material.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_NonExportableKey_ProducesVerifiableSignature()
    {
        // Arrange: a Windows CNG key that refuses export
        using var key = TestKeys.NonExportableRsa();
        Assert.SkipWhen(key is null, "A non-exportable key can only be created with Windows CNG.");
        var rsa = (RSA)key.PrivateKey;

        // Act: the key cannot be exported, but signing works
        var exportable = Record.Exception(() => rsa.ExportParameters(true));
        var (_, signed, _) = SignAndVerify(key);

        // Assert: export is refused, the signature verifies
        Assert.IsAssignableFrom<CryptographicException>(exportable);
        Assert.True(signed.ByteRangeCoversEverythingButContents());
    }

    /// <summary>
    ///     Test that the signed file keeps every byte of the original: the signature is an incremental update.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_Output_StartsWithTheOriginalBytes()
    {
        // Arrange: a PDF and a key
        using var key = TestKeys.SelfSignedRsa();

        // Act: sign
        var (unsigned, signed, _) = SignAndVerify(key);

        // Assert: the original is an unchanged prefix, and the cross-reference chain has two sections, each consistent
        Assert.Equal(unsigned, signed.Bytes[..unsigned.Length]);
        Assert.Equal(2, signed.CrossReferenceChain.Count);
        Assert.True(signed.CrossReferenceIsConsistent());
        Assert.True(signed.HasPdfFraming);
    }

    /// <summary>
    ///     Test that the signed file declares the signature the way Adobe-compatible readers expect.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_Output_DeclaresAcroFormSignatureFieldAndAdobeFilter()
    {
        // Arrange: a PDF and a key
        using var key = TestKeys.SelfSignedRsa();

        // Act: sign
        var (_, signed, _) = SignAndVerify(key);

        // Assert: form, widget, signature dictionary with the detached PKCS#7 sub-filter, and the pages unchanged
        Assert.Contains("/AcroForm << /Fields [", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/SigFlags 3", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/Subtype /Widget /FT /Sig", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/Reason (Reason)", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/M (D:20250304050607+00'00')", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/Annots [", signed.Text, StringComparison.Ordinal);
        Assert.Contains("/Prev ", signed.Text, StringComparison.Ordinal);
        Assert.True(signed.PageCount > 1);
    }

    /// <summary>
    ///     Test that the signature is a certifying signature: a DocMDP reference with permission 1 (no changes allowed)
    ///     in the signature dictionary, named by the catalog's permissions.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_Output_IsCertifiedWithDocMdpNoChangesAllowed()
    {
        // Arrange: a PDF and a key
        using var key = TestKeys.SelfSignedRsa();

        // Act: sign
        var (_, signed, _) = SignAndVerify(key);

        // Assert: the reference has the DocMDP shape, with permission 1 and not 2
        Assert.Contains(
            "/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams "
            + "<< /Type /TransformParams /P 1 /V /1.2 >> /Data ",
            signed.Text,
            StringComparison.Ordinal);
        Assert.Equal(1, signed.DocMdpPermission);

        // Assert: one signature and one DocMDP entry; the catalog and the signature field refer to the same object
        var signature = Assert.Single(signed.SignatureObjectNumbers);
        Assert.Equal(signature, signed.PermsDocMdpObject);
        Assert.Equal(signature, signed.WidgetSignatureObject);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(signed.Text, "/TransformMethod /DocMDP"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(signed.Text, "/Perms <<"));
    }

    /// <summary>
    ///     Test that the certified signature still verifies over the byte range, for RSA and ECDSA, and that the
    ///     update is a consistent extension of the original.
    /// </summary>
    /// <param name="algorithm">The key algorithm.</param>
    [Theory]
    [InlineData("RSA")]
    [InlineData("ECDSA")]
    public void AttestationSigner_Sign_CertifiedOutput_SignatureStillVerifiesOverTheByteRange(string algorithm)
    {
        // Arrange: a PDF and a key
        using var key = algorithm == "RSA" ? TestKeys.SelfSignedRsa() : TestKeys.SelfSignedEcdsa();

        // Act: sign; SignAndVerify checks the CMS signature over the byte range and the signer's thumbprint
        var (unsigned, signed, cms) = SignAndVerify(key);

        // Assert: the range covers everything but the contents, and the permission is among the signed bytes
        Assert.True(signed.ByteRangeCoversEverythingButContents());
        Assert.Equal(key.Info.Thumbprint, cms.SignerInfos[0].Certificate!.Thumbprint);
        var covered = Encoding.Latin1.GetString(signed.SignedBytes);
        Assert.Contains("/P 1 /V /1.2", covered, StringComparison.Ordinal);
        Assert.Contains("/Perms << /DocMDP ", covered, StringComparison.Ordinal);
        Assert.Equal(unsigned, signed.Bytes[..unsigned.Length]);
        Assert.Equal(2, signed.CrossReferenceChain.Count);
        Assert.True(signed.CrossReferenceIsConsistent());
    }

    /// <summary>
    ///     Test that changing the permission to a weaker one breaks the signature: the permission is signed.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_CertifiedOutput_TamperedPermission_SignatureNoLongerVerifies()
    {
        // Arrange: a signed PDF whose permission is changed from 1 to 2 (same length, so the offsets stay valid)
        using var key = TestKeys.SelfSignedRsa();
        var (_, signed, _) = SignAndVerify(key);
        var tampered = (byte[])signed.Bytes.Clone();
        var at = signed.Text.IndexOf("/P 1 /V /1.2", StringComparison.Ordinal) + "/P ".Length;
        tampered[at] = (byte)'2';

        // Act: verify the tampered file
        var inspector = new PdfInspector(tampered);
        var exception = Record.Exception(() => inspector.VerifySignature());

        // Assert: the file reads as permission 2, but the signature no longer verifies
        Assert.Equal(2, inspector.DocMdpPermission);
        Assert.IsAssignableFrom<CryptographicException>(exception);
    }

    /// <summary>
    ///     Test that the byte range leaves out only the hexadecimal signature contents.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_ByteRange_CoversEverythingExceptTheContentsHex()
    {
        // Arrange: a PDF and a key
        using var key = TestKeys.SelfSignedRsa();

        // Act: sign
        var (_, signed, _) = SignAndVerify(key);
        var range = signed.ByteRange!;

        // Assert: starts at zero, the gap is the placeholder, and the second range reaches the end of the file
        Assert.Equal(0, range[0]);
        Assert.Equal(signed.Bytes.Length, range[2] + range[3]);
        Assert.Equal(signed.ContentsHex.Length + 2, range[2] - range[1]);
        Assert.Equal(range[1] + range[3] + signed.ContentsHex.Length + 2, signed.Bytes.Length);
        Assert.True(signed.ByteRangeCoversEverythingButContents());
    }

    /// <summary>
    ///     Test that changing a single byte of the signed content makes the signature fail.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_TamperedContent_SignatureNoLongerVerifies()
    {
        // Arrange: a signed PDF
        using var key = TestKeys.SelfSignedRsa();
        var (_, signed, _) = SignAndVerify(key);
        var tampered = (byte[])signed.Bytes.Clone();
        var at = signed.Text.IndexOf("Line 7", StringComparison.Ordinal) + "Line ".Length;
        tampered[at] = (byte)'8';

        // Act: verify the tampered file
        var inspector = new PdfInspector(tampered);
        var exception = Record.Exception(() => inspector.VerifySignature());

        // Assert: still has the structure, but the signature does not verify
        Assert.True(inspector.ByteRangeCoversEverythingButContents());
        Assert.IsAssignableFrom<CryptographicException>(exception);
    }

    /// <summary>
    ///     Test that appending to the signed file leaves the signature valid but the byte range short of the file end,
    ///     which is how a reader recognizes later additions.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_DataAppendedAfterSigning_IsNotCoveredByTheByteRange()
    {
        // Arrange: a signed PDF with data appended after the end
        using var key = TestKeys.SelfSignedRsa();
        var (_, signed, _) = SignAndVerify(key);
        var extended = new PdfInspector([.. signed.Bytes, .. Encoding.ASCII.GetBytes("% added later\n")]);

        // Act: verify
        var cms = extended.VerifySignature();

        // Assert: the signature over the original range still verifies, and the range no longer reaches the end
        Assert.NotNull(cms);
        Assert.False(extended.ByteRangeCoversEverythingButContents());
    }

    /// <summary>
    ///     Test that the certificates of the chain are embedded together with the signer's certificate.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_OrganizationKeyWithChain_EmbedsTheChainCertificates()
    {
        // Arrange: a key issued by an authority whose certificate is part of the key's chain
        using var key = TestKeys.Organization();

        // Act: sign
        var (_, _, cms) = SignAndVerify(key);

        // Assert: the signer's certificate and the authority's certificate are embedded
        Assert.Equal(2, cms.Certificates.Count);
        Assert.Contains(cms.Certificates.Cast<X509Certificate2>(), c => c.Subject == "CN=Test organization CA");
    }

    /// <summary>
    ///     Test that the same document and key give a signed file whose original part and signature field are stable
    ///     (RSA PKCS#1 signing is deterministic apart from the signing time, which is fixed here).
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_SameInputAndRsaKey_ProducesIdenticalBytes()
    {
        // Arrange: one PDF and one key
        using var key = TestKeys.SelfSignedRsa();
        var pdf = CreatePdf(20);
        var signer = new AttestationSigner();

        // Act: sign twice
        var first = signer.Sign(pdf, key, _signedAt, "Reason");
        var second = signer.Sign(pdf, key, _signedAt, "Reason");

        // Assert: identical
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Test that a PDF that already carries a signature is refused instead of being damaged.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_AlreadySignedPdf_Throws()
    {
        // Arrange: a signed PDF
        using var key = TestKeys.SelfSignedRsa();
        var (_, signed, _) = SignAndVerify(key);

        // Act: sign it again
        var exception = Record.Exception(() => new AttestationSigner().Sign(signed.Bytes, key, _signedAt, "Again"));

        // Assert: refused, as the file no longer has the layout of the attestation writer
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    ///     Test that bytes that are not a PDF of the attestation writer are refused with a clear exception.
    /// </summary>
    /// <param name="content">The content to sign.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not a pdf at all")]
    [InlineData("%PDF-1.4\nno cross reference here\n%%EOF\n")]
    [InlineData("%PDF-1.4\ntrailer\n<< >>\nstartxref\nabc\n%%EOF\n")]
    public void AttestationSigner_Sign_NotAnAttestationPdf_ThrowsInvalidOperation(string content)
    {
        // Arrange: some text and a key
        using var key = TestKeys.SelfSignedRsa();

        // Act: try to sign it
        var exception = Record.Exception(() => new AttestationSigner().Sign(Encoding.Latin1.GetBytes(content), key, _signedAt, "Reason"));

        // Assert: an invalid-operation exception, not an obscure parser error
        Assert.IsType<InvalidOperationException>(exception);
    }

    /// <summary>
    ///     Test that a signature that does not fit the reserved space is reported as a key problem.
    /// </summary>
    [Fact]
    public void AttestationSigner_Sign_ChainTooLargeForTheReservedSpace_ThrowsKeyException()
    {
        // Arrange: a key with a chain of many distinct certificates
        var rsa = RSA.Create(2048);
        var certificate = TestKeys.SelfSigned(rsa, "CN=Signer");
        var chain = Enumerable.Range(0, 80)
            .Select(n => X509CertificateLoader.LoadCertificate(TestKeys.SelfSigned(rsa, $"CN=Intermediate {n} {new string('x', 40)}").RawData))
            .ToList();
        using var key = AttestationSigningKey.Create("Test", certificate, chain, rsa, false);

        // Act: sign
        var exception = Assert.Throws<AttestationKeyException>(() => new AttestationSigner().Sign(CreatePdf(5), key, _signedAt, "Reason"));

        // Assert: the message says what is too large
        Assert.Contains("too large", exception.Message, StringComparison.Ordinal);
    }
}
