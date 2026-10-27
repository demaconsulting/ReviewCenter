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
///     The class is a plain settings holder that is only read after start-up. It never holds a secret: a key is
///     found by a thumbprint or a folder, never by a password.
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
    ///     Gets or sets the key provider: <c>SelfSigned</c> (the default) or <c>CertificateStore</c>, in any letter case.
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
}
