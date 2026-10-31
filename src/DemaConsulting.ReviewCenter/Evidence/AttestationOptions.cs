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
///     The settings of the attestation signing key, bound from the <c>Attestation</c> configuration section.
/// </summary>
/// <remarks>
///     The class is a plain settings holder that is only read after start-up. Most settings only say where a key is
///     (a thumbprint, a folder, a file). <see cref="CertificatePassword"/> is the one secret: it is the password of
///     the certificate file of the <c>File</c> provider, it is meant to come from an environment variable or from
///     <see cref="CertificatePasswordFile"/>, and it is never logged, shown or put into a message.
/// </remarks>
internal sealed class AttestationOptions
{
    /// <summary>
    ///     The name of the configuration section the settings are bound from.
    /// </summary>
    public const string SectionName = "Attestation";

    /// <summary>
    ///     The name of the provider that creates a key and certificate for development and test.
    /// </summary>
    public const string SelfSignedProvider = "SelfSigned";

    /// <summary>
    ///     The name of the provider that uses a certificate and private key from an operating system store.
    /// </summary>
    public const string CertificateStoreProvider = "CertificateStore";

    /// <summary>
    ///     The name of the provider that uses a certificate and private key from a PKCS #12 or PEM file.
    /// </summary>
    public const string FileProvider = "File";

    /// <summary>
    ///     The default number of days before the end of the validity of the signing certificate from which a warning is shown.
    /// </summary>
    public const int DefaultExpiryWarningDays = 30;

    /// <summary>
    ///     Gets or sets the key provider: <c>SelfSigned</c> (the default), <c>CertificateStore</c> or <c>File</c>, in
    ///     any letter case.
    /// </summary>
    public string Provider { get; set; } = SelfSignedProvider;

    /// <summary>
    ///     Gets or sets a value indicating whether a key that is self-signed, unsuitable for signing documents or
    ///     outside its validity period is refused. The default is <c>false</c>.
    /// </summary>
    public bool RequireTrustedKey { get; set; }

    /// <summary>
    ///     Gets or sets the SHA-1 thumbprint (hex, as certlm and MMC show it) of the certificate to use; read by
    ///     the <c>CertificateStore</c> provider only.
    /// </summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>
    ///     Gets or sets the store location, <c>CurrentUser</c> (the default) or <c>LocalMachine</c>; read by the
    ///     <c>CertificateStore</c> provider only.
    /// </summary>
    public string StoreLocation { get; set; } = "CurrentUser";

    /// <summary>
    ///     Gets or sets the store name, such as <c>My</c> (the default); read by the <c>CertificateStore</c> provider only.
    /// </summary>
    public string StoreName { get; set; } = "My";

    /// <summary>
    ///     Gets or sets the folder in which the <c>SelfSigned</c> provider keeps its key, or <c>null</c> or empty
    ///     for the default folder beside the Data Protection keys.
    /// </summary>
    public string? SelfSignedFolder { get; set; }

    /// <summary>
    ///     Gets or sets the path of the certificate file of the <c>File</c> provider: a PKCS #12 file (PFX or P12) with
    ///     the certificate, its private key and optionally its intermediate certificates, or a PEM file with the
    ///     certificate (and, when <see cref="PrivateKeyPath"/> is not set, the private key). A file with a PEM header
    ///     is PEM, anything else is PKCS #12. Read by the <c>File</c> provider only.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    ///     Gets or sets the path of a separate PEM file with the private key of the <c>File</c> provider, or
    ///     <c>null</c> or empty when the key is in <see cref="CertificatePath"/>. Read by the <c>File</c> provider only.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    ///     Gets or sets the password of the PKCS #12 file or of the encrypted PEM key of the <c>File</c> provider;
    ///     a secret, which is normally supplied with the environment variable <c>Attestation__CertificatePassword</c>.
    ///     Not set together with <see cref="CertificatePasswordFile"/>.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    ///     Gets or sets the path of a file that holds the password (see <see cref="CertificatePassword"/>), such as a
    ///     systemd credential or a mounted secret. Not set together with <see cref="CertificatePassword"/>.
    /// </summary>
    public string? CertificatePasswordFile { get; set; }

    /// <summary>
    ///     Gets or sets the number of days before the end of the validity of the signing certificate from which the
    ///     server warns (and the page shows a warning); a certificate that has expired is warned about as well. The
    ///     default is 30; 0 switches the warning off. It applies to every provider except <c>SelfSigned</c>, which
    ///     replaces an expired key by itself.
    /// </summary>
    public int ExpiryWarningDays { get; set; } = DefaultExpiryWarningDays;
}
