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
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The production key provider: a certificate and private key from an operating system certificate store,
///     chosen by thumbprint.
/// </summary>
/// <remarks>
///     The private key stays where it is (a non-exportable key, a smart card or a hardware module); the provider
///     keeps a handle and never exports the key or asks it to sign at start-up, so a smart-card PIN is not prompted
///     for until an attestation is created. The account that runs the server needs permission to use the key. The
///     certificate is not checked for chain trust or revocation: that is the decision of whoever verifies the PDF.
/// </remarks>
/// <param name="options">The attestation settings.</param>
/// <param name="time">The clock for the validity check of the trust policy.</param>
/// <param name="log">The logger.</param>
internal sealed class CertificateStoreKeyProvider(AttestationOptions options, TimeProvider time, ILogger log)
    : AttestationKeyProviderBase(options, time, log)
{
    /// <summary>
    ///     The length of a SHA-1 thumbprint in hex characters.
    /// </summary>
    private const int ThumbprintLength = 40;

    /// <inheritdoc />
    public override string Name => AttestationOptions.CertificateStoreProvider;

    /// <summary>
    ///     Reduces a thumbprint as copied from a certificate dialog to plain upper-case hex.
    /// </summary>
    /// <param name="thumbprint">The thumbprint; may contain spaces and the invisible left-to-right mark that the Windows dialog adds.</param>
    /// <returns>The normalized thumbprint.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the thumbprint is empty or not 40 hex characters.</exception>
    public static string NormalizeThumbprint(string? thumbprint)
    {
        var cleaned = new string((thumbprint ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c is not ('\u200E' or '\u200F'))
            .ToArray()).ToUpperInvariant();
        if (cleaned.Length == 0)
        {
            throw new AttestationKeyException("Attestation:CertificateThumbprint is not set; it is required for the CertificateStore provider.");
        }

        if (cleaned.Length != ThumbprintLength || !cleaned.All(Uri.IsHexDigit))
        {
            throw new AttestationKeyException("Attestation:CertificateThumbprint must be a SHA-1 thumbprint of 40 hexadecimal characters.");
        }

        return cleaned;
    }

    /// <inheritdoc />
    protected override AttestationSigningKey Load()
    {
        var thumbprint = NormalizeThumbprint(Options.CertificateThumbprint);
        if (!Enum.TryParse(Options.StoreLocation, true, out StoreLocation location) || !Enum.IsDefined(location))
        {
            throw new AttestationKeyException("Attestation:StoreLocation must be CurrentUser or LocalMachine.");
        }

        if (string.IsNullOrWhiteSpace(Options.StoreName))
        {
            throw new AttestationKeyException("Attestation:StoreName is not set; use a store name such as My.");
        }

        var where = $"{location}\\{Options.StoreName}";
        var certificate = Find(thumbprint, location, where);
        try
        {
            var privateKey = GetPrivateKey(certificate);
            var chain = BuildChain(certificate);

            // Ownership of the certificate passes to the key
            return AttestationSigningKey.Create(Name, certificate, chain, privateKey, Options.RequireTrustedKey);
        }
        catch
        {
            // The key was not created, so the certificate is still owned here
            certificate.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Finds the certificate in the store.
    /// </summary>
    /// <param name="thumbprint">The normalized thumbprint.</param>
    /// <param name="location">The store location.</param>
    /// <param name="where">The store as text, for messages.</param>
    /// <returns>The certificate; the caller owns it.</returns>
    private X509Certificate2 Find(string thumbprint, StoreLocation location, string where)
    {
        try
        {
            using var store = new X509Store(Options.StoreName, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (found.Count == 0)
            {
                throw new AttestationKeyException(
                    $"The certificate with thumbprint {thumbprint} was not found in the store {where}. Check Attestation:CertificateThumbprint, Attestation:StoreLocation and Attestation:StoreName.");
            }

            // Keep the first match and release the rest
            for (var index = 1; index < found.Count; index++)
            {
                found[index].Dispose();
            }

            return found[0];
        }
        catch (Exception ex) when (ex is CryptographicException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            throw new AttestationKeyException(
                $"The certificate store {where} could not be opened. Check Attestation:StoreLocation and Attestation:StoreName and the permissions of the server account.",
                ex);
        }
    }

    /// <summary>
    ///     Gets the private key handle of the certificate.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>An RSA or ECDSA key handle; the caller owns it.</returns>
    private static AsymmetricAlgorithm GetPrivateKey(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new AttestationKeyException(
                "The certificate has no private key. Install the certificate with its private key in the store.");
        }

        try
        {
            AsymmetricAlgorithm? key = certificate.GetRSAPrivateKey() ?? (AsymmetricAlgorithm?)certificate.GetECDsaPrivateKey();
            return key ?? throw new AttestationKeyException(
                "The certificate has a private key that is neither RSA nor ECDSA; only RSA and ECDSA keys are supported.");
        }
        catch (CryptographicException ex)
        {
            throw new AttestationKeyException(
                "The private key of the certificate is not accessible. The account that runs the server needs permission to use the key "
                + "(for example through the key ACL of certlm.msc, or the smart card or hardware module).",
                ex);
        }
    }

    /// <summary>
    ///     Builds the chain of the certificate to find its intermediate certificates.
    /// </summary>
    /// <param name="certificate">The signer certificate.</param>
    /// <returns>Copies of the intermediate certificates; the certificate itself and the root are not included.</returns>
    private static List<X509Certificate2> BuildChain(X509Certificate2 certificate)
    {
        var result = new List<X509Certificate2>();
        using var chain = new X509Chain();

        // Only the intermediates are wanted, not a verdict: no revocation lookup and any outcome is accepted
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.Build(certificate);
        for (var index = 1; index < chain.ChainElements.Count; index++)
        {
            var element = chain.ChainElements[index].Certificate;
            if (!AttestationKeyPolicy.IsSelfSigned(element))
            {
                // A copy that does not depend on the lifetime of the chain
                result.Add(X509CertificateLoader.LoadCertificate(element.RawData));
            }
        }

        return result;
    }
}
