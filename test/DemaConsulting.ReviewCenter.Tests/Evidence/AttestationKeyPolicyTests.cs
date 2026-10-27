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
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="AttestationKeyPolicy"/> and <see cref="AttestationKeyProviderFactory"/>: the rules
///     that hold for every key provider, and the choice of the provider from the settings.
/// </summary>
public class AttestationKeyPolicyTests
{
    /// <summary>
    ///     Test that nothing is checked while the trusted-key policy is off, so a self-signed key is accepted.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_Enforce_PolicyOff_AcceptsSelfSignedKey()
    {
        // Arrange: a self-signed key
        using var key = TestKeys.SelfSignedRsa();

        // Act: enforce with the policy off
        var exception = Record.Exception(() => AttestationKeyPolicy.Enforce(key.Info, false, DateTimeOffset.UtcNow));

        // Assert: accepted
        Assert.Null(exception);
        Assert.True(key.Info.IsSelfSigned);
    }

    /// <summary>
    ///     Test that a self-signed key is refused when a trusted key is required, naming the setting.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndSelfSigned_Throws()
    {
        // Arrange: a self-signed key
        using var key = TestKeys.SelfSignedRsa(requireTrusted: true);

        // Act: enforce with the policy on
        var exception = Assert.Throws<AttestationKeyException>(() => AttestationKeyPolicy.Enforce(key.Info, true, DateTimeOffset.UtcNow));

        // Assert: the message names the setting and the reason
        Assert.Contains("Attestation:RequireTrustedKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("self-signed", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate from an authority, valid now and allowed to sign, passes the policy.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndOrganizationKey_Accepts()
    {
        // Arrange: a key issued by an authority
        using var key = TestKeys.Organization();

        // Act: enforce with the policy on
        var exception = Record.Exception(() => AttestationKeyPolicy.Enforce(key.Info, true, DateTimeOffset.UtcNow));

        // Assert: accepted
        Assert.Null(exception);
        Assert.False(key.Info.IsSelfSigned);
        Assert.True(key.Info.HasDigitalSignatureUsage);
    }

    /// <summary>
    ///     Test that a certificate whose key usage does not allow signing is refused when a trusted key is required.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndNoSigningUsage_Throws()
    {
        // Arrange: a key whose certificate is for key encipherment only
        using var key = TestKeys.Organization(X509KeyUsageFlags.KeyEncipherment);

        // Act: enforce with the policy on
        var exception = Assert.Throws<AttestationKeyException>(() => AttestationKeyPolicy.Enforce(key.Info, true, DateTimeOffset.UtcNow));

        // Assert: the key usage is the reason
        Assert.Contains("key usage", exception.Message, StringComparison.Ordinal);
        Assert.False(key.Info.HasDigitalSignatureUsage);
    }

    /// <summary>
    ///     Test that an expired certificate and one that is not yet valid are refused when a trusted key is required.
    /// </summary>
    /// <param name="daysFromNow">The day, relative to now, at which the policy is checked.</param>
    [Theory]
    [InlineData(-1000)]
    [InlineData(5000)]
    public void AttestationKeyPolicy_Enforce_TrustedKeyRequiredAndOutsideValidity_Throws(int daysFromNow)
    {
        // Arrange: a key issued by an authority, checked long before or after its validity
        using var key = TestKeys.Organization();

        // Act: enforce at that time
        var exception = Assert.Throws<AttestationKeyException>(
            () => AttestationKeyPolicy.Enforce(key.Info, true, DateTimeOffset.UtcNow.AddDays(daysFromNow)));

        // Assert: the validity is the reason
        Assert.Contains("not valid now", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test which key usages count as allowing documents to be signed.
    /// </summary>
    /// <param name="usage">The key usage of the certificate.</param>
    /// <param name="expected">Whether signing is allowed.</param>
    [Theory]
    [InlineData(X509KeyUsageFlags.DigitalSignature, true)]
    [InlineData(X509KeyUsageFlags.NonRepudiation, true)]
    [InlineData(X509KeyUsageFlags.KeyEncipherment, false)]
    public void AttestationKeyPolicy_HasSigningKeyUsage_Usage_AllowsOnlyDigitalSignatureOrNonRepudiation(X509KeyUsageFlags usage, bool expected)
    {
        // Arrange: a certificate with the usage
        using var rsa = RSA.Create(2048);
        using var certificate = TestKeys.SelfSigned(rsa, "CN=Usage test", usage);

        // Act: ask
        var result = AttestationKeyPolicy.HasSigningKeyUsage(certificate);

        // Assert: as expected
        Assert.Equal(expected, result);
    }

    /// <summary>
    ///     Test that a certificate without a key usage extension is not restricted.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_HasSigningKeyUsage_NoExtension_AllowsSigning()
    {
        // Arrange: a certificate without key usage
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=No usage", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Act: ask
        var result = AttestationKeyPolicy.HasSigningKeyUsage(certificate);

        // Assert: nothing restricts it
        Assert.True(result);
    }

    /// <summary>
    ///     Test that a certificate for code signing only is warned about, and one that also allows documents is not.
    /// </summary>
    /// <param name="purposeOid">The extended key usage that the certificate has.</param>
    /// <param name="expectWarning">Whether a warning is expected.</param>
    [Theory]
    [InlineData("1.3.6.1.5.5.7.3.3", true)]
    [InlineData("1.3.6.1.5.5.7.3.4", false)]
    [InlineData("1.2.840.113583.1.1.5", false)]
    public void AttestationKeyPolicy_Warnings_ExtendedKeyUsage_WarnsOnlyForCodeSigningOnly(string purposeOid, bool expectWarning)
    {
        // Arrange: a certificate with that purpose
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Purpose test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(purposeOid)], false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Act: ask for warnings
        var warnings = AttestationKeyPolicy.Warnings(certificate);

        // Assert: a warning only for code signing alone
        Assert.Equal(expectWarning, warnings.Count > 0);
    }

    /// <summary>
    ///     Test that a blank provider name selects the self-signed provider, whatever the case of a name.
    /// </summary>
    /// <param name="name">The configured name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("SelfSigned")]
    [InlineData("selfsigned")]
    public void AttestationKeyProviderFactory_Create_BlankOrSelfSigned_SelectsSelfSignedProvider(string name)
    {
        // Arrange: options with the name
        var options = new AttestationOptions { Provider = name };

        // Act: create the provider
        using var provider = (IDisposable)AttestationKeyProviderFactory.Create(options, TimeProvider.System, NullLoggerFactory.Instance);

        // Assert: the self-signed provider
        Assert.IsType<SelfSignedKeyProvider>(provider);
    }

    /// <summary>
    ///     Test that the certificate store provider is selected by name, whatever the case.
    /// </summary>
    /// <param name="name">The configured name.</param>
    [Theory]
    [InlineData("CertificateStore")]
    [InlineData("certificatestore")]
    public void AttestationKeyProviderFactory_Create_CertificateStore_SelectsCertificateStoreProvider(string name)
    {
        // Arrange: options with the name
        var options = new AttestationOptions { Provider = name };

        // Act: create the provider
        using var provider = (IDisposable)AttestationKeyProviderFactory.Create(options, TimeProvider.System, NullLoggerFactory.Instance);

        // Assert: the certificate store provider
        Assert.IsType<CertificateStoreKeyProvider>(provider);
    }

    /// <summary>
    ///     Test that a provider name that is not supported is refused, naming the setting and the choices.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderFactory_Create_UnknownProvider_ThrowsNamingTheSetting()
    {
        // Arrange: options with a provider that does not exist
        var options = new AttestationOptions { Provider = "Pfx" };

        // Act: create the provider
        var exception = Assert.Throws<AttestationKeyException>(
            () => AttestationKeyProviderFactory.Create(options, TimeProvider.System, NullLoggerFactory.Instance));

        // Assert: the message names the setting and the supported values
        Assert.Contains("Attestation:Provider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Pfx'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SelfSigned", exception.Message, StringComparison.Ordinal);
        Assert.Contains("CertificateStore", exception.Message, StringComparison.Ordinal);
    }
}
