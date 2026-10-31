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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The default key provider: a self-signed certificate and RSA key that the server creates on first use and
///     keeps, encrypted, in a folder, so that a fresh installation can produce a signed attestation without any setup.
/// </summary>
/// <remarks>
///     The key is for development and test. It proves nothing about the organization (anybody can create such a
///     certificate), so the attestation, the page and the log all say so. The PFX is encrypted with Data Protection
///     (purpose <see cref="ProtectorPurpose"/>) before it is written, so the key ring is the only root of trust and
///     the file is useless without it; a key file of an earlier format (<see cref="LegacyFileName"/>, a PFX without
///     any protection) is not migrated: it is ignored with a warning and a new key is created. A file that cannot be
///     decrypted (the ring was lost or replaced) is reported with the remedy and is never deleted. A key whose
///     certificate has expired is archived beside the file (<c>.retired-thumbprint</c>) and replaced, with a
///     warning; attestations that were signed earlier carry their own certificate and are not affected. Several
///     processes may start at the same time: the file is created through a temporary file and an atomic move, and
///     the loser of the race loads the winner's file (replacing an expired key assumes one server per folder). A
///     provider that may not create the key (the configuration check) reports a missing file instead of writing it.
///     Nothing secret is logged: not the key, not the ciphertext.
/// </remarks>
/// <param name="options">The attestation settings; <see cref="AttestationOptions.SelfSignedFolder"/> must be set.</param>
/// <param name="time">The clock that sets the validity period.</param>
/// <param name="log">The logger.</param>
/// <param name="dataProtection">The Data Protection provider whose key ring protects the file.</param>
/// <param name="allowCreate">
///     Whether the provider may create the folder, the key file and a replacement for an expired key; <c>false</c>
///     only inspects.
/// </param>
internal sealed class SelfSignedKeyProvider(
    AttestationOptions options,
    TimeProvider time,
    ILogger log,
    IDataProtectionProvider dataProtection,
    bool allowCreate = true)
    : AttestationKeyProviderBase(options, time, log)
{
    /// <summary>
    ///     The name of the file inside the folder: the PFX, encrypted with Data Protection.
    /// </summary>
    public const string FileName = "attestation-selfsigned.pfx.protected";

    /// <summary>
    ///     The name of the file of the earlier format, a PFX without protection; it is ignored.
    /// </summary>
    public const string LegacyFileName = "attestation-selfsigned.pfx";

    /// <summary>
    ///     The Data Protection purpose of the file.
    /// </summary>
    public const string ProtectorPurpose = "DemaConsulting.ReviewCenter.AttestationSelfSignedKey.v1";

    /// <summary>
    ///     The Data Protection protector of the file.
    /// </summary>
    private readonly IDataProtector _protector =
        (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector(ProtectorPurpose);

    /// <summary>
    ///     The subject of the generated certificate; it says what the key is.
    /// </summary>
    public const string SubjectName = "CN=ReviewCenter self-signed attestation key (not an organization key)";

    /// <summary>
    ///     The size in bits of the generated RSA key.
    /// </summary>
    private const int KeySize = 3072;

    /// <summary>
    ///     How long before now the certificate becomes valid, to tolerate clock differences between the server and a verifier.
    /// </summary>
    private static readonly TimeSpan BackDate = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     How long the certificate stays valid.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(3650);

    /// <inheritdoc />
    public override string Name => AttestationOptions.SelfSignedProvider;

    /// <inheritdoc />
    protected override AttestationSigningKey Load()
    {
        var folder = Options.SelfSignedFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new AttestationKeyException("Attestation:SelfSignedFolder is not set.");
        }

        try
        {
            // Startup would fail on a regular file at this path too, so the inspect-only mode must not hide it
            if (File.Exists(folder))
            {
                throw new AttestationKeyException(
                    $"Attestation:SelfSignedFolder '{folder}' is a file, not a folder. Set it to a folder the server can write to.")
                {
                    MessageWithoutPath =
                        "Attestation:SelfSignedFolder is a file, not a folder. Set it to a folder the server can write to."
                };
            }

            if (allowCreate)
            {
                CreateFolder(folder);
            }

            var path = Path.Combine(folder, FileName);
            WarnAboutLegacyFile(folder);

            // The first server to need the key creates it; every later start loads the same file
            if (!File.Exists(path))
            {
                if (!allowCreate)
                {
                    throw new AttestationKeyException(
                        "The self-signed attestation key does not exist yet; it will be created on first start.")
                    {
                        WillBeCreated = true
                    };
                }

                Generate(folder, path);
            }

            var key = LoadFile(path);
            if (allowCreate && Time.GetUtcNow() > key.Info.NotAfter)
            {
                // An expired self-signed key is replaced; the old one is kept beside the file
                var expired = key.Info;
                key.Dispose();
                var archive = path + ".retired-" + expired.Thumbprint;
                File.Move(path, archive, overwrite: true);
                Generate(folder, path);
                key = LoadFile(path);
#pragma warning disable CA1848 // Logged once when the key is replaced
                Log.LogWarning(
                    "The self-signed attestation key {Thumbprint} expired on {NotAfter:u}. It was moved to '{Archive}' and a new key "
                    + "{NewThumbprint} was created. Attestations that were signed earlier are not affected.",
                    expired.Thumbprint,
                    expired.NotAfter,
                    Path.GetFileName(archive),
                    key.Info.Thumbprint);
#pragma warning restore CA1848
            }

            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AttestationKeyException(
                $"The self-signed attestation key could not be stored in '{folder}'. Make the folder writable by the server or set Attestation:SelfSignedFolder.",
                ex)
            {
                MessageWithoutPath =
                    "The self-signed attestation key could not be stored in the attestation key folder. "
                    + "Make the folder writable by the server or set Attestation:SelfSignedFolder."
            };
        }
        catch (CryptographicException ex)
        {
            // Only a failure to create the key reaches this point; reading failures are reported by LoadFile
            throw new AttestationKeyException(
                "The self-signed attestation key could not be created because of a cryptographic error. "
                + "See the inner exception in the server log.",
                ex);
        }
    }

    /// <summary>
    ///     Warns about a key file of the earlier format, which is no longer used and holds a key without protection.
    /// </summary>
    /// <param name="folder">The folder of the key file.</param>
    private void WarnAboutLegacyFile(string folder)
    {
        if (!File.Exists(Path.Combine(folder, LegacyFileName)))
        {
            return;
        }

#pragma warning disable CA1848 // Logged once when the key is found
        Log.LogWarning(
            "The file '{File}' in '{Folder}' is a key of an earlier version that is not encrypted. It is ignored. Delete it.",
            LegacyFileName,
            folder);
#pragma warning restore CA1848
    }

    /// <summary>
    ///     Creates the folder; on Unix only its owner may access it.
    /// </summary>
    /// <param name="folder">The folder.</param>
    private static void CreateFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
        }
        else
        {
            Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    ///     Creates a key and certificate and stores them in the file, unless another process stored one first.
    /// </summary>
    /// <param name="folder">The folder of the file.</param>
    /// <param name="path">The path of the file.</param>
    private void Generate(string folder, string path)
    {
        using var rsa = RSA.Create(KeySize);
        var request = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var now = Time.GetUtcNow();
        using var certificate = request.CreateSelfSigned(now - BackDate, now + Lifetime);
        var pfx = certificate.Export(X509ContentType.Pfx);
        byte[] protectedPfx;
        try
        {
            protectedPfx = _protector.Protect(pfx);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }

        // Write beside the target and move atomically so that nobody reads a half-written file
        var temporary = Path.Combine(folder, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Owner-only from the first byte on Unix (ignored on Windows, where the folder ACL applies)
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
            {
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(temporary, streamOptions))
            {
                stream.Write(protectedPfx);
            }

            File.Move(temporary, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another process created the key first; its file is the one to use
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>
    ///     Decrypts the file and loads the certificate and the key from it.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The signing key.</returns>
    /// <exception cref="AttestationKeyException">Thrown when the file cannot be decrypted or is not a PFX with an RSA key.</exception>
    private AttestationSigningKey LoadFile(string path)
    {
        try
        {
            return DecryptAndLoad(path);
        }
        catch (CryptographicException ex)
        {
            throw new AttestationKeyException(
                $"The protected self-signed attestation key file '{path}' could not be decrypted or read. "
                + "It can only be read with the Data Protection key ring that protected it. "
                + "Restore that key ring, or delete the file to create a new key.",
                ex)
            {
                IsUnreadable = true,
                MessageWithoutPath =
                    $"The protected self-signed attestation key file {FileName} could not be decrypted or read. "
                    + "It can only be read with the Data Protection key ring that protected it. "
                    + "Restore that key ring, or delete the file to create a new key."
            };
        }
    }

    /// <summary>
    ///     Reads the file, decrypts it and loads the certificate and the key from it.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The signing key.</returns>
    /// <exception cref="CryptographicException">Thrown when the file cannot be decrypted or is not a PFX with an RSA key.</exception>
    private AttestationSigningKey DecryptAndLoad(string path)
    {
        var pfx = _protector.Unprotect(File.ReadAllBytes(path));
        X509Certificate2 certificate;
        try
        {
            // The key stays in memory only; the file is the one copy on disk
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (PlatformNotSupportedException)
            {
                // A platform that cannot hold ephemeral keys uses its default key storage
                certificate = X509CertificateLoader.LoadPkcs12(pfx, null);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }

        var key = (AsymmetricAlgorithm?)certificate.GetRSAPrivateKey()
                  ?? throw new CryptographicException("The file does not contain an RSA private key.");
        return AttestationSigningKey.Create(Name, certificate, [], key, Options.RequireTrustedKey);
    }
}
