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

using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the <see cref="AuditCommand"/> (the <c>audit verify</c> verb).
/// </summary>
public class AuditCommandTests
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Test that an intact trail is reported with its entry count and exit code 0.
    /// </summary>
    [Fact]
    public void AuditCommand_Run_IntactTrail_ReportsEntryCountWithExitCodeZero()
    {
        // Arrange: a database with two audited events (two users created from the command line)
        var connectionString = TestDatabase.NewConnectionString();
        AddUser(connectionString, "ann");
        AddUser(connectionString, "bob");

        // Act
        var (exitCode, output) = Verify(connectionString);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Audit chain intact: 2 entries", output, StringComparison.Ordinal);
        Assert.DoesNotContain("BROKEN", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a database without any audit entries is intact.
    /// </summary>
    [Fact]
    public void AuditCommand_Run_EmptyTrail_ReportsIntactWithZeroEntries()
    {
        // Arrange: a database location that does not exist yet
        var connectionString = TestDatabase.NewConnectionString();

        // Act
        var (exitCode, output) = Verify(connectionString);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Contains("Audit chain intact: 0 entries", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a tampered entry is reported by sequence with exit code 1.
    /// </summary>
    [Fact]
    public void AuditCommand_Run_TamperedEntry_ReportsFirstBadEntryWithExitCodeOne()
    {
        // Arrange: three audited events, then the second one is edited behind the application's back
        var connectionString = TestDatabase.NewConnectionString();
        AddUser(connectionString, "ann");
        AddUser(connectionString, "bob");
        AddUser(connectionString, "carl");
        Execute(connectionString, "UPDATE AuditEntries SET Detail = 'forged' WHERE Sequence = 2");

        // Act
        var (exitCode, output) = Verify(connectionString);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("Audit chain BROKEN at entry 2", output, StringComparison.Ordinal);
        Assert.DoesNotContain("intact", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a deleted entry in the middle of the trail is reported with exit code 1.
    /// </summary>
    [Fact]
    public void AuditCommand_Run_DeletedEntry_ReportsBrokenChainWithExitCodeOne()
    {
        // Arrange
        var connectionString = TestDatabase.NewConnectionString();
        AddUser(connectionString, "ann");
        AddUser(connectionString, "bob");
        AddUser(connectionString, "carl");
        Execute(connectionString, "DELETE FROM AuditEntries WHERE Sequence = 2");

        // Act
        var (exitCode, output) = Verify(connectionString);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("Audit chain BROKEN at entry 2", output, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a database that cannot be opened is reported as an error with exit code 1.
    /// </summary>
    [Fact]
    public void AuditCommand_Run_UnreachableDatabase_ReportsErrorWithExitCodeOne()
    {
        // Arrange: a database path below a file, which can never be a directory
        var blocker = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(blocker, "not a directory");
        try
        {
            var connectionString = "Data Source=" + Path.Combine(blocker, "x.db");

            // Act
            var (exitCode, output) = Verify(connectionString);

            // Assert
            Assert.Equal(1, exitCode);
            Assert.Contains("Failed", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>
    ///     Test that a null context is refused.
    /// </summary>
    [Fact]
    public void AuditCommand_Run_NullContext_ThrowsArgumentNullException()
    {
        // Arrange: (none)

        // Act and assert: refused
        Assert.Throws<ArgumentNullException>(() => AuditCommand.Run(null!));
    }

    /// <summary>
    ///     Adds a user through the <c>user add</c> command, which records one audit entry.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <param name="username">The user name.</param>
    private static void AddUser(string connectionString, string username)
    {
        var log = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using var context = Context.Create(["user", "add", "--silent", "--log", log, "--username", username, "--display-name", username]);
            UserCommand.Run(context, () => Password, Configure(connectionString));
            Assert.Equal(0, context.ExitCode);
        }
        finally
        {
            File.Delete(log);
        }
    }

    /// <summary>
    ///     Runs <c>audit verify</c> against a database.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <returns>The exit code and everything the command wrote.</returns>
    private static (int ExitCode, string Output) Verify(string connectionString)
    {
        var log = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            int exitCode;
            using (var context = Context.Create(["audit", "verify", "--silent", "--log", log]))
            {
                AuditCommand.Run(context, Configure(connectionString));
                exitCode = context.ExitCode;
            }

            return (exitCode, File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
        }
    }

    /// <summary>
    ///     Creates the configuration adjustment that selects a database.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <returns>The adjustment.</returns>
    private static Action<ConfigurationManager> Configure(string connectionString) =>
        configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = connectionString
        });

    /// <summary>
    ///     Runs a SQL statement directly against the database, bypassing the application.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <param name="sql">The statement.</param>
    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();
    }
}
