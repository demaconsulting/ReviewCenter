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
using System.Text;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     The <c>user add</c> verb: creates a user in the configured database, which is how the first
///     administrator is bootstrapped before anyone can sign in.
/// </summary>
/// <remarks>
///     The command builds its configuration and services the same way the server does (the same
///     <c>appsettings.json</c> and environment variables, the same registration), prepares the database when it
///     does not exist yet, and creates the user through <see cref="UserService"/>, so every user rule applies.
///     The password is never taken from the command line, where it would be visible in the process list and the
///     shell history: it comes from the <see cref="PasswordVariable"/> environment variable, else from the first
///     line of standard input when that is redirected, else from a masked prompt asked twice. The password and
///     its hash are never printed or logged. The class is stateless and therefore thread-safe.
/// </remarks>
internal static class UserCommand
{
    /// <summary>
    ///     The name of the environment variable that supplies the password.
    /// </summary>
    public const string PasswordVariable = "REVIEWCENTER_PASSWORD";

    /// <summary>
    ///     Creates the user described by the command-line context.
    /// </summary>
    /// <param name="context">
    ///     The parsed command line with <see cref="Context.Username"/>, <see cref="Context.DisplayName"/> and
    ///     <see cref="Context.Role"/>; results and errors are reported through it. Must not be null.
    /// </param>
    /// <param name="passwordSource">
    ///     Supplies the password; <c>null</c> (the default) selects the real sources (environment variable,
    ///     standard input, masked prompt). Tests pass a fixed value.
    /// </param>
    /// <param name="configure">
    ///     Optional adjustment of the configuration before the services are built; tests use it to select a
    ///     temporary database.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     Failures that the operator can fix (a broken rule, a name that exists, a database that cannot be
    ///     opened, no password) are reported with <see cref="Context.WriteError"/>, which makes the exit code 1;
    ///     two different prompt entries throw <see cref="InvalidOperationException"/>, which the program reports
    ///     the same way. Side
    ///     effects: creates the database when it does not exist, and one row in it.
    /// </remarks>
    public static void Run(
        Context context,
        Func<string?>? passwordSource = null,
        Action<ConfigurationManager>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The password comes first so that a missing one is reported before the database is touched
        var password = passwordSource is null ? ReadPassword() : passwordSource();
        if (password is null)
        {
            context.WriteError("No password was provided.");
            return;
        }

        // Same configuration sources as the server (appsettings.json next to the program, environment variables)
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(UserCommand).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        configure?.Invoke(builder.Configuration);

        // The command reports through the context, so the host logging stays off
        builder.Logging.ClearProviders();
        ServerHost.AddServices(builder.Services, builder.Configuration);

        try
        {
            using var host = builder.Build();
            DatabaseSetup.EnsureCreated(
                host.Services.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
                host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value);

            var user = host.Services.GetRequiredService<UserService>()
                .CreateAsync(context.Username, context.DisplayName, context.Role, password)
                .GetAwaiter()
                .GetResult();
            context.WriteLine($"Created user '{user.Username}' with the role {user.Role}.");
        }
        catch (DomainRuleException ex)
        {
            context.WriteError(ex.Message);
        }
        catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException)
        {
            context.WriteError($"Failed to prepare the database: {ex.Message}");
        }
    }

    /// <summary>
    ///     Reads the password from the environment variable, standard input or a masked prompt.
    /// </summary>
    /// <returns>The password, or <c>null</c> when standard input is empty.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the two prompt entries differ.</exception>
    private static string? ReadPassword()
    {
        // 1. The environment variable, for scripts and containers
        var fromEnvironment = Environment.GetEnvironmentVariable(PasswordVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        // 2. The first line of redirected standard input, for pipes
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine();
        }

        // 3. An interactive prompt, asked twice because a typo in the only password would lock the user out
        Console.Error.Write("Password: ");
        var first = ReadMasked();
        Console.Error.Write("Repeat password: ");
        var second = ReadMasked();
        if (first != second)
        {
            throw new InvalidOperationException("The passwords do not match.");
        }

        return first;
    }

    /// <summary>
    ///     Reads one line from the keyboard without echoing it.
    /// </summary>
    /// <returns>The typed text; the Enter key ends the line and Backspace removes the last character.</returns>
    private static string ReadMasked()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return text.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                {
                    text.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }
    }
}
