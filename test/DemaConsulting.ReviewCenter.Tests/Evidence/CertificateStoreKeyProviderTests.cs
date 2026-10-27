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
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="CertificateStoreKeyProvider"/>.
/// </summary>
/// <remarks>
///     The tests that need a certificate install one, with a non-exportable private key, in the CurrentUser\My store
///     for the duration of the class and remove it, and its key, afterwards. They are skipped where that is not
///     possible (macOS, or a store that cannot be written to).
/// </remarks>
public sealed class CertificateStoreKeyProviderTests : IDisposable
{
    /// <summary>
    ///     The password that protects the PFX used to import the test certificate.
    /// </summary>
    private const string PfxPassword = "test-only";

    /// <summary>
    ///     The test certificate as installed in the store, or null when it could not be installed.
    /// </summary>
    private readonly X509Certificate2? _installed;

    /// <summary>
    ///     The reason why the certificate could not be installed, or null when it was.
    /// </summary>
    private readonly string? _unavailable;

    /// <summary>
    ///     Installs the test certificate with a non-exportable key in the CurrentUser\My store, if the platform allows.
    /// </summary>
    public CertificateStoreKeyProviderTests()
    {
        if (OperatingSystem.IsMacOS())
        {
            _unavailable = "The CurrentUser certificate store is not writable on macOS without a prompt.";
            return;
        }

        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=ReviewCenter test attestation key", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            var pfx = ephemeral.Export(X509ContentType.Pfx, PfxPassword);

            // Persisted, so that the key outlives the import and can be found through the store; not exportable
            _installed = X509CertificateLoader.LoadPkcs12(pfx, PfxPassword, X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(_installed);
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _unavailable = "The CurrentUser\\My certificate store cannot be written: " + ex.Message;
            RemoveInstalled();
        }
    }

    /// <summary>
    ///     Removes the certificate from the store and deletes its private key.
    /// </summary>
    public void Dispose()
    {
        RemoveInstalled();
    }

    /// <summary>
    ///     Test that thumbprints as people copy them are reduced to plain upper-case hex.
    /// </summary>
    /// <param name="text">The thumbprint as entered.</param>
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67")]
    [InlineData("\u200E0123456789ABCDEF0123456789ABCDEF01234567")]
    public void CertificateStoreKeyProvider_NormalizeThumbprint_CopiedText_GivesUpperCaseHex(string text)
    {
        // Arrange: nothing beyond the text

        // Act: normalize
        var result = CertificateStoreKeyProvider.NormalizeThumbprint(text);

        // Assert: 40 upper-case hex characters
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF01234567", result);
    }

