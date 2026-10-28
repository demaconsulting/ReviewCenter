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
using System.Security.Cryptography.X509Certificates;
using DemaConsulting.ReviewCenter.Evidence;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Creates signing keys and certificates for the attestation tests, in memory.
/// </summary>
/// <remarks>
///     Keys are generated per call (never shared), because <see cref="AttestationSigningKey"/> owns and disposes
///     what it is given. Nothing is written to a store or a file.
/// </remarks>
internal static class TestKeys
{
    /// <summary>
    ///     Creates a self-signed RSA signing key.
    /// </summary>
    /// <param name="requireTrusted">Whether the key says the trusted-key policy is in effect.</param>
    /// <param name="usage">The key usage of the certificate.</param>
    /// <returns>The key; the caller disposes it.</returns>
    public static AttestationSigningKey SelfSignedRsa(
        bool requireTrusted = false,
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature)
    {
        var rsa = RSA.Create(2048);
        return Wrap(AttestationOptions.SelfSignedProvider, SelfSigned(rsa, "CN=Test self-signed RSA", usage), [], rsa, requireTrusted);
    }

    /// <summary>
    ///     Creates a self-signed RSA signing key whose certificate lists the given Extended Key Usage purposes.
    /// </summary>
    /// <param name="requireTrusted">Whether the key says the trusted-key policy is in effect.</param>
    /// <param name="purposes">The purpose object identifiers; none means the certificate has no such extension.</param>
    /// <returns>The key; the caller disposes it.</returns>
    public static AttestationSigningKey WithPurposes(bool requireTrusted, params string[] purposes)
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test purposes RSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        if (purposes.Length > 0)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([.. purposes.Select(p => new Oid(p))], false));
        }

        return Wrap("Test", request.CreateSelfSigned(Past, Future), [], rsa, requireTrusted);
    }

    /// <summary>
    ///     Creates a self-signed RSA signing key as the certificate store provider reports it: a self-signed
    ///     certificate that the administrator supplied.
    /// </summary>
    /// <returns>The key; the caller disposes it.</returns>
    public static AttestationSigningKey SelfSignedFromStore()
    {
        var rsa = RSA.Create(2048);
        return Wrap(AttestationOptions.CertificateStoreProvider, SelfSigned(rsa, "CN=Test store self-signed RSA"), [], rsa, false);
    }

    /// <summary>
    ///     Creates a self-signed ECDSA (P-256) signing key.
    /// </summary>
    /// <returns>The key; the caller disposes it.</returns>
    public static AttestationSigningKey SelfSignedEcdsa()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Test self-signed ECDSA", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return Wrap("Test", request.CreateSelfSigned(Past, Future), [], ecdsa, false);
    }

    /// <summary>
    ///     Creates a signing key whose certificate is issued by a separate authority, as an organization key is, and
    ///     whose chain holds the authority certificate.
    /// </summary>
    /// <param name="usage">The key usage of the certificate.</param>
    /// <param name="notAfter">The end of the validity of the certificate.</param>
    /// <param name="purposes">The Extended Key Usage purposes of the certificate; <c>null</c> for none.</param>
    /// <returns>The key; the caller disposes it.</returns>
    public static AttestationSigningKey Organization(
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature,
        DateTimeOffset? notAfter = null,
        string[]? purposes = null)
    {
        var past = Past;
        var future = Future;
        using var authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest("CN=Test organization CA", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var authority = authorityRequest.CreateSelfSigned(past, future);

        var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test organization signer, O=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        if (purposes is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([.. purposes.Select(p => new Oid(p))], false));
        }

        var serial = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var issued = request.Create(authority, past, notAfter ?? future, serial);
        var certificate = issued.CopyWithPrivateKey(rsa);

        // The chain holds a copy of the authority certificate without its private key
        var chain = new[] { X509CertificateLoader.LoadCertificate(authority.RawData) };
        return Wrap("Test", certificate, chain, rsa, true);
    }

    /// <summary>
    ///     Creates a self-signed certificate for a key.
    /// </summary>
    /// <param name="rsa">The key.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="usage">The key usage.</param>
    /// <returns>The certificate with its private key.</returns>
    public static X509Certificate2 SelfSigned(RSA rsa, string subject, X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature)
    {
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        return request.CreateSelfSigned(Past, Future);
    }

    /// <summary>
    ///     Creates a signing key with a key that cannot be exported (a Windows CNG key), when the platform has one.
    /// </summary>
    /// <returns>The key; the caller disposes it. <c>null</c> when the platform cannot create such a key.</returns>
    public static AttestationSigningKey? NonExportableRsa()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        // The certificate is made from an exportable key and imported again without the Exportable flag, which gives
        // a key that is held by the system and cannot be exported; it is not persisted, so nothing is left behind
        using var source = RSA.Create(2048);
        using var created = SelfSigned(source, "CN=Test non-exportable RSA");
        var pfx = created.Export(X509ContentType.Pfx, "test-only");
        var certificate = X509CertificateLoader.LoadPkcs12(pfx, "test-only", X509KeyStorageFlags.EphemeralKeySet);
        var rsa = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("The imported certificate has no RSA key.");
        return Wrap("Test", certificate, [], rsa, false);
    }

    /// <summary>
    ///     Gets the start of the validity of every test certificate.
    /// </summary>
    private static DateTimeOffset Past => DateTimeOffset.UtcNow.AddDays(-2);

    /// <summary>
    ///     Gets the end of the validity of every test certificate unless a test chooses another.
    /// </summary>
    private static DateTimeOffset Future => DateTimeOffset.UtcNow.AddYears(2);

    /// <summary>
    ///     Wraps a certificate and its key as a signing key.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="certificate">The certificate.</param>
    /// <param name="chain">The intermediate certificates.</param>
    /// <param name="key">The private key.</param>
    /// <param name="requireTrusted">Whether the trusted-key policy is in effect.</param>
    /// <returns>The signing key.</returns>
    private static AttestationSigningKey Wrap(
        string provider,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        AsymmetricAlgorithm key,
        bool requireTrusted)
    {
        return AttestationSigningKey.Create(provider, certificate, chain, key, requireTrusted);
    }
}
