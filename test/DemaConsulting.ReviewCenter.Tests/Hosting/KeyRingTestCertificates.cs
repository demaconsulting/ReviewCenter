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
using DemaConsulting.ReviewCenter.Utilities;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Creates certificate files for the key ring tests: new key pairs written into a temporary folder. Nothing uses
///     a certificate store or a file outside the folder it is given.
/// </summary>
internal static class KeyRingTestCertificates
{
    /// <summary>
    ///     Creates a self-signed RSA certificate with its private key.
    /// </summary>
    /// <param name="name">The common name.</param>
    /// <returns>The certificate; the caller disposes it.</returns>
    private static X509Certificate2 CreateRsa(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    /// <summary>
    ///     Writes a new certificate with its private key as a PKCS #12 file.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <param name="name">The name of the certificate, which is also the file name without the extension.</param>
    /// <param name="password">The password of the file; <c>null</c> for none.</param>
    /// <returns>The path of the PFX file.</returns>
    public static string WritePfx(TemporaryDirectory folder, string name, string? password)
    {
        using var certificate = CreateRsa(name);
        var path = folder.GetFilePath(name + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }

    /// <summary>
    ///     Writes a new certificate as PEM files: one file with the certificate and the key, or two files.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <param name="name">The name of the certificate, which is also the file name without the extension.</param>
    /// <param name="keyPassword">The password that encrypts the private key; <c>null</c> for an unencrypted key.</param>
    /// <param name="separateKey">Whether the private key goes into its own file.</param>
    /// <returns>The path of the certificate file and, when the key is separate, the path of the key file.</returns>
    public static (string CertificatePath, string? KeyPath) WritePem(
        TemporaryDirectory folder,
        string name,
        string? keyPassword,
        bool separateKey)
    {
        using var certificate = CreateRsa(name);
        using var key = certificate.GetRSAPrivateKey()!;
        var keyPem = keyPassword is null
            ? key.ExportPkcs8PrivateKeyPem()
            : key.ExportEncryptedPkcs8PrivateKeyPem(
                keyPassword,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048));
        var certificatePem = certificate.ExportCertificatePem();
        var certificatePath = folder.GetFilePath(name + ".pem");
        if (!separateKey)
        {
            File.WriteAllText(certificatePath, certificatePem + "\n" + keyPem);
            return (certificatePath, null);
        }

        var keyPath = folder.GetFilePath(name + ".key.pem");
        File.WriteAllText(certificatePath, certificatePem);
        File.WriteAllText(keyPath, keyPem);
        return (certificatePath, keyPath);
    }

    /// <summary>
    ///     Writes only the public part of a new certificate, as a DER file without a private key.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <param name="name">The name of the certificate.</param>
    /// <returns>The path of the file.</returns>
    public static string WritePublicOnly(TemporaryDirectory folder, string name)
    {
        using var certificate = CreateRsa(name);
        var path = folder.GetFilePath(name + ".cer");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
        return path;
    }

    /// <summary>
    ///     Writes a new elliptic curve certificate with its private key as a PKCS #12 file.
    /// </summary>
    /// <param name="folder">The temporary folder.</param>
    /// <param name="name">The name of the certificate.</param>
    /// <param name="password">The password of the file.</param>
    /// <returns>The path of the PFX file.</returns>
    public static string WriteEcdsaPfx(TemporaryDirectory folder, string name, string password)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = folder.GetFilePath(name + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }
}
