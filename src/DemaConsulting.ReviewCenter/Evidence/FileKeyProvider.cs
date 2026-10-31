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
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The key provider for an organization certificate that is kept in a file: a PKCS #12 file (PFX or P12), or a
///     PEM certificate with its private key in the same file or in a separate one.
/// </summary>
/// <remarks>
///     This is the provider for a server that has no operating system certificate store, such as a Linux host or a
///     container: the certificate and key are mounted or copied to the server, and the password, if the key has one,
///     is a secret that comes from <c>Attestation:CertificatePassword</c> (normally the environment variable
///     <c>Attestation__CertificatePassword</c>) or <c>Attestation:CertificatePasswordFile</c>, resolved by
///     <see cref="SecretSource"/> so that the same rules and messages apply as for every other secret. The format
///     is told from the content: a file with a PEM header is PEM, anything else is PKCS #12. The private key is held
///     in memory only, where the platform allows it (macOS does not support an ephemeral key set for PKCS #12, so the
///     default key set is used there, as for the key ring certificate). Certificates in the file besides the signer
///     become the intermediate certificates that are embedded in the attestation, except for a self-signed root. A key
///     that has no password is accepted with a warning, and refused when <c>DataProtection:RequireEncryptedKeys</c>
///     is set. On Unix the file that holds the private key (the separate key file for PEM, else the certificate file;
///     <c>PrivateKeyPath</c> is ignored for PKCS #12) and the password file are warned about when other users can
///     read them. A legacy encrypted PEM key (<c>Proc-Type: 4,ENCRYPTED</c>) is refused with a message that tells the
///     operator to convert it to PKCS #8. The bytes of the files that are read are cleared from memory after the load.
///     The files are never changed. The certificate is not checked for chain trust or revocation: that is the decision of whoever
///     verifies the PDF. No message holds the password; the messages for the configuration check name the file but
///     not its folder.
/// </remarks>
/// <param name="options">The attestation settings.</param>
/// <param name="time">The clock for the validity check of the trust policy and the expiry warning.</param>
/// <param name="log">The logger.</param>
/// <param name="requireEncryptedKeys">Whether a key without a password is refused (<c>DataProtection:RequireEncryptedKeys</c>).</param>
internal sealed class FileKeyProvider(
    AttestationOptions options,
    TimeProvider time,
    ILogger log,
    bool requireEncryptedKeys)
    : AttestationKeyProviderBase(options, time, log)
{
    /// <summary>
    ///     The name of the setting of the certificate file, for messages.
    /// </summary>
    private const string CertificateSetting = "Attestation:CertificatePath";

    /// <summary>
    ///     The name of the setting of the separate key file, for messages.
    /// </summary>
    private const string KeySetting = "Attestation:PrivateKeyPath";

    /// <summary>
    ///     The text that tells the operator where the password is set, for messages.
    /// </summary>
    private const string PasswordSettings = "Attestation:CertificatePassword or Attestation:CertificatePasswordFile";

    /// <summary>
    ///     The mode bits that let a user other than the owner read or change a file.
    /// </summary>
    private const UnixFileMode OtherUsersAccess =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    /// <inheritdoc />
    public override string Name => AttestationOptions.FileProvider;

    /// <inheritdoc />
    protected override AttestationSigningKey Load()
    {
        if (string.IsNullOrWhiteSpace(Options.CertificatePath))
        {
            throw new AttestationKeyException(
                $"{CertificateSetting} is not set; it is required for the {AttestationOptions.FileProvider} provider.");
        }

        var path = Options.CertificatePath;
        var keyPath = string.IsNullOrWhiteSpace(Options.PrivateKeyPath) ? null : Options.PrivateKeyPath;
        var password = ResolvePassword();
        var content = ReadFile(path, CertificateSetting);
        LoadedCertificate loaded;
        try
        {
            var isPem = content.AsSpan().IndexOf("-----BEGIN"u8) >= 0;

            // A PKCS #12 file holds the key itself, so the key file setting is ignored for it
            if (!isPem)
            {
                keyPath = null;
            }

            if (keyPath is not null && !File.Exists(keyPath))
            {
                throw Problem(keyPath, name => $"{KeySetting} '{name}' could not be loaded: the file does not exist.");
            }

            loaded = isPem ? LoadPem(path, content, keyPath, password) : LoadPkcs12(path, content, password);
        }
        finally
        {
            // The file can hold an unencrypted private key, so it is not left in memory
            CryptographicOperations.ZeroMemory(content);
        }

        AsymmetricAlgorithm? privateKey = null;
        try
        {
            privateKey = GetPrivateKey(loaded.Signer, path);

            // The file that holds the private key: the separate key file, else the certificate file
            var keyFile = keyPath ?? path;
            CheckProtection(loaded.IsProtected, keyFile);
            WarnAboutOtherUsers(keyFile, keyPath is null ? "certificate file" : "private key file");
            if (!string.IsNullOrWhiteSpace(Options.CertificatePasswordFile))
            {
                WarnAboutOtherUsers(Options.CertificatePasswordFile, "password file");
            }

            // Ownership of the certificate, the chain and the key passes to the signing key
            return AttestationSigningKey.Create(
                Name, loaded.Signer, loaded.Chain, privateKey, Options.RequireTrustedKey);
        }
        catch
        {
            // The signing key was not created, so everything that was loaded is still owned here
            privateKey?.Dispose();
            foreach (var certificate in loaded.Chain)
            {
                certificate.Dispose();
            }

            loaded.Signer.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Makes an exception whose message names the file as given and whose path-free message names only the file name.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="message">Makes the message from the way the file is named.</param>
    /// <param name="inner">The underlying error, or <c>null</c>.</param>
    /// <returns>The exception.</returns>
    private static AttestationKeyException Problem(string path, Func<string, string> message, Exception? inner = null) =>
        new(message(path), inner) { MessageWithoutPath = message(Path.GetFileName(path)) };

    /// <summary>
    ///     Gets the password from the inline setting or the password file.
    /// </summary>
    /// <returns>The password, or <c>null</c> when none is set.</returns>
    /// <exception cref="AttestationKeyException">Thrown when both settings are set or the file cannot be used.</exception>
    private string? ResolvePassword()
    {
        try
        {
            return SecretSource.Resolve(
                Options.CertificatePassword,
                "Attestation:CertificatePassword",
                Options.CertificatePasswordFile,
                "Attestation:CertificatePasswordFile");
        }
        catch (InvalidOperationException ex)
        {
            // The message names the file as configured; the configuration check names its name only
            var file = Options.CertificatePasswordFile;
            var withoutPath = !string.IsNullOrWhiteSpace(file) && ex.Message.Contains(file, StringComparison.Ordinal)
                ? ex.Message.Replace(file, Path.GetFileName(file), StringComparison.Ordinal)
                : null;
            throw new AttestationKeyException(ex.Message, ex) { MessageWithoutPath = withoutPath };
        }
    }

    /// <summary>
    ///     Reads a certificate or key file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="setting">The name of the setting that holds the path.</param>
    /// <returns>The bytes of the file.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the file does not exist or cannot be read.</exception>
    private static byte[] ReadFile(string path, string setting)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Problem(path, name => $"{setting} '{name}' could not be loaded: the file does not exist.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            throw Problem(path, name => $"{setting} '{name}' could not be loaded: the file cannot be read.", ex);
        }
    }

    /// <summary>
    ///     Loads a certificate, its private key and its other certificates from a PKCS #12 file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The bytes of the file.</param>
    /// <param name="password">The password, or <c>null</c> when none is set.</param>
    /// <returns>What was loaded; the caller owns the certificates.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the file needs a password, has another one or is not usable.</exception>
    private static LoadedCertificate LoadPkcs12(string path, byte[] content, string? password)
    {
        // The key is held in memory only; macOS does not support that for PKCS #12, so it uses the default key set
        var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        X509Certificate2Collection all;
        try
        {
            all = X509CertificateLoader.LoadPkcs12Collection(content, password, flags);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException or IOException
                                       or NotSupportedException)
        {
            // NotSupportedException includes PlatformNotSupportedException: whatever the platform cannot load is the
            // operator's setting to change, never a crash
            throw ClassifyPkcs12Failure(path, content, password, ex);
        }

        var signer = all.Cast<X509Certificate2>().FirstOrDefault(certificate => certificate.HasPrivateKey);
        if (signer is null)
        {
            foreach (var certificate in all)
            {
                certificate.Dispose();
            }

            throw Problem(
                path,
                name => $"{CertificateSetting} '{name}' could not be loaded: the file holds no certificate with a private key.");
        }

        return new LoadedCertificate(signer, TakeIntermediates(all, signer), IsPkcs12KeyProtected(content, password));
    }

    /// <summary>
    ///     Tells from the structure of a PKCS #12 file whether its private key is protected by the password.
    /// </summary>
    /// <param name="content">The bytes of the file, which loaded successfully.</param>
    /// <param name="password">The password that opened the file, or <c>null</c> when none is set.</param>
    /// <returns>
    ///     <c>true</c> only when a password is set and the file holds a private key that is encrypted (a shrouded key
    ///     bag, or a key bag in an encrypted safe) and no key in an unencrypted safe; a password that the file does not
    ///     use protects nothing.
    /// </returns>
    private static bool IsPkcs12KeyProtected(byte[] content, string? password)
    {
        if (password is null)
        {
            return false;
        }

        try
        {
            var protectedKey = false;
            foreach (var safe in Pkcs12Info.Decode(content, out _).AuthenticatedSafe)
            {
                // A key bag read from an encrypted safe was decrypted by the call below; it is a protected key
                var encryptedSafe = safe.ConfidentialityMode != Pkcs12ConfidentialityMode.None;
                if (safe.ConfidentialityMode == Pkcs12ConfidentialityMode.Password)
                {
                    safe.Decrypt(password);
                }
                else if (encryptedSafe)
                {
                    // Encrypted to a certificate: its content cannot be inspected without that key, and it is not plain
                    protectedKey = true;
                    continue;
                }

                foreach (var bag in safe.GetBags())
                {
                    if (bag is Pkcs12KeyBag && !encryptedSafe)
                    {
                        return false;
                    }

                    protectedKey |= bag is Pkcs12ShroudedKeyBag || (bag is Pkcs12KeyBag && encryptedSafe);
                }
            }

            return protectedKey;
        }
        catch (Exception ex) when (ex is CryptographicException or System.Formats.Asn1.AsnContentException
                                       or InvalidOperationException)
        {
            // What cannot be inspected is not shown to be protected
            return false;
        }
    }

    /// <summary>
    ///     Tells why a PKCS #12 file could not be opened.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="content">The bytes of the file.</param>
    /// <param name="password">The password, or <c>null</c> when none is set.</param>
    /// <param name="inner">The error of the load.</param>
    /// <returns>The exception to throw.</returns>
    private static AttestationKeyException ClassifyPkcs12Failure(
        string path, byte[] content, string? password, Exception inner)
    {
        try
        {
            _ = Pkcs12Info.Decode(content, out _);
        }
        catch (Exception ex) when (ex is CryptographicException or System.Formats.Asn1.AsnContentException)
        {
            return Problem(
                path,
                name => $"{CertificateSetting} '{name}' could not be loaded: it is neither a PEM file nor a PKCS #12 (PFX) file.",
                inner);
        }

        return password is null
            ? Problem(path, name => $"The certificate file '{name}' needs a password. Set {PasswordSettings}.", inner)
            : Problem(
                path,
                name => $"The certificate file '{name}' could not be opened with the password that is set: "
                        + "wrong password, or not a certificate with a private key.",
                inner);
    }

    /// <summary>
    ///     Loads a certificate and its private key from PEM files.
    /// </summary>
    /// <param name="path">The PEM file with the certificate, and the key when there is no separate key file.</param>
    /// <param name="content">The bytes of the file at <paramref name="path"/>.</param>
    /// <param name="keyPath">The PEM file with the private key, or <c>null</c> when it is in <paramref name="path"/>.</param>
    /// <param name="password">The password of an encrypted key, or <c>null</c> when none is set.</param>
    /// <returns>What was loaded; the caller owns the certificates.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the key needs a password, has another one or is not usable.</exception>
    private static LoadedCertificate LoadPem(string path, byte[] content, string? keyPath, string? password)
    {
        var keyFile = keyPath ?? path;
        var keyBytes = keyPath is null ? content : ReadFile(keyPath, KeySetting);
        bool legacyEncrypted;
        bool encrypted;
        try
        {
            // Searched as bytes, so that no string with the key is made
            legacyEncrypted = keyBytes.AsSpan().IndexOf("Proc-Type: 4,ENCRYPTED"u8) >= 0;
            encrypted = keyBytes.AsSpan().IndexOf("-----BEGIN ENCRYPTED PRIVATE KEY-----"u8) >= 0;
        }
        finally
        {
            if (keyPath is not null)
            {
                // The key file can hold an unencrypted private key (the certificate file is cleared by the caller)
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }

        if (legacyEncrypted)
        {
            throw Problem(
                keyFile,
                name => $"The private key in '{name}' is in the legacy encrypted PEM format (Proc-Type: 4,ENCRYPTED), "
                        + "which is not supported. Convert it to PKCS #8, for example with "
                        + "'openssl pkcs8 -topk8 -in old.key -out new.key', and use the new file.");
        }

        // The file that holds the key is the one the messages name
        var fileKind = keyPath is null ? "certificate file" : "private key file";
        if (encrypted && password is null)
        {
            throw Problem(keyFile, name => $"The {fileKind} '{name}' needs a password. Set {PasswordSettings}.");
        }

        X509Certificate2 signer;
        try
        {
            signer = encrypted
                ? X509Certificate2.CreateFromEncryptedPemFile(path, password!, keyPath)
                : X509Certificate2.CreateFromPemFile(path, keyPath);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException or IOException
                                       or NotSupportedException)
        {
            throw encrypted
                ? Problem(
                    keyFile,
                    name => $"The {fileKind} '{name}' could not be opened with the password that is set: "
                            + "wrong password, or not a private key.",
                    ex)
                : Problem(
                    path,
                    name => $"{CertificateSetting} '{name}' could not be loaded: it is not a PEM certificate with a "
                            + $"private key. Set {KeySetting} when the key is in another file.",
                    ex);
        }

        // The other certificates of the file are the chain of the signer
        var all = new X509Certificate2Collection();
        try
        {
            all.ImportFromPemFile(path);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            signer.Dispose();
            throw Problem(
                path,
                name => $"{CertificateSetting} '{name}' could not be loaded: the PEM data is not usable.",
                ex);
        }

        return new LoadedCertificate(signer, TakeIntermediates(all, signer), encrypted);
    }

    /// <summary>
    ///     Keeps the intermediate certificates of a collection and releases the rest.
    /// </summary>
    /// <param name="all">What was loaded, including the signer's own entry, if any.</param>
    /// <param name="signer">The signer certificate, which is kept by the caller.</param>
    /// <returns>The certificates that are neither the signer nor self-signed (a root is the verifier's to trust).</returns>
    private static List<X509Certificate2> TakeIntermediates(X509Certificate2Collection all, X509Certificate2 signer)
    {
        var intermediates = new List<X509Certificate2>();
        foreach (var certificate in all)
        {
            if (ReferenceEquals(certificate, signer))
            {
                // The caller keeps the signer
                continue;
            }

            var isSigner = string.Equals(certificate.Thumbprint, signer.Thumbprint, StringComparison.OrdinalIgnoreCase);
            if (isSigner || AttestationKeyPolicy.IsSelfSigned(certificate))
            {
                certificate.Dispose();
            }
            else
            {
                intermediates.Add(certificate);
            }
        }

        return intermediates;
    }

    /// <summary>
    ///     Gets the private key handle of the certificate.
    /// </summary>
    /// <param name="certificate">The certificate.</param>
    /// <param name="path">The path of the file, for the message.</param>
    /// <returns>An RSA or ECDSA key; the caller owns it.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the key is neither RSA nor ECDSA or cannot be used.</exception>
    private static AsymmetricAlgorithm GetPrivateKey(X509Certificate2 certificate, string path)
    {
        try
        {
            AsymmetricAlgorithm? key = certificate.GetRSAPrivateKey() ?? (AsymmetricAlgorithm?)certificate.GetECDsaPrivateKey();
            return key ?? throw Problem(
                path,
                name => $"{CertificateSetting} '{name}' has a private key that is neither RSA nor ECDSA; "
                        + "only RSA and ECDSA keys are supported.");
        }
        catch (CryptographicException ex)
        {
            throw Problem(
                path,
                name => $"The private key of the certificate file '{name}' is not usable: wrong password, or the platform cannot use the key.",
                ex);
        }
    }

    /// <summary>
    ///     Refuses or warns about a private key that is not protected by a password.
    /// </summary>
    /// <param name="isProtected">Whether the key is protected by a password.</param>
    /// <param name="keyFile">The file that holds the private key.</param>
    /// <exception cref="AttestationKeyException">Thrown when the key is not protected and encryption is required.</exception>
    private void CheckProtection(bool isProtected, string keyFile)
    {
        if (isProtected)
        {
            return;
        }

        if (requireEncryptedKeys)
        {
            throw Problem(
                keyFile,
                name => $"The private key in '{name}' is not protected by a password, and "
                        + "DataProtection:RequireEncryptedKeys is set. Protect the key with a password and set "
                        + $"{PasswordSettings}.");
        }

        NoteWarning(
            $"The private key in '{Path.GetFileName(keyFile)}' is not protected by a password. Anyone who can read "
            + "the file can sign attestations. Protect the key with a password, or set "
            + "DataProtection:RequireEncryptedKeys to refuse such a key.");
    }

    /// <summary>
    ///     Warns about a file with a secret that users other than the owner can read, on a system with Unix permissions.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="description">What the file is, for the message.</param>
    private void WarnAboutOtherUsers(string path, string description)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & OtherUsersAccess) != 0)
            {
                NoteWarning(
                    $"The {description} '{Path.GetFileName(path)}' can be accessed by other users (mode "
                    + $"{Convert.ToString((int)mode, 8)}). Make it accessible to the account of the server only, "
                    + "for example with chmod 600.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The permissions cannot be read: the file was read a moment ago, so this is not worth a failed start
        }
    }

    /// <summary>
    ///     What was loaded from a certificate file.
    /// </summary>
    /// <param name="Signer">The certificate with the private key.</param>
    /// <param name="Chain">The intermediate certificates.</param>
    /// <param name="IsProtected">Whether the private key was protected by a password.</param>
    private sealed record LoadedCertificate(X509Certificate2 Signer, List<X509Certificate2> Chain, bool IsProtected);
}
