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

        // Act: attempt to create the database
        var exception = Assert.Throws<InvalidOperationException>(() => DatabaseSetup.EnsureCreated(factory, options));

        // Assert: the error names the provider and the folder was never created
        Assert.Contains("Oracle", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(folder));
    }
}
