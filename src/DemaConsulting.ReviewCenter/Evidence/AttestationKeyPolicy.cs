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

using System.Security.Cryptography.X509Certificates;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The rules that decide whether a signing key is acceptable, kept apart from where the key comes from so that
///     every provider is held to the same policy.
/// </summary>
/// <remarks>
///     The policy checks what can be known locally from the certificate: whether it is self-signed, whether its key
///     usage allows signing documents, and whether it is within its validity period. It deliberately does not check
///     that the chain is trusted or that the certificate is not revoked; that is the verifier's decision. A
///     code-signing-only certificate is classified and reported; its code-signing usage is not a reason to refuse it.
/// </remarks>
internal static class AttestationKeyPolicy
{
    /// <summary>
    ///     The note for a code-signing-only certificate; it is logged at start-up and printed in the attestation.
    /// </summary>
    internal const string CodeSigningNotice =
        "This certificate is a code-signing certificate. Adobe Acrobat will not trust it for document signatures; "
        + "use a document-signing certificate for production.";

    /// <summary>
    ///     The object identifier of the Extended Key Usage "code signing" purpose.
    /// </summary>
    private const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";

    /// <summary>
    ///     The object identifiers of the purposes that suit signing a document: e-mail protection (S/MIME), the
    ///     Microsoft document signing purpose, the Adobe document signing purpose and any purpose.
    /// </summary>
    private static readonly string[] DocumentSigningOids =
        ["1.3.6.1.5.5.7.3.4", "1.3.6.1.4.1.311.10.3.12", "1.2.840.113583.1.1.5", "2.5.29.37.0"];

    /// <summary>
    ///     Checks a key against the trusted-key policy.
    /// </summary>
    /// <param name="info">The description of the key.</param>
    /// <param name="requireTrusted">Whether the policy is in effect; when false nothing is checked.</param>
    /// <param name="now">The current time.</param>
    /// <exception cref="AttestationKeyException">
    ///     Thrown when the policy is in effect and the key is self-signed, may not sign documents, or is outside its
    ///     validity period.
    /// </exception>
    public static void Enforce(AttestationKeyInfo info, bool requireTrusted, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!requireTrusted)
        {
            return;
        }

        if (info.IsSelfSigned)
        {
            throw new AttestationKeyException(
                "Attestation:RequireTrustedKey is true but the signing key is self-signed. "
                + "Set Attestation:Provider to CertificateStore or File and use a certificate issued by your organization.");
        }

        if (!info.HasDigitalSignatureUsage)
        {
            throw new AttestationKeyException(
                "Attestation:RequireTrustedKey is true but the certificate's key usage does not allow digital signatures or non-repudiation. "
                + "Use a document-signing certificate (key usage digitalSignature or nonRepudiation).");
        }

        if (now < info.NotBefore || now > info.NotAfter)
        {
            throw new AttestationKeyException(
                $"Attestation:RequireTrustedKey is true but the signing certificate is not valid now (valid {info.NotBefore:u} to {info.NotAfter:u}).");
        }
    }

    /// <summary>
    ///     Tells an operator that the signing certificate is about to expire or has expired, so that it is replaced
    ///     before attestations are no longer accepted.
    /// </summary>
    /// <param name="info">The description of the key.</param>
    /// <param name="now">The current time.</param>
    /// <param name="days">
    ///     The number of days before the end of the validity from which to warn (<c>Attestation:ExpiryWarningDays</c>);
    ///     0 or less switches the warning off.
    /// </param>
    /// <returns>
    ///     The warning, which names the thumbprint and the date; <c>null</c> when the warning is off or the end of the
    ///     validity is more than <paramref name="days"/> days away. The text does not change from day to day, so a log
    ///     can show it when it changes and not every day.
    /// </returns>
    /// <remarks>
    ///     The warning is independent of <c>Attestation:RequireTrustedKey</c>: with the policy a key that has expired
    ///     is refused (<see cref="Enforce"/>), without it the key keeps signing, which the warning must not let pass
    ///     unnoticed. Attestations that were signed earlier carry their own certificate and are not affected.
    /// </remarks>
    public static string? ExpiryWarning(AttestationKeyInfo info, DateTimeOffset now, int days)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (days <= 0)
        {
            return null;
        }

        var date = info.NotAfter.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (now > info.NotAfter)
        {
            return $"The signing certificate {info.Thumbprint} expired on {date} (UTC). "
                   + "Replace it; attestations that were signed earlier are not affected.";
        }

        // A comparison of days as a number: a very large setting must not overflow a time span
        return (info.NotAfter - now).TotalDays <= days
            ? $"The signing certificate {info.Thumbprint} expires on {date} (UTC). Replace it before then."
            : null;
    }

    /// <summary>
    ///     Gets whether a certificate is its own issuer.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>True when the subject and the issuer are the same name.</returns>
    public static bool IsSelfSigned(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);
    }

    /// <summary>
    ///     Gets whether a certificate may be used to sign documents.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>
    ///     True when the key usage extension is absent (no restriction) or allows digital signature or
    ///     non-repudiation (content commitment).
    /// </returns>
    public static bool HasSigningKeyUsage(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return usage is null
            || (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) != 0;
    }

    /// <summary>
    ///     Lists the things about a certificate that do not prevent signing but are likely to disappoint a verifier.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The warnings; empty when there is nothing to say.</returns>
    public static IReadOnlyList<string> Warnings(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var warnings = new List<string>();

        // A certificate that is for code signing only is not meant for documents; readers will not trust it
        if (IsCodeSigningOnly(certificate))
        {
            warnings.Add(CodeSigningNotice);
        }

        return warnings;
    }

    /// <summary>
    ///     Gets whether a certificate is for code signing only.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>
    ///     True when the Extended Key Usage extension lists code signing and none of the document signing purposes
    ///     (e-mail protection, Microsoft or Adobe document signing, any purpose). A certificate without the
    ///     extension has no restriction and is not code-signing-only.
    /// </returns>
    public static bool IsCodeSigningOnly(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var purposes = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault()?.EnhancedKeyUsages;
        return purposes is not null
            && purposes.OfType<System.Security.Cryptography.Oid>().Any(oid => oid.Value == CodeSigningOid)
            && !purposes.OfType<System.Security.Cryptography.Oid>()
                .Any(oid => oid.Value is not null && DocumentSigningOids.Contains(oid.Value));
    }
}
