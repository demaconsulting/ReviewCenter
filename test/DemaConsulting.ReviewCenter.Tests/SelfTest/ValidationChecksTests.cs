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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.SelfTest;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Tests.SelfTest;

/// <summary>
///     Unit tests for the individual self-validation checks. Each check is exercised once: the light ones directly,
///     and the ones that start git, a review workflow, a signer or a web host through the one shared real run of
///     the default validation (see <see cref="RealValidationRun"/>), so the suite does not pay for them twice.
/// </summary>
[Collection("Sequential")]
public partial class ValidationChecksTests
{
    /// <summary>
    ///     Matches the line of the host check that reports the loopback port it listened on.
    /// </summary>
    /// <returns>The pattern; group 1 is the port.</returns>
    [GeneratedRegex(@"ReviewCenter_HostStart - Passed \(\d+ ms\): listened on 127\.0\.0\.1:(\d+);")]
    private static partial Regex HostListeningLine();

    /// <summary>
    ///     The pinned fingerprint of the fingerprint check, computed once and independently of the production code.
    /// </summary>
    private const string PinnedFingerprint = "13e028da06299fcedffff47b2b55cca46a68111f3124d77d355364339c4215f2";

    /// <summary>
    ///     Test that the fingerprint check compares with the pinned literal value.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_Fingerprint_Run_MatchesPinnedLiteral()
    {
        // Arrange: nothing; the check hashes its own fixed files

        // Act: run the check
        var detail = await FingerprintCheck.RunAsync(CancellationToken.None);

        // Assert: the check passed and reports the pinned fingerprint that this test also pins
        Assert.Equal(PinnedFingerprint, FingerprintCheck.ExpectedFingerprint);
        Assert.Contains(PinnedFingerprint[..12], detail);
    }

    /// <summary>
    ///     Test that the password hash check passes.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_PasswordHash_Run_AcceptsRightAndRejectsWrongPassword()
    {
        // Arrange: nothing; the check hashes a throw-away password

        // Act: run the check, which throws when a right password is rejected or a wrong one is accepted
        var detail = await PasswordHashCheck.RunAsync(CancellationToken.None);

        // Assert: the check reports a result
        Assert.False(string.IsNullOrWhiteSpace(detail));
    }

    /// <summary>
    ///     Test that the TOTP check passes with the RFC 6238 vectors.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_Totp_Run_PassesWithRfcVectors()
    {
        // Arrange: nothing; the check computes codes for the published vectors

        // Act: run the check, which throws when any published code or window rule is violated
        var detail = await TotpCheck.RunAsync(CancellationToken.None);

        // Assert: the check reports a result and the name is the documented one
        Assert.Equal("ReviewCenter_Totp", TotpCheck.Name);
        Assert.Contains("RFC 6238", detail);
    }

    /// <summary>
    ///     Test that the audit chain check detects a tampered entry.
    /// </summary>
    [Fact]
    public void ValidationChecks_AuditChain_Run_DetectsTamperedEntry()
    {
        // Arrange: the shared real run, in which the check built its own chain in a temporary database

        // Act: read its log
        var log = RealValidationRun.Result.Log;

        // Assert: the check passed, which means the intact chain verified and the changed entry was reported
        Assert.Matches(@"✓ ReviewCenter_AuditChain - Passed \(\d+ ms\): 3 entries intact; change of entry 2 detected", log);
    }

    /// <summary>
    ///     Test that the checks that start git, the workflow, the signer and the web host each report in the real run.
    /// </summary>
    [Fact]
    public void ValidationChecks_RealRun_HeavyChecks_ReportTheirDetails()
    {
        // Arrange: the shared real run

        // Act: read its log
        var log = RealValidationRun.Result.Log;

        // Assert: each check shows what it verified
        Assert.Contains("ReviewCenter_GitAvailable - Passed", log);
        Assert.Contains("ReviewCenter_GitRoundTrip - Passed", log);
        Assert.Contains("embedded SQLite", log);
        Assert.Contains("ReviewCenter_AttestationPdf - Passed", log);
        Assert.Contains("/login", log);
        Assert.Contains("stopped cleanly", log);
    }

