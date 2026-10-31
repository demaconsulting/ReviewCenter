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
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Hosting;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.SelfTest;

/// <summary>
///     A production installation over a temporary folder for the tests of the key check: the database with the
///     current schema, a key ring (unencrypted, or encrypted with a certificate) and the production protectors.
/// </summary>
/// <remarks>
///     The installation can point the configuration of the process (the environment variables the check reads) at
///     itself with <see cref="Configure"/>, which is undone when the returned scope is disposed.
/// </remarks>
internal sealed class KeysInstall : IDisposable
{
    /// <summary>
    ///     A repository token that must never appear in a message.
    /// </summary>
    public const string TokenText = "ghp_keys-install-token-0123456789";

    /// <summary>
    ///     A certificate password that must never appear in a message.
    /// </summary>
    public const string Password = "keys-install-password";

    /// <summary>
    ///     The production services.
    /// </summary>
    private readonly ServiceProvider _provider;

    /// <summary>
    ///     The settings of the installation.
    /// </summary>
    private readonly Dictionary<string, string?> _settings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KeysInstall"/> class.
    /// </summary>
    /// <param name="certificatePath">The certificate that encrypts the key ring, or <c>null</c> for a ring of unencrypted keys.</param>
    public KeysInstall(string? certificatePath = null)
    {
        Folder = new TemporaryDirectory();
        _settings = SelfTestServices.CreateSettings(Folder);
        if (certificatePath is not null)
        {
            _settings["DataProtection:Protection"] = "Auto";
            _settings["DataProtection:CertificatePath"] = certificatePath;
            _settings["DataProtection:CertificatePassword"] = Password;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddServices(services, configuration);
        _provider = services.BuildServiceProvider();
        DatabaseSetup.EnsureCreated(
            _provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
            new DatabaseOptions { ConnectionString = ConnectionString });
    }

    /// <summary>
    ///     Gets the temporary folder.
    /// </summary>
    public TemporaryDirectory Folder { get; }

    /// <summary>
    ///     Gets the folder of the key ring.
    /// </summary>
    public string KeysFolder => Path.Combine(Folder.DirectoryPath, "keys");

    /// <summary>
    ///     Gets the connection string of the database.
    /// </summary>
    private string ConnectionString => _settings["Database:ConnectionString"]!;

    /// <summary>
    ///     Gets the folder of the self-signed attestation key.
    /// </summary>
    public string AttestationFolder => Path.Combine(Folder.DirectoryPath, "attestation");

    /// <summary>
    ///     Makes the production attestation key provider create the protected self-signed key.
    /// </summary>
    /// <returns>The thumbprint of the key.</returns>
    public string CreateAttestationKey() =>
        _provider.GetRequiredService<IAttestationKeyProvider>().GetSigningKey().Info.Thumbprint;

    /// <summary>
    ///     Makes the ring create its first key.
    /// </summary>
    public void CreateKey() => _ = _provider.GetRequiredService<ICredentialProtector>().Protect("tkn-create-key");

    /// <summary>
    ///     Protects a token with the ring and stores it in a repository row.
    /// </summary>
    /// <param name="id">The id of the repository.</param>
    /// <param name="token">The token.</param>
    /// <returns>The protected text that was stored.</returns>
    public string AddToken(int id, string token)
    {
        var cipher = _provider.GetRequiredService<ICredentialProtector>().Protect(token);
        SecretSeed.AddRepositoryToken(ConnectionString, id, cipher);
        return cipher;
    }

    /// <summary>
    ///     Protects a second-factor secret with the ring and stores it.
    /// </summary>
    /// <param name="userId">The id of the user.</param>
    public void AddTotp(int userId) =>
        SecretSeed.AddTotpSecret(
            ConnectionString,
            userId,
            _provider.GetRequiredService<ITotpSecretProtector>().Protect(userId, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));

    /// <summary>
    ///     Gets the key files of the ring.
    /// </summary>
    /// <returns>The paths.</returns>
    public string[] KeyFiles() =>
        Directory.Exists(KeysFolder) ? Directory.GetFiles(KeysFolder, "key-*.xml") : [];

    /// <summary>
    ///     Takes a snapshot of the files of the installation: path, hash and write time.
    /// </summary>
    /// <returns>The snapshot, comparable with another.</returns>
    /// <remarks>
    ///     The database of the installation is in write-ahead log mode, and SQLite may leave its empty <c>-wal</c> and
    ///     <c>-shm</c> side files behind after a read-only open. They hold no data and are not part of the snapshot;
    ///     the database file and every other file are.
    /// </remarks>
    public List<(string Path, string Hash, DateTime Written)> Snapshot() =>
        Directory.GetFiles(Folder.DirectoryPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("-wal", StringComparison.Ordinal) && !path.EndsWith("-shm", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), File.GetLastWriteTimeUtc(path)))
            .ToList();

    /// <summary>
    ///     Points the configuration of the process at this installation.
    /// </summary>
    /// <param name="overrides">Settings (environment variable names, <c>Section__Name</c>) that replace the ones of the installation.</param>
    /// <returns>The scope; disposing it restores the environment.</returns>
    public ConfiguredKeysEnvironment Configure(params KeyValuePair<string, string?>[] overrides)
    {
        var values = _settings.ToDictionary(pair => pair.Key.Replace(":", "__", StringComparison.Ordinal), pair => pair.Value);
        foreach (var (name, value) in overrides)
        {
            values[name] = value;
        }

        values[TestDatabase.ConnectionStringVariable] = ConnectionString;
        return new ConfiguredKeysEnvironment(values.ToArray());
    }

    /// <summary>
    ///     Points the configuration of the process at this installation, with the key ring of another folder.
    /// </summary>
    /// <param name="keysPath">The folder of the key ring to use.</param>
    /// <returns>The scope; disposing it restores the environment.</returns>
    public ConfiguredKeysEnvironment ConfigureKeysPath(string keysPath) =>
        Configure(new KeyValuePair<string, string?>("DataProtection__KeysPath", keysPath));

    /// <inheritdoc />
    public void Dispose()
    {
        // The services first: they hold the database, and the folder cannot be deleted while it is in use
        _provider.Dispose();
        Folder.Dispose();
    }
}
