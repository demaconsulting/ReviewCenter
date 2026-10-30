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

using DemaConsulting.ReviewCenter.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     The opt-in check (<c>--validate --check-config</c>) that the database the server is configured to use can be
///     opened and has the schema this build expects.
/// </summary>
/// <remarks>
///     This is the only check that looks outside its own temporary folder, which is why it must be asked for. It
///     loads the configuration the way the server does (<c>appsettings.json</c> and the environment variables),
///     recognizes the provider, opens the database <b>read-only</b> and compares the tables and columns of the
///     entity model with the database. It never writes, never creates the database, and never prints the
///     connection string, the path or any secret: problems are reported by table and column name only. With no
///     configuration and no database at the default location, or with a configured file that does not exist yet
///     (it is created at the first start), it is skipped. There is no schema version number in
///     the database, so the comparison of the tables and columns is the version check. The class is stateless and
///     thread-safe.
/// </remarks>
internal static class ConfiguredDatabaseCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_ConfiguredDatabase";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The provider and the number of tables and columns that matched; never the connection string.</returns>
    /// <exception cref="ValidationSkippedException">Thrown when no database is configured and none exists at the default location, or the configured file does not exist yet.</exception>
    /// <exception cref="ValidationFailedException">Thrown when the provider is unsupported, the database cannot be opened or the schema differs.</exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        // The same configuration sources as the server: appsettings.json next to the program and the environment
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(ConfiguredDatabaseCheck).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        var options = new DatabaseOptions();
        builder.Configuration.GetSection(DatabaseSetup.SectionName).Bind(options);

        // Nothing configured and nothing at the default place: there is nothing to check
        if (string.IsNullOrWhiteSpace(options.ConnectionString) && !File.Exists(DatabaseSetup.GetDefaultDatabasePath()))
        {
            throw new ValidationSkippedException(
                "No database is configured (Database:ConnectionString is empty) and none exists at the default location.");
        }

        try
        {
            DatabaseSetup.RequireSupportedProvider(options);

            // A database that does not exist yet is not a defect: it is created at the first start
            if (DatabaseSetup.TryGetMissingDatabaseFile(options) is not null)
            {
                throw new ValidationSkippedException(
                    "The configured database file does not exist yet; it is created at the first start.");
            }

            // Read-only and unpooled: the check can neither change the database nor keep it locked
            var connectionString = new SqliteConnectionStringBuilder(DatabaseSetup.ResolveConnectionString(options))
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            await using var connection = new SqliteConnection(connectionString.ToString());
            await connection.OpenAsync(cancellationToken);
            var (tables, columns, problems) = await CompareSchemaAsync(connection, connectionString.ToString(), cancellationToken);
            ValidationFailedException.ThrowUnless(
                problems.Count == 0,
                "The database schema does not match this version of ReviewCenter: " + string.Join("; ", problems) + ".");

            return $"provider {options.Provider.Trim()}; opened read-only; schema matches ({tables} tables, {columns} columns)";
        }
        catch (InvalidOperationException ex)
        {
            // Configuration errors (unsupported provider, in-memory database) never contain the connection string
            throw new ValidationFailedException(ex.Message);
        }
        catch (SqliteException ex)
        {
            // The SQLite message names the problem, not the path or the connection string
            throw new ValidationFailedException($"The configured database could not be read: {ex.Message}");
        }
    }

    /// <summary>
    ///     Compares the tables and columns of the entity model with those of the open database.
    /// </summary>
    /// <param name="connection">The open read-only connection.</param>
    /// <param name="connectionString">The read-only connection string, used only to build the model.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The number of expected tables and columns and the list of differences, by name.</returns>
    private static async Task<(int Tables, int Columns, List<string> Problems)> CompareSchemaAsync(
        SqliteConnection connection,
        string connectionString,
        CancellationToken cancellationToken)
    {
        // The model is only read: no connection is opened by creating the context
        using var context = new ReviewCenterDbContext(
            new DbContextOptionsBuilder<ReviewCenterDbContext>().UseSqlite(connectionString).Options);
        var expected = context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() != null)
            .ToDictionary(
                entity => entity.GetTableName()!,
                entity => entity.GetFlattenedProperties()
                    .Select(property => property.GetColumnName(StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value))
                    .Where(column => column != null)
                    .Select(column => column!)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var actualTables = await ReadAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'", cancellationToken);
        var problems = new List<string>();
        foreach (var table in expected.Keys.Except(actualTables, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add($"table '{table}' is missing");
        }

        foreach (var table in actualTables.Except(expected.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            problems.Add($"table '{table}' is not part of this version");
        }

        foreach (var (table, columns) in expected.Where(pair => actualTables.Contains(pair.Key)))
        {
            // Table names come from the model, never from the user; they are quoted all the same
            var actualColumns = await ReadAsync(
                connection, $"SELECT name FROM pragma_table_info('{table.Replace("'", "''", StringComparison.Ordinal)}')", cancellationToken);
            problems.AddRange(columns.Except(actualColumns, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(column => $"column '{table}.{column}' is missing"));
            problems.AddRange(actualColumns.Except(columns, StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(column => $"column '{table}.{column}' is not part of this version"));
        }

        return (expected.Count, expected.Values.Sum(columns => columns.Count), problems);
    }

    /// <summary>
    ///     Reads the first column of a query as a set of names.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The read-only query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The names.</returns>
    private static async Task<HashSet<string>> ReadAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
