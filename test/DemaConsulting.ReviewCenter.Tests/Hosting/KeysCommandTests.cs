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
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the <see cref="KeysCommand"/> (the <c>keys reprotect</c> verb). Everything lives in temporary
///     folders: the database is a temporary SQLite file and the key ring is a temporary folder that is protected by
///     its permissions only, so no key store of the operating system is touched.
/// </summary>
public class KeysCommandTests
{
    /// <summary>
    ///     A token whose text must never appear in the output.
    /// </summary>
    private const string TokenText = "tkn-Reprotect-Secret-Value";

    /// <summary>
    ///     The plaintext second-factor secret that is stored.
    /// </summary>
    private static readonly byte[] TotpSecret = [11, 22, 33, 44, 55, 66, 77, 88, 99, 110];

    /// <summary>
    ///     Test that every secret is readable with the production protectors after the command, and that the
    ///     command reports the counts with exit code 0.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_SecretsOfTheRing_AreStillReadableAfterwards()
    {
        // Arrange: two repository tokens and one second-factor secret that the ring protected
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddToken(2, "tkn-second");
        install.AddTotp(7);

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert: success with the counts, and a fresh start of the production services reads everything
        Assert.Equal(0, exitCode);
        Assert.Contains("Re-protected 2 repository token(s) and 1 second-factor secret(s)", output, StringComparison.Ordinal);
        using var restarted = SelfTestServices.Create(install.Folder);
        Assert.Equal(TokenText, restarted.GetRequiredService<ICredentialProtector>().Unprotect(install.ReadToken(1)));
        Assert.Equal("tkn-second", restarted.GetRequiredService<ICredentialProtector>().Unprotect(install.ReadToken(2)));
        Assert.Equal(TotpSecret, restarted.GetRequiredService<ITotpSecretProtector>().Unprotect(7, install.ReadTotp(7)));
    }

