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

using DemaConsulting.ReviewCenter.SelfTest;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.SelfTest;

/// <summary>
///     Unit tests for the individual self-validation checks. Each check is exercised once: the light ones directly,
///     and the ones that start git, a review workflow, a signer or a web host through the one shared real run of
///     the default validation (see <see cref="RealValidationRun"/>), so the suite does not pay for them twice.
/// </summary>
[Collection("Sequential")]
public class ValidationChecksTests
{
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
    ///     Test that the audit chain check detects a tampered entry.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_AuditChain_Run_DetectsTamperedEntry()
    {
        // Arrange: nothing; the check builds its own chain in a temporary database

        // Act: run the check, which throws unless the intact chain verifies and the changed entry is reported
        var detail = await AuditChainCheck.RunAsync(CancellationToken.None);

        // Assert: the check reports that the change was detected
        Assert.Contains("detected", detail);
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
    ///     Test that the opt-in check fails for a configured database file that does not exist, without creating it.
    /// </summary>
    [Fact]
    public async Task ValidationChecks_ConfiguredDatabase_MissingFile_FailsWithoutCreatingIt()
    {
        // Arrange: a connection string to a file that does not exist
        using var folder = new TemporaryDirectory();
        var path = folder.GetFilePath("absent.db");
        using var configuration = new ConfiguredDatabaseEnvironment($"Data Source={path};Pooling=False");

        // Act: run the check
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(
            () => ConfiguredDatabaseCheck.RunAsync(CancellationToken.None));

        // Assert: it fails, does not leak the path and does not create the file
        Assert.Contains("does not exist", exception.Message);
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
