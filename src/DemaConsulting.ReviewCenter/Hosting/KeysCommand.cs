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

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Data.Common;
using System.Security.Cryptography;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     The <c>keys reprotect</c> verb: rolls a new Data Protection key and re-protects every stored repository token
///     and second-factor secret with it, so that old key material can be retired.
/// </summary>
/// <remarks>
///     The command builds its configuration and services the same way the server does (the same
///     <c>appsettings.json</c> and environment variables, the same registration), so it reads the same key ring and
///     the same database; the server must be stopped while it runs. It is the only verb that writes. It first runs the
///     <see cref="SecretScan"/> and refuses, with a clear message and without any change, when a protected secret
///     cannot be read or the ring or the database cannot be scanned: re-protecting would then lose what is readable
///     now only by chance of the ring. Otherwise it creates a new key (<see cref="IKeyManager.CreateNewKey"/>, which
///     uses the production protection of the key files), decrypts every secret with the production protector and
///     protects it again, which uses the newest key, in one transaction per table. It then reports the counts and,
///     from the header of each stored payload (a magic number followed by the 16-byte key id; read for this report
///     only), which keys still protect a stored secret and which keys of the ring nothing references any more, and says
///     what the operator can do next. Key ids are reported as GUIDs; a secret, a ciphertext and a certificate
///     password are never printed or logged. Automatic key generation is switched off for the command, so that a
///     refusal cannot create a key as a side effect. After the new key exists the services are built a second time,
///     because a key ring that was read earlier is refreshed in the background and could still protect with the old
///     key for a moment. A second run is fine and gives another new key. The class is
///     stateless and therefore thread-safe.
/// </remarks>
internal static class KeysCommand
{
    /// <summary>
    ///     The number at the start of every Data Protection payload.
    /// </summary>
    private const uint PayloadMagic = 0x09F0C9F0;

    /// <summary>
    ///     The length of the header of a payload: the magic number and the key id.
    /// </summary>
    private const int PayloadHeaderLength = 4 + 16;

    /// <summary>
    ///     The query for the id and the encrypted token of every repository that has one.
    /// </summary>
    private const string RepositorySelectSql =
        "SELECT \"Id\", \"EncryptedToken\" FROM \"Repositories\" WHERE \"EncryptedToken\" IS NOT NULL AND \"EncryptedToken\" <> ''";

    /// <summary>
    ///     The statement that replaces the token of one repository, if it is still the one that was read.
    /// </summary>
    private const string RepositoryUpdateSql =
        "UPDATE \"Repositories\" SET \"EncryptedToken\" = $new WHERE \"Id\" = $id AND \"EncryptedToken\" = $old";

    /// <summary>
    ///     The query for the user id and the protected secret of every second-factor row.
    /// </summary>
    private const string TotpSelectSql =
        "SELECT \"UserId\", \"ProtectedSecret\" FROM \"UserTotps\" WHERE \"ProtectedSecret\" IS NOT NULL AND \"ProtectedSecret\" <> ''";

    /// <summary>
    ///     The statement that replaces the secret of one user, if it is still the one that was read.
    /// </summary>
    private const string TotpUpdateSql =
        "UPDATE \"UserTotps\" SET \"ProtectedSecret\" = $new WHERE \"UserId\" = $id AND \"ProtectedSecret\" = $old";

