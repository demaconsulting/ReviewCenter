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
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the <see cref="SecretScan"/> unit: the read-only scan of the key ring and of the protected
///     secrets of the database. Everything lives in temporary folders and uses the production protectors over a
///     key ring in a temporary folder; no key store of the operating system is touched.
/// </summary>
public class SecretScanTests
{
    /// <summary>
    ///     A token whose text must never appear in a result.
    /// </summary>
    private const string TokenText = "tkn-Scan-Secret-Value";

    /// <summary>
    ///     Test that every secret that was protected with the ring is counted and none is unreadable, per kind.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_SecretsOfTheRing_AreCountedReadable()
    {
        // Arrange: an installation with two repository tokens and one second-factor secret protected by its ring
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddToken(2, "tkn-second");
        install.AddTotp(7);

        // Act
        var result = await install.ScanAsync();

        // Assert: counts per kind, nothing unreadable, and the ring is described
        Assert.Equal(2, result.RepositoryTokens);
        Assert.Equal(1, result.TotpSecrets);
        Assert.Equal(3, result.ProtectedSecrets);
        Assert.Equal(0, result.UnreadableSecrets);
        Assert.Empty(result.UnreadableRepositoryIds);
        Assert.Empty(result.UnreadableUserIds);
        Assert.Equal(1, result.KeyFiles);
        Assert.Equal(1, result.PlaintextKeyFiles);
        Assert.False(result.RingIsEmpty);
    }

    /// <summary>
    ///     Test that a database next to another ring (a ring that was swapped in) has every secret unreadable, and
    ///     that the ids of the rows are reported.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_OtherRing_CountsEverySecretUnreadableWithIds()
    {
        // Arrange: a database full of secrets, and another installation whose ring has a key of its own
        using var install = new Install();
        install.AddToken(3, TokenText);
        install.AddTotp(9);
        using var other = new Install();
        other.CreateKey();

        // Act: scan the database of the first with the ring and protectors of the other
        var result = await SecretScan.ScanAsync(
            other.KeysFolder, install.Database, other.Credentials, other.TotpSecrets, TestContext.Current.CancellationToken);

        // Assert: the ring has a key, but none of the secrets can be read, and the rows are named
        Assert.False(result.RingIsEmpty);
        Assert.Equal(1, result.UnreadableRepositoryTokens);
        Assert.Equal(1, result.UnreadableTotpSecrets);
        Assert.Equal([3], result.UnreadableRepositoryIds);
        Assert.Equal([9], result.UnreadableUserIds);
    }

    /// <summary>
    ///     Test that only the secrets that the ring cannot read are counted when the database holds both kinds.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_MixedSecrets_CountsOnlyTheUnreadableOnes()
    {
        // Arrange: one token of the ring and one that was protected by another ring
        using var install = new Install();
        install.AddToken(1, "tkn-mine");
        using var other = new Install();
        install.AddProtectedToken(2, other.Credentials.Protect("tkn-foreign"));

        // Act
        var result = await install.ScanAsync();

        // Assert: two tokens, one unreadable: the foreign one
        Assert.Equal(2, result.RepositoryTokens);
        Assert.Equal(1, result.UnreadableRepositoryTokens);
        Assert.Equal([2], result.UnreadableRepositoryIds);
    }

    /// <summary>
    ///     Test that an empty ring with secrets in the database counts them all as unreadable without asking the ring,
    ///     which would create a key.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_EmptyRingWithSecrets_CountsThemUnreadableAndCreatesNoKey()
    {
        // Arrange: a database with secrets, and an installation whose ring has never been used
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddTotp(2);
        using var lost = new Install();

        // Act: scan the database with the empty ring and its production protectors
        var result = await SecretScan.ScanAsync(
            lost.KeysFolder, install.Database, lost.Credentials, lost.TotpSecrets, TestContext.Current.CancellationToken);

        // Assert: empty ring, everything unreadable, and no key file was created by the scan
        Assert.True(result.RingIsEmpty);
        Assert.Equal(2, result.ProtectedSecrets);
        Assert.Equal(2, result.UnreadableSecrets);
        Assert.Empty(lost.KeyFiles());
    }

