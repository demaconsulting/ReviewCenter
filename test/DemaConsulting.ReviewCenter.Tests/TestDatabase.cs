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

using System.Data.Common;
using System.Runtime.CompilerServices;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Makes the SQLite databases of the in-process test applications cheap to commit to: the journal is kept in
///     memory and the commit does not wait for the disk.
/// </summary>
/// <remarks>
///     With the default settings every commit of the product database waits for several disk flushes, which is
///     the dominant cost of arranging a review in a test (a signed review is about a hundred commits) and is far
///     slower on a CI runner than on a developer machine. The settings change only how long a commit takes, not
///     what it stores: the tests read their data back through new connections of the same process, so they see
///     exactly what they would see with the default settings. A crash of the operating system could lose the file,
///     which does not matter for a throw-away database in a temporary folder. The product defaults are not touched.
/// </remarks>
internal sealed class FastCommitInterceptor : DbConnectionInterceptor
{
    /// <summary>
    ///     The statement that turns the disk waits off for a connection.
    /// </summary>
    private const string Pragmas = "PRAGMA synchronous=OFF; PRAGMA journal_mode=MEMORY;";

    /// <summary>
    ///     Registers the interceptor on the database context of an application under test.
    /// </summary>
    /// <param name="services">The services of the application.</param>
    public static void AddTo(IServiceCollection services)
    {
        services.ConfigureDbContext<ReviewCenterDbContext>(builder => builder.AddInterceptors(new FastCommitInterceptor()));
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (IsReadOnly(connection))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (IsReadOnly(connection))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    ///     Checks whether a connection is read-only, as the one with which EF Core probes whether the database exists
    ///     is; the journal mode of such a connection cannot be changed.
    /// </summary>
    /// <param name="connection">The connection that was just opened.</param>
    /// <returns><c>true</c> when the connection was opened read-only.</returns>
    private static bool IsReadOnly(DbConnection connection)
    {
        return new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly;
    }
}

/// <summary>
///     Keeps every test that starts the application away from the real per-user application data folder.
/// </summary>
/// <remarks>
///     The product default database lives under the user's local application data, which a test run must
///     never create or modify. A module initializer points the whole test process (and every server
///     process it launches, because child processes inherit the environment) at a private temporary
///     folder through the <c>Database__ConnectionString</c> configuration variable. Tests that need a
///     database of their own ask for a fresh file with <see cref="NewConnectionString"/>.
/// </remarks>
internal static class TestDatabase
{
    /// <summary>
    ///     Name of the environment variable that supplies <c>Database:ConnectionString</c>.
    /// </summary>
    public const string ConnectionStringVariable = "Database__ConnectionString";

    /// <summary>
    ///     The private folder that holds every database file created by this test run.
    /// </summary>
    private static readonly string _root = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Creates the self-signed attestation key of the test run once, before the first application that needs it.
    /// </summary>
    /// <remarks>
    ///     Every in-process application of the run keeps its key in the same private folder, so the first one to need it
    ///     creates the file and the others load it. When many applications start at once, one of them can open the file
    ///     in the moment after another moved it into place (a virus scanner or indexer may hold it briefly) and fails
    ///     to start. Creating the key first, alone, removes that race without giving every application its own key,
    ///     which would cost an RSA key generation per application.
    /// </remarks>
    private static readonly Lazy<bool> _attestationKey = new(CreateAttestationKey);

    /// <summary>
    ///     Makes sure the shared self-signed attestation key exists and can be read.
    /// </summary>
    public static void EnsureAttestationKey() => _ = _attestationKey.Value;

    /// <summary>
    ///     Creates the key in the folder the product uses beside the database, and reads it back once.
    /// </summary>
    /// <returns><c>true</c> when the key could be read.</returns>
    private static bool CreateAttestationKey()
    {
        var options = new AttestationOptions { SelfSignedFolder = Path.Combine(_root, "keys", "attestation-selfsigned") };
        const int attempts = 10;
        for (var attempt = 1; attempt < attempts; attempt++)
        {
            try
            {
                return ReadAttestationKey(options);
            }
            catch (AttestationKeyException)
            {
                // The file was moved into place a moment ago and is still held by a scanner: try again
                Thread.Sleep(100);
            }
        }

        return ReadAttestationKey(options);
    }

    /// <summary>
    ///     Creates the key if it does not exist yet, and loads it.
    /// </summary>
    /// <param name="options">The options with the folder of the key.</param>
    /// <returns><c>true</c> when the key could be loaded.</returns>
    private static bool ReadAttestationKey(AttestationOptions options)
    {
        using var provider = new SelfSignedKeyProvider(options, TimeProvider.System, NullLogger.Instance);
        _ = provider.GetSigningKey();
        return true;
    }

    /// <summary>
    ///     Creates a connection string for a new, empty database file inside the private folder.
    /// </summary>
    /// <returns>A SQLite connection string with pooling off so the file can be deleted afterwards.</returns>
    public static string NewConnectionString()
    {
        Directory.CreateDirectory(_root);
        return $"Data Source={Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db")};Pooling=False";
    }

    /// <summary>
    ///     Redirects the process-wide default database into the private folder and schedules its removal.
    /// </summary>
    /// <remarks>
    ///     Runs once when the test assembly loads, before any test starts a server.
    /// </remarks>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(ConnectionStringVariable, NewConnectionString());

        // Best-effort cleanup: files that are still locked are left for the operating system's temp cleanup
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Ignored: the folder is under the temp directory
            }
            catch (UnauthorizedAccessException)
            {
                // Ignored: the folder is under the temp directory
            }
        };
    }
}
