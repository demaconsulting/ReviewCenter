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

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     A failure the user should be told about as a plain message, without a stack trace.
/// </summary>
/// <remarks>
///     Used for every anticipated operational problem: a bad argument, an invalid steps file, a missing
///     element, a timeout, a server or browser that cannot start. Unexpected programming errors use other
///     exception types so they are not hidden behind a friendly message.
/// </remarks>
public sealed class PuppetException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PuppetException"/> class.
    /// </summary>
    /// <param name="message">The message shown to the user.</param>
    public PuppetException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PuppetException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The message shown to the user.</param>
    /// <param name="innerException">The underlying cause.</param>
    public PuppetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
///     The collaborators a run uses to reach the outside world; replaced by fakes in tests.
/// </summary>
/// <param name="StartServer">Starts the ReviewCenter server from the given assembly path.</param>
/// <param name="LaunchBrowser">Launches the browser and returns its page.</param>
internal sealed record PuppetServices(
    Func<string, CancellationToken, Task<IRunningServer>> StartServer,
    Func<Task<IBrowserPage>> LaunchBrowser)
{
    /// <summary>
    ///     Gets the production collaborators.
    /// </summary>
    /// <returns>Services backed by a real server process and headless Chromium.</returns>
    public static PuppetServices CreateDefault()
    {
        return new PuppetServices(ServerProcess.StartAsync, PlaywrightBrowserLauncher.LaunchAsync);
    }
}

/// <summary>
///     Entry point of the Puppet tool: runs a steps file against the ReviewCenter web UI.
/// </summary>
/// <remarks>
///     The Puppet is a minimal helper that lets an AI drive and screenshot the UI. It has one verb,
///     <c>run</c>: start the server (unless <c>--url</c> is given), launch headless Chromium, execute the
///     steps in order, and stop both again in a <c>finally</c> block. Cleanup is best effort.
///     Exit codes: 0 every step succeeded, 1 a step failed, 2 the run could not be performed.
/// </remarks>
internal static class PuppetProgram
{
    /// <summary>
    ///     Exit code: every step succeeded.
    /// </summary>
    public const int ExitOk = 0;

    /// <summary>
    ///     Exit code: a step failed.
    /// </summary>
    public const int ExitStepFailed = 1;

    /// <summary>
    ///     Exit code: the run could not be performed (bad arguments, invalid steps file, server or browser problem).
    /// </summary>
    public const int ExitCannotRun = 2;

    /// <summary>
    ///     The one-line usage text.
    /// </summary>
    private const string UsageLine =
        "Usage: puppet run <steps.json> [--server-dll <path> | --url <base-url>] [--out <dir>] [--timeout <seconds>]";

    /// <summary>
    ///     The default folder for screenshots, relative to the current directory (git-ignored in this repository).
    /// </summary>
    private const string DefaultOutDirectory = "artifacts/puppet";

    /// <summary>
    ///     The default per-wait timeout in seconds.
    /// </summary>
    private const int DefaultTimeoutSeconds = 10;

    /// <summary>
    ///     The largest accepted per-wait timeout in seconds.
    /// </summary>
    private const int MaxTimeoutSeconds = 600;

