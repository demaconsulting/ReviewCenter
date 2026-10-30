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

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Storage;

/// <summary>
///     Chooses and prepares the database from the configured <see cref="DatabaseOptions"/>: provider
///     selection, connection string and default file location, and schema creation.
/// </summary>
/// <remarks>
///     Only SQLite is implemented. The provider switch in <see cref="Configure"/> is the one place
///     to extend when another provider is added; any other provider name is reported as a clear
///     configuration error rather than silently falling back. The class is stateless and thread-safe.
/// </remarks>
internal static class DatabaseSetup
{
    /// <summary>
    ///     The name of the configuration section that holds <see cref="DatabaseOptions"/>.
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    ///     The name of the SQLite provider, the only supported one.
    /// </summary>
    public const string SqliteProvider = "Sqlite";

    /// <summary>
    ///     The folder name, inside the application data folder, that holds the default database.
    /// </summary>
    private const string DefaultFolderName = "ReviewCenter";

    /// <summary>
    ///     The file name of the default database.
    /// </summary>
    private const string DefaultFileName = "reviewcenter.db";

    /// <summary>
    ///     Gets the default database file path for the current user and machine.
    /// </summary>
    /// <returns>An absolute path under the local application data folder.</returns>
    /// <remarks>
    ///     Side effect: none; the file and its folder are only created by <see cref="EnsureCreated"/>.
    /// </remarks>
    public static string GetDefaultDatabasePath()
    {
        return GetDefaultDatabasePath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppContext.BaseDirectory);
    }

    /// <summary>
    ///     Computes the default database file path from the given folders.
    /// </summary>
    /// <param name="localApplicationData">
    ///     The local application data folder; may be empty (a Linux service without a home folder).
    /// </param>
    /// <param name="baseDirectory">The application directory, used when no data folder is available.</param>
    /// <returns>
    ///     <c>{localApplicationData}/ReviewCenter/reviewcenter.db</c>, or
    ///     <c>{baseDirectory}/data/reviewcenter.db</c> when the data folder is empty.
    /// </returns>
    /// <remarks>
    ///     Separated from <see cref="GetDefaultDatabasePath()"/> so the fallback can be tested on any machine.
    /// </remarks>
    public static string GetDefaultDatabasePath(string localApplicationData, string baseDirectory)
    {
        // A service account without a home folder reports an empty data folder; keep the data beside the program then
        return string.IsNullOrEmpty(localApplicationData)
            ? Path.Combine(baseDirectory, "data", DefaultFileName)
            : Path.Combine(localApplicationData, DefaultFolderName, DefaultFileName);
    }

    /// <summary>
    ///     Computes the SQLite connection string for the options.
    /// </summary>
    /// <param name="options">The configured options. Must not be null.</param>
    /// <returns>A full SQLite connection string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the connection string selects an in-memory database, which cannot work here because each
    ///     connection would get its own empty database.
    /// </exception>
    /// <remarks>
    ///     An empty value selects the default file. A value containing <c>=</c> is taken as a full
    ///     connection string (for example <c>Data Source=...;Pooling=False</c>); anything else is taken as a
    ///     database file path. Relative paths are resolved by SQLite against the working directory.
    /// </remarks>
    public static string ResolveConnectionString(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.ConnectionString?.Trim() ?? string.Empty;

        // Default location when nothing is configured
        if (configured.Length == 0)
        {
            return new SqliteConnectionStringBuilder { DataSource = GetDefaultDatabasePath() }.ToString();
        }

        // A full connection string is used as it is; a plain path is wrapped
        var resolved = configured.Contains('=', StringComparison.Ordinal)
            ? configured
            : new SqliteConnectionStringBuilder { DataSource = configured }.ToString();

        // Each connection to an in-memory database is a separate empty database, so the schema created at
        // startup would not exist for the requests that follow: refuse it instead of starting a host that
        // cannot serve data
        var builder = new SqliteConnectionStringBuilder(resolved);
        if (builder.Mode == SqliteOpenMode.Memory ||
            string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource.Trim().Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "An in-memory SQLite database is not supported because every connection would get its own empty database. " +
                "Configure a database file path instead.");
        }

        return resolved;
    }

    /// <summary>
    ///     Gets the folder that holds the database file, the base of the default key ring and git cache folders.
    /// </summary>
    /// <param name="options">The configured options. Must not be null.</param>
    /// <returns>
    ///     The absolute folder of the database file, or the folder of the default database when the options name no
    ///     usable file (an in-memory database or a <c>file:</c> URI).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public static string ResolveDataDirectory(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? dataSource = null;
        try
        {
            dataSource = new SqliteConnectionStringBuilder(ResolveConnectionString(options)).DataSource;
        }
        catch (InvalidOperationException)
        {
            // An unsupported (in-memory) configuration is reported where the database is opened; here the default will do
        }

        if (string.IsNullOrEmpty(dataSource) || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            dataSource = GetDefaultDatabasePath();
        }

        return Path.GetDirectoryName(Path.GetFullPath(dataSource))!;
    }

    /// <summary>
    ///     Selects the database provider and connection on a context options builder.
    /// </summary>
    /// <param name="builder">The builder to configure. Must not be null.</param>
    /// <param name="options">The configured options. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the provider is not supported; the message lists the supported providers.
    /// </exception>
    public static void Configure(DbContextOptionsBuilder builder, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        // The provider switch: add the other providers here (and in RequireSupportedProvider) when implemented
        RequireSupportedProvider(options);
        builder.UseSqlite(ResolveConnectionString(options));
    }

    /// <summary>
    ///     Throws a clear configuration error when the configured provider is not supported.
    /// </summary>
    /// <param name="options">The configured options.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the provider is not supported; the message lists the supported providers.
    /// </exception>
    /// <remarks>
    ///     Internal so the opt-in configured-database self-check can reuse the provider test instead of repeating it.
    /// </remarks>
    internal static void RequireSupportedProvider(DatabaseOptions options)
    {
        if (!string.Equals(options.Provider?.Trim(), SqliteProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The database provider '{options.Provider}' is not supported. Supported providers: {SqliteProvider}.");
        }
    }

    /// <summary>
    ///     Finds out whether the configured SQLite database is a plain file that does not exist yet.
    /// </summary>
    /// <param name="options">The configured options. Must not be null.</param>
    /// <returns>
    ///     The absolute path of the missing database file, or <c>null</c> when the file exists or the data source
    ///     is not a plain file. A <c>file:</c> URI is not checked here: it is refused or opened by the read-only
    ///     connection of <see cref="ResolveReadOnlyConnectionString"/>, which never creates it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the provider is not supported or the connection string selects an in-memory database.
    /// </exception>
    /// <remarks>
    ///     Commands that only read an existing database (<c>audit verify</c>, <c>--check-config</c>) use this to
    ///     refuse or skip instead of creating an empty database at a mistyped path. Side effects: none.
    /// </remarks>
    internal static string? TryGetMissingDatabaseFile(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequireSupportedProvider(options);
        var dataSource = new SqliteConnectionStringBuilder(ResolveConnectionString(options)).DataSource;
        if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = Path.GetFullPath(dataSource);
        return File.Exists(path) ? null : path;
    }

    /// <summary>
    ///     Gets the connection string of the configured SQLite database with the open mode forced to read-only.
    /// </summary>
    /// <param name="options">The configured options. Must not be null.</param>
    /// <returns>A full SQLite connection string whose mode is <c>ReadOnly</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the provider is not supported or the connection string selects an in-memory database.
    /// </exception>
    /// <remarks>
    ///     A read-only open can never create the file, so a command that only reads an existing database stays safe
    ///     even when the file is deleted between a check and the open. This also holds for a <c>file:</c> URI: SQLite
    ///     refuses a URI parameter such as <c>mode=rwc</c> that asks for more than the open flags allow, and the
    ///     open fails instead of creating the database. Side effects: none.
    /// </remarks>
    internal static string ResolveReadOnlyConnectionString(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequireSupportedProvider(options);
        return new SqliteConnectionStringBuilder(ResolveConnectionString(options))
        {
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
    }

    /// <summary>
    ///     Makes sure the database folder and the schema exist.
    /// </summary>
    /// <param name="factory">The factory that creates database contexts. Must not be null.</param>
    /// <param name="options">The configured options, used to find the database file folder. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the provider is not supported.</exception>
    /// <remarks>
    ///     Side effects: creates the folder of the SQLite file when it is missing, and creates the file and
    ///     the tables when they do not exist. An existing database is left unchanged, which is also why a
    ///     future schema change needs a migration strategy (there are no migrations yet). Failures from the
    ///     file system or the database (for example an unwritable folder) propagate to the caller.
    /// </remarks>
    public static void EnsureCreated(IDbContextFactory<ReviewCenterDbContext> factory, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        // Validate the provider before touching the file system
        RequireSupportedProvider(options);

        // SQLite creates the file but not its folder
        var dataSource = new SqliteConnectionStringBuilder(ResolveConnectionString(options)).DataSource;
        if (!string.IsNullOrEmpty(dataSource) &&
            !dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        // Create the schema when the database is new
        using var context = factory.CreateDbContext();
        context.Database.EnsureCreated();
    }
}
