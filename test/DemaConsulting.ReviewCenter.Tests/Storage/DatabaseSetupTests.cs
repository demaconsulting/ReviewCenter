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

using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for <see cref="DatabaseSetup"/>: provider selection, connection string resolution, default
///     location and schema creation.
/// </summary>
public class DatabaseSetupTests
{
    /// <summary>
    ///     Test that the default path lives in a ReviewCenter folder under the local application data folder.
    /// </summary>
    [Fact]
    public void DatabaseSetup_GetDefaultDatabasePath_WithDataFolder_UsesAppDataSubfolder()
    {
        // Arrange: a known application data folder
        var data = Path.Combine(Path.GetTempPath(), "appdata");
        var baseDirectory = Path.Combine(Path.GetTempPath(), "base");

        // Act: compute the default path
        var path = DatabaseSetup.GetDefaultDatabasePath(data, baseDirectory);

        // Assert: <data>/ReviewCenter/reviewcenter.db
        Assert.Equal(Path.Combine(data, "ReviewCenter", "reviewcenter.db"), path);
    }

    /// <summary>
    ///     Test that an account without an application data folder falls back to a data folder beside the program.
    /// </summary>
    [Fact]
    public void DatabaseSetup_GetDefaultDatabasePath_WithoutDataFolder_FallsBackToBaseDirectory()
    {
        // Arrange: an empty application data folder, as reported for some service accounts
        var baseDirectory = Path.Combine(Path.GetTempPath(), "base");

        // Act: compute the default path
        var path = DatabaseSetup.GetDefaultDatabasePath(string.Empty, baseDirectory);

        // Assert: <base>/data/reviewcenter.db
        Assert.Equal(Path.Combine(baseDirectory, "data", "reviewcenter.db"), path);
    }

    /// <summary>
    ///     Test that the data directory is the folder of the configured database file, and the default folder when
    ///     nothing usable is configured.
    /// </summary>
    [Fact]
    public void DatabaseSetup_ResolveDataDirectory_Configured_IsFolderOfTheDatabaseFile()
    {
        // Arrange: a plain path, a full connection string, an empty value and an in-memory value
        var folder = Path.Combine(Path.GetTempPath(), "rc-data-dir");
        var plain = new DatabaseOptions { ConnectionString = Path.Combine(folder, "a.db") };
        var full = new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(folder, "b.db")};Pooling=False" };
        var empty = new DatabaseOptions { ConnectionString = string.Empty };
        var memory = new DatabaseOptions { ConnectionString = "Data Source=:memory:" };
        var defaultFolder = Path.GetDirectoryName(Path.GetFullPath(DatabaseSetup.GetDefaultDatabasePath()));

