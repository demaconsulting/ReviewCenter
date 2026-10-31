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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     What the attestation, the page and the audit event show about the signing key: everything public, nothing secret.
/// </summary>
/// <param name="Provider">The provider that supplied the key (<c>SelfSigned</c>, <c>CertificateStore</c> or <c>File</c>).</param>
/// <param name="Subject">The certificate subject.</param>
/// <param name="Issuer">The certificate issuer.</param>
/// <param name="Thumbprint">The SHA-1 thumbprint in upper-case hex, as certlm and MMC show it.</param>
/// <param name="SerialNumber">The certificate serial number in hex.</param>
/// <param name="Algorithm">The key algorithm and size, such as <c>RSA-3072</c> or <c>ECDSA-256</c>.</param>
/// <param name="NotBefore">The start of the certificate validity.</param>
/// <param name="NotAfter">The end of the certificate validity.</param>
/// <param name="IsSelfSigned">Whether the certificate is its own issuer.</param>
/// <param name="HasDigitalSignatureUsage">
///     Whether the certificate may be used to sign documents: the key usage extension is absent or allows digital
///     signature or non-repudiation.
/// </param>
/// <param name="RequireTrustedKey">Whether the <c>Attestation:RequireTrustedKey</c> policy is in effect.</param>
/// <param name="IsCodeSigningOnly">
///     Whether the certificate is for code signing only (see <see cref="AttestationKeyPolicy.IsCodeSigningOnly"/>);
///     such a certificate is used, but PDF readers do not trust it for document signatures.
/// </param>
internal sealed record AttestationKeyInfo(
    string Provider,
    string Subject,
    string Issuer,
    string Thumbprint,
    string SerialNumber,
    string Algorithm,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsSelfSigned,
    bool HasDigitalSignatureUsage,
    bool RequireTrustedKey,
    bool IsCodeSigningOnly = false);