    /// <summary>
    ///     Test that a ring whose only key file is malformed counts the secrets as unreadable without asking the ring,
    ///     which would create a replacement key, and leaves the malformed file as it is.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_MalformedKeyFileOnly_CountsSecretsUnreadableAndCreatesNoKey()
    {
        // Arrange: a database with secrets, and a ring whose only key file is not a key
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddTotp(2);
        using var damaged = new Install();
        Directory.CreateDirectory(damaged.KeysFolder);
        var malformed = Path.Combine(damaged.KeysFolder, "key-00000000-0000-0000-0000-000000000009.xml");
        await File.WriteAllTextAsync(malformed, "this is not a key", TestContext.Current.CancellationToken);

        // Act
        var result = await SecretScan.ScanAsync(
            damaged.KeysFolder, install.Database, damaged.Credentials, damaged.TotpSecrets, TestContext.Current.CancellationToken);

        // Assert: everything unreadable, and neither a new key file nor a change of the malformed one
        Assert.Equal(1, result.KeyFiles);
        Assert.Equal(2, result.UnreadableSecrets);
        Assert.Equal([malformed], damaged.KeyFiles());
        Assert.Equal("this is not a key", await File.ReadAllTextAsync(malformed, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that key files are counted as plaintext or encrypted by their content, and that a database that does
    ///     not exist holds no secret and is not created.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_PlaintextKeyFile_IsCountedAndMissingDatabaseIsNotCreated()
    {
        // Arrange: a key folder with one plaintext and one encrypted key file, and a database that does not exist
        using var install = new Install();
        using var folder = new TemporaryDirectory();
        var keys = Path.Combine(folder.DirectoryPath, "keys");
        Directory.CreateDirectory(keys);
        await File.WriteAllTextAsync(
            Path.Combine(keys, "key-00000000-0000-0000-0000-000000000001.xml"),
            "<key><descriptor><descriptor><masterKey>AAAA</masterKey></descriptor></descriptor></key>",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(keys, "key-00000000-0000-0000-0000-000000000002.xml"),
            "<key><descriptor><descriptor><encryptedSecret>BBBB</encryptedSecret></descriptor></descriptor></key>",
            TestContext.Current.CancellationToken);
        var missing = folder.GetFilePath("missing.db");
        var database = new DatabaseOptions { ConnectionString = $"Data Source={missing};Pooling=False" };

        // Act
        var result = await SecretScan.ScanAsync(
            keys, database, install.Credentials, install.TotpSecrets, TestContext.Current.CancellationToken);

        // Assert: two files, one of them plaintext, no secrets, and the database was not created
        Assert.Equal(2, result.KeyFiles);
        Assert.Equal(1, result.PlaintextKeyFiles);
        Assert.Equal(0, result.ProtectedSecrets);
        Assert.False(File.Exists(missing), "The scan must never create the database");
    }

    /// <summary>
    ///     Test that a database without the tables, and one with the tables but no rows, hold no secrets and no error.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_NoTablesOrNoRows_HoldsNoSecrets()
    {
        // Arrange: a database from before the tables existed, and an installation with the empty current schema
        using var install = new Install();
        using var folder = new TemporaryDirectory();
        var old = folder.GetFilePath("old.db");
        await using (var connection = new SqliteConnection($"Data Source={old};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE \"Other\" (\"Id\" INTEGER)";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Act
        var withoutTables = await SecretScan.ScanAsync(
            install.KeysFolder,
            new DatabaseOptions { ConnectionString = $"Data Source={old};Pooling=False" },
            install.Credentials,
            install.TotpSecrets,
            TestContext.Current.CancellationToken);
        var withoutRows = await install.ScanAsync();

        // Assert: nothing to protect in either
        Assert.Equal(0, withoutTables.ProtectedSecrets);
        Assert.Equal(0, withoutRows.ProtectedSecrets);
    }

    /// <summary>
    ///     Test that the scan changes neither the database nor the key ring: the files keep their content and their
    ///     time stamps, and no key file appears.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_AnyRing_LeavesDatabaseAndRingUnchanged()
    {
        // Arrange: a database with secrets and a ring with a key
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddTotp(1);
        var before = Snapshot(install);

        // Act: scan with the ring of the installation and then with an empty ring
        await install.ScanAsync();
        using var lost = new Install();
        await SecretScan.ScanAsync(
            lost.KeysFolder, install.Database, lost.Credentials, lost.TotpSecrets, TestContext.Current.CancellationToken);

        // Assert: every file is as it was, and no new key file exists
        Assert.Equal(before, Snapshot(install));
        Assert.Single(install.KeyFiles());
        Assert.Empty(lost.KeyFiles());
    }

    /// <summary>
    ///     Test that the result never holds a secret, a ciphertext or a path.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_Result_HoldsNoSecretCiphertextOrPath()
    {
        // Arrange: a token with a known text
        using var install = new Install();
        var cipher = install.AddToken(1, TokenText);

        // Act
        var text = (await install.ScanAsync()).ToString();

        // Assert: the text of the result has none of them
        Assert.DoesNotContain(TokenText, text, StringComparison.Ordinal);
        Assert.DoesNotContain(cipher, text, StringComparison.Ordinal);
        Assert.DoesNotContain(install.KeysFolder, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a long list of ids is shortened in a message.
    /// </summary>
    [Fact]
    public void SecretScan_FormatIds_ManyIds_ShowsTenAndTheCountOfTheOthers()
    {
        // Arrange: twelve ids
        var ids = Enumerable.Range(1, 12).ToList();

        // Act
        var few = SecretScan.FormatIds([4, 5]);
        var many = SecretScan.FormatIds(ids);

        // Assert
        Assert.Equal("4, 5", few);
        Assert.Equal("1, 2, 3, 4, 5, 6, 7, 8, 9, 10 and 2 more", many);
    }

    /// <summary>
    ///     Test that a null argument is refused.
    /// </summary>
    [Fact]
    public async Task SecretScan_ScanAsync_NullArgument_ThrowsArgumentNullException()
    {
        // Arrange
        using var install = new Install();

        // Act and assert: each argument is checked
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => SecretScan.ScanAsync(null!, install.Database, install.Credentials, install.TotpSecrets, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => SecretScan.ScanAsync(install.KeysFolder, null!, install.Credentials, install.TotpSecrets, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => SecretScan.ScanAsync(install.KeysFolder, install.Database, null!, install.TotpSecrets, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => SecretScan.ScanAsync(install.KeysFolder, install.Database, install.Credentials, null!, token));
    }

    /// <summary>
    ///     Takes a snapshot of the files of an installation: path, hash and write time.
    /// </summary>
    /// <param name="install">The installation.</param>
    /// <returns>The snapshot, comparable with another.</returns>
    private static List<(string Path, string Hash, DateTime Written)> Snapshot(Install install) =>
        Directory.GetFiles(install.Folder.DirectoryPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("-wal", StringComparison.Ordinal)
                           && !path.EndsWith("-shm", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (
                path,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                File.GetLastWriteTimeUtc(path)))
            .ToList();

    /// <summary>
    ///     A production installation over a temporary folder: the database with the current schema and the key ring
    ///     with the production protectors.
    /// </summary>
    private sealed class Install : IDisposable
    {
        /// <summary>
        ///     The production services.
        /// </summary>
        private readonly ServiceProvider _provider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Install"/> class.
        /// </summary>
        public Install()
        {
            Folder = new TemporaryDirectory();
            _provider = SelfTestServices.Create(Folder);
            Database = new DatabaseOptions { ConnectionString = ConnectionString };
        }

        /// <summary>
        ///     Gets the temporary folder.
        /// </summary>
        public TemporaryDirectory Folder { get; }

        /// <summary>
        ///     Gets the database options of the installation.
        /// </summary>
        public DatabaseOptions Database { get; }

        /// <summary>
        ///     Gets the folder of the key ring.
        /// </summary>
        public string KeysFolder => Path.Combine(Folder.DirectoryPath, "keys");

        /// <summary>
        ///     Gets the production protector of repository tokens.
        /// </summary>
        public ICredentialProtector Credentials => _provider.GetRequiredService<ICredentialProtector>();

        /// <summary>
        ///     Gets the production protector of second-factor secrets.
        /// </summary>
        public ITotpSecretProtector TotpSecrets => _provider.GetRequiredService<ITotpSecretProtector>();

        /// <summary>
        ///     Gets the connection string of the database.
        /// </summary>
        private string ConnectionString => $"Data Source={Folder.GetFilePath("selftest.db")};Pooling=False";

        /// <summary>
        ///     Makes the ring create its first key.
        /// </summary>
        public void CreateKey() => _ = Credentials.Protect("tkn-create-key");

        /// <summary>
        ///     Protects a token with the ring and stores it in a repository row.
        /// </summary>
        /// <param name="id">The id of the repository.</param>
        /// <param name="token">The token.</param>
        /// <returns>The protected text that was stored.</returns>
        public string AddToken(int id, string token)
        {
            var cipher = Credentials.Protect(token);
            AddProtectedToken(id, cipher);
            return cipher;
        }

        /// <summary>
        ///     Stores a protected token in a repository row.
        /// </summary>
        /// <param name="id">The id of the repository.</param>
        /// <param name="cipher">The protected text.</param>
        public void AddProtectedToken(int id, string cipher) => SecretSeed.AddRepositoryToken(ConnectionString, id, cipher);

        /// <summary>
        ///     Protects a second-factor secret with the ring and stores it.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        public void AddTotp(int userId) =>
            SecretSeed.AddTotpSecret(ConnectionString, userId, TotpSecrets.Protect(userId, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));

        /// <summary>
        ///     Scans the database with the ring and the protectors of the installation.
        /// </summary>
        /// <returns>The result.</returns>
        public Task<SecretScanResult> ScanAsync() =>
            SecretScan.ScanAsync(KeysFolder, Database, Credentials, TotpSecrets, TestContext.Current.CancellationToken);

        /// <summary>
        ///     Gets the key files of the ring.
        /// </summary>
        /// <returns>The paths.</returns>
        public string[] KeyFiles() =>
            Directory.Exists(KeysFolder) ? Directory.GetFiles(KeysFolder, "key-*.xml") : [];

        /// <inheritdoc />
        public void Dispose()
        {
            // The services first: they hold the database, and the folder cannot be deleted while it is in use
            _provider.Dispose();
            Folder.Dispose();
        }
    }
}
