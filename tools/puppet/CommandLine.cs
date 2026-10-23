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

using System.Globalization;
using System.Text.RegularExpressions;

namespace DemaConsulting.ReviewCenter.Puppet;

/// <summary>
///     The parsed Puppet command line: the verb, its arguments, and the global options.
/// </summary>
/// <remarks>
///     Parsing is strict on purpose. A driver tool is usually run by scripts and agents that
///     cannot ask a human what they meant, so an unknown verb, a missing argument, or a malformed
///     value is reported immediately with a message that names the problem. Instances are
///     immutable after <see cref="Parse"/> returns and safe to share between threads.
/// </remarks>
internal sealed partial class CommandLine
{
    /// <summary>
    ///     The verb that starts a session.
    /// </summary>
    public const string StartVerb = "start";

    /// <summary>
    ///     The hidden verb the launcher uses to re-invoke this executable as the session daemon.
    /// </summary>
    public const string DaemonVerb = "__daemon";

    /// <summary>
    ///     The per-command timeout, in seconds, used when <c>--timeout</c> is not supplied.
    /// </summary>
    public const int DefaultTimeoutSeconds = 10;

    /// <summary>
    ///     The environment variable consulted for a session id when <c>--session</c> is omitted.
    /// </summary>
    public const string SessionEnvironmentVariable = "REVIEWCENTER_PUPPET_SESSION";

    /// <summary>
    ///     Every verb a user may type, with the number of positional arguments it takes.
    /// </summary>
    private static readonly Dictionary<string, int> VerbArity = new(StringComparer.Ordinal)
    {
        [StartVerb] = 0,
        ["goto"] = 1,
        ["click"] = 1,
        ["fill"] = 2,
        ["text"] = 1,
        ["screenshot"] = 0,
        ["snapshot"] = 0,
        ["stop"] = 0,
        ["list"] = 0,
        [DaemonVerb] = 0
    };

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandLine"/> class.
    /// </summary>
    /// <param name="verb">The verb.</param>
    /// <param name="positionals">The positional arguments.</param>
    private CommandLine(string verb, IReadOnlyList<string> positionals)
    {
        Verb = verb;
        Positionals = positionals;
    }

    /// <summary>
    ///     Gets the verb (for example <c>click</c>).
    /// </summary>
    public string Verb { get; }

    /// <summary>
    ///     Gets the positional arguments of the verb, in order.
    /// </summary>
    public IReadOnlyList<string> Positionals { get; }

    /// <summary>
    ///     Gets the session id given with <c>--session</c>, or <c>null</c> when it was omitted.
    /// </summary>
    public string? SessionId { get; private init; }

    /// <summary>
    ///     Gets the per-command timeout in seconds (<see cref="DefaultTimeoutSeconds"/> unless overridden).
    /// </summary>
    public int TimeoutSeconds { get; private init; } = DefaultTimeoutSeconds;

    /// <summary>
    ///     Gets the screenshot output path given with <c>--out</c>, or <c>null</c> for the default location.
    /// </summary>
    public string? OutputPath { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether the screenshot should capture the full scrollable page.
    /// </summary>
    public bool FullPage { get; private init; }

    /// <summary>
    ///     Gets the path of the server assembly given with <c>--server</c>, or <c>null</c> to use the default.
    /// </summary>
    public string? ServerPath { get; private init; }

    /// <summary>
    ///     Gets the per-command timeout in milliseconds.
    /// </summary>
    public int TimeoutMilliseconds => TimeoutSeconds * 1000;

    /// <summary>
    ///     Checks whether a session id is acceptable.
    /// </summary>
    /// <param name="sessionId">The id to check.</param>
    /// <returns>
    ///     <c>true</c> when the id is 1 to 32 letters, digits, hyphens, or underscores. The tight
    ///     character set keeps an id usable as a file name without any path traversal risk.
    /// </returns>
    public static bool IsValidSessionId(string? sessionId)
    {
        return sessionId is not null && SessionIdPattern().IsMatch(sessionId);
    }

    /// <summary>
    ///     Parses the command-line arguments.
    /// </summary>
    /// <param name="args">The arguments, excluding the executable name.</param>
    /// <returns>The parsed command line.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the verb is missing or unknown, a required argument or option value is
    ///     missing, an option is not recognized, or a value is malformed.
    /// </exception>
    public static CommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("No command given. Run with --help to see the available commands.", nameof(args));
        }