        // Act and Assert: the folder of the file, or the default folder
        Assert.Equal(folder, DatabaseSetup.ResolveDataDirectory(plain));
        Assert.Equal(folder, DatabaseSetup.ResolveDataDirectory(full));
        Assert.Equal(defaultFolder, DatabaseSetup.ResolveDataDirectory(empty));
        Assert.Equal(defaultFolder, DatabaseSetup.ResolveDataDirectory(memory));
        Assert.Throws<ArgumentNullException>(() => DatabaseSetup.ResolveDataDirectory(null!));
    }

    /// <summary>
    ///     Test that an empty connection string selects the default database file.
    /// </summary>
    [Fact]
    public void DatabaseSetup_ResolveConnectionString_Empty_UsesDefaultFile()
    {
        // Arrange: options with nothing configured
        var options = new DatabaseOptions();

        // Act: resolve the connection string
        var connectionString = DatabaseSetup.ResolveConnectionString(options);

        // Assert: the data source is the default path
        Assert.Equal(
            DatabaseSetup.GetDefaultDatabasePath(),
            new SqliteConnectionStringBuilder(connectionString).DataSource);
    }

    /// <summary>
    ///     Test that a plain value is treated as a database file path.
    /// </summary>
    [Fact]
    public void DatabaseSetup_ResolveConnectionString_PlainPath_BecomesDataSource()
    {
        // Arrange: options with a file path
        var path = Path.Combine(Path.GetTempPath(), "some folder", "x.db");
        var options = new DatabaseOptions { ConnectionString = path };

        // Act: resolve the connection string
        var connectionString = DatabaseSetup.ResolveConnectionString(options);

        // Assert: the path is the data source
        Assert.Equal(path, new SqliteConnectionStringBuilder(connectionString).DataSource);
    }

    /// <summary>
    ///     Test that a value that is already a connection string is used unchanged.
    /// </summary>
    [Fact]
    public void DatabaseSetup_ResolveConnectionString_FullConnectionString_IsUsedAsIs()
    {
        // Arrange: options with a full connection string
        var options = new DatabaseOptions { ConnectionString = "Data Source=x.db;Pooling=False" };

        // Act: resolve the connection string
        var connectionString = DatabaseSetup.ResolveConnectionString(options);

        // Assert: unchanged
        Assert.Equal("Data Source=x.db;Pooling=False", connectionString);
    }

    /// <summary>
    ///     Test that an in-memory SQLite database is refused with a message that asks for a file path.
    /// </summary>
    /// <param name="connectionString">An in-memory form of the connection string.</param>
    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData(":memory:")]
    [InlineData("Mode=Memory")]
    [InlineData("Data Source=")]
    public void DatabaseSetup_ResolveConnectionString_InMemory_ThrowsClearError(string connectionString)
    {
        // Arrange: options that select an in-memory database
        var options = new DatabaseOptions { ConnectionString = connectionString };

        // Act: resolve the connection string
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseSetup.ResolveConnectionString(options));

        // Assert: the message names the problem and the remedy
        Assert.Contains("in-memory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("file path", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unsupported provider is rejected with a message naming it and the supported one.
    /// </summary>
    [Fact]
    public void DatabaseSetup_Configure_UnsupportedProvider_ThrowsClearError()
    {
        // Arrange: options that name a provider the product does not support
        var options = new DatabaseOptions { Provider = "Oracle" };
        var builder = new DbContextOptionsBuilder<ReviewCenterDbContext>();

        // Act: configure the context
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseSetup.Configure(builder, options));

        // Assert: the message names both providers
        Assert.Contains("Oracle", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Sqlite", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the provider name is matched case-insensitively.
    /// </summary>
    [Fact]
    public void DatabaseSetup_Configure_ProviderInOtherCase_IsAccepted()
    {
        // Arrange: options with a lowercase provider name and a temporary database
        var options = new DatabaseOptions { Provider = "sqlite", ConnectionString = TestDatabase.NewConnectionString() };
        var builder = new DbContextOptionsBuilder<ReviewCenterDbContext>();

        // Act: configure the context
        DatabaseSetup.Configure(builder, options);

        // Assert: configured for SQLite without error
        Assert.True(builder.IsConfigured);
    }

    /// <summary>
    ///     Test that creating the database makes a missing folder, the file and the tables.
    /// </summary>
    [Fact]
    public void DatabaseSetup_EnsureCreated_MissingFolder_CreatesFolderFileAndTables()
    {
        // Arrange: a database path in a folder that does not exist yet
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(folder, "nested", "db.sqlite");
        var options = new DatabaseOptions { ConnectionString = $"Data Source={file};Pooling=False" };
        using var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();

        try
        {
            // Act: create the database
            DatabaseSetup.EnsureCreated(factory, options);

            // Assert: the file exists and the tables can be queried
            Assert.True(File.Exists(file));
            using var context = factory.CreateDbContext();
            Assert.Empty(context.Projects);
            Assert.Empty(context.Reviews);
            Assert.Empty(context.ReviewFiles);
            Assert.Empty(context.Threads);
            Assert.Empty(context.Comments);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an unsupported provider is rejected before any file is created.
    /// </summary>
    [Fact]
    public void DatabaseSetup_EnsureCreated_UnsupportedProvider_ThrowsAndCreatesNothing()
    {
        // Arrange: an unsupported provider and a path that must stay untouched
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        var options = new DatabaseOptions { Provider = "Oracle", ConnectionString = Path.Combine(folder, "db.sqlite") };
        using var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => builder.UseSqlite("Data Source=:memory:"))
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();

        try
        {
            // Act: attempt to create the database
            var exception = Assert.Throws<InvalidOperationException>(() => DatabaseSetup.EnsureCreated(factory, options));

            // Assert: the error names the provider and the folder was never created
            Assert.Contains("Oracle", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Test that a database opened the way the product opens it keeps SQLite's durable commit settings (a rollback
    ///     journal on disk and full synchronization), while the test-only commit interceptor, which the heavy
    ///     application tests use to skip the disk waits, is the only thing that relaxes them. This test is not cost
    ///     reduced on purpose: it pins the production behavior that the faster tests do not exercise.
    /// </summary>
    [Fact]
    public void DatabaseSetup_Configure_ProductionSettings_KeepDurableCommits_AndOnlyTheTestInterceptorRelaxesThem()
    {
        // Arrange: one database as the product configures it, one with the test-only interceptor on top
        var folder = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var plainOptions = new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(folder, "plain.db")};Pooling=False" };
        var fastOptions = new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(folder, "fast.db")};Pooling=False" };
        var services = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, plainOptions));
        using var plainProvider = services.BuildServiceProvider();
        var fastServices = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, fastOptions));
        fastServices.ConfigureDbContext<ReviewCenterDbContext>(
            builder => builder.AddInterceptors(new FastCommitInterceptor(folder)));
        using var fastProvider = fastServices.BuildServiceProvider();

        try
        {
            // Act: read the pragmas on a connection that the context opened
            var plain = ReadCommitSettings(plainProvider);
            var fast = ReadCommitSettings(fastProvider);

            // Assert: the product keeps a rollback journal on disk and FULL synchronization; the interceptor relaxes both
            Assert.Equal(("delete", 2L), plain);
            Assert.Equal(("memory", 0L), fast);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Reads the journal mode and synchronous setting of a connection that a context of the provider opened.
    /// </summary>
    /// <param name="provider">The service provider with the context factory.</param>
    /// <returns>The journal mode and the synchronous level.</returns>
    private static (string JournalMode, long Synchronous) ReadCommitSettings(ServiceProvider provider)
    {
        using var context = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>().CreateDbContext();
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode";
        var mode = (string)journal.ExecuteScalar()!;
        using var sync = connection.CreateCommand();
        sync.CommandText = "PRAGMA synchronous";
        var level = (long)sync.ExecuteScalar()!;
        return (mode, level);
    }
}
