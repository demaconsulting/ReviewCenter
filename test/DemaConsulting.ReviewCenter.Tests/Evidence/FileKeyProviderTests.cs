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

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="FileKeyProvider"/>: loading a PKCS #12 or PEM certificate and key, the password
///     from a setting or a file, the chain, the refusals and messages an administrator has to understand, the warnings
///     about an unprotected key and about the expiry, and the policy that every provider shares.
/// </summary>
/// <remarks>
///     The certificates are created in the test, from a root, an intermediate and a signer, and written to a temporary
///     folder; the clock is fixed so that the expiry tests do not depend on the date. No certificate store is used.
/// </remarks>
public sealed class FileKeyProviderTests
{
    /// <summary>
    ///     The password of the protected files of the tests.
    /// </summary>
    private const string Password = "file-provider-secret-1";

    /// <summary>
    ///     The time of the clock of the tests.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Test that a PFX file with the password in the setting gives the signer certificate and its key, in the
    ///     <c>File</c> provider.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PfxWithPasswordSetting_LoadsSignerAndKey()
    {
        // Arrange: a protected PFX and the password in the setting
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: false);
        using var provider = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the signer, its key and the provider name
        Assert.Equal("File", key.Info.Provider);
        Assert.Equal(issued.Signer.Thumbprint, key.Info.Thumbprint, ignoreCase: true);
        Assert.Contains("CN=File test signer", key.Info.Subject, StringComparison.Ordinal);
        Assert.Equal("RSA-2048", key.Info.Algorithm);
        Assert.IsAssignableFrom<RSA>(key.PrivateKey);
        Assert.Empty(provider.Describe().Warnings);
    }

    /// <summary>
    ///     Test that the password can come from a file, which is read through the secret source and trimmed.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PfxWithPasswordFile_LoadsSigner()
    {
        // Arrange: a protected PFX and a file that holds the password and a newline
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: false);
        var passwordFile = folder.GetFilePath("password.txt");
        File.WriteAllText(passwordFile, Password + "\n");
        RestrictToOwner(passwordFile);
        using var provider = Create(new AttestationOptions { CertificatePath = path, CertificatePasswordFile = passwordFile });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the signer was found with the password of the file
        Assert.Equal(issued.Signer.Thumbprint, key.Info.Thumbprint, ignoreCase: true);
    }

    /// <summary>
    ///     Test that the other certificates of a PFX file become the chain, except for the signer and the
    ///     self-signed root.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PfxWithIntermediateAndRoot_ChainHoldsTheIntermediateOnly()
    {
        // Arrange: a PFX with the signer, the intermediate and the root
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: true);
        using var provider = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the signer is the certificate with the key, and the chain is the intermediate
        Assert.Equal(issued.Signer.Thumbprint, key.Certificate.Thumbprint, ignoreCase: true);
        var intermediate = Assert.Single(key.Chain);
        Assert.Equal(issued.Intermediate.Thumbprint, intermediate.Thumbprint, ignoreCase: true);
        Assert.False(key.Info.IsSelfSigned);
    }

    /// <summary>
    ///     Test that a PEM certificate with its private key in a separate file is loaded, and that the other
    ///     certificates in the PEM file become the chain.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PemWithSeparateKeyAndChain_LoadsSignerAndChain()
    {
        // Arrange: a PEM file with the signer, intermediate and root, and an unencrypted key file
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var (certificatePath, keyPath) = WritePem(folder, issued, keyPassword: null, separateKey: true, chain: true);
        using var provider = Create(new AttestationOptions { CertificatePath = certificatePath, PrivateKeyPath = keyPath });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the signer, the intermediate as the chain, and a warning that the key is not protected
        Assert.Equal(issued.Signer.Thumbprint, key.Certificate.Thumbprint, ignoreCase: true);
        var intermediate = Assert.Single(key.Chain);
        Assert.Equal(issued.Intermediate.Thumbprint, intermediate.Thumbprint, ignoreCase: true);
        Assert.Contains(provider.Describe().Warnings, w => w.Contains("not protected by a password", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a PEM file with an encrypted key in the same file is loaded with the password, with no warning
    ///     about protection.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_EncryptedPemInOneFile_LoadsSignerWithPassword()
    {
        // Arrange: one PEM file with the certificate and an encrypted key
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var (certificatePath, _) = WritePem(folder, issued, Password, separateKey: false, chain: false);
        using var provider = Create(new AttestationOptions { CertificatePath = certificatePath, CertificatePassword = Password });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the signer was found, and nothing is warned about
        Assert.Equal(issued.Signer.Thumbprint, key.Certificate.Thumbprint, ignoreCase: true);
        Assert.Empty(provider.Describe().Warnings);
    }

    /// <summary>
    ///     Test that an encrypted key in a separate PEM file is loaded with the password from the setting.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_EncryptedPemWithSeparateKey_LoadsSignerWithPassword()
    {
        // Arrange: a PEM certificate and an encrypted key file
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var (certificatePath, keyPath) = WritePem(folder, issued, Password, separateKey: true, chain: false);
        using var provider = Create(
            new AttestationOptions { CertificatePath = certificatePath, PrivateKeyPath = keyPath, CertificatePassword = Password });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the signer was found
        Assert.Equal(issued.Signer.Thumbprint, key.Certificate.Thumbprint, ignoreCase: true);
    }

    /// <summary>
    ///     Test that a missing certificate path is refused, naming the setting and the provider.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_NoCertificatePath_NamesTheSetting()
    {
        // Arrange: a provider without a path
        using var provider = Create(new AttestationOptions());

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the exact message
        Assert.Equal("Attestation:CertificatePath is not set; it is required for the File provider.", exception.Message);
    }

    /// <summary>
    ///     Test that a certificate file that does not exist is refused, and that the message for the configuration
    ///     check names the file but not its folder.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_MissingFile_NamesTheFileAndTheCheckMessageNamesNoFolder()
    {
        // Arrange: a path in a folder that does not hold the file
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("missing.pfx");
        using var provider = Create(new AttestationOptions { CertificatePath = path });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the server message names the path, the message for the check only the file name
        Assert.Contains("Attestation:CertificatePath", exception.Message, StringComparison.Ordinal);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(exception.MessageWithoutPath);
        Assert.Contains("'missing.pfx'", exception.MessageWithoutPath, StringComparison.Ordinal);
        Assert.DoesNotContain(folder.DirectoryPath, exception.MessageWithoutPath, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a separate key file that does not exist is refused, naming the setting.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_MissingKeyFile_NamesTheSetting()
    {
        // Arrange: a PEM certificate and a key path that does not exist
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var (certificatePath, _) = WritePem(folder, issued, keyPassword: null, separateKey: true, chain: false);
        using var provider = Create(
            new AttestationOptions { CertificatePath = certificatePath, PrivateKeyPath = folder.GetFilePath("none.key") });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the setting and the reason, and no folder in the message for the check
        Assert.Contains("Attestation:PrivateKeyPath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(folder.DirectoryPath, exception.MessageWithoutPath!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a protected PFX file without a password is refused with the message that names both settings, and
    ///     that the status of the provider reports the problem instead of throwing.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PfxWithoutPassword_AsksForThePassword()
    {
        // Arrange: a protected PFX and no password
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: false);
        using var provider = Create(new AttestationOptions { CertificatePath = path });

        // Act: ask for the key, and describe it
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);
        var status = provider.Describe();

        // Assert: the message tells what to set; the status is not available
        Assert.Equal(
            $"The certificate file '{path}' needs a password. "
            + "Set Attestation:CertificatePassword or Attestation:CertificatePasswordFile.",
            exception.Message);
        Assert.Equal(
            "The certificate file 'signing.pfx' needs a password. "
            + "Set Attestation:CertificatePassword or Attestation:CertificatePasswordFile.",
            exception.MessageWithoutPath);
        Assert.False(status.IsAvailable);
        Assert.Equal(exception.Message, status.Problem);
    }

    /// <summary>
    ///     Test that a protected PEM key without a password is refused with the same message.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_EncryptedPemWithoutPassword_AsksForThePassword()
    {
        // Arrange: a PEM file with an encrypted key and no password
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var (certificatePath, _) = WritePem(folder, issued, Password, separateKey: false, chain: false);
        using var provider = Create(new AttestationOptions { CertificatePath = certificatePath });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: it needs a password
        Assert.Contains("needs a password", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Attestation:CertificatePasswordFile", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a wrong password is refused without echoing the password, in the message and in the log, for a
    ///     PFX file and for an encrypted PEM key.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_WrongPassword_SaysSoAndEchoesNoPassword()
    {
        // Arrange: a protected PFX and a protected PEM, each with a password that is not theirs
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var pfx = WritePfx(folder, issued, Password, chain: false);
        var (pem, _) = WritePem(folder, issued, Password, separateKey: false, chain: false);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        const string wrong = "not-the-password-99";
        using var pfxProvider = Create(new AttestationOptions { CertificatePath = pfx, CertificatePassword = wrong }, log: factory.CreateLogger("test"));
        using var pemProvider = Create(new AttestationOptions { CertificatePath = pem, CertificatePassword = wrong }, log: factory.CreateLogger("test"));

        // Act: ask each for the key
        var pfxException = Assert.Throws<AttestationKeyException>(pfxProvider.GetSigningKey);
        var pemException = Assert.Throws<AttestationKeyException>(pemProvider.GetSigningKey);

        // Assert: both say the password is wrong, and neither password appears anywhere
        Assert.Contains("wrong password", pfxException.Message, StringComparison.Ordinal);
        Assert.Contains("wrong password", pemException.Message, StringComparison.Ordinal);
        foreach (var text in new[] { pfxException.Message, pemException.Message, pfxException.MessageWithoutPath!, pemException.MessageWithoutPath! })
        {
            Assert.DoesNotContain(wrong, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(wrong, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a file that is neither PEM nor PKCS #12 is refused, saying so.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_FileThatIsNotACertificate_SaysSo()
    {
        // Arrange: a text file
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("notes.pfx");
        File.WriteAllText(path, "this is not a certificate");
        using var provider = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: it is neither PEM nor PKCS #12
        Assert.Contains("neither a PEM file nor a PKCS #12", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a legacy encrypted PEM key (<c>Proc-Type: 4,ENCRYPTED</c>) is refused with a message that tells the
    ///     operator to convert it to PKCS #8, and not with the message for a file that holds no key.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_LegacyEncryptedPemKey_AsksToConvertToPkcs8()
    {
        // Arrange: a text file with the headers of a legacy encrypted RSA key
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("legacy.pem");
        File.WriteAllText(
            path,
            "-----BEGIN RSA PRIVATE KEY-----\n"
            + "Proc-Type: 4,ENCRYPTED\n"
            + "DEK-Info: AES-128-CBC,00112233445566778899AABBCCDDEEFF\n"
            + "\n"
            + "AAAA\n"
            + "-----END RSA PRIVATE KEY-----\n");
        RestrictToOwner(path);
        using var provider = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password });

        // Act: ask for the key
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);

        // Assert: the message names the file and the remedy, and no password or folder
        Assert.Contains("'" + path + "'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("legacy encrypted PEM format", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Convert it to PKCS #8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("openssl pkcs8 -topk8", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not a PEM certificate with a private key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'legacy.pem'", exception.MessageWithoutPath, StringComparison.Ordinal);
        Assert.DoesNotContain(folder.DirectoryPath, exception.MessageWithoutPath!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a PFX file ignores <c>PrivateKeyPath</c>: a key file that does not exist does not stop the load.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PfxWithPrivateKeyPath_IgnoresIt()
    {
        // Arrange: a protected PFX and a key path that does not exist
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: false);
        using var provider = Create(
            new AttestationOptions
            {
                CertificatePath = path,
                CertificatePassword = Password,
                PrivateKeyPath = folder.GetFilePath("ignored.key")
            });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: the PFX was used and nothing was said about the key path
        Assert.Equal(issued.Signer.Thumbprint, key.Info.Thumbprint, ignoreCase: true);
        Assert.Empty(provider.Describe().Warnings);
    }

    /// <summary>
    ///     Test that a password in the setting and a password file together are refused by the secret source, naming
    ///     both settings, and that a password file that does not exist names the file only in the check message.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_PasswordSettingsConflictOrFileMissing_AreRefusedWithTheirMessages()
    {
        // Arrange: both settings set, and a password file that does not exist
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: false);
        var passwordFile = folder.GetFilePath("password.txt");
        using var both = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password, CertificatePasswordFile = passwordFile });
        using var missing = Create(new AttestationOptions { CertificatePath = path, CertificatePasswordFile = passwordFile });

        // Act: ask each for the key
        var conflict = Assert.Throws<AttestationKeyException>(both.GetSigningKey);
        var notFound = Assert.Throws<AttestationKeyException>(missing.GetSigningKey);

        // Assert: the messages of the secret source, no password, and no folder in the check message
        Assert.Equal(
            "Attestation:CertificatePassword and Attestation:CertificatePasswordFile are both set; set only one of them.",
            conflict.Message);
        Assert.DoesNotContain(Password, conflict.Message, StringComparison.Ordinal);
        Assert.Contains("Attestation:CertificatePasswordFile refers to the file", notFound.Message, StringComparison.Ordinal);
        Assert.Contains(passwordFile, notFound.Message, StringComparison.Ordinal);
        Assert.Contains("'password.txt'", notFound.MessageWithoutPath, StringComparison.Ordinal);
        Assert.DoesNotContain(folder.DirectoryPath, notFound.MessageWithoutPath!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a PFX file without a password is loaded with a warning when encryption is not required, and
    ///     that the warning is logged, shown in the status and names the file without its folder.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_UnprotectedPfx_LoadsWithWarning()
    {
        // Arrange: a PFX without a password
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, password: null, chain: false);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var provider = Create(new AttestationOptions { CertificatePath = path }, log: factory.CreateLogger("test"));

        // Act: load the key and describe it
        var key = provider.GetSigningKey();
        var status = provider.Describe();

        // Assert: the key works; one warning is logged and shown, and it names no folder
        Assert.Equal(issued.Signer.Thumbprint, key.Info.Thumbprint, ignoreCase: true);
        var warning = Assert.Single(status.Warnings);
        Assert.Contains("'signing.pfx' is not protected by a password", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(folder.DirectoryPath, warning, StringComparison.Ordinal);
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("not protected by a password", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a PFX file and a PEM key without a password are refused when encryption is required, naming the
    ///     setting that requires it.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_UnprotectedKeyWithRequireEncryptedKeys_IsRefused()
    {
        // Arrange: an unprotected PFX and an unprotected PEM, and providers that require encryption
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var pfx = WritePfx(folder, issued, password: null, chain: false);
        var (pem, _) = WritePem(folder, issued, keyPassword: null, separateKey: false, chain: false);
        using var pfxProvider = Create(new AttestationOptions { CertificatePath = pfx }, requireEncryptedKeys: true);
        using var pemProvider = Create(new AttestationOptions { CertificatePath = pem }, requireEncryptedKeys: true);

        // Act: ask each for the key
        var pfxException = Assert.Throws<AttestationKeyException>(pfxProvider.GetSigningKey);
        var pemException = Assert.Throws<AttestationKeyException>(pemProvider.GetSigningKey);

        // Assert: refused, naming DataProtection:RequireEncryptedKeys and where to set the password
        foreach (var exception in new[] { pfxException, pemException })
        {
            Assert.Contains("DataProtection:RequireEncryptedKeys", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Attestation:CertificatePasswordFile", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(folder.DirectoryPath, exception.MessageWithoutPath!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that on Unix a key file and a password file that other users can read are warned about, with the
    ///     file name and the mode, and that files for the owner only are not.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void FileKeyProvider_GetSigningKey_FilesReadableByOthersOnUnix_WarnsAboutEachOfThem()
    {
        // Arrange: a PFX and a password file that the group can read, and the same two for the owner only
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file permissions are only enforced on Linux and macOS.");
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: false);
        var passwordFile = folder.GetFilePath("password.txt");
        File.WriteAllText(passwordFile, Password);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        File.SetUnixFileMode(passwordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        using var loose = Create(new AttestationOptions { CertificatePath = path, CertificatePasswordFile = passwordFile });

        // Act: describe the key
        var warnings = loose.Describe().Warnings;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(passwordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var strict = Create(new AttestationOptions { CertificatePath = path, CertificatePasswordFile = passwordFile });
        var strictWarnings = strict.Describe().Warnings;

        // Assert: two warnings with names and modes and no folder, and none for the owner-only files
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("'signing.pfx'", StringComparison.Ordinal) && w.Contains("640", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("'password.txt'", StringComparison.Ordinal) && w.Contains("604", StringComparison.Ordinal));
        Assert.All(warnings, w => Assert.DoesNotContain(folder.DirectoryPath, w, StringComparison.Ordinal));
        Assert.Empty(strictWarnings);
    }

    /// <summary>
    ///     Test that on Unix only the file that holds the private key is checked for its mode: the key file when a
    ///     PEM certificate has a separate one, and not the certificate file; and the PFX, not an ignored
    ///     <c>PrivateKeyPath</c>.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void FileKeyProvider_GetSigningKey_SeparateKeyOrPfxWithKeyPath_ChecksOnlyTheFileThatHoldsTheKey()
    {
        // Arrange: a PEM certificate that the group can read and a key file for the owner only
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file permissions are only enforced on Linux and macOS.");
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var (certificatePath, keyPath) = WritePem(folder, issued, Password, separateKey: true, chain: false);
        var pfx = WritePfx(folder, issued, Password, chain: false);
        File.SetUnixFileMode(certificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        using var certificateLoose = Create(
            new AttestationOptions { CertificatePath = certificatePath, PrivateKeyPath = keyPath, CertificatePassword = Password });

        // Act: describe it, then describe the same files with the key file readable by the group instead, then a PFX
        // for the owner only with the (now loose) key file as its ignored PrivateKeyPath
        var certificateLooseWarnings = certificateLoose.Describe().Warnings;
        File.SetUnixFileMode(certificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(keyPath!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        using var keyLoose = Create(
            new AttestationOptions { CertificatePath = certificatePath, PrivateKeyPath = keyPath, CertificatePassword = Password });
        var keyLooseWarnings = keyLoose.Describe().Warnings;
        using var pfxProvider = Create(
            new AttestationOptions { CertificatePath = pfx, CertificatePassword = Password, PrivateKeyPath = keyPath });
        var pfxWarnings = pfxProvider.Describe().Warnings;

        // Assert: no warning for the loose certificate file, one for the loose key file, none for the ignored path
        Assert.Empty(certificateLooseWarnings);
        var warning = Assert.Single(keyLooseWarnings);
        Assert.Contains("private key file 'signing.key.pem'", warning, StringComparison.Ordinal);
        Assert.Empty(pfxWarnings);
    }

    /// <summary>
    ///     Test that a certificate that ends within the warning period is warned about, in the log and in the status,
    ///     with the thumbprint and the date, and that a warning period of 0 or a certificate that ends later gives none.
    /// </summary>
    [Fact]
    public void FileKeyProvider_Describe_EndsWithinWarningDays_WarnsUnlessDisabledOrFarAway()
    {
        // Arrange: a certificate that ends in ten days; providers with 30 days, with 0 and with 5
        using var folder = new TemporaryDirectory();
        using var issued = Issue(Now.AddDays(10));
        var path = WritePfx(folder, issued, Password, chain: false);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        using var warned = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password }, log: factory.CreateLogger("test"));
        using var disabled = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password, ExpiryWarningDays = 0 });
        using var farAway = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password, ExpiryWarningDays = 5 });

        // Act: describe each
        var warnedStatus = warned.Describe();
        var disabledStatus = disabled.Describe();
        var farAwayStatus = farAway.Describe();

        // Assert: the first warns, naming the thumbprint and the date, and logs it once; the others do not
        var warning = Assert.Single(warnedStatus.Warnings);
        Assert.Contains(issued.Signer.Thumbprint, warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2030-06-25", warning, StringComparison.Ordinal);
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("2030-06-25", StringComparison.Ordinal));
        Assert.Empty(disabledStatus.Warnings);
        Assert.Empty(farAwayStatus.Warnings);
    }

    /// <summary>
    ///     Test that a certificate that has expired is used with a warning when the trusted-key policy is off, also
    ///     with the warning period at its default.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_ExpiredWithoutRequireTrustedKey_SignsAndWarns()
    {
        // Arrange: a certificate that ended a day before the clock; the policy is off
        using var folder = new TemporaryDirectory();
        using var issued = Issue(Now.AddDays(-1));
        var path = WritePfx(folder, issued, Password, chain: false);
        using var provider = Create(new AttestationOptions { CertificatePath = path, CertificatePassword = Password });

        // Act: load the key and describe it
        var key = provider.GetSigningKey();
        var status = provider.Describe();

        // Assert: the key is returned and the warning says it has expired
        Assert.NotNull(key);
        Assert.True(status.IsAvailable);
        var warning = Assert.Single(status.Warnings);
        Assert.Contains("expired on 2030-06-14", warning, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a certificate that has expired is refused when a trusted key is required, as for every provider.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_ExpiredWithRequireTrustedKey_IsRefused()
    {
        // Arrange: an organization certificate that ended a day before the clock; the policy is on
        using var folder = new TemporaryDirectory();
        using var issued = Issue(Now.AddDays(-1));
        var path = WritePfx(folder, issued, Password, chain: true);
        using var provider = Create(
            new AttestationOptions { CertificatePath = path, CertificatePassword = Password, RequireTrustedKey = true });

        // Act: ask for the key, and describe it
        var exception = Assert.Throws<AttestationKeyException>(provider.GetSigningKey);
        var status = provider.Describe();

        // Assert: the policy names the validity, and the status reports the problem
        Assert.Contains("Attestation:RequireTrustedKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not valid now", exception.Message, StringComparison.Ordinal);
        Assert.False(status.IsAvailable);
    }

    /// <summary>
    ///     Test that a valid organization certificate from a file satisfies the trusted-key policy.
    /// </summary>
    [Fact]
    public void FileKeyProvider_GetSigningKey_ValidOrganizationCertificateWithRequireTrustedKey_Accepts()
    {
        // Arrange: a valid certificate issued by an intermediate; the policy is on
        using var folder = new TemporaryDirectory();
        using var issued = Issue();
        var path = WritePfx(folder, issued, Password, chain: true);
        using var provider = Create(
            new AttestationOptions { CertificatePath = path, CertificatePassword = Password, RequireTrustedKey = true });

        // Act: load the key
        var key = provider.GetSigningKey();

        // Assert: accepted, and described as trusted
        Assert.True(key.Info.RequireTrustedKey);
        Assert.False(key.Info.IsSelfSigned);
    }

    /// <summary>
    ///     Creates the provider under test, with the fixed clock.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <param name="requireEncryptedKeys">Whether a key without a password is refused.</param>
    /// <param name="log">The logger; none when not given.</param>
    /// <returns>The provider; the caller disposes it.</returns>
    private static FileKeyProvider Create(AttestationOptions options, bool requireEncryptedKeys = false, ILogger? log = null) =>
        new(options, new FixedTimeProvider(Now), log ?? NullLogger.Instance, requireEncryptedKeys);

    /// <summary>
    ///     Creates a root, an intermediate issued by it, and a signer issued by the intermediate.
    /// </summary>
    /// <param name="signerNotAfter">The end of the validity of the signer; one year after the clock when not given.</param>
    /// <returns>The certificates; the caller disposes them.</returns>
    private static IssuedCertificates Issue(DateTimeOffset? signerNotAfter = null)
    {
        var notBefore = Now.AddDays(-30);
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=File test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 1, true));
        using var root = rootRequest.CreateSelfSigned(notBefore, Now.AddYears(10));

        using var intermediateKey = RSA.Create(2048);
        var intermediateRequest = new CertificateRequest("CN=File test intermediate", intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var intermediateIssued = intermediateRequest.Create(root, notBefore, Now.AddYears(5), [1, 2, 3, 4]);
        using var intermediateWithKey = intermediateIssued.CopyWithPrivateKey(intermediateKey);

        using var signerKey = RSA.Create(2048);
        var signerRequest = new CertificateRequest("CN=File test signer, O=Test", signerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        signerRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var signerIssued = signerRequest.Create(intermediateWithKey, notBefore, signerNotAfter ?? Now.AddYears(1), [5, 6, 7, 8]);
        return new IssuedCertificates(
            signerIssued.CopyWithPrivateKey(signerKey),
            X509CertificateLoader.LoadCertificate(intermediateIssued.RawData),
            X509CertificateLoader.LoadCertificate(root.RawData),
            RSA.Create(signerKey.ExportParameters(includePrivateParameters: true)));
    }

    /// <summary>
    ///     Makes a file readable and writable by its owner only on a system with Unix permissions, so that the files
    ///     of a test do not cause the warning about a file that other users can read.
    /// </summary>
    /// <param name="path">The file.</param>
    private static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    ///     Writes a PKCS #12 file named <c>signing.pfx</c>.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="issued">The certificates.</param>
    /// <param name="password">The password; <c>null</c> for none.</param>
    /// <param name="chain">Whether the intermediate and the root are in the file.</param>
    /// <returns>The path of the file.</returns>
    private static string WritePfx(TemporaryDirectory folder, IssuedCertificates issued, string? password, bool chain)
    {
        var collection = new X509Certificate2Collection(issued.Signer);
        if (chain)
        {
            collection.Add(issued.Intermediate);
            collection.Add(issued.Root);
        }

        var path = folder.GetFilePath("signing.pfx");
        File.WriteAllBytes(path, collection.Export(X509ContentType.Pfx, password)!);
        RestrictToOwner(path);
        return path;
    }

    /// <summary>
    ///     Writes the certificate as PEM, and the private key in the same file or a separate one.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="issued">The certificates.</param>
    /// <param name="keyPassword">The password that encrypts the key; <c>null</c> for an unencrypted key.</param>
    /// <param name="separateKey">Whether the key goes into its own file.</param>
    /// <param name="chain">Whether the intermediate and the root are in the certificate file.</param>
    /// <returns>The path of the certificate file and, when the key is separate, of the key file.</returns>
    private static (string CertificatePath, string? KeyPath) WritePem(
        TemporaryDirectory folder, IssuedCertificates issued, string? keyPassword, bool separateKey, bool chain)
    {
        var keyPem = keyPassword is null
            ? issued.Key.ExportPkcs8PrivateKeyPem()
            : issued.Key.ExportEncryptedPkcs8PrivateKeyPem(
                keyPassword, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048));
        var certificatePem = issued.Signer.ExportCertificatePem();
        if (chain)
        {
            certificatePem += "\n" + issued.Intermediate.ExportCertificatePem() + "\n" + issued.Root.ExportCertificatePem();
        }

        var certificatePath = folder.GetFilePath("signing.pem");
        if (!separateKey)
        {
            File.WriteAllText(certificatePath, certificatePem + "\n" + keyPem);
            RestrictToOwner(certificatePath);
            return (certificatePath, null);
        }

        var keyPath = folder.GetFilePath("signing.key.pem");
        File.WriteAllText(certificatePath, certificatePem);
        File.WriteAllText(keyPath, keyPem);
        RestrictToOwner(certificatePath);
        RestrictToOwner(keyPath);
        return (certificatePath, keyPath);
    }

    /// <summary>
    ///     The certificates of a test: the signer with its key, the intermediate and the root without keys, and a
    ///     copy of the key of the signer for writing PEM.
    /// </summary>
    /// <param name="Signer">The signer certificate with its private key.</param>
    /// <param name="Intermediate">The intermediate certificate.</param>
    /// <param name="Root">The self-signed root certificate.</param>
    /// <param name="Key">The key of the signer.</param>
    private sealed record IssuedCertificates(X509Certificate2 Signer, X509Certificate2 Intermediate, X509Certificate2 Root, RSA Key)
        : IDisposable
    {
        /// <summary>
        ///     Releases the certificates and the key.
        /// </summary>
        public void Dispose()
        {
            Signer.Dispose();
            Intermediate.Dispose();
            Root.Dispose();
            Key.Dispose();
        }
    }
}
