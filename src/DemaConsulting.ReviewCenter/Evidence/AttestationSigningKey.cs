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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     A signing certificate with the handle of its private key, as supplied by an <see cref="IAttestationKeyProvider"/>.
/// </summary>
/// <remarks>
///     The private key is a handle: the signer asks it to sign and the key material is never exported, so it can
///     stay in a non-exportable store key, a smart card or a hardware module. The instance owns the certificates and
///     the key and releases them in <see cref="Dispose"/>; the provider that created it keeps it alive for as long as
///     the application runs.
/// </remarks>
internal sealed class AttestationSigningKey : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AttestationSigningKey"/> class.
    /// </summary>
    /// <param name="certificate">The signer certificate.</param>
    /// <param name="chain">The intermediate certificates.</param>
    /// <param name="privateKey">The private key handle.</param>
    /// <param name="info">The public description.</param>
    private AttestationSigningKey(
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        AsymmetricAlgorithm privateKey,
        AttestationKeyInfo info)
    {
        Certificate = certificate;
        Chain = chain;
        PrivateKey = privateKey;
        Info = info;
    }

    /// <summary>
    ///     Gets the provider that supplied the key.
    /// </summary>
    public string Provider => Info.Provider;

    /// <summary>
    ///     Gets the signer certificate.
    /// </summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>
    ///     Gets the intermediate certificates between the signer certificate and its root; the signer certificate and
    ///     the root are not included. The list is empty when the chain is unknown or there is no intermediate.
    /// </summary>
    public IReadOnlyList<X509Certificate2> Chain { get; }

    /// <summary>
    ///     Gets the private key handle, an <see cref="RSA"/> or an <see cref="ECDsa"/>. It is never exported.
    /// </summary>
    public AsymmetricAlgorithm PrivateKey { get; }

    /// <summary>
    ///     Gets the public description of the key.
    /// </summary>
    public AttestationKeyInfo Info { get; }

    /// <summary>
    ///     Creates the signing key and works out its public description.
    /// </summary>
    /// <param name="provider">The name of the provider that supplied the key.</param>
    /// <param name="certificate">The signer certificate; ownership passes to the new instance.</param>
    /// <param name="chain">The intermediate certificates; ownership passes to the new instance.</param>
    /// <param name="privateKey">An RSA or ECDSA private key handle; ownership passes to the new instance.</param>
    /// <param name="requireTrustedKey">Whether the trusted-key policy is in effect (shown in the description).</param>
    /// <returns>The signing key.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the key is neither RSA nor ECDSA.</exception>
    public static AttestationSigningKey Create(
        string provider,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        AsymmetricAlgorithm privateKey,
        bool requireTrustedKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(privateKey);

        // Only the two algorithms that CMS and PDF readers commonly verify are accepted
        var algorithm = privateKey switch
        {
            RSA rsa => "RSA-" + rsa.KeySize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ECDsa ecdsa => "ECDSA-" + ecdsa.KeySize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new AttestationKeyException("The signing key must be an RSA or ECDSA key; other algorithms are not supported.")
        };

        var info = new AttestationKeyInfo(
            provider,
            certificate.Subject,
            certificate.Issuer,
            certificate.Thumbprint,
            certificate.SerialNumber,
            algorithm,
            new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero),
            new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            AttestationKeyPolicy.IsSelfSigned(certificate),
            AttestationKeyPolicy.HasSigningKeyUsage(certificate),
            requireTrustedKey,
            AttestationKeyPolicy.IsCodeSigningOnly(certificate));
        return new AttestationSigningKey(certificate, chain, privateKey, info);
    }

    /// <summary>
    ///     Releases the private key handle and the certificates.
    /// </summary>
    public void Dispose()
    {
        PrivateKey.Dispose();
        Certificate.Dispose();
        foreach (var certificate in Chain)
        {
            certificate.Dispose();
        }
    }
}