        var verb = args[0];
        if (!VerbArity.TryGetValue(verb, out var arity))
        {
            throw new ArgumentException($"Unknown command '{verb}'. Run with --help to see the available commands.", nameof(args));
        }

        var positionals = new List<string>();
        string? session = null;
        string? output = null;
        string? server = null;
        var timeout = DefaultTimeoutSeconds;
        var fullPage = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--session":
                    session = RequireValue(args, ref i, arg);
                    if (!IsValidSessionId(session))
                    {
                        throw new ArgumentException(
                            $"--session '{session}' is not valid; use 1 to 32 letters, digits, '-' or '_'.", nameof(args));
                    }

                    break;
                case "--timeout":
                    timeout = ParseTimeout(RequireValue(args, ref i, arg));
                    break;
                case "--out":
                    output = RequireValue(args, ref i, arg);
                    break;
                case "--server":
                    server = RequireValue(args, ref i, arg);
                    break;
                case "--full-page":
                    fullPage = true;
                    break;
                default:
                    // A leading dash that is not a known option is a typo, not a selector or text
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.", nameof(args));
                    }

                    positionals.Add(arg);
                    break;
            }
        }

        if (positionals.Count != arity)
        {
            throw new ArgumentException(
                $"'{verb}' takes {arity} argument(s) but {positionals.Count} were given.", nameof(args));
        }

        ValidateOptionsForVerb(verb, output, fullPage, server);

        return new CommandLine(verb, positionals)
        {
            SessionId = session,
            TimeoutSeconds = timeout,
            OutputPath = output,
            FullPage = fullPage,
            ServerPath = server
        };
    }

    /// <summary>
    ///     Reads the value that must follow an option.
    /// </summary>
    /// <param name="args">All arguments.</param>
    /// <param name="index">Index of the option; advanced to the value.</param>
    /// <param name="option">The option name, for error messages.</param>
    /// <returns>The option value.</returns>
    /// <exception cref="ArgumentException">Thrown when no value follows the option.</exception>
    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        }

        index++;
        return args[index];
    }

    /// <summary>
    ///     Parses the <c>--timeout</c> value.
    /// </summary>
    /// <param name="value">The text after <c>--timeout</c>.</param>
    /// <returns>The timeout in whole seconds, between 1 and 600.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not an integer in range.</exception>
    private static int ParseTimeout(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds is < 1 or > 600)
        {
            throw new ArgumentException($"--timeout requires a whole number of seconds from 1 to 600 but was '{value}'.", nameof(value));
        }

        return seconds;
    }

    /// <summary>
    ///     Rejects options that do not apply to the verb they were given with.
    /// </summary>
    /// <param name="verb">The verb.</param>
    /// <param name="output">The <c>--out</c> value, if any.</param>
    /// <param name="fullPage">Whether <c>--full-page</c> was given.</param>
    /// <param name="server">The <c>--server</c> value, if any.</param>
    /// <exception cref="ArgumentException">Thrown when an option is used with the wrong verb.</exception>
    private static void ValidateOptionsForVerb(string verb, string? output, bool fullPage, string? server)
    {
        if ((output is not null || fullPage) && verb != "screenshot")
        {
            throw new ArgumentException("--out and --full-page only apply to 'screenshot'.", nameof(verb));
        }

        if (server is not null && verb != StartVerb && verb != DaemonVerb)
        {
            throw new ArgumentException("--server only applies to 'start'.", nameof(verb));
        }
    }

    /// <summary>
    ///     Gets the compiled pattern that defines a valid session id.
    /// </summary>
    /// <returns>The regular expression.</returns>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$")]
    private static partial Regex SessionIdPattern();
}