    /// <summary>
    ///     Test that a missing or malformed thumbprint is refused, naming the setting.
    /// </summary>
    /// <param name="text">The thumbprint as entered.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    [InlineData("zz23456789abcdef0123456789abcdef01234567")]
    public void CertificateStoreKeyProvider_NormalizeThumbprint_MissingOrMalformed_ThrowsNamingTheSetting(string? text)
    {
        // Arrange: nothing beyond the text

        // Act: normalize
        var exception = Assert.Throws<AttestationKeyException>(() => CertificateStoreKeyProvider.NormalizeThumbprint(text));

        // Assert: the setting is named
        Assert.Contains("Attestation:CertificateThumbprint", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a store location that does not exist is reported, naming the setting.
    /// </summary>
    [Fact]
    public void CertificateStoreKeyProvider_GetSigningKey_UnknownStoreLocation_ThrowsNamingTheSetting()
    {
        // Arrange: options with a location that does not exist
        using var provider = NewProvider(new AttestationOptions
        {
            Provider = "CertificateStore",
            CertificateThumbprint = new string('A', 40),
            StoreLocation = "Everywhere",
        });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(() => provider.GetSigningKey());

        // Assert: the setting is named
        Assert.Contains("Attestation:StoreLocation", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate that is not in the store is reported with its thumbprint and the store.
    /// </summary>
    [Fact]
    public void CertificateStoreKeyProvider_GetSigningKey_CertificateNotInStore_ThrowsNamingThumbprintAndStore()
    {
        // Arrange: a thumbprint that no certificate has
        var thumbprint = new string('A', 40);
        using var provider = NewProvider(new AttestationOptions { CertificateThumbprint = thumbprint });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(() => provider.GetSigningKey());

        // Assert: the thumbprint and the store are named, and the problem is described
        Assert.Contains(thumbprint, exception.Message, StringComparison.Ordinal);
        Assert.Contains("CurrentUser\\My", exception.Message, StringComparison.Ordinal);
        var status = provider.Describe();
        Assert.False(status.IsAvailable);
        Assert.Equal("CertificateStore", status.Provider);
        Assert.Equal(exception.Message, status.Problem);
    }

    /// <summary>
    ///     Test that the certificate is found by thumbprint, its key is kept in the store (not exportable) and the
    ///     key signs a PDF that verifies.
    /// </summary>
    [Fact]
    public void CertificateStoreKeyProvider_GetSigningKey_CertificateInStore_FindsItAndSigns()
    {
        // Arrange: the installed certificate
        Assert.SkipWhen(_installed is null, _unavailable ?? "no certificate");
        using var provider = NewProvider(new AttestationOptions { CertificateThumbprint = _installed.Thumbprint.ToLowerInvariant() });

        // Act: ask for the key and sign with it
        var key = provider.GetSigningKey();
        var signed = new PdfInspector(new AttestationSigner().Sign(SamplePdf(), key, DateTimeOffset.UtcNow, "Reason"));

        // Assert: the right certificate, a CertificateStore key that is not self-signed-by-this-server, and a verifying signature
        Assert.Equal(_installed.Thumbprint, key.Info.Thumbprint);
        Assert.Equal("CertificateStore", key.Info.Provider);
        Assert.Equal("CN=ReviewCenter test attestation key", key.Info.Subject);
        if (OperatingSystem.IsWindows())
        {
            // The key stays in the store: it cannot be exported
            Assert.Throws<CryptographicException>(() => ((RSA)key.PrivateKey).ExportParameters(true));
        }

        Assert.Equal(_installed.Thumbprint, signed.VerifySignature().SignerInfos[0].Certificate!.Thumbprint);
        Assert.True(provider.Describe().IsAvailable);
    }

    /// <summary>
    ///     Test that a self-signed certificate from the store is refused when a trusted key is required.
    /// </summary>
    [Fact]
    public void CertificateStoreKeyProvider_GetSigningKey_RequireTrustedKeyAndSelfSignedCertificate_Throws()
    {
        // Arrange: the installed certificate, which is self-signed
        Assert.SkipWhen(_installed is null, _unavailable ?? "no certificate");
        using var provider = NewProvider(new AttestationOptions
        {
            CertificateThumbprint = _installed.Thumbprint,
            RequireTrustedKey = true,
        });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(() => provider.GetSigningKey());

        // Assert: the policy is the reason
        Assert.Contains("Attestation:RequireTrustedKey", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate in the store without its private key is reported as such.
    /// </summary>
    [Fact]
    public void CertificateStoreKeyProvider_GetSigningKey_CertificateWithoutPrivateKey_Throws()
    {
        // Arrange: the public part of the installed certificate, which is installed under another store name
        Assert.SkipWhen(_installed is null, _unavailable ?? "no certificate");
        using var publicOnly = X509CertificateLoader.LoadCertificate(_installed.RawData);
        using (var store = new X509Store(StoreName.TrustedPeople, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadWrite);
            store.Add(publicOnly);
        }

        try
        {
            using var provider = NewProvider(new AttestationOptions
            {
                CertificateThumbprint = publicOnly.Thumbprint,
                StoreName = "TrustedPeople",
            });

            // Act: ask for the key
            var exception = Assert.Throws<AttestationKeyException>(() => provider.GetSigningKey());

            // Assert: the missing private key is reported
            Assert.Contains("no private key", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(publicOnly);
        }
    }

    /// <summary>
    ///     Test that a server configured for the certificate store starts with the certificate, which is what an
    ///     administrator does in production, and resolves its key.
    /// </summary>
    [Fact]
    public void CertificateStoreKeyProvider_GetSigningKey_ConfiguredForTheServer_StartsWithTheCertificate()
    {
        // Arrange: the installed certificate and the settings that name it
        Assert.SkipWhen(_installed is null, _unavailable ?? "no certificate");
        using var context = Context.Create(["serve", "--silent"]);
        var options = new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString(),
                    ["Attestation:Provider"] = "CertificateStore",
                    ["Attestation:CertificateThumbprint"] = _installed.Thumbprint,
                });
            }
        };

        // Act: create the application, which validates the key, and ask for the key
        using var app = ServerHost.CreateApplication(context, options);
        var provider = app.Services.GetRequiredService<IAttestationKeyProvider>();

        // Assert: the certificate of the store is the signing key
        Assert.Equal("CertificateStore", provider.Name);
        Assert.Equal(_installed.Thumbprint, provider.GetSigningKey().Info.Thumbprint);
    }

    /// <summary>
    ///     Creates a provider over the options, with the store defaults of the settings.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The provider.</returns>
    private static CertificateStoreKeyProvider NewProvider(AttestationOptions options)
    {
        return new CertificateStoreKeyProvider(options, TimeProvider.System, NullLogger.Instance);
    }

    /// <summary>
    ///     Builds a small unsigned PDF to sign.
    /// </summary>
    /// <returns>The PDF bytes.</returns>
    private static byte[] SamplePdf()
    {
        var writer = new PdfDocumentWriter(
            new PdfDocumentInfo("T", "A", "S", "K", "C", "P", DateTimeOffset.UtcNow), "header", "footer");
        writer.Paragraph("Certificate store test");
        return writer.Build();
    }

    /// <summary>
    ///     Removes the certificate from the store and deletes its key, if it was installed.
    /// </summary>
    private void RemoveInstalled()
    {
        if (_installed is null)
        {
            return;
        }

        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(_installed);
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the certificate is reported by the leftover check of the test run
        }

        DeleteKey(_installed);
        _installed.Dispose();
    }

    /// <summary>
    ///     Deletes the persisted private key of a certificate, where the platform keeps one that outlives the certificate.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    private static void DeleteKey(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is RSACng cng)
        {
            cng.Key.Delete();
        }
    }
}
