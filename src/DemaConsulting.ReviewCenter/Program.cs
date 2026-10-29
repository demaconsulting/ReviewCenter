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

using System.Reflection;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.SelfTest;

namespace DemaConsulting.ReviewCenter;

/// <summary>
///     Main program entry point for the ReviewCenter.
/// </summary>
internal static class Program
{
    /// <summary>
    ///     Gets the application version string.
    /// </summary>
    /// <remarks>
    ///     The version is read from the <see cref="AssemblyInformationalVersionAttribute"/> via
    ///     reflection on every access. There is no caching; callers that need the value more than
    ///     once should store the result locally.
    /// </remarks>
    public static string Version
    {
        get
        {
            // Get the assembly containing this program
            var assembly = typeof(Program).Assembly;

            // Try to get version from assembly attributes, fallback to AssemblyVersion, or default to 0.0.0
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? assembly.GetName().Version?.ToString()
                   ?? "0.0.0";
        }
    }

    /// <summary>
    ///     Main entry point for the ReviewCenter.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code: 0 for success, non-zero for failure.</returns>
    /// <exception cref="Exception">Thrown when an unexpected error occurs; re-thrown after writing to stderr.</exception>
    /// <remarks>
    ///     <see cref="ArgumentException"/> and <see cref="InvalidOperationException"/> are treated as
    ///     expected errors: their messages are written to stderr and exit code 1 is returned without
    ///     a stack trace. Any other exception is written to stderr and then re-thrown so that the
    ///     runtime can record it in event logs.
    /// </remarks>
    public static int Main(string[] args)
    {
        try
        {
            // Create context from command-line arguments
            using var context = Context.Create(args);

            // Run the program logic
            Run(context);

            // Return the exit code from the context
            return context.ExitCode;
        }
        catch (ArgumentException ex)
        {
            // Print expected argument exceptions and return error code
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            // Print expected operation exceptions and return error code
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            // Print unexpected exceptions and re-throw to generate event logs
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Runs the program logic based on the provided context, with no external stop request.
    /// </summary>
    /// <param name="context">The context containing command line arguments and program state.</param>
    /// <remarks>
    ///     Convenience overload for callers that never need to stop a long-running action early
    ///     (the process Main entry point stops the server with Ctrl+C instead).
    /// </remarks>
    public static void Run(Context context)
    {
        RunWithCancellation(context, CancellationToken.None);
    }

    /// <summary>
    ///     Runs the program logic based on the provided context, allowing long-running work to be stopped.
    /// </summary>
    /// <param name="context">The context containing command line arguments and program state.</param>
    /// <param name="cancellationToken">
    ///     Requests a graceful stop of long-running work (the web server started by <c>serve</c>).
    ///     Ignored by all other actions, which run to completion immediately.
    /// </param>
    /// <remarks>
    ///     Dispatch is priority-ordered: version check first, then help, then self-validation,
    ///     then the serve verb, then the user add verb, then the audit verify verb, then the default tool logic. Only the highest-priority
    ///     matching action is executed per invocation. The <c>serve</c> action blocks until the server stops.
    ///     A distinct method name (rather than an overload of <see cref="Run(Context)"/>) keeps
    ///     the common call site simple.
    /// </remarks>
    public static void RunWithCancellation(Context context, CancellationToken cancellationToken)
    {
        // Priority 1: Version query
        if (context.Version)
        {
            context.WriteLine(Version);
            return;
        }

        // Print application banner
        PrintBanner(context);

        // Priority 2: Help
        if (context.Help)
        {
            PrintHelp(context);
            return;
        }

        // Priority 3: Self-Validation
        if (context.Validate)
        {
            Validation.Run(context);
            return;
        }

        // Priority 4: Run the web server until it is stopped
        if (context.Serve)
        {
            // A non-zero server result has already been reported through the context, which
            // turns it into the process exit code
            ServerHost.RunAsync(context, cancellationToken).GetAwaiter().GetResult();
            return;
        }

        // Priority 5: Create a user
        if (context.UserAdd)
        {
            UserCommand.Run(context);
            return;
        }

        // Priority 6: Verify the audit trail
        if (context.AuditVerify)
        {
            AuditCommand.Run(context);
            return;
        }

        // Priority 7: Default tool functionality
        RunToolLogic(context);
    }

    /// <summary>
    ///     Prints the application banner.
    /// </summary>
    /// <param name="context">The context for output.</param>
    private static void PrintBanner(Context context)
    {
        context.WriteLine($"ReviewCenter version {Version}");
        context.WriteLine("Copyright (c) DEMA Consulting");
        context.WriteLine("");
    }

    /// <summary>
    ///     Prints usage information.
    /// </summary>
    /// <param name="context">The context for output.</param>
    private static void PrintHelp(Context context)
    {
        context.WriteLine("Usage: reviewcenter [options]");
        context.WriteLine("       reviewcenter serve [--urls <url>] [options]");
        context.WriteLine("       reviewcenter user add --username <name> --display-name <text> [--system-admin] [options]");
        context.WriteLine("       reviewcenter audit verify [options]");
        context.WriteLine("");
        context.WriteLine("Verbs:");
        context.WriteLine("  serve                      Run the ReviewCenter web server until stopped");
        context.WriteLine("  user add                   Create a user; the password is read from the REVIEWCENTER_PASSWORD");
        context.WriteLine("                             environment variable, else standard input, else a prompt");
        context.WriteLine("  audit verify               Verify the audit trail hash chain; exit code 0 when intact, 1 when broken");
        context.WriteLine("");
        context.WriteLine("Options:");
        context.WriteLine("  -v, --version              Display version information");
        context.WriteLine("  -?, -h, --help             Display this help message");
        context.WriteLine("  --silent                   Suppress console output");
        context.WriteLine("  --validate                 Run self-validation");
        context.WriteLine("  --check-config             With --validate, also check the configured database (read-only)");
        context.WriteLine("  --results <file>           Write validation results to file (.trx or .xml)");
        context.WriteLine("  --depth <#>                Set heading depth for markdown output (default: 1)");
        context.WriteLine("  --log <file>               Write output to log file");
        context.WriteLine("  --urls <url>               URL for 'serve' to listen on (default: http://localhost:5000)");
        context.WriteLine("  --username <name>          User name for 'user add' (3-50 letters, digits, '.', '_' or '-')");
        context.WriteLine("  --display-name <text>      Display name for 'user add'");
        context.WriteLine("  --system-admin             Make the user of 'user add' a system administrator (default: an ordinary user)");
    }

    /// <summary>
    ///     Runs the default tool logic used when no action was requested.
    /// </summary>
    /// <param name="context">The context containing command line arguments and program state.</param>
    private static void RunToolLogic(Context context)
    {
        context.WriteLine("Run 'reviewcenter serve' to start the ReviewCenter web server, or 'reviewcenter --help' for usage.");
    }
}