    /// <summary>
    ///     Re-protects the stored secrets as described by the command-line context.
    /// </summary>
    /// <param name="context">The parsed command line; results and errors are reported through it. Must not be null.</param>
    /// <param name="configure">
    ///     Optional adjustment of the configuration before the services are built; tests use it to select a
    ///     temporary database and key folder.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     Success prints the new key id, the counts and the next steps. A refusal (unreadable secrets, a ring or a
    ///     database that cannot be scanned, unusable key ring settings, a missing database) and a failure while
    ///     writing are reported with <see cref="Context.WriteError"/>, which makes the exit code 1. Side effects: a
    ///     new key file in the key folder and new protected text in the two tables; nothing at all when the command
    ///     refuses; a table is either completely re-protected or unchanged.
    /// </remarks>
    public static void Run(Context context, Action<ConfigurationManager>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Same configuration sources as the server (appsettings.json next to the program, environment variables)
        IHost Build()
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ApplicationName = typeof(KeysCommand).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory
            });
            configure?.Invoke(builder.Configuration);

            // The command reports through the context, so the host logging stays off
            builder.Logging.ClearProviders();
            ServerHost.AddServices(builder.Services, builder.Configuration);

            // Data Protection may not create a key on its own, whatever it is asked: a refusal must change nothing,
            // and the one new key is created explicitly
            builder.Services.AddOptions<KeyManagementOptions>().Configure(keys => keys.AutoGenerateKeys = false);
            return builder.Build();
        }

        try
        {
            using var host = Build();
            Reprotect(context, host.Services, Build);
        }
        catch (InvalidOperationException ex)
        {
            // Key ring settings and ring problems, an in-memory database: the messages name the setting or the
            // folder and never hold a key, a secret or the content of a file
            context.WriteError($"Refusing to re-protect, nothing was changed: {ex.Message}");
        }
        catch (DbException ex)
        {
            context.WriteError($"Failed to read or write the database: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.WriteError($"Failed to read or write the key ring or the database: {ex.Message}");
        }
        catch (CryptographicException)
        {
            // The message of a cryptographic failure describes a payload or a key; it is of no use here
            context.WriteError(
                "Failed to protect with the key ring: the new key could not be created or used. See " +
                "DataProtection:Protection and the certificate settings.");
        }
    }

    /// <summary>
    ///     Reads the id of the Data Protection key from the header of a protected payload.
    /// </summary>
    /// <param name="payload">The stored text of a protected value.</param>
    /// <param name="base64Url">
    ///     <c>true</c> for the URL-safe Base64 that <c>IDataProtector.Protect(string)</c> returns (repository tokens),
    ///     <c>false</c> for standard Base64 of the payload bytes (second-factor secrets).
    /// </param>
    /// <returns>The key id, or <c>null</c> when the text is no Data Protection payload.</returns>
    /// <remarks>
    ///     Diagnostic only: nothing else of the payload is read, and the result is used for the report and for no
    ///     decision about a secret.
    /// </remarks>
    internal static Guid? ReadKeyId(string? payload, bool base64Url)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = base64Url ? Base64Url.DecodeFromChars(payload) : Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length < PayloadHeaderLength || BinaryPrimitives.ReadUInt32BigEndian(bytes) != PayloadMagic)
        {
            return null;
        }

        return new Guid(bytes.AsSpan(4, 16));
    }

    /// <summary>
    ///     Scans, refuses or rolls the key, re-protects the secrets and reports.
    /// </summary>
    /// <param name="context">The context that receives the output.</param>
    /// <param name="services">The production services, used for the scan and to create the new key.</param>
    /// <param name="build">
    ///     Builds a second set of the production services after the new key exists: a Data Protection key ring that
    ///     was read before the new key was created is refreshed in the background and may still protect with the
    ///     old key for a moment, a new set reads the ring from the key folder and uses the newest key at once.
    /// </param>
    private static void Reprotect(Context context, IServiceProvider services, Func<IHost> build)
    {
        var plan = services.GetRequiredService<KeyRingPlan>();
        if (plan.Problem is not null)
        {
            context.WriteError($"Refusing to re-protect, nothing was changed: {plan.Problem}");
            return;
        }

        var settings = services.GetRequiredService<IOptions<KeyRingSettings>>().Value;
        var database = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var missing = DatabaseSetup.TryGetMissingDatabaseFile(database);
        if (missing is not null)
        {
            context.WriteError($"The database file does not exist: {missing}");
            return;
        }

        var folder = ServerHost.ResolveKeysPath(settings, database);
        var keyManager = services.GetRequiredService<IKeyManager>();
        var credentials = services.GetRequiredService<ICredentialProtector>();
        var totpSecrets = services.GetRequiredService<ITotpSecretProtector>();

        // Step 1: the scan. The ring is read the way the server reads it; unencrypted key files are no reason to
        // refuse here, because retiring them is what this command is for
        var relaxed = new KeyRingPlan(plan.Mode, plan.LocalMachine, false, null, [], null);
        KeyRingProtection.CheckRing(relaxed, keyManager, folder, NullLogger.Instance);
        var scan = SecretScan.ScanAsync(folder, database, credentials, totpSecrets).GetAwaiter().GetResult();
        if (scan.UnreadableSecrets > 0)
        {
            context.WriteError(DescribeRefusal(folder, scan));
            return;
        }

        // Step 2: the new key, protected as the configuration says
        var options = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var newKey = keyManager.CreateNewKey(now, now + options.NewKeyLifetime);
        context.WriteLine($"New Data Protection key: {newKey.KeyId}");

        // Step 3: every secret again, with the newest key, one transaction per table; from a fresh set of services
        var tokens = 0;
        var totps = 0;
        string? inProgress = null;
        try
        {
            using var second = build();
            var freshKeys = second.Services.GetRequiredService<IKeyManager>();
            credentials = second.Services.GetRequiredService<ICredentialProtector>();
            totpSecrets = second.Services.GetRequiredService<ITotpSecretProtector>();
            // Read-write but never creating, and unpooled: the command cannot create a database or keep one locked
            var connectionString = new SqliteConnectionStringBuilder(DatabaseSetup.ResolveConnectionString(database))
            {
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            if (scan.RepositoryTokens > 0)
            {
                inProgress = "repository token";
                tokens = ReprotectTable(
                    connection,
                    RepositorySelectSql,
                    RepositoryUpdateSql,
                    (_, text) => credentials.Protect(credentials.Unprotect(text)));
            }

            if (scan.TotpSecrets > 0)
            {
                inProgress = "second-factor secret";
                totps = ReprotectTable(connection, TotpSelectSql, TotpUpdateSql, (id, text) => ReprotectTotp(totpSecrets, id, text));
            }

            // Step 4: what the stored payloads reference now
            inProgress = null;
            Report(context, freshKeys, connection, newKey.KeyId, folder, tokens, totps);
        }
        catch (Exception ex) when (ex is DomainRuleException or InvalidOperationException or DbException
            or IOException or UnauthorizedAccessException or CryptographicException)
        {
            // The new key exists now, so whatever fails from here on, the caller must hear that and that a second run
            // is fine. A secret that was readable a moment ago, a row that changed while the command ran (the
            // server is not stopped?), a locked database, a full disk: the table of the failure is unchanged. The
            // message of a cryptographic failure describes a payload or a key and is never shown; the others hold
            // no secret.
            var reason = ex is CryptographicException
                ? "the key ring could not protect or read a secret."
                : ex.Message;
            var unchanged = inProgress is null
                ? "no table was being changed."
                : $"the {inProgress} table, which was being changed, is unchanged.";
            context.WriteError(
                $"Re-protecting stopped: {reason} A new key was created ({newKey.KeyId}). Tables that were completed " +
                $"before this stay re-protected ({tokens} repository token(s), {totps} second-factor secret(s)); " +
                $"{unchanged} Stop the server and run the command again.");
        }
    }

    /// <summary>
    ///     Re-protects every secret of one table in one transaction.
    /// </summary>
    /// <param name="connection">The open read-write connection.</param>
    /// <param name="selectSql">The query for the id and the protected text (a constant of this class).</param>
    /// <param name="updateSql">The statement that replaces one value if it is unchanged (a constant of this class).</param>
    /// <param name="transform">Decrypts the value of a row and protects it again.</param>
    /// <returns>The number of rows that were re-protected.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a row changed after it was read; nothing is committed then.</exception>
    private static int ReprotectTable(
        SqliteConnection connection,
        string selectSql,
        string updateSql,
        Func<int, string, string> transform)
    {
        // Disposing the transaction without a commit rolls everything back, whatever fails in between
        using var transaction = connection.BeginTransaction();
        var rows = ReadRows(connection, transaction, selectSql);
        foreach (var (id, text) in rows)
        {
            var replacement = transform(id, text);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = updateSql;
            command.Parameters.AddWithValue("$new", replacement);
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$old", text);
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("A stored secret changed while it was being re-protected.");
            }
        }

        transaction.Commit();
        return rows.Count;
    }

    /// <summary>
    ///     Decrypts a second-factor secret and protects it again, clearing the plaintext.
    /// </summary>
    /// <param name="protector">The production protector.</param>
    /// <param name="userId">The id of the user, which binds the secret.</param>
    /// <param name="protectedText">The stored text.</param>
    /// <returns>The text to store.</returns>
    private static string ReprotectTotp(ITotpSecretProtector protector, int userId, string protectedText)
    {
        var secret = protector.Unprotect(userId, protectedText);
        try
        {
            return protector.Protect(userId, secret);
        }
        finally
        {
            Array.Clear(secret);
        }
    }

    /// <summary>
    ///     Reads the id and the protected text of every row of a query.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the connection is in, or <c>null</c>.</param>
    /// <param name="sql">The query (a constant of this class).</param>
    /// <returns>The rows.</returns>
    /// <exception cref="SqliteException">Thrown when the table does not exist or cannot be read.</exception>
    private static List<(int Id, string Text)> ReadRows(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var rows = new List<(int Id, string Text)>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>
    ///     Makes the message for a refusal because secrets cannot be read.
    /// </summary>
    /// <param name="folder">The key folder; the only path that may appear in a message.</param>
    /// <param name="scan">What the scan found.</param>
    /// <returns>The message: counts and row ids only.</returns>
    private static string DescribeRefusal(string folder, SecretScanResult scan)
    {
        var reason = scan.RingIsEmpty
            ? $"the key ring '{folder}' is empty"
            : "the key ring cannot decrypt them";
        return
            $"Refusing to re-protect, nothing was changed: {scan.UnreadableSecrets} of {scan.ProtectedSecrets} " +
            $"protected secrets cannot be read ({reason}): {scan.UnreadableRepositoryTokens} repository token(s) " +
            $"(repository ids {SecretScan.FormatIds(scan.UnreadableRepositoryIds)}) and {scan.UnreadableTotpSecrets} " +
            $"second-factor secret(s) (user ids {SecretScan.FormatIds(scan.UnreadableUserIds)}). Re-protecting would " +
            "lose them for good. Restore the key folder (and the certificates in DataProtection:CertificatePath and " +
            "DataProtection:PreviousCertificatePaths) that belongs to this database and run 'reviewcenter " +
            "--validate --check-config' until it passes, or enter the repository tokens again and have an " +
            "administrator reset the two-factor secret of each affected user.";
    }

    /// <summary>
    ///     Prints the counts, the keys that stored payloads still reference, and the next steps.
    /// </summary>
    /// <param name="context">The context that receives the output.</param>
    /// <param name="keyManager">The key manager, for the ids of the keys of the ring.</param>
    /// <param name="connection">The open connection, which is read again after the commits.</param>
    /// <param name="newKeyId">The id of the key that was created.</param>
    /// <param name="folder">The key folder.</param>
    /// <param name="tokens">The number of repository tokens that were re-protected.</param>
    /// <param name="totps">The number of second-factor secrets that were re-protected.</param>
    private static void Report(
        Context context,
        IKeyManager keyManager,
        SqliteConnection connection,
        Guid newKeyId,
        string folder,
        int tokens,
        int totps)
    {
        context.WriteLine($"Re-protected {tokens} repository token(s) and {totps} second-factor secret(s) with the new key.");

        // What the stored payloads reference now, read from the payload headers
        var references = new SortedDictionary<Guid, (int Tokens, int Totps)>();
        var unknown = 0;
        if (tokens > 0)
        {
            foreach (var (_, text) in ReadRows(connection, null, RepositorySelectSql))
            {
                Count(references, ReadKeyId(text, base64Url: true), isToken: true, ref unknown);
            }
        }

        if (totps > 0)
        {
            foreach (var (_, text) in ReadRows(connection, null, TotpSelectSql))
            {
                Count(references, ReadKeyId(text, base64Url: false), isToken: false, ref unknown);
            }
        }

        var older = references.Where(entry => entry.Key != newKeyId).ToList();
        if (older.Count == 0 && unknown == 0)
        {
            context.WriteLine($"No stored secret references an older key; all {tokens + totps} reference {newKeyId}.");
        }
        else
        {
            context.WriteLine("Stored secrets that still reference another key:");
            foreach (var (keyId, (tokenCount, totpCount)) in older)
            {
                context.WriteLine($"  key {keyId}: {tokenCount} repository token(s), {totpCount} second-factor secret(s)");
            }

            if (unknown > 0)
            {
                context.WriteLine($"  {unknown} stored value(s) whose key id could not be read");
            }
        }

        // The keys of the ring that no stored secret needs any more
        var unreferenced = keyManager.GetAllKeys()
            .Select(key => key.KeyId)
            .Where(id => id != newKeyId && !references.ContainsKey(id))
            .Order()
            .ToList();
        var plaintext = KeyRingProtection.InspectKeyFiles(folder).Plaintext;
        context.WriteLine(unreferenced.Count == 0
            ? "The key ring holds no older key that nothing references."
            : $"Older keys that no stored secret references: {string.Join(", ", unreferenced)}");
        context.WriteLine($"Key files of the ring that are not encrypted: {plaintext}");
        context.WriteLine("");
        context.WriteLine("Next steps:");
        context.WriteLine("  - Run 'reviewcenter --validate --check-config' to confirm that the key ring and the database fit.");
        context.WriteLine("  - The key files key-<id>.xml of the older keys listed above can be deleted from the key folder.");
        context.WriteLine("    Keep a copy until the new key ring is backed up. Once every older key file is deleted, the");
        context.WriteLine("    previous certificate (DataProtection:PreviousCertificatePaths) is no longer needed and can be dropped.");
        context.WriteLine("  - Sign-in cookies that were protected with an older key stop working when its file is deleted:");
        context.WriteLine("    users sign in again once.");
        context.WriteLine("  - The self-signed attestation key file is not changed by this command. If the key that protected it");
        context.WriteLine("    is deleted, delete attestation-selfsigned.pfx.protected too; a new key is created at the next start.");
        context.WriteLine("  - Start the server again.");
    }

    /// <summary>
    ///     Counts one stored payload by the key it references.
    /// </summary>
    /// <param name="references">The counts per key id.</param>
    /// <param name="keyId">The key id of the payload, or <c>null</c> when it could not be read.</param>
    /// <param name="isToken"><c>true</c> for a repository token, <c>false</c> for a second-factor secret.</param>
    /// <param name="unknown">The number of payloads without a readable key id.</param>
    private static void Count(
        SortedDictionary<Guid, (int Tokens, int Totps)> references,
        Guid? keyId,
        bool isToken,
        ref int unknown)
    {
        if (keyId is null)
        {
            unknown++;
            return;
        }

        references.TryGetValue(keyId.Value, out var current);
        references[keyId.Value] = isToken ? (current.Tokens + 1, current.Totps) : (current.Tokens, current.Totps + 1);
    }
}
