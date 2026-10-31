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

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="SelfSignedKeyProvider"/>: creating, persisting and reloading the default key, its
///     protection at rest, the trusted-key policy, and the errors an administrator has to understand.
/// </summary>
public sealed class SelfSignedKeyProviderTests : IDisposable
{
    /// <summary>
    ///     The key ring that protects the key files of the tests, unless a test needs another one.
    /// </summary>
    private static readonly IDataProtectionProvider Ring = new EphemeralDataProtectionProvider();

    /// <summary>
    ///     The folders that the tests created, removed when the test class is disposed.
    /// </summary>
    private readonly List<string> _folders = [];

    /// <summary>
    ///     Removes the temporary folders.
    /// </summary>
    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
            else if (File.Exists(folder))
            {
                File.Delete(folder);
            }
        }
    }

    /// <summary>
    ///     Test that the first use creates the key file, and that the key is a self-signed RSA key that says it is
    ///     not an organization key.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_FirstUse_CreatesSelfSignedKeyFile()
    {
        // Arrange: an empty folder that does not exist yet
        var folder = NewFolder();
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);

        // Act: ask for the key
        var key = provider.GetSigningKey();

        // Assert: the file exists, the key is self-signed and RSA, and the subject says what it is
        Assert.True(File.Exists(Path.Combine(folder, SelfSignedKeyProvider.FileName)));
        Assert.True(key.Info.IsSelfSigned);
        Assert.Equal("SelfSigned", key.Info.Provider);
        Assert.Equal("RSA-3072", key.Info.Algorithm);
        Assert.Contains("not an organization key", key.Info.Subject, StringComparison.Ordinal);
        Assert.True(key.Info.HasDigitalSignatureUsage);
    }

    /// <summary>
    ///     Test that the certificate is valid from shortly before the creation time for about ten years.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_FirstUse_SetsTheValidityPeriod()
    {
        // Arrange: a fixed clock
        var now = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);
        using var provider = new SelfSignedKeyProvider(Options(NewFolder()), new FixedTimeProvider(now), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);

        // Act: create the key
        var info = provider.GetSigningKey().Info;

        // Assert: valid from ten minutes before (to the second) for ten years
        Assert.InRange((now - info.NotBefore).TotalMinutes, 9.9, 10.1);
        Assert.InRange((info.NotAfter - now).TotalDays, 3649, 3651);
    }

    /// <summary>
    ///     Test that a later provider, as after a restart, loads the same key instead of creating a new one, and that
    ///     the loaded key signs.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_ExistingFile_ReloadsTheSameKeyAndSigns()
    {
        // Arrange: a folder with a key created by a first provider
        var folder = NewFolder();
        string thumbprint;
        using (var first = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring))
        {
            thumbprint = first.GetSigningKey().Info.Thumbprint;
        }

        using var second = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);

        // Act: a second provider loads the key, and signs with it
        var key = second.GetSigningKey();
        var writer = new PdfDocumentWriter(
            new PdfDocumentInfo("T", "A", "S", "K", "C", "P", DateTimeOffset.UtcNow), "h", "f");
        writer.Paragraph("text");
        var signed = new PdfInspector(new AttestationSigner().Sign(writer.Build(), key, DateTimeOffset.UtcNow, "Reason"));

        // Assert: the same certificate, one file, and the signature verifies
        Assert.Equal(thumbprint, key.Info.Thumbprint);
        Assert.Single(Directory.GetFiles(folder));
        Assert.Equal(thumbprint, signed.VerifySignature().SignerInfos[0].Certificate!.Thumbprint);
    }

    /// <summary>
    ///     Test that the key is kept: repeated calls give the same key object.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_RepeatedCalls_ReturnTheSameKey()
    {
        // Arrange: a provider
        using var provider = new SelfSignedKeyProvider(Options(NewFolder()), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);

        // Act: ask twice
        var first = provider.GetSigningKey();
        var second = provider.GetSigningKey();

        // Assert: the same instance
        Assert.Same(first, second);
    }

    /// <summary>
    ///     Test that providers that start at the same time on the same folder end up with one key, one file, and
    ///     no temporary file left behind.
    /// </summary>
    [Fact]
    public async Task SelfSignedKeyProvider_GetSigningKey_ConcurrentFirstUse_CreatesOneKeyForEveryone()
    {
        // Arrange: four providers on one empty folder, each created and disposed by the task that uses it
        var folder = NewFolder();

        // Act: all ask at once
        var thumbprints = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () =>
            {
                using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);
                return provider.GetSigningKey().Info.Thumbprint;
            },
            TestContext.Current.CancellationToken)));

        // Assert: one thumbprint, one file, no leftovers
        Assert.Single(thumbprints.Distinct());
        Assert.Equal(new[] { SelfSignedKeyProvider.FileName }, Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    /// <summary>
    ///     Test that a self-signed key is accepted by default and refused when a trusted key is required, and that
    ///     the policy is checked again on every call.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_RequireTrustedKey_IsCheckedOnEveryCall()
    {
        // Arrange: a provider that already has its key
        var options = Options(NewFolder());
        using var provider = new SelfSignedKeyProvider(options, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);
        Assert.NotNull(provider.GetSigningKey());

        // Act: the policy is switched on, asked, and switched off again
        options.RequireTrustedKey = true;
        var refused = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);
        var status = provider.Describe();
        options.RequireTrustedKey = false;
        var accepted = provider.GetSigningKey();

        // Assert: refused with the reason while on, not remembered afterwards
        Assert.Contains("self-signed", refused.Message, StringComparison.Ordinal);
        Assert.False(status.IsAvailable);
        Assert.Equal(refused.Message, status.Problem);
        Assert.Null(status.Info);
        Assert.NotNull(accepted);
        Assert.True(provider.Describe().IsAvailable);
    }

    /// <summary>
    ///     Test that the key that was found is logged with a warning that it is self-signed, and that no private
    ///     material is logged.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_FirstUse_LogsSelfSignedWarningWithoutSecrets()
    {
        // Arrange: a provider with a capturing logger
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var provider = new SelfSignedKeyProvider(Options(NewFolder()), TimeProvider.System, factory.CreateLogger("test"), Ring);

        // Act: create the key
        var key = provider.GetSigningKey();

        // Assert: a warning about the key, the thumbprint is logged, nothing else of the key
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("self-signed", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Message.Contains(key.Info.Thumbprint, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Test that a missing folder setting is reported, naming the setting.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_FolderNotSet_ThrowsNamingTheSetting()
    {
        // Arrange: options without a folder
        using var provider = new SelfSignedKeyProvider(
            new AttestationOptions { SelfSignedFolder = " " },
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Ring);

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the setting is named
        Assert.Contains("Attestation:SelfSignedFolder", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a folder that cannot be created is reported in terms an administrator can act on.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_FolderIsAFile_ThrowsNamingTheSetting()
    {
        // Arrange: a file where the folder should be
        var folder = NewFolder();
        File.WriteAllText(folder, "not a folder");
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the folder and the setting are named, and the failure is not remembered
        Assert.Contains(folder, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Attestation:SelfSignedFolder", exception.Message, StringComparison.Ordinal);
        Assert.False(provider.Describe().IsAvailable);
    }

    /// <summary>
    ///     Test that a key file that cannot be read is reported, naming the file and the remedy.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_CorruptFile_ThrowsNamingTheFile()
    {
        // Arrange: a folder with a file that is not a key
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, SelfSignedKeyProvider.FileName);
        File.WriteAllText(path, "this is not a PFX file");
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the file is named, with the remedy; the file is left alone
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("delete the file", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.IsUnreadable);
        Assert.Equal("this is not a PFX file", File.ReadAllText(path));
    }

    /// <summary>
    ///     Test that an unexpected failure while the key is loaded does not escape <c>Describe</c>: the page that
    ///     shows the key status must not break, and the message holds no path.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_Describe_UnexpectedFailure_ReportsUnavailableWithoutDetail()
    {
        // Arrange: a folder name that the platform refuses with an exception that is not an attestation key error
        var folder = Path.Combine(Path.GetTempPath(), "bad\0folder");
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);
        Assert.IsNotType<AttestationKeyException>(Record.Exception(provider.GetSigningKey));

        // Act: describe the key
        var status = provider.Describe();

        // Assert: unavailable, with a safe message that does not repeat the folder
        Assert.False(status.IsAvailable);
        Assert.Null(status.Info);
        Assert.Equal("SelfSigned", status.Provider);
        Assert.Contains("unexpected error", status.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("folder", status.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the key file is encrypted: its bytes are not a PFX, do not show the certificate subject, and the
    ///     key cannot be loaded from the file without the key ring.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_FirstUse_FileIsNotAReadablePfx()
    {
        // Arrange: a provider that creates the key in a new folder
        var folder = NewFolder();
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);
        provider.GetSigningKey();
        var path = Path.Combine(folder, SelfSignedKeyProvider.FileName);

        // Act: read the file as bytes and try to load it as a PFX
        var bytes = File.ReadAllBytes(path);
        var loaded = Record.Exception(() => X509CertificateLoader.LoadPkcs12(bytes, null));

        // Assert: the loader refuses it, and the subject text is not in the file; the file is the only one
        Assert.IsAssignableFrom<CryptographicException>(loaded);
        Assert.False(System.Text.Encoding.ASCII.GetString(bytes).Contains("ReviewCenter", StringComparison.Ordinal));
        Assert.Equal(new[] { SelfSignedKeyProvider.FileName }, Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    /// <summary>
    ///     Test that a key ring that is kept in a folder, as the server keeps it, decrypts the file after a restart.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_RestartWithPersistedRing_ReloadsTheSameKey()
    {
        // Arrange: a key created under a ring that lives in a folder
        var folder = NewFolder();
        var ringFolder = NewFolder();
        string thumbprint;
        using (var first = new SelfSignedKeyProvider(
                   Options(folder),
                   TimeProvider.System,
                   Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                   DataProtectionProvider.Create(new DirectoryInfo(ringFolder))))
        {
            thumbprint = first.GetSigningKey().Info.Thumbprint;
        }

        // Act: a new provider and a new ring object over the same folder, as after a restart
        using var second = new SelfSignedKeyProvider(
            Options(folder),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            DataProtectionProvider.Create(new DirectoryInfo(ringFolder)));
        var key = second.GetSigningKey();

        // Assert: the same key, and no second key file
        Assert.Equal(thumbprint, key.Info.Thumbprint);
        Assert.Single(Directory.GetFiles(folder));
    }

    /// <summary>
    ///     Test that a key ring that is not the one that protected the file is reported with the file named and the
    ///     remedy, that the file is not deleted, and that nothing of the file is in the message.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_OtherKeyRing_ThrowsNamingTheFileAndKeepsIt()
    {
        // Arrange: a key created under one ring, and a provider with another
        var folder = NewFolder();
        using (var first = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring))
        {
            first.GetSigningKey();
        }

        var path = Path.Combine(folder, SelfSignedKeyProvider.FileName);
        var before = File.ReadAllBytes(path);
        using var other = new SelfSignedKeyProvider(
            Options(folder),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new EphemeralDataProtectionProvider());

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(other.GetSigningKey);
        var status = other.Describe();

        // Assert: the file is named with the remedy, flagged unreadable, left alone, and not echoed
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("delete the file", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.IsUnreadable);
        Assert.False(exception.WillBeCreated);
        Assert.False(status.IsAvailable);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.DoesNotContain(Convert.ToBase64String(before, 0, 24), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a key file of the earlier format, a PFX without protection, is not used: a warning tells the
    ///     operator to delete it, a new protected key is created, and the old file is left alone.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_LegacyPlaintextFile_IsIgnoredWithWarning()
    {
        // Arrange: a folder with a PFX of the earlier format, and a provider with a capturing logger
        var folder = NewFolder();
        Directory.CreateDirectory(folder);
        var legacyPath = Path.Combine(folder, SelfSignedKeyProvider.LegacyFileName);
        string legacyThumbprint;
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=legacy", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var legacy = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            legacyThumbprint = legacy.Thumbprint;
            File.WriteAllBytes(legacyPath, legacy.Export(X509ContentType.Pfx));
        }

        var legacyBytes = File.ReadAllBytes(legacyPath);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, factory.CreateLogger("test"), Ring);

        // Act: ask for the key
        var key = provider.GetSigningKey();

        // Assert: a new key, a protected file beside the untouched legacy file, and a warning that says to delete it
        Assert.NotEqual(legacyThumbprint, key.Info.Thumbprint);
        Assert.True(File.Exists(Path.Combine(folder, SelfSignedKeyProvider.FileName)));
        Assert.Equal(legacyBytes, File.ReadAllBytes(legacyPath));
        Assert.Contains(
            logs.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains(SelfSignedKeyProvider.LegacyFileName, StringComparison.Ordinal)
                 && e.Message.Contains("Delete it", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that an expired self-signed key is moved aside under its thumbprint and replaced, with a warning, and
    ///     that the replacement is kept for the next start.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_ExpiredKey_IsArchivedAndRegeneratedWithWarning()
    {
        // Arrange: a key created now, and a provider that runs eleven years later
        var folder = NewFolder();
        var created = DateTimeOffset.UtcNow;
        string oldThumbprint;
        using (var first = new SelfSignedKeyProvider(Options(folder), new FixedTimeProvider(created), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring))
        {
            oldThumbprint = first.GetSigningKey().Info.Thumbprint;
        }

        var later = new FixedTimeProvider(created.AddDays(4000));
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        string newThumbprint;
        using (var second = new SelfSignedKeyProvider(Options(folder), later, factory.CreateLogger("test"), Ring))
        {
            // Act: ask for the key
            newThumbprint = second.GetSigningKey().Info.Thumbprint;
        }

        // Assert: a new key, the old one archived under its thumbprint, a warning, and the new key is the one kept
        var archive = Path.Combine(folder, SelfSignedKeyProvider.FileName + ".retired-" + oldThumbprint);
        Assert.NotEqual(oldThumbprint, newThumbprint);
        Assert.True(File.Exists(archive));
        Assert.Contains(
            logs.Entries,
            e => e.Level == LogLevel.Warning
                 && e.Message.Contains("expired", StringComparison.Ordinal)
                 && e.Message.Contains(oldThumbprint, StringComparison.Ordinal));
        using var third = new SelfSignedKeyProvider(Options(folder), later, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);
        Assert.Equal(newThumbprint, third.GetSigningKey().Info.Thumbprint);
    }

    /// <summary>
    ///     Test that a provider that may not create anything reports a missing key as one that will be created, and
    ///     leaves no folder and no file behind.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndNoKey_ReportsWillBeCreatedAndCreatesNothing()
    {
        // Arrange: a folder that does not exist, and a provider that may not create
        var folder = NewFolder();
        using var provider = new SelfSignedKeyProvider(
            Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring, allowCreate: false);

        // Act: ask for the key, and for its status
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);
        var status = provider.Describe();

        // Assert: the key will be created, nothing was created, and it is not an unreadable key
        Assert.True(exception.WillBeCreated);
        Assert.False(exception.IsUnreadable);
        Assert.Contains("will be created on first start", exception.Message, StringComparison.Ordinal);
        Assert.False(status.IsAvailable);
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>
    ///     Test that a provider that may not create anything still detects a regular file where the folder should
    ///     be, so the inspection does not report a key that will be created when the start would fail.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndFolderIsAFile_ThrowsNamingTheSetting()
    {
        // Arrange: a regular file at the configured folder path, and a provider that may not create
        var blocked = NewFolder();
        File.WriteAllText(blocked, "not a folder");
        using var provider = new SelfSignedKeyProvider(
            Options(blocked), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring, allowCreate: false);

        // Act
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: a failure of the setting, not "will be created", and the path-free message holds no path
        Assert.False(exception.WillBeCreated);
        Assert.Contains("Attestation:SelfSignedFolder", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.MessageWithoutPath);
        Assert.DoesNotContain(blocked, exception.MessageWithoutPath, StringComparison.Ordinal);
        Assert.Contains("Attestation:SelfSignedFolder", exception.MessageWithoutPath, StringComparison.Ordinal);
        Assert.True(File.Exists(blocked), "The inspection must leave the file as it was");
    }

    /// <summary>
    ///     Test that a provider that may not create anything still describes a key that exists, and neither changes
    ///     the folder nor replaces an expired key.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_CreationNotAllowedAndExpiredKey_DescribesItAndChangesNothing()
    {
        // Arrange: a key created now, and an inspecting provider that runs eleven years later
        var folder = NewFolder();
        var created = DateTimeOffset.UtcNow;
        string thumbprint;
        using (var first = new SelfSignedKeyProvider(Options(folder), new FixedTimeProvider(created), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring))
        {
            thumbprint = first.GetSigningKey().Info.Thumbprint;
        }

        using var inspector = new SelfSignedKeyProvider(
            Options(folder),
            new FixedTimeProvider(created.AddDays(4000)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Ring,
            allowCreate: false);

        // Act: ask for the key
        var key = inspector.GetSigningKey();

        // Assert: the expired key is described as it is, and the folder holds only the original file
        Assert.Equal(thumbprint, key.Info.Thumbprint);
        Assert.Equal(new[] { SelfSignedKeyProvider.FileName }, Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    /// <summary>
    ///     Test that no message, log entry or exception of any scenario holds a part of the key file, which is the
    ///     ciphertext of the key.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_AnyScenario_NoSecretOrCiphertextInMessages()
    {
        // Arrange: a key created with a capturing logger, and a provider with another ring over the same file
        var folder = NewFolder();
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var first = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, factory.CreateLogger("test"), Ring);
        first.GetSigningKey();
        var bytes = File.ReadAllBytes(Path.Combine(folder, SelfSignedKeyProvider.FileName));
        var fragments = new[] { Convert.ToBase64String(bytes, 8, 24), Convert.ToHexString(bytes, 8, 16), "PRIVATE KEY" };
        using var other = new SelfSignedKeyProvider(
            Options(folder), TimeProvider.System, factory.CreateLogger("test"), new EphemeralDataProtectionProvider());

        // Act: the wrong ring fails, and the status is described
        var exception = Assert.Throws<AttestationKeyException>(other.GetSigningKey);
        var status = other.Describe();

        // Assert: none of the texts holds a fragment of the file
        var texts = logs.Entries.Select(e => e.Message).Append(exception.Message).Append(status.Problem ?? string.Empty);
        Assert.All(
            texts,
            text => Assert.All(fragments, fragment => Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    ///     Test that a cryptographic failure while the key is created is not reported as a key file that cannot be
    ///     decrypted: it is a failure to create the key, it is not flagged unreadable, and no file is left behind.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_ProtectFails_IsNotReportedAsUnreadable()
    {
        // Arrange: a ring whose protector fails with a cryptographic error when it protects the new key
        var folder = NewFolder();
        using var provider = new SelfSignedKeyProvider(
            Options(folder),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new FailingProtectorProvider());

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: a creation failure with the inner error, not the message of a key file that cannot be decrypted
        Assert.False(exception.IsUnreadable);
        Assert.False(exception.WillBeCreated);
        Assert.Contains("could not be created", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("decrypted", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<CryptographicException>(exception.InnerException);
        Assert.False(File.Exists(Path.Combine(folder, SelfSignedKeyProvider.FileName)));
    }

    /// <summary>
    ///     Test that the messages for a report that prints no path (the configuration check) name the key file, or the
    ///     setting, but never the folder, while the messages for the server log keep the full path.
    /// </summary>
    [Fact]
    public void SelfSignedKeyProvider_GetSigningKey_StorageAndUnreadableFailures_MessageWithoutPathHoldsNoFolder()
    {
        // Arrange: a file where the folder should be, and a key file that another ring protected
        var blocked = NewFolder();
        File.WriteAllText(blocked, "not a folder");
        using var storage = new SelfSignedKeyProvider(
            Options(blocked), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring);
        var folder = NewFolder();
        using (var first = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Ring))
        {
            first.GetSigningKey();
        }

        using var other = new SelfSignedKeyProvider(
            Options(folder),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new EphemeralDataProtectionProvider());

        // Act
        var storageFailure = Assert.Throws<AttestationKeyException>(storage.GetSigningKey);
        var unreadable = Assert.Throws<AttestationKeyException>(other.GetSigningKey);

        // Assert: the server messages hold the folder, the path-free messages hold only the setting or the file name
        Assert.Contains(blocked, storageFailure.Message, StringComparison.Ordinal);
        Assert.NotNull(storageFailure.MessageWithoutPath);
        Assert.DoesNotContain(blocked, storageFailure.MessageWithoutPath, StringComparison.Ordinal);
        Assert.Contains("Attestation:SelfSignedFolder", storageFailure.MessageWithoutPath, StringComparison.Ordinal);
        Assert.Contains(folder, unreadable.Message, StringComparison.Ordinal);
        Assert.NotNull(unreadable.MessageWithoutPath);
        Assert.DoesNotContain(folder, unreadable.MessageWithoutPath, StringComparison.Ordinal);
        Assert.Contains(SelfSignedKeyProvider.FileName, unreadable.MessageWithoutPath, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Gets a path for a new temporary folder that is removed when the class is disposed.
    /// </summary>
    /// <returns>The path; the folder does not exist yet.</returns>
    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterAttestationTests-" + Guid.NewGuid().ToString("N"));
        _folders.Add(folder);
        return folder;
    }

    /// <summary>
    ///     Creates options that store the key in a folder.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The options.</returns>
    private static AttestationOptions Options(string folder)
    {
        return new AttestationOptions { SelfSignedFolder = folder };
    }

    /// <summary>
    ///     A Data Protection provider whose protectors fail with a cryptographic error when they protect.
    /// </summary>
    private sealed class FailingProtectorProvider : IDataProtectionProvider
    {
        /// <inheritdoc />
        public IDataProtector CreateProtector(string purpose)
        {
            return new FailingProtector();
        }
    }

    /// <summary>
    ///     A protector that cannot protect.
    /// </summary>
    private sealed class FailingProtector : IDataProtector
    {
        /// <inheritdoc />
        public IDataProtector CreateProtector(string purpose)
        {
            return this;
        }

        /// <inheritdoc />
        public byte[] Protect(byte[] plaintext)
        {
            throw new CryptographicException("The key ring cannot protect.");
        }

        /// <inheritdoc />
        public byte[] Unprotect(byte[] protectedData)
        {
            throw new CryptographicException("The key ring cannot unprotect.");
        }
    }
}