    /// <summary>
    ///     Test that a new key exists afterwards, that every stored payload references it and none the old key, and
    ///     that the report names the old key as one that nothing references any more.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_NewKey_IsTheOnlyOneReferencedAndOldKeyIsReported()
    {
        // Arrange: secrets protected with the first key of the ring
        using var install = new Install();
        var oldPayload = install.AddToken(1, TokenText);
        install.AddTotp(7);
        var oldKeyId = KeysCommand.ReadKeyId(oldPayload, base64Url: true);
        Assert.NotNull(oldKeyId);
        Assert.Single(install.KeyFiles());

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert: a second key file, referenced by every payload; the old key is not referenced and is reported
        Assert.Equal(0, exitCode);
        Assert.Equal(2, install.KeyFiles().Length);
        using var restarted = SelfTestServices.Create(install.Folder);
        var keyIds = restarted.GetRequiredService<IKeyManager>().GetAllKeys().Select(key => key.KeyId).ToList();
        Assert.Contains(oldKeyId.Value, keyIds);
        var newKeyId = Assert.Single(keyIds, id => id != oldKeyId.Value);
        Assert.Equal(newKeyId, KeysCommand.ReadKeyId(install.ReadToken(1), base64Url: true));
        Assert.Equal(newKeyId, KeysCommand.ReadKeyId(install.ReadTotp(7), base64Url: false));
        Assert.Contains($"New Data Protection key: {newKeyId}", output, StringComparison.Ordinal);
        Assert.Contains($"all 2 reference {newKeyId}", output, StringComparison.Ordinal);
        Assert.Contains($"Older keys that no stored secret references: {oldKeyId.Value}", output, StringComparison.Ordinal);
        Assert.Contains("Next steps:", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a second run is fine: it succeeds, gives yet another key and leaves every secret readable.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_SecondRun_IsIdempotentAndGivesAnotherKey()
    {
        // Arrange
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddTotp(7);

        // Act: twice
        var (firstExit, firstOutput) = Reprotect(install.Folder);
        var (secondExit, secondOutput) = Reprotect(install.Folder);

        // Assert: both succeed with the same counts, there are three keys, and everything is readable
        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Contains("Re-protected 1 repository token(s) and 1 second-factor secret(s)", secondOutput, StringComparison.Ordinal);
        Assert.Equal(3, install.KeyFiles().Length);
        Assert.NotEqual(NewKeyLine(firstOutput), NewKeyLine(secondOutput));
        using var restarted = SelfTestServices.Create(install.Folder);
        Assert.Equal(TokenText, restarted.GetRequiredService<ICredentialProtector>().Unprotect(install.ReadToken(1)));
        Assert.Equal(TotpSecret, restarted.GetRequiredService<ITotpSecretProtector>().Unprotect(7, install.ReadTotp(7)));
        Assert.Equal(NewKeyId(secondOutput), KeysCommand.ReadKeyId(install.ReadToken(1), base64Url: true));
    }

    /// <summary>
    ///     Test that an unreadable secret makes the command refuse with exit code 1, naming the row and no secret,
    ///     and that nothing at all changes: no key file, no stored value.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_UnreadableSecret_RefusesAndChangesNothing()
    {
        // Arrange: one token of the ring and one that another ring protected, and a second-factor secret
        using var install = new Install();
        var mine = install.AddToken(1, TokenText);
        using var other = new Install();
        var foreign = other.AddToken(2, "tkn-foreign");
        install.AddProtectedToken(2, foreign);
        install.AddTotp(7);
        var totp = install.ReadTotp(7);
        var keyFilesBefore = install.KeyFileSnapshot();

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert: refused with the row named, and neither the key files nor the stored values changed
        Assert.Equal(1, exitCode);
        Assert.Contains("Refusing to re-protect, nothing was changed", output, StringComparison.Ordinal);
        Assert.Contains("1 of 3 protected secrets cannot be read", output, StringComparison.Ordinal);
        Assert.Contains("repository ids 2", output, StringComparison.Ordinal);
        Assert.DoesNotContain("tkn-foreign", output, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign, output, StringComparison.Ordinal);
        Assert.Equal(keyFilesBefore, install.KeyFileSnapshot());
        Assert.Equal(mine, install.ReadToken(1));
        Assert.Equal(foreign, install.ReadToken(2));
        Assert.Equal(totp, install.ReadTotp(7));
    }

    /// <summary>
    ///     Test that an empty key ring next to a database with secrets is refused and no key is created.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_EmptyRingWithSecrets_RefusesAndCreatesNoKey()
    {
        // Arrange: a database full of secrets whose ring is lost: its folder is deleted
        using var install = new Install();
        install.AddToken(1, TokenText);
        install.AddTotp(7);
        Directory.Delete(install.KeysFolder, recursive: true);
        var token = install.ReadToken(1);

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert: refused, everything counted unreadable, and neither a key nor a changed value
        Assert.Equal(1, exitCode);
        Assert.Contains("2 of 2 protected secrets cannot be read", output, StringComparison.Ordinal);
        Assert.Contains("is empty", output, StringComparison.Ordinal);
        Assert.Empty(install.KeyFiles());
        Assert.Equal(token, install.ReadToken(1));
    }

    /// <summary>
    ///     Test that neither the output of a success nor the plaintext or ciphertext of a secret ever meet.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_Report_HoldsNoSecretValue()
    {
        // Arrange
        using var install = new Install();
        var tokenBefore = install.AddToken(1, TokenText);
        install.AddTotp(7);
        var totpBefore = install.ReadTotp(7);

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert: neither plaintext nor ciphertext (old or new) appears in the output
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(TokenText, output, StringComparison.Ordinal);
        Assert.DoesNotContain(tokenBefore, output, StringComparison.Ordinal);
        Assert.DoesNotContain(install.ReadToken(1), output, StringComparison.Ordinal);
        Assert.DoesNotContain(totpBefore, output, StringComparison.Ordinal);
        Assert.DoesNotContain(install.ReadTotp(7), output, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(TotpSecret), output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a database without secrets is fine: the command succeeds with zero counts.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_NoSecrets_SucceedsWithZeroCounts()
    {
        // Arrange: a database with the schema and no secrets
        using var install = new Install();

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Re-protected 0 repository token(s) and 0 second-factor secret(s)", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a database file that does not exist is refused and nothing is created.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_MissingDatabase_RefusesAndCreatesNothing()
    {
        // Arrange: settings that point at a folder with neither a database nor a key ring
        using var folder = new TemporaryDirectory();
        var log = folder.GetFilePath("out.log");
        string output;
        int exitCode;
        using (var context = Context.Create(["keys", "reprotect", "--silent", "--log", log]))
        {
            // Act
            KeysCommand.Run(context, configuration => configuration.AddInMemoryCollection(SelfTestServices.CreateSettings(folder)));
            exitCode = context.ExitCode;
        }

        output = File.ReadAllText(log);

        // Assert: an error that names the file, and no database and no key ring
        Assert.Equal(1, exitCode);
        Assert.Contains("The database file does not exist", output, StringComparison.Ordinal);
        Assert.False(File.Exists(folder.GetFilePath("selftest.db")));
        Assert.False(Directory.Exists(Path.Combine(folder.DirectoryPath, "keys")));
    }

    /// <summary>
    ///     Test that the key id is read from the header of a payload of both stored forms, and that text that is no
    ///     payload gives no key id.
    /// </summary>
    [Fact]
    public void KeysCommand_ReadKeyId_Payload_GivesKeyIdOrNull()
    {
        // Arrange: payloads of both kinds from a ring with one key
        using var install = new Install();
        var token = install.AddToken(1, TokenText);
        var totp = install.TotpSecrets.Protect(3, TotpSecret);
        using var restarted = SelfTestServices.Create(install.Folder);
        var keyId = Assert.Single(restarted.GetRequiredService<IKeyManager>().GetAllKeys()).KeyId;

        // Act and assert: both forms give the key of the ring; anything else gives nothing
        Assert.Equal(keyId, KeysCommand.ReadKeyId(token, base64Url: true));
        Assert.Equal(keyId, KeysCommand.ReadKeyId(totp, base64Url: false));
        Assert.Null(KeysCommand.ReadKeyId(string.Empty, base64Url: true));
        Assert.Null(KeysCommand.ReadKeyId(null, base64Url: true));
        Assert.Null(KeysCommand.ReadKeyId("not a payload!", base64Url: true));
        Assert.Null(KeysCommand.ReadKeyId(Convert.ToBase64String(new byte[40]), base64Url: false));
        Assert.Null(KeysCommand.ReadKeyId(Convert.ToBase64String(new byte[3]), base64Url: false));
    }

    /// <summary>
    ///     Test that a failure while the second table is written (here a trigger that aborts every update) is
    ///     reported honestly: the new key exists, the first table stays re-protected, the second is unchanged, and
    ///     the command can be run again.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_SecondTableFails_ReportsNewKeyAndCompletedTable()
    {
        // Arrange: a token and a second-factor secret, and a trigger that makes the second table fail
        using var install = new Install();
        var tokenBefore = install.AddToken(1, TokenText);
        install.AddTotp(7);
        var totpBefore = install.ReadTotp(7);
        install.BlockTotpUpdates();

        // Act
        var (exitCode, output) = Reprotect(install.Folder);

        // Assert: exit code 1 and the honest message, with no secret in it
        Assert.Equal(1, exitCode);
        Assert.Contains("Re-protecting stopped", output, StringComparison.Ordinal);
        Assert.Contains("A new key was created", output, StringComparison.Ordinal);
        Assert.Contains("(1 repository token(s), 0 second-factor secret(s))", output, StringComparison.Ordinal);
        Assert.Contains("the second-factor secret table, which was being changed, is unchanged", output, StringComparison.Ordinal);
        Assert.Contains("run the command again", output, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing was changed", output, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be created", output, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenText, output, StringComparison.Ordinal);
        Assert.DoesNotContain(tokenBefore, output, StringComparison.Ordinal);
        Assert.DoesNotContain(install.ReadToken(1), output, StringComparison.Ordinal);
        Assert.DoesNotContain(totpBefore, output, StringComparison.Ordinal);

        // Assert: the new key exists, the token table was committed with it, and the other table is as it was
        Assert.Equal(2, install.KeyFiles().Length);
        Assert.NotEqual(tokenBefore, install.ReadToken(1));
        Assert.Equal(totpBefore, install.ReadTotp(7));
        using var restarted = SelfTestServices.Create(install.Folder);
        Assert.Equal(TokenText, restarted.GetRequiredService<ICredentialProtector>().Unprotect(install.ReadToken(1)));
        Assert.Equal(TotpSecret, restarted.GetRequiredService<ITotpSecretProtector>().Unprotect(7, install.ReadTotp(7)));
    }

    /// <summary>
    ///     Test that key ring settings that cannot be used (encrypted keys are required while the protection is
    ///     permissions only) make the command refuse, create no key and change nothing.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_KeyRingSettingsProblem_RefusesAndCreatesNoKey()
    {
        // Arrange: a database with secrets, and settings that the key ring plan rejects
        using var install = new Install();
        var token = install.AddToken(1, TokenText);
        install.AddTotp(7);
        var totp = install.ReadTotp(7);
        var keyFilesBefore = install.KeyFileSnapshot();

        // Act
        var (exitCode, output) = Reprotect(
            install.Folder,
            new Dictionary<string, string?> { ["DataProtection:RequireEncryptedKeys"] = "true" });

        // Assert: refused with the setting named, and neither a key nor a stored value changed
        Assert.Equal(1, exitCode);
        Assert.Contains("Refusing to re-protect, nothing was changed", output, StringComparison.Ordinal);
        Assert.Contains("DataProtection:RequireEncryptedKeys", output, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenText, output, StringComparison.Ordinal);
        Assert.Equal(keyFilesBefore, install.KeyFileSnapshot());
        Assert.Equal(token, install.ReadToken(1));
        Assert.Equal(totp, install.ReadTotp(7));
    }

    /// <summary>
    ///     Test that a null context is refused.
    /// </summary>
    [Fact]
    public void KeysCommand_Run_NullContext_ThrowsArgumentNullException()
    {
        // Arrange: (none)

        // Act and assert: refused
        Assert.Throws<ArgumentNullException>(() => KeysCommand.Run(null!));
    }

    /// <summary>
    ///     Runs <c>keys reprotect</c> against the installation in a folder.
    /// </summary>
    /// <param name="folder">The temporary folder of the installation.</param>
    /// <param name="extraSettings">Settings that replace those of the installation, or <c>null</c>.</param>
    /// <returns>The exit code and everything the command wrote.</returns>
    private static (int ExitCode, string Output) Reprotect(
        TemporaryDirectory folder,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var log = folder.GetFilePath($"out-{Guid.NewGuid():N}.log");
        var settings = SelfTestServices.CreateSettings(folder);
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        int exitCode;
        using (var context = Context.Create(["keys", "reprotect", "--silent", "--log", log]))
        {
            KeysCommand.Run(context, configuration => configuration.AddInMemoryCollection(settings));
            exitCode = context.ExitCode;
        }

        return (exitCode, File.ReadAllText(log));
    }

    /// <summary>
    ///     Gets the line of the output that names the new key.
    /// </summary>
    /// <param name="output">The output of the command.</param>
    /// <returns>The line.</returns>
    private static string NewKeyLine(string output) =>
        output.Split('\n').Single(line => line.StartsWith("New Data Protection key:", StringComparison.Ordinal)).Trim();

    /// <summary>
    ///     Gets the id of the new key from the output.
    /// </summary>
    /// <param name="output">The output of the command.</param>
    /// <returns>The key id.</returns>
    private static Guid NewKeyId(string output) =>
        Guid.Parse(NewKeyLine(output)["New Data Protection key:".Length..].Trim());

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
        ///     Protects the second-factor secret with the ring and stores it.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        public void AddTotp(int userId) =>
            SecretSeed.AddTotpSecret(ConnectionString, userId, TotpSecrets.Protect(userId, TotpSecret));

        /// <summary>
        ///     Reads the stored token of a repository.
        /// </summary>
        /// <param name="id">The id of the repository.</param>
        /// <returns>The stored protected text.</returns>
        public string ReadToken(int id) =>
            ReadScalar("SELECT \"EncryptedToken\" FROM \"Repositories\" WHERE \"Id\" = $id", id);

        /// <summary>
        ///     Reads the stored secret of a user.
        /// </summary>
        /// <param name="userId">The id of the user.</param>
        /// <returns>The stored protected text.</returns>
        public string ReadTotp(int userId) =>
            ReadScalar("SELECT \"ProtectedSecret\" FROM \"UserTotps\" WHERE \"UserId\" = $id", userId);

        /// <summary>
        ///     Makes every update of the second-factor table fail, so that the table of the command's second step
        ///     cannot be written.
        /// </summary>
        public void BlockTotpUpdates()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TRIGGER \"BlockTotpUpdates\" BEFORE UPDATE ON \"UserTotps\" " +
                "BEGIN SELECT RAISE(ABORT, 'update blocked by the test'); END";
            command.ExecuteNonQuery();
        }

        /// <summary>
        ///     Gets the key files of the ring.
        /// </summary>
        /// <returns>The paths.</returns>
        public string[] KeyFiles() =>
            Directory.Exists(KeysFolder) ? Directory.GetFiles(KeysFolder, "key-*.xml") : [];

        /// <summary>
        ///     Gets the names and the hashes of the content of the key files, to tell that nothing changed.
        /// </summary>
        /// <returns>The names with a hash of the content, in name order.</returns>
        public List<string> KeyFileSnapshot() =>
            KeyFiles()
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => $"{Path.GetFileName(path)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}")
                .ToList();

        /// <inheritdoc />
        public void Dispose()
        {
            // The services first: they hold the database, and the folder cannot be deleted while it is in use
            _provider.Dispose();
            Folder.Dispose();
        }

        /// <summary>
        ///     Reads one text value with one id parameter.
        /// </summary>
        /// <param name="sql">The query.</param>
        /// <param name="id">The id.</param>
        /// <returns>The value.</returns>
        private string ReadScalar(string sql, int id)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id);
            return (string)command.ExecuteScalar()!;
        }
    }
}
