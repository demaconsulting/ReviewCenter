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

using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="SelfSignedKeyProvider"/>: creating, persisting and reloading the default key, the
///     trusted-key policy, and the errors an administrator has to understand.
/// </summary>
public sealed class SelfSignedKeyProviderTests : IDisposable
{
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
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

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
        using var provider = new SelfSignedKeyProvider(Options(NewFolder()), new FixedTimeProvider(now), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

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
        using (var first = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance))
        {
            thumbprint = first.GetSigningKey().Info.Thumbprint;
        }

        using var second = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

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
        using var provider = new SelfSignedKeyProvider(Options(NewFolder()), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

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
        // Arrange: four providers on one empty folder
        var folder = NewFolder();
        var providers = Enumerable.Range(0, 4)
            .Select(_ => new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance))
            .ToList();
        try
        {
            // Act: all ask at once
            var thumbprints = await Task.WhenAll(providers.Select(p => Task.Run(() => p.GetSigningKey().Info.Thumbprint, TestContext.Current.CancellationToken)));

            // Assert: one thumbprint, one file, no leftovers
            Assert.Single(thumbprints.Distinct());
            Assert.Equal(new[] { SelfSignedKeyProvider.FileName }, Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            foreach (var provider in providers)
            {
                provider.Dispose();
            }
        }
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
        using var provider = new SelfSignedKeyProvider(options, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
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
        using var provider = new SelfSignedKeyProvider(Options(NewFolder()), TimeProvider.System, factory.CreateLogger("test"));

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
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

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
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

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
        using var provider = new SelfSignedKeyProvider(Options(folder), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the file is named, with the remedy; the file is left alone
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Delete the file", exception.Message, StringComparison.Ordinal);
        Assert.Equal("this is not a PFX file", File.ReadAllText(path));
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
}
