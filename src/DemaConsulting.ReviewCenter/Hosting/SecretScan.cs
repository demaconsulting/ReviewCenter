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

using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.Data.Sqlite;

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     What a read-only scan found: the state of the key ring and how many protected secrets of the database can
///     still be read with it.
/// </summary>
/// <param name="KeyFiles">The number of <c>key-*.xml</c> files in the key folder.</param>
/// <param name="PlaintextKeyFiles">The number of those files that hold their key unencrypted.</param>
/// <param name="RepositoryTokens">The number of stored repository tokens (<c>Repositories.EncryptedToken</c>).</param>
/// <param name="UnreadableRepositoryTokens">The number of repository tokens that cannot be decrypted.</param>
/// <param name="TotpSecrets">The number of stored second-factor secrets (<c>UserTotps.ProtectedSecret</c>).</param>
/// <param name="UnreadableTotpSecrets">The number of second-factor secrets that cannot be decrypted.</param>
/// <param name="UnreadableRepositoryIds">The ids of the repositories whose token cannot be decrypted, ascending.</param>
/// <param name="UnreadableUserIds">The ids of the users whose second-factor secret cannot be decrypted, ascending.</param>
/// <remarks>
///     The result holds counts and row ids only; no secret, no ciphertext and no path ever ends up in it. It is an
///     immutable value, so it can be passed around and compared freely.
/// </remarks>
internal sealed record SecretScanResult(
    int KeyFiles,
    int PlaintextKeyFiles,
    int RepositoryTokens,
    int UnreadableRepositoryTokens,
    int TotpSecrets,
    int UnreadableTotpSecrets,
    IReadOnlyList<int> UnreadableRepositoryIds,
    IReadOnlyList<int> UnreadableUserIds)
{
    /// <summary>
    ///     Gets the number of protected secrets in the database, of both kinds.
    /// </summary>
    public int ProtectedSecrets => RepositoryTokens + TotpSecrets;

    /// <summary>
    ///     Gets the number of protected secrets that cannot be decrypted, of both kinds.
    /// </summary>
    public int UnreadableSecrets => UnreadableRepositoryTokens + UnreadableTotpSecrets;

    /// <summary>
    ///     Gets a value indicating whether the key folder holds no key file at all, which is what a lost or never
    ///     restored key ring looks like.
    /// </summary>
    public bool RingIsEmpty => KeyFiles == 0;
}

/// <summary>
///     The read-only scan that tells whether the secrets stored in the database can still be decrypted with the key
///     ring that is configured.
/// </summary>
/// <remarks>
///     A restored database next to a missing or wrong key ring looks healthy until somebody uses a repository token or
///     signs in with the second factor. The scan finds that at once: it counts the key files of the ring (and how many
///     of them are not encrypted), then decrypts every stored repository token and second-factor secret with the
///     production protectors, discards the plaintext on the spot, and counts the ones that fail. It is strictly
///     read-only. The database is opened read-only, so a missing file is never created and a missing database, table
///     or row simply means no secrets. Nothing is ever protected, so no key is created; and because decrypting
///     against a ring without any usable key file (none, or only malformed or unreadable ones) would make Data
///     Protection create a key, such a ring is not asked at all and every secret is counted as unreadable. The
///     caller that must not create keys under any circumstance (the self-validation) additionally switches
///     automatic key generation off. The class is stateless and thread-safe.
/// </remarks>
internal static class SecretScan
{
    /// <summary>
    ///     The query that returns 2 when the repository table has both columns the scan reads.
    /// </summary>
    private const string RepositoryProbeSql =
        "SELECT COUNT(*) FROM pragma_table_info('Repositories') WHERE name IN ('Id', 'EncryptedToken')";

    /// <summary>
    ///     The query for the id and the encrypted token of every repository that has one.
    /// </summary>
    private const string RepositorySelectSql =
        "SELECT \"Id\", \"EncryptedToken\" FROM \"Repositories\" WHERE \"EncryptedToken\" IS NOT NULL AND \"EncryptedToken\" <> ''";

    /// <summary>
    ///     The query that returns 2 when the second-factor table has both columns the scan reads.
    /// </summary>
    private const string TotpProbeSql =
        "SELECT COUNT(*) FROM pragma_table_info('UserTotps') WHERE name IN ('UserId', 'ProtectedSecret')";

    /// <summary>
    ///     The query for the user id and the protected secret of every second-factor row.
    /// </summary>
    private const string TotpSelectSql =
        "SELECT \"UserId\", \"ProtectedSecret\" FROM \"UserTotps\" WHERE \"ProtectedSecret\" IS NOT NULL AND \"ProtectedSecret\" <> ''";

