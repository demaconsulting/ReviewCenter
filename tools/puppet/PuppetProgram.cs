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

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     A failure the user should be told about as a plain message, without a stack trace.
/// </summary>
/// <remarks>
///     Used for every anticipated operational problem: a bad argument, a missing element, a timeout, a
///     browser that cannot start. Unexpected programming errors use other exception types so they are not
///     hidden behind a friendly message.
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
}

/// <summary>
///     Entry point of the Puppet tool: <c>start</c> one persistent headless browser, drive it with one command
///     per invocation, then <c>stop</c> it.
/// </summary>
/// <remarks>
///     The Puppet is a minimal helper that lets an AI drive and screenshot the UI. <c>start</c> spawns a
///     detached daemon (this same assembly with the hidden verb <c>daemon</c>); every other verb is a thin
///     client that sends one command to it and prints the result, the page URL and title, and any new browser
///     errors. Exit codes: 0 ok, 1 the command failed, 2 usage error or Puppet is not running.
/// </remarks>
internal static class PuppetProgram
{
    /// <summary>
    ///     The commands: usage text, the minimum and maximum positional arguments, and the allowed options.
    ///     <c>daemon</c> is the hidden verb that <c>start</c> spawns.
    /// </summary>
    private static readonly Dictionary<string, (string Usage, int Min, int Max, string Options)> Commands = new()
    {
        ["start"] = ("[--url <base-url>] [--viewport <WxH>] [--headed]", 0, 0, "url viewport headed"),
        ["daemon"] = (string.Empty, 0, 0, "url viewport headed control-file"),
        ["goto"] = ("<url-or-path>", 1, 1, string.Empty),
        ["reload"] = (string.Empty, 0, 0, string.Empty),
        ["back"] = (string.Empty, 0, 0, string.Empty),
        ["click"] = ("<selector>", 1, 1, string.Empty),
        ["dblclick"] = ("<selector>", 1, 1, string.Empty),
        ["hover"] = ("<selector>", 1, 1, string.Empty),
        ["fill"] = ("<selector> <text>", 2, 2, string.Empty),
        ["press"] = ("<key> [selector]", 1, 2, string.Empty),
        ["type"] = ("<text>", 1, 1, string.Empty),
        ["select"] = ("<selector> <value>", 2, 2, string.Empty),
        ["check"] = ("<selector>", 1, 1, string.Empty),
        ["uncheck"] = ("<selector>", 1, 1, string.Empty),
        ["upload"] = ("<selector> <file>", 2, 2, string.Empty),
        ["mouse-move"] = ("<x> <y>", 2, 2, string.Empty),
        ["mouse-click"] = ("<x> <y>", 2, 2, string.Empty),
        ["wait"] = ("<selector|text=...> [--timeout <ms>]", 1, 1, "timeout"),
        ["text"] = ("[selector]", 0, 1, string.Empty),
        ["snapshot"] = (string.Empty, 0, 0, string.Empty),
        ["screenshot"] = ("<file.png> [--full] [--selector <selector>]", 1, 1, "full selector"),
        ["viewport"] = ("<WxH>", 1, 1, string.Empty),
        ["theme"] = ("<light|dark>", 1, 1, string.Empty),
        ["eval"] = ("<js-expression>", 1, 1, string.Empty),
        ["console"] = (string.Empty, 0, 0, string.Empty),
        ["reset"] = (string.Empty, 0, 0, string.Empty),
        ["stop"] = (string.Empty, 0, 0, string.Empty),
    };

    /// <summary>
    ///     The process entry point.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static Task<int> Main(string[] args)
    {
        return RunAsync(args, Console.Out, Console.Error, ControlChannel.DefaultControlFile, CancellationToken.None);
    }