    /// <summary>
    ///     The process entry point.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        // Ctrl+C cancels instead of killing the process, so the browser and server are still stopped
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        return await RunAsync(args, Console.Out, Console.Error, PuppetServices.CreateDefault(), cancellation.Token);
    }

    /// <summary>
    ///     Runs one Puppet invocation.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Receives one result line per executed step.</param>
    /// <param name="error">Receives error messages and warnings.</param>
    /// <param name="services">The collaborators to use.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code (see the <c>Exit*</c> constants).</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        PuppetServices services,
        CancellationToken cancellationToken)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h" or "-?")
        {
            PrintUsage(output);
            return ExitOk;
        }

        IRunningServer? server = null;
        IBrowserPage? page = null;
        try
        {
            var options = ParseOptions(args);
            var steps = StepFile.Parse(ReadSteps(options.StepsPath), options.StepsPath);

            // Use the given server, or start our own
            var baseUrl = options.Url;
            if (baseUrl is null)
            {
                server = await services.StartServer(ResolveServerPath(options.ServerDll), cancellationToken);
                baseUrl = server.Url;
            }

            page = await services.LaunchBrowser();
            var allOk = await StepRunner.RunAsync(
                steps,
                page,
                baseUrl,
                Path.GetFullPath(options.OutDirectory),
                options.TimeoutSeconds * 1000,
                result => output.WriteLine(result.ToJson()),
                cancellationToken);
            return allOk ? ExitOk : ExitStepFailed;
        }
        catch (PuppetException ex)
        {
            WriteLine(error, ex.Message);
            return ExitCannotRun;
        }
        catch (OperationCanceledException)
        {
            WriteLine(error, "Cancelled.");
            return ExitCannotRun;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            // For example an unusable --out path
            WriteLine(error, ex.Message);
            return ExitCannotRun;
        }
        finally
        {
            // Best effort: a failed cleanup is reported but never changes the exit code
            await DisposeQuietlyAsync(page, error);
            await DisposeQuietlyAsync(server, error);
        }
    }

    /// <summary>
    ///     Finds the server assembly to run.
    /// </summary>
    /// <param name="explicitPath">The <c>--server-dll</c> value, or <c>null</c>.</param>
    /// <returns>The path of an existing server assembly.</returns>
    /// <exception cref="PuppetException">Thrown when no server assembly can be found.</exception>
    internal static string ResolveServerPath(string? explicitPath)
    {
        // The path recorded at build time is the default, so a built checkout needs no arguments
        var path = explicitPath
                   ?? typeof(PuppetProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                       .FirstOrDefault(a => a.Key == "ServerDllPath")?.Value;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new PuppetException(
                $"The ReviewCenter server assembly was not found ({path ?? "no path configured"}). " +
                "Build the solution first, or pass --server-dll <path to DemaConsulting.ReviewCenter.dll>.");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    ///     Parses the command line.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="PuppetException">Thrown when the arguments are not valid.</exception>
    private static Options ParseOptions(string[] args)
    {
        if (args.Length == 0 || args[0] != "run")
        {
            throw new PuppetException("Expected the verb 'run'. " + UsageLine);
        }

        string? stepsPath = null;
        string? serverDll = null;
        string? url = null;
        var outDirectory = DefaultOutDirectory;
        var timeout = DefaultTimeoutSeconds;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server-dll":
                    serverDll = ValueOf(args, ref i);
                    break;
                case "--url":
                    url = ValueOf(args, ref i);
                    break;
                case "--out":
                    outDirectory = ValueOf(args, ref i);
                    break;
                case "--timeout":
                    timeout = ParseTimeout(ValueOf(args, ref i));
                    break;
                default:
                    if (args[i].StartsWith('-') || stepsPath is not null)
                    {
                        throw new PuppetException($"Unexpected argument '{args[i]}'. {UsageLine}");
                    }

                    stepsPath = args[i];
                    break;
            }
        }

        if (stepsPath is null)
        {
            throw new PuppetException("The steps file is missing. " + UsageLine);
        }

        ValidateUrlOptions(url, serverDll);
        return new Options(stepsPath, serverDll, url?.TrimEnd('/'), outDirectory, timeout);
    }

    /// <summary>
    ///     Parses the <c>--timeout</c> value.
    /// </summary>
    /// <param name="text">The option value.</param>
    /// <returns>The timeout in seconds.</returns>
    /// <exception cref="PuppetException">Thrown when the value is not a whole number in range.</exception>
    private static int ParseTimeout(string text)
    {
        if (!int.TryParse(text, out var timeout) || timeout is < 1 or > MaxTimeoutSeconds)
        {
            throw new PuppetException($"--timeout must be a whole number of seconds from 1 to {MaxTimeoutSeconds}, not '{text}'.");
        }

        return timeout;
    }

    /// <summary>
    ///     Checks the <c>--url</c> and <c>--server-dll</c> options.
    /// </summary>
    /// <param name="url">The <c>--url</c> value, or <c>null</c>.</param>
    /// <param name="serverDll">The <c>--server-dll</c> value, or <c>null</c>.</param>
    /// <exception cref="PuppetException">Thrown when the combination is not valid.</exception>
    private static void ValidateUrlOptions(string? url, string? serverDll)
    {
        if (url is null)
        {
            return;
        }

        if (serverDll is not null)
        {
            throw new PuppetException("--url and --server-dll cannot be used together.");
        }

        if (!(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            throw new PuppetException($"--url must be an absolute http or https URL, not '{url}'.");
        }
    }

    /// <summary>
    ///     Gets the value that follows an option.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="index">The index of the option; advanced to the value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="PuppetException">Thrown when the option has no value.</exception>
    private static string ValueOf(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new PuppetException($"The option {args[index]} needs a value.");
        }

        index++;
        return args[index];
    }

    /// <summary>
    ///     Reads the steps file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="PuppetException">Thrown when the file cannot be read.</exception>
    private static string ReadSteps(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PuppetException($"Cannot read the steps file '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Disposes an object during teardown, reporting (not throwing) a failure.
    /// </summary>
    /// <param name="resource">The object to dispose; may be <c>null</c>.</param>
    /// <param name="error">Receives the warning.</param>
    /// <returns>A task that completes when the disposal attempt has finished.</returns>
    private static async Task DisposeQuietlyAsync(IAsyncDisposable? resource, TextWriter error)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            await resource.DisposeAsync();
        }
        catch (Exception ex)
        {
            WriteLine(error, $"warning: cleanup failed, a server or browser may still be running: {ex.Message}");
        }
    }

    /// <summary>
    ///     Writes one line of text; synchronous because the console writers are.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="text">The text.</param>
    private static void WriteLine(TextWriter writer, string text)
    {
        writer.WriteLine(text);
    }

    /// <summary>
    ///     Prints the usage text.
    /// </summary>
    /// <param name="output">Receives the text.</param>
    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine(UsageLine);
        output.WriteLine();
        output.WriteLine("  --server-dll <path>   ReviewCenter server assembly to start (default: the one built next to this tool)");
        output.WriteLine("  --url <base-url>      Drive an already running server instead of starting one");
        output.WriteLine($"  --out <dir>           Screenshot folder (default: {DefaultOutDirectory})");
        output.WriteLine($"  --timeout <seconds>   Wait limit for each step, 1-{MaxTimeoutSeconds} (default: {DefaultTimeoutSeconds})");
        output.WriteLine();
        output.WriteLine("Exit codes: 0 every step succeeded, 1 a step failed, 2 the run could not be performed");
        output.WriteLine("First-time setup: pwsh <output folder>/playwright.ps1 install chromium");
    }

    /// <summary>
    ///     The parsed command line.
    /// </summary>
    /// <param name="StepsPath">The steps file.</param>
    /// <param name="ServerDll">The server assembly given with <c>--server-dll</c>, or <c>null</c>.</param>
    /// <param name="Url">The base URL given with <c>--url</c>, or <c>null</c>.</param>
    /// <param name="OutDirectory">The screenshot folder.</param>
    /// <param name="TimeoutSeconds">The per-wait timeout in seconds.</param>
    private sealed record Options(string StepsPath, string? ServerDll, string? Url, string OutDirectory, int TimeoutSeconds);
}