    /// <summary>
    ///     Scans the key ring and the protected secrets of the database.
    /// </summary>
    /// <param name="keysFolder">The folder of the key ring. Must not be null.</param>
    /// <param name="database">The database options; the database is opened read-only. Must not be null.</param>
    /// <param name="credentials">The production protector of repository tokens. Must not be null.</param>
    /// <param name="totpSecrets">The production protector of second-factor secrets. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The counts; a missing database, table or row is no secret and no error.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the database configuration is not usable (an in-memory database).</exception>
    /// <exception cref="SqliteException">Thrown when the database exists but cannot be read.</exception>
    /// <remarks>
    ///     Side effects: none on the database or the key folder. The scan never calls a <c>Protect</c> method.
    /// </remarks>
    public static async Task<SecretScanResult> ScanAsync(
        string keysFolder,
        DatabaseOptions database,
        ICredentialProtector credentials,
        ITotpSecretProtector totpSecrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keysFolder);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(totpSecrets);

        var (keyFiles, plaintext, encrypted) = KeyRingProtection.InspectKeyFiles(keysFolder);

        // A file that is neither a plaintext nor an encrypted key (malformed, unreadable) is no key: with only such
        // files Data Protection would create a replacement key when asked, so the ring is not asked then either
        var ringHasNoKey = plaintext + encrypted == 0;

        // The rows are read first, then decrypted: the database is not held open while the ring is asked
        var repositories = new List<(int Id, string Text)>();
        var totps = new List<(int Id, string Text)>();
        if (DatabaseSetup.TryGetMissingDatabaseFile(database) is null)
        {
            // Read-only and unpooled: the scan can neither create nor change the database, nor keep it locked
            var connectionString = new SqliteConnectionStringBuilder(DatabaseSetup.ResolveReadOnlyConnectionString(database))
            {
                Pooling = false
            };
            await using var connection = new SqliteConnection(connectionString.ToString());
            await connection.OpenAsync(cancellationToken);
            await ReadAsync(connection, RepositoryProbeSql, RepositorySelectSql, repositories, cancellationToken);
            await ReadAsync(connection, TotpProbeSql, TotpSelectSql, totps, cancellationToken);
        }

        var unreadableRepositories = new List<int>();
        foreach (var (id, text) in repositories)
        {
            // Without a key file the ring cannot hold what protected the value, and asking would create a key
            if (ringHasNoKey || !CanUnprotect(() => _ = credentials.Unprotect(text)))
            {
                unreadableRepositories.Add(id);
            }
        }

        var unreadableTotps = new List<int>();
        foreach (var (id, text) in totps)
        {
            if (ringHasNoKey || !CanUnprotect(() => Array.Clear(totpSecrets.Unprotect(id, text))))
            {
                unreadableTotps.Add(id);
            }
        }

        unreadableRepositories.Sort();
        unreadableTotps.Sort();
        return new SecretScanResult(
            keyFiles,
            plaintext,
            repositories.Count,
            unreadableRepositories.Count,
            totps.Count,
            unreadableTotps.Count,
            unreadableRepositories,
            unreadableTotps);
    }

    /// <summary>
    ///     Formats the ids of unreadable rows for a message, shortened when there are many.
    /// </summary>
    /// <param name="ids">The row ids.</param>
    /// <returns>The ids separated by commas; at most ten, followed by the number of the others.</returns>
    public static string FormatIds(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        const int shown = 10;
        var text = string.Join(", ", ids.Take(shown));
        return ids.Count > shown ? $"{text} and {ids.Count - shown} more" : text;
    }

    /// <summary>
    ///     Decrypts one value with the action and tells whether that worked; the plaintext never leaves the action.
    /// </summary>
    /// <param name="decrypt">The action that decrypts and discards the result.</param>
    /// <returns><c>true</c> when the value could be decrypted.</returns>
    private static bool CanUnprotect(Action decrypt)
    {
        try
        {
            decrypt();
            return true;
        }
        catch (Exception ex) when (ex is DomainRuleException or CryptographicException or FormatException)
        {
            // Unreadable is the finding, not an error; the message of the exception is not used
            return false;
        }
    }

    /// <summary>
    ///     Reads the id and the protected text of every row of a table; nothing when the table or a column is missing.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <param name="probeSql">A query that returns 2 when the table and both columns exist (a constant of this class).</param>
    /// <param name="selectSql">The query for the id and the protected text of the rows with a value (a constant of this class).</param>
    /// <param name="rows">The list that receives the rows with a value.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    private static async Task ReadAsync(
        SqliteConnection connection,
        string probeSql,
        string selectSql,
        List<(int Id, string Text)> rows,
        CancellationToken cancellationToken)
    {
        // A database from before a table existed has no secrets of that kind
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = probeSql;
            if (Convert.ToInt32(await probe.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 2)
            {
                return;
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = selectSql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((reader.GetInt32(0), reader.GetString(1)));
        }
    }
}
