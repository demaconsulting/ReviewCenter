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
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
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
    ///     Test how certificates are classified by their Extended Key Usage, and that the warning agrees and has the
    ///     defined text.
    /// </summary>
    /// <param name="purposeOids">The purposes of the certificate, separated by semicolons; empty for no extension.</param>
    /// <param name="expected">Whether the certificate is for code signing only.</param>
    [Theory]
    [InlineData("1.3.6.1.5.5.7.3.3", true)]
    [InlineData("1.3.6.1.5.5.7.3.3;1.3.6.1.5.5.7.3.1", true)]
    [InlineData("1.3.6.1.4.1.311.10.3.12", false)]
    [InlineData("1.2.840.113583.1.1.5", false)]
    [InlineData("1.3.6.1.5.5.7.3.4", false)]
    [InlineData("2.5.29.37.0", false)]
    [InlineData("", false)]
    [InlineData("1.3.6.1.5.5.7.3.3;1.3.6.1.4.1.311.10.3.12", false)]
    [InlineData("1.3.6.1.5.5.7.3.3;1.3.6.1.5.5.7.3.4", false)]
    public void AttestationKeyPolicy_IsCodeSigningOnly_ExtendedKeyUsage_ClassifiesCertificates(string purposeOids, bool expected)
    {
        // Arrange: a certificate with those purposes (or none)
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Purpose test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (purposeOids.Length > 0)
        {
            var purposes = purposeOids.Split(';').Select(oid => new Oid(oid)).ToArray();
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([.. purposes], false));
        }

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Act: classify and ask for warnings
        var result = AttestationKeyPolicy.IsCodeSigningOnly(certificate);
        var warnings = AttestationKeyPolicy.Warnings(certificate);

        // Assert: classified as expected; the warning is exactly the defined notice, and only for code signing only
        Assert.Equal(expected, result);
        Assert.Equal(expected ? [AttestationKeyPolicy.CodeSigningNotice] : [], warnings);
    }

    /// <summary>
    ///     Test that the notice has the defined wording.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_CodeSigningNotice_HasTheDefinedWording()
    {
        // Assert: the text that the log and the attestation show
        Assert.Equal(
            "This certificate is a code-signing certificate. Adobe Acrobat will not trust it for document signatures; "
            + "use a document-signing certificate for production.",
            AttestationKeyPolicy.CodeSigningNotice);
    }

    /// <summary>
    ///     Test that the key description tells whether the certificate is for code signing only.
    /// </summary>
    [Fact]
    public void AttestationSigningKey_Create_CodeSigningOnlyCertificate_SetsInfoFlag()
    {
        // Arrange: keys with code signing only, no purposes, and the default test key
        using var codeSigning = TestKeys.WithPurposes(false, "1.3.6.1.5.5.7.3.3");
        using var noPurpose = TestKeys.WithPurposes(false);
        using var defaultKey = TestKeys.SelfSignedRsa();

        // Assert: only the first is flagged
        Assert.True(codeSigning.Info.IsCodeSigningOnly);
        Assert.False(noPurpose.Info.IsCodeSigningOnly);
        Assert.False(defaultKey.Info.IsCodeSigningOnly);
    }

    /// <summary>
    ///     Test that a code-signing-only certificate is warned about once, at first load, and not refused for its
    ///     purpose, also when a trusted key is required: that policy does not look at the purpose.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderBase_GetSigningKey_CodeSigningOnlyCertificate_LogsWarningOnceAndDoesNotRefuse()
    {
        // Arrange: a provider whose key was issued by an authority and is for code signing only; the policy is on
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var provider = new FixedKeyProvider(
            TestKeys.Organization(purposes: ["1.3.6.1.5.5.7.3.3"]),
            new AttestationOptions { RequireTrustedKey = true },
            factory.CreateLogger("test"));

        // Act: ask twice
        var first = provider.GetSigningKey();
        var second = provider.GetSigningKey();

        // Assert: the same key is returned both times, and the warning with the thumbprint is logged once
        Assert.Same(first, second);
        Assert.True(first.Info.IsCodeSigningOnly);
        var warning = Assert.Single(
            logs.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains(AttestationKeyPolicy.CodeSigningNotice, StringComparison.Ordinal));
        Assert.Contains(first.Info.Thumbprint, warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A provider that returns a key it was given, to test the part that every provider shares.
    /// </summary>
    /// <param name="key">The key to return.</param>
    /// <param name="options">The attestation settings.</param>
    /// <param name="log">The logger.</param>
    /// <param name="time">The clock; the system clock when not given.</param>
    private sealed class FixedKeyProvider(
        AttestationSigningKey key, AttestationOptions options, ILogger log, TimeProvider? time = null)
        : AttestationKeyProviderBase(options, time ?? TimeProvider.System, log)
    {
        /// <inheritdoc />
        public override string Name => "Fixed";

        /// <inheritdoc />
        protected override AttestationSigningKey Load() => key;
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
        using var provider = (IDisposable)AttestationKeyProviderFactory.Create(
            options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: true);

        // Assert: the self-signed provider
        Assert.IsType<SelfSignedKeyProvider>(provider);
    }

    /// <summary>
    ///     Test that a provider that the factory creates without the right to create a key reports a missing key as
    ///     one that will be created, and creates no folder and no file.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderFactory_Create_CreationNotAllowed_ProviderCreatesNothing()
    {
        // Arrange: options with a folder that does not exist, and a provider that may not create the key
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterNoCreate-" + Guid.NewGuid().ToString("N"));
        var options = new AttestationOptions { SelfSignedFolder = folder };
        var provider = AttestationKeyProviderFactory.Create(
            options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: false);
        using var owner = (IDisposable)provider;

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: it will be created at the first start, and the folder does not exist
        Assert.True(exception.WillBeCreated);
        Assert.False(Directory.Exists(folder));
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
        using var provider = (IDisposable)AttestationKeyProviderFactory.Create(
            options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: true);

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
            () => AttestationKeyProviderFactory.Create(
                options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: true));

        // Assert: the message names the setting and the supported values
        Assert.Contains("Attestation:Provider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Pfx'", exception.Message, StringComparison.Ordinal);
        Assert.EndsWith("Use SelfSigned, CertificateStore or File.", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the file provider is selected by name, whatever the case, and receives the encryption requirement.
    /// </summary>
    /// <param name="name">The configured name.</param>
    [Theory]
    [InlineData("File")]
    [InlineData("file")]
    public void AttestationKeyProviderFactory_Create_File_SelectsFileProvider(string name)
    {
        // Arrange: options with the name
        var options = new AttestationOptions { Provider = name };

        // Act: create the provider
        using var provider = (IDisposable)AttestationKeyProviderFactory.Create(
            options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: true);

        // Assert: the file provider
        var file = Assert.IsType<FileKeyProvider>(provider);
        Assert.Equal("File", file.Name);
    }

    /// <summary>
    ///     Test that a negative number of warning days is refused, naming the setting and the value, for every provider.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderFactory_Create_NegativeExpiryWarningDays_ThrowsNamingTheSetting()
    {
        // Arrange: options with a number of days that is not valid
        var options = new AttestationOptions { ExpiryWarningDays = -5 };

        // Act: create the provider
        var exception = Assert.Throws<AttestationKeyException>(
            () => AttestationKeyProviderFactory.Create(
                options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: true));

        // Assert: the message names the setting and the value
        Assert.Contains("Attestation:ExpiryWarningDays", exception.Message, StringComparison.Ordinal);
        Assert.Contains("-5", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the expiry warning names the thumbprint and the date, and appears for a certificate that ends
    ///     within the number of days and for one that has ended.
    /// </summary>
    /// <param name="daysUntilEnd">The days from now to the end of the validity; negative when it has ended.</param>
    /// <param name="expectedWord">The word the warning uses.</param>
    [Theory]
    [InlineData(10, "expires")]
    [InlineData(30, "expires")]
    [InlineData(-1, "expired")]
    [InlineData(-400, "expired")]
    public void AttestationKeyPolicy_ExpiryWarning_EndsWithinDaysOrHasEnded_NamesThumbprintAndDate(int daysUntilEnd, string expectedWord)
    {
        // Arrange: a key, and a time that is the given number of days before the end of its certificate
        using var key = TestKeys.Organization();
        var now = key.Info.NotAfter.AddDays(-daysUntilEnd);

        // Act: ask for the warning with the default 30 days
        var warning = AttestationKeyPolicy.ExpiryWarning(key.Info, now, 30);

        // Assert: it names the thumbprint and the date of the end
        Assert.NotNull(warning);
        Assert.Contains(expectedWord, warning, StringComparison.Ordinal);
        Assert.Contains(key.Info.Thumbprint, warning, StringComparison.Ordinal);
        Assert.Contains(key.Info.NotAfter.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), warning, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that there is no expiry warning for a certificate that ends later than the number of days, and none at
    ///     all when the number of days is 0, even for a certificate that has ended.
    /// </summary>
    [Fact]
    public void AttestationKeyPolicy_ExpiryWarning_FarAwayOrDisabled_ReturnsNull()
    {
        // Arrange: a key, a time 90 days before its end, and a time 5 days after it
        using var key = TestKeys.Organization();
        var before = key.Info.NotAfter.AddDays(-90);
        var after = key.Info.NotAfter.AddDays(5);

        // Act: ask with 30 days, and with the warning switched off
        var farWarning = AttestationKeyPolicy.ExpiryWarning(key.Info, before, 30);
        var disabledWarning = AttestationKeyPolicy.ExpiryWarning(key.Info, after, 0);

        // Assert: nothing to warn about
        Assert.Null(farWarning);
        Assert.Null(disabledWarning);
    }

    /// <summary>
    ///     Test that a certificate that has expired is warned about, and the key is still used, when the trusted-key
    ///     policy is off, and that the warning is logged when it appears and not on every call.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderBase_GetSigningKey_ExpiredKeyWithoutPolicy_WarnsOnceAndStillSigns()
    {
        // Arrange: a provider whose certificate ended five days before the clock; the policy is off
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var key = TestKeys.Organization();
        using var provider = new FixedKeyProvider(
            key,
            new AttestationOptions(),
            factory.CreateLogger("test"),
            new FixedTimeProvider(key.Info.NotAfter.AddDays(5)));

        // Act: ask twice, then describe the key
        var first = provider.GetSigningKey();
        var second = provider.GetSigningKey();
        var status = provider.Describe();

        // Assert: the key is returned, one warning was logged, and the status carries it
        Assert.Same(first, second);
        var warning = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("expired", StringComparison.Ordinal));
        Assert.Contains(first.Info.Thumbprint, warning.Message, StringComparison.Ordinal);
        Assert.True(status.IsAvailable);
        Assert.Single(status.Warnings, w => w.Contains("expired", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a certificate that ends soon is warned about in the status, and that the status of a certificate
    ///     that ends later has no warning.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderBase_Describe_EndsSoonOrLater_WarnsOnlyWhenSoon()
    {
        // Arrange: one provider whose clock is ten days before the end of its certificate and one a year before
        var soonKey = TestKeys.Organization();
        var laterKey = TestKeys.Organization();
        using var soon = new FixedKeyProvider(
            soonKey, new AttestationOptions(), NullLogger.Instance, new FixedTimeProvider(soonKey.Info.NotAfter.AddDays(-10)));
        using var later = new FixedKeyProvider(
            laterKey, new AttestationOptions(), NullLogger.Instance, new FixedTimeProvider(laterKey.Info.NotAfter.AddDays(-365)));

        // Act: describe both
        var soonStatus = soon.Describe();
        var laterStatus = later.Describe();

        // Assert: only the first has a warning
        Assert.Single(soonStatus.Warnings, w => w.Contains("expires", StringComparison.Ordinal));
        Assert.Empty(laterStatus.Warnings);
    }

    /// <summary>
    ///     Test that the self-signed provider does not add an expiry warning of its own to the one it logs when it
    ///     replaces a key that has expired, however long the warning period is.
    /// </summary>
    [Fact]
    public void AttestationKeyProviderFactory_Create_SelfSigned_StatusHasNoExpiryWarning()
    {
        // Arrange: a self-signed provider with a warning period that is longer than any validity
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterSelfSignedWarn-" + Guid.NewGuid().ToString("N"));
        var options = new AttestationOptions { SelfSignedFolder = folder, ExpiryWarningDays = 1_000_000 };
        try
        {
            using var provider = (IDisposable)AttestationKeyProviderFactory.Create(
                options, TimeProvider.System, NullLoggerFactory.Instance, new EphemeralDataProtectionProvider(), allowCreate: true);

            // Act: describe the key
            var status = ((IAttestationKeyProvider)provider).Describe();

            // Assert: available, and nothing to warn about
            Assert.True(status.IsAvailable);
            Assert.Empty(status.Warnings);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
