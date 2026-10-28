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
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Builds the production services over a private, embedded SQLite database inside a temporary folder, so a
///     check exercises the real services without touching the configured database, key folders or port.
/// </summary>
/// <remarks>
///     The services are registered by the same <see cref="ServerHost.AddServices"/> the server uses, so the check
///     runs the production code. Only the configuration differs: it is an in-memory set that points the database,
///     the Data Protection keys, the git cache and the attestation key at the temporary folder. The
///     <c>appsettings.json</c> file and the environment variables are deliberately not read, which is what makes
///     the self-validation independent of how the server is configured. The class is stateless and thread-safe.
/// </remarks>
internal static class SelfTestServices
{
    /// <summary>
    ///     The password of the throw-away user; it satisfies the production password policy and protects nothing.
    /// </summary>
    public const string SelfTestSecret = "correct horse battery";

    /// <summary>
    ///     Creates the service provider and prepares the embedded database.
    /// </summary>
    /// <param name="folder">The temporary folder that holds everything the services write. Must not be null.</param>
    /// <returns>The provider. The caller owns it and must dispose it before the folder is deleted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> is null.</exception>
    /// <remarks>
    ///     Side effects: creates the database file and its schema in <paramref name="folder"/>. Pooling is off so
    ///     the file is released when the provider is disposed and can be deleted afterwards.
    /// </remarks>
    public static ServiceProvider Create(TemporaryDirectory folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        // An in-memory configuration only: appsettings.json and the environment are never consulted
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CreateSettings(folder))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddServices(services, configuration);

        // Only the speed of a commit changes, not what is stored (see FastCommitInterceptor)
        services.ConfigureDbContext<ReviewCenterDbContext>(builder => builder.AddInterceptors(new FastCommitInterceptor()));

        var provider = services.BuildServiceProvider();
        try
        {
            DatabaseSetup.EnsureCreated(
                provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
            return provider;
        }
        catch
        {
            // The caller never sees the provider when the database cannot be prepared
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Creates the settings that point every place the services write at the temporary folder.
    /// </summary>
    /// <param name="folder">The temporary folder. Must not be null.</param>
    /// <returns>The configuration keys and values: database, key ring, git cache and attestation key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder"/> is null.</exception>
    /// <remarks>
    ///     The host check applies the same settings to a real web host, so both checks share one definition of
    ///     "everything inside the temporary folder".
    /// </remarks>
    public static Dictionary<string, string?> CreateSettings(TemporaryDirectory folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new Dictionary<string, string?>
        {
            ["Database:Provider"] = DatabaseSetup.SqliteProvider,
            ["Database:ConnectionString"] = $"Data Source={folder.GetFilePath("selftest.db")};Pooling=False",
            ["DataProtection:KeysPath"] = Path.Combine(folder.DirectoryPath, "keys"),
            ["Git:CachePath"] = Path.Combine(folder.DirectoryPath, "git-cache"),
            ["Git:TimeoutSeconds"] = "60",
            ["Attestation:SelfSignedFolder"] = Path.Combine(folder.DirectoryPath, "attestation")
        };
    }

    /// <summary>
    ///     Makes the commits of the throw-away database cheap: the journal is kept in memory and a commit does not
    ///     wait for the disk.
    /// </summary>
    /// <remarks>
    ///     With the default settings every commit waits for several disk flushes, which is the dominant cost of a
    ///     review workflow (about a hundred commits). The settings change only how long a commit takes, not what
    ///     is stored, and a lost file would only be a lost temporary database. The product database settings are
    ///     not touched. A read-only connection is skipped because its journal mode cannot be changed.
    /// </remarks>
    private sealed class FastCommitInterceptor : DbConnectionInterceptor
    {
        /// <summary>
        ///     The statements that turn the disk waits off for a connection.
        /// </summary>
        private const string Pragmas = "PRAGMA synchronous=OFF; PRAGMA journal_mode=MEMORY;";

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
        ///     Checks whether a connection was opened read-only.
        /// </summary>
        /// <param name="connection">The connection that was just opened.</param>
        /// <returns><c>true</c> when the connection is read-only.</returns>
        private static bool IsReadOnly(DbConnection connection)
        {
            return new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly;
        }
    }
}
