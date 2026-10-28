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
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the <see cref="UserCommand"/> (the <c>user add</c> verb).
/// </summary>
public class UserCommandTests
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Test that the command creates the user in a new database with a hashed password, and reports it
    ///     without printing the password.
    /// </summary>
    [Fact]
    public async Task UserCommand_Run_ValidUser_CreatesUserAndReportsItWithoutPassword()
    {
        // Arrange: a new database and a password supplied by the source
        var connectionString = TestDatabase.NewConnectionString();

        // Act: add a system administrator
        var (exitCode, output) = Run(connectionString, () => Password, "--username", "root", "--display-name", "Root User", "--system-admin");

        // Assert: success, a report that names the user, no password in the output, and the user can sign in
        Assert.Equal(0, exitCode);
        Assert.Contains("Created user 'root' (system administrator).", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, output, StringComparison.Ordinal);
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), connectionString, ["serve", "--silent"]);
        var user = await app.Services.GetRequiredService<UserService>()
            .AuthenticateAsync("root", Password, TestContext.Current.CancellationToken);
        Assert.Equal("Root User", user?.DisplayName);
        Assert.True(user?.IsSystemAdmin);
    }

    /// <summary>
    ///     Test that the user is an ordinary user unless the switch is given.
    /// </summary>
    [Fact]
    public async Task UserCommand_Run_NoSwitch_CreatesOrdinaryUser()
    {
        // Arrange: a new database
        var connectionString = TestDatabase.NewConnectionString();

        // Act: add a user without the switch
        var (exitCode, _) = Run(connectionString, () => Password, "--username", "ann", "--display-name", "Ann");

        // Assert: the user is not a system administrator
        Assert.Equal(0, exitCode);
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), connectionString, ["serve", "--silent"]);
        var user = await app.Services.GetRequiredService<UserService>().GetAsync("ann", TestContext.Current.CancellationToken);
        Assert.False(user?.IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a user name that exists is reported as an error with exit code 1.
    /// </summary>
    [Fact]
    public void UserCommand_Run_UserNameTaken_ReportsErrorWithExitCodeOne()
    {
        // Arrange: a database that already has the user
        var connectionString = TestDatabase.NewConnectionString();
        Run(connectionString, () => Password, "--username", "ann", "--display-name", "Ann");

        // Act: add the same user again
        var (exitCode, output) = Run(connectionString, () => Password, "--username", "ANN", "--display-name", "Other");

        // Assert: exit code 1 and a message that says the name is taken
        Assert.Equal(1, exitCode);
        Assert.Contains("already", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a password that breaks the policy is reported as an error with exit code 1, without
    ///     printing the password.
    /// </summary>
    [Fact]
    public async Task UserCommand_Run_PasswordTooShort_ReportsErrorAndCreatesNothing()
    {
        // Arrange: a new database
        var connectionString = TestDatabase.NewConnectionString();

        // Act: add a user with a short password
        var (exitCode, output) = Run(connectionString, () => "short", "--username", "ann", "--display-name", "Ann");

        // Assert: exit code 1, the rule is explained, the password is not echoed, and nothing was created
        Assert.Equal(1, exitCode);
        Assert.Contains("characters", output, StringComparison.Ordinal);
        Assert.DoesNotContain("short", output, StringComparison.Ordinal);
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), connectionString, ["serve", "--silent"]);
        Assert.Null(await app.Services.GetRequiredService<UserService>().GetAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a missing password is reported as an error with exit code 1 before a database is created.
    /// </summary>
    [Fact]
    public void UserCommand_Run_NoPassword_ReportsErrorWithExitCodeOne()
    {
        // Arrange: a database location that does not exist yet
        var connectionString = TestDatabase.NewConnectionString();

        // Act: add a user whose password source has nothing
        var (exitCode, output) = Run(connectionString, () => null, "--username", "ann", "--display-name", "Ann");

        // Assert: exit code 1 and the message; no database file was created
        Assert.Equal(1, exitCode);
        Assert.Contains("No password was provided.", output, StringComparison.Ordinal);
        var file = connectionString.Split(';')[0].Replace("Data Source=", string.Empty, StringComparison.Ordinal);
        Assert.False(File.Exists(file));
    }

    /// <summary>
    ///     Test that a null context is refused.
    /// </summary>
    [Fact]
    public void UserCommand_Run_NullContext_ThrowsArgumentNullException()
    {
        // Arrange: (none)

        // Act and assert: refused
        Assert.Throws<ArgumentNullException>(() => UserCommand.Run(null!));
    }

    /// <summary>
    ///     Runs the user command with the given arguments against a database.
    /// </summary>
    /// <param name="connectionString">The connection string of the database.</param>
    /// <param name="passwordSource">Supplies the password.</param>
    /// <param name="arguments">The arguments after <c>user add</c>.</param>
    /// <returns>The exit code and everything the command wrote.</returns>
    private static (int ExitCode, string Output) Run(string connectionString, Func<string?> passwordSource, params string[] arguments)
    {
        var log = Path.Combine(Path.GetTempPath(), "ReviewCenterTests-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            int exitCode;
            using (var context = Context.Create(["user", "add", "--silent", "--log", log, .. arguments]))
            {
                UserCommand.Run(
                    context,
                    passwordSource,
                    configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = connectionString
                    }));
                exitCode = context.ExitCode;
            }

            return (exitCode, File.ReadAllText(log));
        }
        finally
        {
            File.Delete(log);
        }
    }
}