    /// <summary>
    ///     Runs one Puppet invocation.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Receives results.</param>
    /// <param name="error">Receives error messages.</param>
    /// <param name="controlFile">The control file shared with the daemon.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>The exit code: 0 ok, 1 the command failed, 2 usage error or not running.</returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        string controlFile,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "-?")
        {
            PrintUsage(args.Length == 0 ? error : output);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var request = ParseCommand(args);
            var o = request.Options;
            return request.Command switch
            {
                "daemon" => await BrowserDaemon.RunAsync(
                    o.GetValueOrDefault("control-file", controlFile),
                    o.GetValueOrDefault("url"),
                    o.GetValueOrDefault("viewport", PlaywrightBrowser.DefaultViewport),
                    o.ContainsKey("headed")),
                "start" => await StartAsync(request, output, error, controlFile, cancellationToken),
                _ => await SendAsync(request, output, error, controlFile, cancellationToken)
            };
        }
        catch (PuppetException ex)
        {
            await error.WriteLineAsync(ex.Message, cancellationToken);
            return 2;
        }
    }

    /// <summary>
    ///     Parses and validates one command line.
    /// </summary>
    /// <param name="args">The arguments; the first is the command name.</param>
    /// <returns>The request, with file paths made absolute.</returns>
    /// <exception cref="PuppetException">Thrown for an unknown command, a wrong argument count, or a bad value.</exception>
    internal static PuppetRequest ParseCommand(string[] args)
    {
        var name = args[0];
        if (!Commands.TryGetValue(name, out var spec))
        {
            throw new PuppetException($"Unknown command '{name}'. Run 'puppet --help' for the list.");
        }

        var positional = new List<string>();
        var options = new Dictionary<string, string>();
        using var rest = args.Skip(1).GetEnumerator();
        while (rest.MoveNext())
        {
            var arg = rest.Current;
            var option = arg.StartsWith("--", StringComparison.Ordinal) ? arg[2..] : null;
            if (option is null)
            {
                positional.Add(arg);
            }
            else if (!spec.Options.Split(' ').Contains(option))
            {
                throw new PuppetException($"Unknown option '{arg}' for '{name}'. Usage: puppet {name} {spec.Usage}");
            }
            else if (option is "full" or "headed")
            {
                options[option] = "true";
            }
            else
            {
                options[option] = rest.MoveNext() ? rest.Current : throw new PuppetException($"The option {arg} needs a value.");
            }
        }

        if (positional.Count < spec.Min || positional.Count > spec.Max)
        {
            throw new PuppetException($"Usage: puppet {name} {spec.Usage}");
        }

        Validate(name, positional, options);
        return new PuppetRequest(name, [.. positional], options);
    }

    /// <summary>
    ///     Formats a response: the result, then the page URL and title, then one line per new browser error.
    /// </summary>
    /// <param name="response">The daemon's response.</param>
    /// <param name="output">Receives the result of a successful command, the URL line, and the event lines.</param>
    /// <param name="error">Receives the message of a failed command.</param>
    /// <returns>0 when the command succeeded; otherwise 1.</returns>
    internal static int Format(PuppetResponse response, TextWriter output, TextWriter error)
    {
        (response.Ok ? output : error).WriteLine(response.Output);
        if (response.Url.Length > 0)
        {
            output.WriteLine($"@ {response.Url}  \"{response.Title}\"");
        }

        foreach (var text in response.Events)
        {
            output.WriteLine($"! {text}");
        }

        return response.Ok ? 0 : 1;
    }

    /// <summary>
    ///     Checks the values of a parsed command and makes its file paths absolute (the daemon runs elsewhere).
    /// </summary>
    /// <param name="name">The command name.</param>
    /// <param name="args">The positional arguments; file paths are replaced by absolute ones.</param>
    /// <param name="options">The options; the <c>start</c> URL loses its trailing slash.</param>
    /// <exception cref="PuppetException">Thrown when a value is not valid.</exception>
    private static void Validate(string name, List<string> args, Dictionary<string, string> options)
    {
        if (options.TryGetValue("timeout", out var timeout) && !(int.TryParse(timeout, out var ms) && ms > 0))
        {
            throw new PuppetException($"--timeout must be a positive number of milliseconds, not '{timeout}'.");
        }

        if (options.TryGetValue("viewport", out var viewport))
        {
            PlaywrightBrowser.ParseViewport(viewport);
        }

        if (options.TryGetValue("url", out var url))
        {
            options["url"] = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? url.TrimEnd('/')
                : throw new PuppetException($"--url must be an absolute http or https URL, not '{url}'.");
        }

        if (name is "mouse-move" or "mouse-click" && !args.All(a => float.TryParse(a, CultureInfo.InvariantCulture, out _)))
        {
            throw new PuppetException("The mouse coordinates must be numbers.");
        }

        if (name == "viewport")
        {
            PlaywrightBrowser.ParseViewport(args[0]);
        }

        if (name == "theme" && args[0] is not ("light" or "dark"))
        {
            throw new PuppetException($"The theme must be light or dark, not '{args[0]}'.");
        }

        if (name == "screenshot")
        {
            args[0] = Path.GetFullPath(args[0]);
        }

        if (name == "upload")
        {
            args[1] = File.Exists(args[1]) ? Path.GetFullPath(args[1]) : throw new PuppetException($"The file '{args[1]}' does not exist.");
        }
    }

    /// <summary>
    ///     Handles <c>start</c>: refuses when one is running, otherwise spawns the daemon and waits until it is ready.
    /// </summary>
    /// <returns>0 when started; 1 when already running or the daemon failed.</returns>
    private static async Task<int> StartAsync(
        PuppetRequest request,
        TextWriter output,
        TextWriter error,
        string controlFile,
        CancellationToken cancellationToken)
    {
        var existing = ControlChannel.Read(controlFile);
        if (existing?.Port is int running && await ControlChannel.IsAnsweringAsync(running))
        {
            await error.WriteLineAsync($"puppet is already running (port {running}); run 'puppet stop' first.", cancellationToken);
            return 1;
        }

        // Whatever is left over is stale or an old start-up error
        File.Delete(controlFile);
        var arguments = new List<string> { typeof(PuppetProgram).Assembly.Location, "daemon", "--control-file", controlFile };
        foreach (var (key, value) in request.Options)
        {
            arguments.Add($"--{key}");
            arguments.AddRange(value == "true" ? [] : [value]);
        }

        using var process = Spawn(arguments);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exited = process.HasExited;
            var info = ControlChannel.Read(controlFile);
            if (info?.Error is not null)
            {
                File.Delete(controlFile);
                await error.WriteLineAsync(info.Error, cancellationToken);
                return 1;
            }

            if (info?.Port is int port && await ControlChannel.IsAnsweringAsync(port))
            {
                await output.WriteLineAsync($"puppet started (port {port}{(info.BaseUrl is null ? string.Empty : $", base URL {info.BaseUrl}")})", cancellationToken);
                return 0;
            }

            if (exited)
            {
                await error.WriteLineAsync($"The Puppet daemon exited with code {process.ExitCode} before it was ready.", cancellationToken);
                return 1;
            }

            await Task.Delay(100, cancellationToken);
        }

        process.Kill();
        await error.WriteLineAsync("The Puppet daemon was not ready within 30 seconds.", cancellationToken);
        return 1;
    }

    /// <summary>
    ///     Starts the daemon process detached from this process's console and pipes.
    /// </summary>
    /// <param name="arguments">The daemon's command line, starting with the assembly path.</param>
    /// <returns>The process.</returns>
    private static Process Spawn(List<string> arguments)
    {
        // On Windows only a shell-executed process inherits none of the caller's pipe handles, so a caller
        // that waits for the end of our output (an agent's shell tool) is not held up by the long-lived daemon.
        // Elsewhere .NET passes only stdin/stdout/stderr to the child, so redirecting them and closing our ends is enough.
        var windows = OperatingSystem.IsWindows();
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            Arguments = string.Join(' ', arguments.Select(a => $"\"{a}\"")),
            UseShellExecute = windows,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = !windows,
            RedirectStandardOutput = !windows,
            RedirectStandardError = !windows
        };

        try
        {
            var process = Process.Start(info) ?? throw new PuppetException("Could not start the Puppet daemon.");
            if (!windows)
            {
                process.StandardInput.Close();
                process.StandardOutput.Close();
                process.StandardError.Close();
            }

            return process;
        }
        catch (Win32Exception ex)
        {
            throw new PuppetException($"Could not start the Puppet daemon ('dotnet' not found?): {ex.Message}");
        }
    }

    /// <summary>
    ///     Sends a command to the running daemon and prints the result.
    /// </summary>
    /// <returns>0 when the command succeeded; 1 when it failed.</returns>
    /// <exception cref="PuppetException">Thrown when Puppet is not running.</exception>
    private static async Task<int> SendAsync(
        PuppetRequest request,
        TextWriter output,
        TextWriter error,
        string controlFile,
        CancellationToken cancellationToken)
    {
        const string notRunning = "puppet is not running; start it with: puppet start [--url <base-url>]";
        if (ControlChannel.Read(controlFile)?.Port is not int port)
        {
            throw new PuppetException(notRunning);
        }

        try
        {
            return Format(await ControlChannel.SendAsync(port, request, cancellationToken), output, error);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            throw new PuppetException($"{notRunning} (the control file is stale)");
        }
    }

    /// <summary>
    ///     Prints the usage text.
    /// </summary>
    /// <param name="writer">Receives the text.</param>
    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: puppet <command> [arguments]");
        foreach (var (name, spec) in Commands.Where(c => c.Key != "daemon"))
        {
            writer.WriteLine($"  puppet {name} {spec.Usage}".TrimEnd());
        }

        writer.WriteLine();
        writer.WriteLine("Selectors: testid:foo or foo (data-testid), css=..., text=..., role=...");
        writer.WriteLine("Exit codes: 0 ok, 1 the command failed, 2 usage error or Puppet is not running");
        writer.WriteLine("First-time setup: pwsh <output folder>/playwright.ps1 install chromium");
    }
}