    /// <summary>
    ///     Test that the review workflow check signs with a separate reviewer and also shows that the author's
    ///     signature is refused, that the approved review is locked and that an administrator's reopen unlocks it.
    /// </summary>
    [Fact]
    public void ReviewWorkflowCheck_RunAsync_SeparateReviewerLockAndReopen_ReportsDetails()
    {
        // Arrange: the shared real run, in which the workflow check ran on a temporary embedded database

        // Act: read its log
        var log = RealValidationRun.Result.Log;

        // Assert: the check passed and reports what it proved, in the order of the workflow
        Assert.Equal("ReviewCenter_ReviewWorkflow", ReviewWorkflowCheck.Name);
        Assert.Matches(
            @"ReviewCenter_ReviewWorkflow - Passed \(\d+ ms\): embedded SQLite; review signed by a separate reviewer; "
            + @"author signature refused; approved review locked and reopened by an administrator; "
            + @"evidence index fingerprint \w+…, record digest \w+…",
            log);
    }

    /// <summary>
    ///     Test that the real validation passes in a hostile environment and uses nothing of it: the program copy has
    ///     a broken <c>appsettings.json</c> (and so does the working directory), and the environment names all-interface
    ///     listen addresses, the Development environment and a database, key ring, git cache and attestation key
    ///     outside the temporary folders. Before the host was built isolated, the broken file made the host check fail
    ///     and the ambient endpoint decided where it listened.
    /// </summary>
    [Fact]
    public void ValidationChecks_HostStart_HostileEnvironment_PassesOnLoopbackAndTouchesNothingAmbient()
    {
        // Arrange: the shared real process run, made in the hostile environment (see RealValidationRun)

        // Act: read the outcome of that run
        var run = RealValidationRun.Process;

        // Assert: every check passed, including the host check, which listened on one loopback port that is not the
        // poisoned one
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Passed: 9", run.Output);
        var match = HostListeningLine().Match(run.Output);
        Assert.True(match.Success, "The host check should report a loopback address:" + Environment.NewLine + run.Output);
        Assert.NotEqual(run.PoisonedPort, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        // Assert: nothing was created where the poisoned settings pointed, and the checks removed all they created
        Assert.Empty(run.SentinelEntries);
        Assert.Empty(run.PrivateTempEntries);
    }

    /// <summary>
    ///     Test that the opt-in check is skipped, not failed, when nothing is configured.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_NothingConfigured_IsSkipped()
    {
        // Arrange: no configured connection string and no database at the default location
        Assert.SkipWhen(
            File.Exists(DatabaseSetup.GetDefaultDatabasePath()),
            "A database exists at the default location of this machine.");
        using var configuration = new ConfiguredDatabaseEnvironment(null);

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: the reason says that nothing is configured
        Assert.Contains("No database is configured", exception.Message);
    }

    /// <summary>
    ///     Test that the opt-in check passes for a database with the current schema, and changes nothing.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_CurrentSchema_PassesWithoutWriting()
    {
        // Arrange: a database with the current schema in a temporary folder
        using var folder = new TemporaryDirectory();
        var connectionString = CreateDatabase(folder);
        using var configuration = new ConfiguredDatabaseEnvironment(connectionString);
        var path = folder.GetFilePath("review.db");
        var before = (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length);

        // Act: run the check
        var detail = await ConfiguredDatabaseCheck.RunAsync(CancellationToken.None);

        // Assert: the schema matches, the file is untouched and the report does not reveal the path
        Assert.Contains("schema matches", detail);
        Assert.Contains("opened read-only", detail);
        Assert.DoesNotContain(folder.DirectoryPath, detail);
        Assert.Equal(before, (File.GetLastWriteTimeUtc(path), new FileInfo(path).Length));
    }

    /// <summary>
    ///     Test that the opt-in check fails with the table name, not the path, for a database with a missing table.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_MissingTable_FailsNamingTableOnly()
    {
        // Arrange: a database of the current schema from which one table was dropped
        using var folder = new TemporaryDirectory();
        var connectionString = CreateDatabase(folder);
        var table = DropFirstTable(connectionString);
        using var configuration = new ConfiguredDatabaseEnvironment(connectionString);

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: the message names the table and neither the folder nor the connection string
        Assert.Contains($"table '{table}' is missing", exception.Message);
        Assert.DoesNotContain(folder.DirectoryPath, exception.Message);
        Assert.DoesNotContain("Data Source", exception.Message);
    }

    /// <summary>
    ///     Test that the opt-in check is skipped for a configured database file that does not exist yet (a fresh
    ///     installation), without creating it.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_MissingFile_IsSkippedWithoutCreatingIt()
    {
        // Arrange: a connection string to a file that does not exist
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("absent.db");
        using var configuration = new ConfiguredDatabaseEnvironment($"Data Source={path};Pooling=False");

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationSkippedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: it is skipped with the reason, does not leak the path and does not create the file
        Assert.Contains("does not exist yet", exception.Message);
        Assert.DoesNotContain(folder.DirectoryPath, exception.Message);
        Assert.False(File.Exists(path), "The check must never create the database");
    }

    /// <summary>
    ///     Test that the opt-in check fails for an unsupported provider.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_UnsupportedProvider_Fails()
    {
        // Arrange: a configured provider that this version does not support
        using var configuration = new ConfiguredDatabaseEnvironment("Data Source=x.db", provider: "Oracle");

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: the provider is named
        Assert.Contains("Oracle", exception.Message);
    }

    /// <summary>
    ///     Test that the services the checks build run their throw-away database with the fast commit settings.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_FastCommit_SelfTestServices_RelaxesTheEmbeddedDatabase()
    {
        // Arrange: the production services over an embedded database in a temporary folder
        using var folder = new TemporaryDirectory();
        var provider = SelfTestServices.Create(folder);
        try
        {
            // Act: read the commit settings of a connection of that database
            var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
            await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var settings = await ReadCommitSettingsAsync(context.Database.GetDbConnection());

            // Assert: synchronous=OFF (0) and journal_mode=MEMORY
            Assert.Equal(("0", "memory"), settings);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that the production service registration alone, which the server uses, keeps the safe defaults.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_FastCommit_ProductionServices_KeepDefaultCommitSettings()
    {
        // Arrange: the production registration over the same kind of database, without the self-test interceptor
        using var folder = new TemporaryDirectory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(SelfTestServices.CreateSettings(folder)).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        ServerHost.AddServices(services, configuration);
        var provider = services.BuildServiceProvider();
        try
        {
            // Act: create the database and read the commit settings of a connection
            DatabaseSetup.EnsureCreated(
                provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
                provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
            var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
            await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
            var settings = await ReadCommitSettingsAsync(context.Database.GetDbConnection());

            // Assert: synchronous=FULL (2) and the write-ahead log the product database uses; the relaxation never
            // reaches the product registration
            Assert.Equal(("2", "wal"), settings);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that the interceptor relaxes a database inside its folder and leaves every other one as it is.
    /// </summary>
    /// <param name="database">Where the database is, relative to the allowed folder.</param>
    /// <param name="options">Extra connection string options.</param>
    /// <param name="relaxed">Whether the commit settings are expected to change.</param>
    [Theory]
    [InlineData("inside.db", "", true)]
    [InlineData("sub/inside.db", "", true)]
    [InlineData("../outside.db", "", false)]
    [InlineData("../allowed-sibling/other.db", "", false)]
    [InlineData("inside.db", "Mode=ReadOnly", false)]
    public async Task ValidationChecks_FastCommit_Interceptor_RelaxesOnlyDatabasesInsideItsFolder(
        string database,
        string options,
        bool relaxed)
    {
        // Arrange: an allowed folder next to a sibling folder whose name starts the same, and an interceptor for it
        using var root = new TemporaryDirectory();
        var allowed = Directory.CreateDirectory(root.GetFilePath("allowed")).FullName;
        Directory.CreateDirectory(root.GetFilePath("allowed-sibling"));
        Directory.CreateDirectory(Path.Combine(allowed, "sub"));
        var path = Path.GetFullPath(Path.Combine(allowed, database));
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        var interceptor = new FastCommitInterceptor(allowed);
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False;{options}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Act: let the interceptor see the connection that was just opened
        await interceptor.ConnectionOpenedAsync(connection, null!, CancellationToken.None);
        var settings = await ReadCommitSettingsAsync(connection);

        // Assert: only a writable database inside the folder is relaxed
        Assert.Equal(relaxed ? ("0", "memory") : ("2", "delete"), settings);
    }

    /// <summary>
    ///     Test that the interceptor never touches an in-memory database, also not through the synchronous hook.
    /// </summary>
    [Fact]
    public void ValidationChecks_FastCommit_Interceptor_LeavesInMemoryDatabaseAlone()
    {
        // Arrange: an in-memory connection and an interceptor for an arbitrary folder
        using var root = new TemporaryDirectory();
        var interceptor = new FastCommitInterceptor(root.DirectoryPath);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // Act: let the interceptor see the connection
        interceptor.ConnectionOpened(connection, null!);

        // Assert: the commit mode of the in-memory database stays at the default (FULL)
        using var synchronous = connection.CreateCommand();
        synchronous.CommandText = "PRAGMA synchronous";
        Assert.Equal(2L, synchronous.ExecuteScalar());
    }

    /// <summary>
    ///     Test that an interceptor cannot be created without the folder that makes it safe.
    /// </summary>
    /// <param name="folder">The invalid folder value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidationChecks_FastCommit_Interceptor_RequiresAllowedFolder(string? folder)
    {
        // Act and assert: a missing folder is refused, so the guard cannot be left out
        Assert.ThrowsAny<ArgumentException>(() => new FastCommitInterceptor(folder!));
    }

    /// <summary>
    ///     Reads the commit settings of an open or closed SQLite connection.
    /// </summary>
    /// <param name="connection">The connection; opened when it is closed.</param>
    /// <returns>The values of <c>PRAGMA synchronous</c> and <c>PRAGMA journal_mode</c>.</returns>
    private static async Task<(string Synchronous, string Journal)> ReadCommitSettingsAsync(System.Data.Common.DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var synchronous = connection.CreateCommand();
        synchronous.CommandText = "PRAGMA synchronous";
        var sync = Convert.ToString(await synchronous.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        await using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode";
        var mode = Convert.ToString(await journal.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
        return (sync, mode.ToLowerInvariant());
    }

    /// <summary>
    ///     Creates a database with the current schema in a folder.
    /// </summary>
    /// <param name="folder">The folder; the file is <c>review.db</c>.</param>
    /// <returns>The connection string of the database.</returns>
    private static string CreateDatabase(TemporaryDirectory folder)
    {
        var connectionString = $"Data Source={folder.GetFilePath("review.db")};Pooling=False";
        using var context = new ReviewCenterDbContext(
            new DbContextOptionsBuilder<ReviewCenterDbContext>().UseSqlite(connectionString).Options);
        context.Database.EnsureCreated();
        return connectionString;
    }

    /// <summary>
    ///     Drops the first table of a database.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <returns>The name of the table that was dropped.</returns>
    private static string DropFirstTable(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        var table = (string)select.ExecuteScalar()!;
        using var drop = connection.CreateCommand();
        drop.CommandText = $"PRAGMA foreign_keys = OFF; DROP TABLE \"{table}\"";
        drop.ExecuteNonQuery();
        return table;
    }

    /// <summary>
    ///     Points the database configuration of the process at a value for a test and restores it afterwards.
    /// </summary>
    private sealed class ConfiguredDatabaseEnvironment : IDisposable
    {
        /// <summary>
        ///     The environment variable that holds the provider.
        /// </summary>
        private const string ProviderVariable = "Database__Provider";

        /// <summary>
        ///     The connection string variable value before the test.
        /// </summary>
        private readonly string? _connectionString = Environment.GetEnvironmentVariable(TestDatabase.ConnectionStringVariable);

        /// <summary>
        ///     The provider variable value before the test.
        /// </summary>
        private readonly string? _provider = Environment.GetEnvironmentVariable(ProviderVariable);

        /// <summary>
        ///     Initializes a new instance of the <see cref="ConfiguredDatabaseEnvironment"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string to configure, or null for none.</param>
        /// <param name="provider">The provider to configure, or null to leave the default.</param>
        public ConfiguredDatabaseEnvironment(string? connectionString, string? provider = null)
        {
            Environment.SetEnvironmentVariable(TestDatabase.ConnectionStringVariable, connectionString);
            Environment.SetEnvironmentVariable(ProviderVariable, provider);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(TestDatabase.ConnectionStringVariable, _connectionString);
            Environment.SetEnvironmentVariable(ProviderVariable, _provider);
        }
    }
}
