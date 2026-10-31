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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Cli;

/// <summary>
///     Context class that handles command-line arguments and program output.
/// </summary>
internal sealed class Context : IDisposable
{
    /// <summary>
    ///     Log file stream writer (if logging is enabled).
    /// </summary>
    private StreamWriter? _logWriter;

    /// <summary>
    ///     Indicates whether errors have been reported.
    /// </summary>
    private bool _hasErrors;

    /// <summary>
    ///     Gets a value indicating whether the version flag was specified.
    /// </summary>
    public bool Version { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether the help flag was specified.
    /// </summary>
    public bool Help { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether the silent flag was specified.
    /// </summary>
    public bool Silent { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether the validate flag was specified.
    /// </summary>
    public bool Validate { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether <c>--check-config</c> was specified.
    /// </summary>
    /// <remarks>
    ///     Only meaningful together with <see cref="Validate"/>; <see cref="Create"/> rejects the flag
    ///     otherwise. It opts the self-validation in to one extra, read-only check of the configured database.
    /// </remarks>
    public bool CheckConfig { get; private init; }

    /// <summary>
    ///     Gets the validation results file path.
    /// </summary>
    public string? ResultsFile { get; private init; }

    /// <summary>
    ///     Gets the heading depth for markdown output (default is 1).
    /// </summary>
    public int HeadingDepth { get; private init; } = 1;

    /// <summary>
    ///     Gets a value indicating whether the <c>serve</c> verb was specified.
    /// </summary>
    public bool Serve { get; private init; }

    /// <summary>
    ///     Gets the URL the web server listens on when <see cref="Serve"/> is <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see cref="DefaultUrls"/> when <c>--urls</c> is not supplied. The value is
    ///     always an absolute <c>http</c> or <c>https</c> URL because <see cref="Create"/> rejects
    ///     anything else.
    /// </remarks>
    public string Urls { get; private init; } = DefaultUrls;

    /// <summary>
    ///     Gets a value indicating whether the <c>user add</c> verb was specified.
    /// </summary>
    public bool UserAdd { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether the <c>audit verify</c> verb was specified.
    /// </summary>
    public bool AuditVerify { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether the <c>keys reprotect</c> verb was specified.
    /// </summary>
    public bool KeysReprotect { get; private init; }

    /// <summary>
    ///     Gets the user name given with <c>--username</c>; set whenever <see cref="UserAdd"/> is <c>true</c>.
    /// </summary>
    public string? Username { get; private init; }

    /// <summary>
    ///     Gets the display name given with <c>--display-name</c>; set whenever <see cref="UserAdd"/> is <c>true</c>.
    /// </summary>
    public string? DisplayName { get; private init; }

    /// <summary>
    ///     Gets a value indicating whether <c>--system-admin</c> was given: the new user is a system administrator.
    /// </summary>
    public bool SystemAdmin { get; private init; }

    /// <summary>
    ///     Gets the proposed exit code for the application (0 for success, 1 for errors).
    /// </summary>
    public int ExitCode => _hasErrors ? 1 : 0;

    /// <summary>
    ///     The URL the web server listens on when <c>--urls</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     Port 5000 on the loopback interface is the conventional ASP.NET Core development
    ///     address, and binding to loopback by default keeps a freshly started server from being
    ///     reachable from other machines unless the operator explicitly asks for that.
    /// </remarks>
#pragma warning disable S1075 // The default listen address is a deliberate, documented constant
    public const string DefaultUrls = "http://localhost:5000";
#pragma warning restore S1075

    /// <summary>
    ///     Private constructor - use Create factory method instead.
    /// </summary>
    private Context()
    {
    }

    /// <summary>
    ///     Creates a Context instance from command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A new Context instance.</returns>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the specified log file cannot be opened.</exception>
    public static Context Create(string[] args)
    {
        // Validate input
        ArgumentNullException.ThrowIfNull(args);

        var parser = new ArgumentParser();
        parser.ParseArguments(args);

        var result = new Context
        {
            Version = parser.Version,
            Help = parser.Help,
            Silent = parser.Silent,
            Validate = parser.Validate,
            CheckConfig = parser.CheckConfig,
            ResultsFile = parser.ResultsFile,
            HeadingDepth = parser.HeadingDepth,
            Serve = parser.Serve,
            Urls = parser.Urls ?? DefaultUrls,
            UserAdd = parser.UserAdd,
            AuditVerify = parser.AuditVerify,
            KeysReprotect = parser.KeysReprotect,
            Username = parser.Username,
            DisplayName = parser.DisplayName,
            SystemAdmin = parser.SystemAdmin
        };

        // Open log file if specified
        if (parser.LogFile != null)
        {
            result.OpenLogFile(parser.LogFile);
        }

        return result;
    }

    /// <summary>
    ///     Opens the log file for writing
    /// </summary>
    /// <param name="logFile">Log file path</param>
    private void OpenLogFile(string logFile)
    {
        try
        {
            // Open with AutoFlush enabled so log entries are immediately written to disk
            // even if the application terminates unexpectedly before Dispose is called
            _logWriter = new StreamWriter(logFile, append: false) { AutoFlush = true };
        }
        // Generic catch is justified here to wrap any file system exception with context.
        // Expected exceptions include IOException, UnauthorizedAccessException, ArgumentException,
        // NotSupportedException, and other file system-related exceptions.
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to open log file '{logFile}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Helper class for parsing command-line arguments
    /// </summary>
    private sealed class ArgumentParser
    {
        /// <summary>
        ///     Gets a value indicating whether the version flag was specified.
        /// </summary>
        public bool Version { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the help flag was specified.
        /// </summary>
        public bool Help { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the silent flag was specified.
        /// </summary>
        public bool Silent { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the validate flag was specified.
        /// </summary>
        public bool Validate { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the <c>--check-config</c> flag was specified.
        /// </summary>
        public bool CheckConfig { get; private set; }

        /// <summary>
        ///     Gets the log file path.
        /// </summary>
        public string? LogFile { get; private set; }

        /// <summary>
        ///     Gets the validation results file path.
        /// </summary>
        public string? ResultsFile { get; private set; }

        /// <summary>
        ///     Gets the heading depth for markdown output.
        /// </summary>
        public int HeadingDepth { get; private set; } = 1;

        /// <summary>
        ///     Gets a value indicating whether the <c>serve</c> verb was specified.
        /// </summary>
        public bool Serve { get; private set; }

        /// <summary>
        ///     Gets the URL supplied with <c>--urls</c>, or <c>null</c> when the option was not given.
        /// </summary>
        public string? Urls { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the <c>user add</c> verb was specified.
        /// </summary>
        public bool UserAdd { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the <c>audit verify</c> verb was specified.
        /// </summary>
        public bool AuditVerify { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the <c>keys reprotect</c> verb was specified.
        /// </summary>
        public bool KeysReprotect { get; private set; }

        /// <summary>
        ///     Gets the value of <c>--username</c>, or <c>null</c> when the option was not given.
        /// </summary>
        public string? Username { get; private set; }

        /// <summary>
        ///     Gets the value of <c>--display-name</c>, or <c>null</c> when the option was not given.
        /// </summary>
        public string? DisplayName { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether <c>--system-admin</c> was given.
        /// </summary>
        public bool SystemAdmin { get; private set; }

        /// <summary>
        ///     Parses command-line arguments
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <exception cref="ArgumentException">
        ///     Thrown when an argument is unknown or malformed, when <c>--urls</c> is given
        ///     without the <c>serve</c> verb, when <c>--check-config</c> is given without <c>--validate</c>,
        ///     or when the <c>user add</c> options and the verb do not match.
        /// </exception>
        public void ParseArguments(string[] args)
        {
            // Validate input
            ArgumentNullException.ThrowIfNull(args);

            int i = 0;
            while (i < args.Length)
            {
                var arg = args[i++];
                i = ParseArgument(arg, args, i);
            }

            // --urls only has meaning for the serve verb; accepting it silently elsewhere would
            // hide a likely typo (a forgotten verb) from the operator
            if (Urls != null && !Serve)
            {
                throw new ArgumentException("--urls requires the serve verb", nameof(args));
            }

            // --check-config adds a check to the self-validation run, so without --validate it would do
            // nothing; reject it for the same typo-protection reason
            if (CheckConfig && !Validate)
            {
                throw new ArgumentException("--check-config requires --validate", nameof(args));
            }

            // The user options belong to 'user add' for the same reason, and 'user add' cannot work without
            // the two names that identify the account
            if (!UserAdd && (Username != null || DisplayName != null || SystemAdmin))
            {
                throw new ArgumentException("--username, --display-name and --system-admin require the user add verb", nameof(args));
            }

            if (UserAdd && (Username == null || DisplayName == null))
            {
                throw new ArgumentException("user add requires --username and --display-name", nameof(args));
            }
        }

        /// <summary>
        ///     Parses a single argument
        /// </summary>
        /// <param name="arg">Argument to parse</param>
        /// <param name="args">All arguments</param>
        /// <param name="index">Current index</param>
        /// <returns>Updated index</returns>
        private int ParseArgument(string arg, string[] args, int index)
        {
            switch (arg)
            {
                case "-v":
                case "--version":
                    Version = true;
                    return index;

                case "-?":
                case "-h":
                case "--help":
                    Help = true;
                    return index;

                case "--silent":
                    Silent = true;
                    return index;

                case "--validate":
                    Validate = true;
                    return index;

                case "--check-config":
                    CheckConfig = true;
                    return index;

                case "--log":
                    LogFile = GetRequiredStringArgument(arg, args, index, "a filename argument");
                    return index + 1;

                case "--results":
                case "--result":
                    ResultsFile = GetRequiredStringArgument(arg, args, index, "a results filename argument");
                    return index + 1;

                case "--depth":
                    HeadingDepth = GetRequiredIntArgument(arg, args, index, "a heading depth argument", 1, 6);
                    return index + 1;

                case "serve":
                    Serve = true;
                    return index;

                case "--urls":
                    Urls = GetRequiredUrlArgument(arg, args, index);
                    return index + 1;

                case "user":
                    // 'add' is the only action; it must follow directly
                    if (index >= args.Length || args[index] != "add")
                    {
                        throw new ArgumentException("user requires the add action", nameof(args));
                    }

                    UserAdd = true;
                    return index + 1;

                case "audit":
                    // 'verify' is the only action; it must follow directly
                    if (index >= args.Length || args[index] != "verify")
                    {
                        throw new ArgumentException("audit requires the verify action", nameof(args));
                    }

                    AuditVerify = true;
                    return index + 1;

                case "keys":
                    // 'reprotect' is the only action; it must follow directly
                    if (index >= args.Length || args[index] != "reprotect")
                    {
                        throw new ArgumentException("keys requires the reprotect action", nameof(args));
                    }

                    KeysReprotect = true;
                    return index + 1;

                case "--username":
                    Username = GetRequiredStringArgument(arg, args, index, "a user name argument");
                    return index + 1;

                case "--display-name":
                    DisplayName = GetRequiredStringArgument(arg, args, index, "a display name argument");
                    return index + 1;

                case "--system-admin":
                    SystemAdmin = true;
                    return index;

                default:
                    throw new ArgumentException($"Unsupported argument '{arg}'", nameof(args));
            }
        }

        /// <summary>
        ///     Gets a required string argument value
        /// </summary>
        /// <param name="arg">Argument name</param>
        /// <param name="args">All arguments</param>
        /// <param name="index">Current index</param>
        /// <param name="description">Description of what's required</param>
        /// <returns>Argument value</returns>
        private static string GetRequiredStringArgument(string arg, string[] args, int index, string description)
        {
            if (index >= args.Length)
            {
                throw new ArgumentException($"{arg} requires {description}", nameof(args));
            }

            return args[index];
        }

        /// <summary>
        ///     Gets a required argument value that must be an absolute http or https URL
        /// </summary>
        /// <param name="arg">Argument name</param>
        /// <param name="args">All arguments</param>
        /// <param name="index">Current index</param>
        /// <returns>The URL exactly as supplied by the user</returns>
        /// <exception cref="ArgumentException">
        ///     Thrown when the value is missing or is not an absolute http/https URL.
        /// </exception>
        private static string GetRequiredUrlArgument(string arg, string[] args, int index)
        {
            var value = GetRequiredStringArgument(arg, args, index, "a URL argument");

            // The web host can only bind http/https endpoints, so anything else (relative paths,
            // file:// URLs, bare host names) is rejected here with a clear message rather than
            // failing later with a less helpful host start-up error
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"{arg} requires an absolute http or https URL but was '{value}'", nameof(args));
            }

            return value;
        }

        /// <summary>
        ///     Gets a required integer argument value
        /// </summary>
        /// <param name="arg">Argument name</param>
        /// <param name="args">All arguments</param>
        /// <param name="index">Current index</param>
        /// <param name="description">Description of what's required</param>
        /// <param name="min">Minimum valid value (inclusive)</param>
        /// <param name="max">Maximum valid value (inclusive)</param>
        /// <returns>Argument value as an integer in [min, max]</returns>
        private static int GetRequiredIntArgument(string arg, string[] args, int index, string description, int min = 1, int max = int.MaxValue)
        {
            var value = GetRequiredStringArgument(arg, args, index, description);
            if (!int.TryParse(value, out var result) || result < min || result > max)
            {
                throw new ArgumentException($"{arg} requires an integer between {min} and {max} for {description}", nameof(args));
            }

            return result;
        }
    }

    /// <summary>
    ///     Writes a line of output to the console and log file (if logging is enabled).
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <remarks>
    ///     Output is written to stdout. When <see cref="Silent"/> is <c>true</c>, stdout output is
    ///     suppressed, but the message is still written to the log file when one is open.
    /// </remarks>
    public void WriteLine(string message)
    {
        // Write to console unless silent mode is enabled
        if (!Silent)
        {
            Console.WriteLine(message);
        }

        // Write to log file if logging is enabled
        _logWriter?.WriteLine(message);
    }

    /// <summary>
    ///     Writes an error message to the error console and log file (if logging is enabled).
    /// </summary>
    /// <param name="message">The error message to write.</param>
    /// <remarks>
    ///     <c>_hasErrors</c> is set to <c>true</c> unconditionally, so <see cref="ExitCode"/> will
    ///     return 1 regardless of whether <see cref="Silent"/> suppresses the console output.
    ///     Stderr output is suppressed when <see cref="Silent"/> is <c>true</c>, but the message
    ///     is still written to the log file when one is open.
    /// </remarks>
    public void WriteError(string message)
    {
        // Mark that we have encountered errors
        _hasErrors = true;

        // Write to error console unless silent mode is enabled
        if (!Silent)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ForegroundColor = previousColor;
        }

        // Write to log file if logging is enabled
        _logWriter?.WriteLine(message);
    }

    /// <summary>
    ///     Disposes resources used by the Context.
    /// </summary>
    public void Dispose()
    {
        // Close and dispose the log file writer if it exists
        _logWriter?.Dispose();
        _logWriter = null;
    }
}
