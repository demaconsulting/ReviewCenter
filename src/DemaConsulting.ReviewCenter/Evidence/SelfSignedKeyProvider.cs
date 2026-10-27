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
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The default key provider: a self-signed certificate and RSA key that the server creates on first use and
///     keeps in a folder, so that a fresh installation can produce a signed attestation without any setup.
/// </summary>
/// <remarks>
///     The key is for development and test. It proves nothing about the organization (anybody can create such a
///     certificate), so the attestation, the page and the log all say so. The key is stored as a PFX without a
///     password in a folder that only the server account should read; that is an honest limit of the default, and the
///     reason that <c>Attestation:RequireTrustedKey</c> exists to refuse it. Several processes may start at the same
///     time: the file is created through a temporary file and an atomic move, and the loser of the race loads the
///     winner's file.
/// </remarks>
/// <param name="options">The attestation settings; <see cref="AttestationOptions.SelfSignedFolder"/> must be set.</param>
/// <param name="time">The clock that sets the validity period.</param>
/// <param name="log">The logger.</param>
internal sealed class SelfSignedKeyProvider(AttestationOptions options, TimeProvider time, ILogger log)
    : AttestationKeyProviderBase(options, time, log)
{
    /// <summary>
    ///     The name of the file inside the folder.
    /// </summary>
    public const string FileName = "attestation-selfsigned.pfx";

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
            CreateFolder(folder);
            var path = Path.Combine(folder, FileName);

            // The first server to need the key creates it; every later start loads the same file
            if (!File.Exists(path))
            {
                Generate(folder, path);
            }

            return LoadFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AttestationKeyException(
                $"The self-signed attestation key could not be stored in '{folder}'. Make the folder writable by the server or set Attestation:SelfSignedFolder.",
                ex);
        }
        catch (CryptographicException ex)
        {
            throw new AttestationKeyException(
                $"The self-signed attestation key file '{Path.Combine(folder, FileName)}' could not be read. Delete the file to create a new key.",
                ex);
        }
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
                stream.Write(pfx);
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
    ///     Loads the certificate and the key from the file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The signing key.</returns>
    private AttestationSigningKey LoadFile(string path)
    {
        X509Certificate2 certificate;
        try
        {
            // The key stays in memory only; the file is the one copy on disk
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (PlatformNotSupportedException)
        {
            // A platform that cannot hold ephemeral keys uses its default key storage
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, null);
        }

        var key = (AsymmetricAlgorithm?)certificate.GetRSAPrivateKey()
                  ?? throw new CryptographicException("The file does not contain an RSA private key.");
        return AttestationSigningKey.Create(Name, certificate, [], key, Options.RequireTrustedKey);
    }
}
