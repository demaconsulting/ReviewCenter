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
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.ContentSources;

/// <summary>
///     The outcome of one git command.
/// </summary>
/// <param name="ExitCode">The exit code of the git process.</param>
/// <param name="Output">The raw standard output, at most the cap given to the runner.</param>
/// <param name="Error">The last part of the standard error with every secret removed and the repository location replaced by its name.</param>
internal sealed record GitResult(int ExitCode, byte[] Output, string Error);

/// <summary>
///     The retained end of the standard error of a git call, before redaction.
/// </summary>
/// <param name="Bytes">The retained bytes.</param>
/// <param name="Dropped">Whether earlier bytes were dropped to stay within the limit.</param>
internal sealed record CapturedError(byte[] Bytes, bool Dropped);

/// <summary>
///     Runs the installed git program safely: no shell, a curated environment, a time limit on every call and the
///     token only in the environment of the child process.
/// </summary>
/// <remarks>
///     <para>
///         Every call uses <see cref="ProcessStartInfo.ArgumentList"/> (never a command line string), starts from an
///         empty environment with only what git needs, turns prompts off and ignores the system and user
///         configuration, so a command can never ask for input, run a hook or an external helper, or pick up
///         credentials from elsewhere. A call that outlives <see cref="GitOptions.TimeoutSeconds"/> is stopped
///         together with all its child processes.
///     </para>
///     <para>
///         The token is passed as an authorization header through <c>GIT_CONFIG_COUNT</c>, <c>GIT_CONFIG_KEY_0</c> and
///         <c>GIT_CONFIG_VALUE_0</c> in the environment of the child process only: it is not in the arguments, in a
///         file, in the URL or in a credential helper. That needs git 2.31 or newer, which is checked once. All text
///         that comes back from git passes <see cref="Redact"/> before it can reach a message. The class is
///         thread-safe.
///     </para>
/// </remarks>
internal sealed class GitRunner
{
    /// <summary>
    ///     The oldest supported git version, as major and minor; <c>GIT_CONFIG_COUNT</c> arrived in 2.31. Internal so
    ///     the self-validation can name the version the server really requires.
    /// </summary>
    internal static readonly Version MinimumVersion = new(2, 31);

    /// <summary>
    ///     How much of the end of standard error is kept in a result, in bytes.
    /// </summary>
    private const int MaxErrorBytes = 8 * 1024;

    /// <summary>
    ///     How much of the end of standard error is retained while reading, in bytes: far more than
    ///     <see cref="MaxErrorBytes"/>, so redaction sees the text around the cut.
    /// </summary>
    private const int RetainedErrorBytes = 64 * 1024;

    /// <summary>
    ///     How many of the first characters of the retained error are set aside when earlier output was dropped; more
    ///     than the longest secret form (an escaped token of 1000 characters is at most 12000 characters).
    /// </summary>
    private const int DroppedMarginChars = 16 * 1024;

    /// <summary>
    ///     The most standard output a call reads when the caller names no cap, in bytes.
    /// </summary>
    internal const long DefaultMaxOutputBytes = 64L * 1024 * 1024;

    /// <summary>
    ///     The text that replaces a secret.
    /// </summary>
    private const string Mask = "***";

    /// <summary>
    ///     The environment variables copied from the server process; everything else is dropped.
    /// </summary>
    private static readonly string[] InheritedVariables =
    [
        "PATH", "SystemRoot", "SYSTEMDRIVE", "windir", "ComSpec", "PATHEXT", "TEMP", "TMP", "TMPDIR",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "NO_PROXY", "no_proxy"
    ];

    /// <summary>
    ///     Finds <c>user:password@</c> in a URL.
    /// </summary>
    private static readonly Regex UserInfoPattern = new(@"(?<=://)[^/\s@]+@", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    ///     The settings (time limit and cache folder).
    /// </summary>
    private readonly GitOptions _options;

    /// <summary>
    ///     The git executable: <c>git</c>, found through <c>PATH</c>, unless a test names another.
    /// </summary>
    private readonly string _executable;
    /// <summary>
    ///     Whether the git version was checked successfully.
    /// </summary>
    private volatile bool _versionChecked;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GitRunner"/> class.
    /// </summary>
    /// <param name="options">The git settings. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public GitRunner(GitOptions options)
        : this(options, "git")
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="GitRunner"/> class that starts another executable, so tests
    ///     can simulate a missing or unsuitable git.
    /// </summary>
    /// <param name="options">The git settings. Must not be null.</param>
    /// <param name="executable">The name or path of the git executable. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal GitRunner(GitOptions options, string executable)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(executable);
        _options = options;
        _executable = executable;
    }

    /// <summary>
    ///     Removes every secret, and the location of the repository, from text that comes from git before it is shown
    ///     or logged.
    /// </summary>
    /// <param name="text">The text, or <c>null</c>.</param>
    /// <param name="username">The user name of the credentials, or <c>null</c>.</param>
    /// <param name="token">The token of the credentials, or <c>null</c>.</param>
    /// <param name="location">The registered location of the repository, or <c>null</c> to leave locations alone.</param>
    /// <param name="name">The repository name that replaces the location; required when <paramref name="location"/> is given.</param>
    /// <returns>
    ///     The text with the location replaced by the repository name, and the token, <c>user:token</c>, the base64
    ///     form of <c>user:token</c> and the user information of URLs replaced by <c>***</c>.
    /// </returns>
    internal static string Redact(string? text, string? username, string? token, string? location = null, string? name = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // The user information goes first, so a location that carries some is found in its masked form
        var result = UserInfoPattern.Replace(text, Mask + "@");
        if (!string.IsNullOrWhiteSpace(location))
        {
            result = ReplaceLocation(result, location, name ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(token))
        {
            var pair = $"{username}:{token}";
            var secrets = new[]
            {
                Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)),
                pair,
                Uri.EscapeDataString(token),
                token
            };
            foreach (var secret in secrets.Where(s => s.Length > 0))
            {
                result = result.Replace(secret, Mask, StringComparison.Ordinal);
            }
        }

        return result;
    }

    /// <summary>
    ///     Replaces the location of a repository in text by the repository name.
    /// </summary>
    /// <param name="text">The text, with the user information of any URL already masked.</param>
    /// <param name="location">The registered location.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>
    ///     The text with every form git prints for the location replaced: as registered, without or with masked user
    ///     information, with or without a trailing slash or <c>.git</c>, with forward or back slashes for a local
    ///     path, the local path of a <c>file://</c> URL, and the host name of a web address (git names it when it
    ///     cannot connect). Case is ignored, because host names and Windows paths are.
    /// </returns>
    internal static string ReplaceLocation(string text, string location, string name)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var withoutUserInfo = UserInfoPattern.Replace(location, string.Empty);
        var bases = new List<string> { location, withoutUserInfo, UserInfoPattern.Replace(location, Mask + "@") };
        var isWebAddress = location.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (Uri.TryCreate(withoutUserInfo, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                bases.Add(uri.LocalPath);
            }
            else if (isWebAddress && uri.Host.Length >= 3)
            {
                forms.Add(uri.Authority);
                forms.Add(uri.Host);
            }
        }

        foreach (var trimmed in bases.Select(form => form.TrimEnd('/', '\\')))
        {
            var withoutGit = trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
            foreach (var variant in new[] { trimmed, withoutGit, withoutGit + ".git" })
            {
                forms.Add(variant);
                if (!isWebAddress)
                {
                    forms.Add(variant.Replace('\\', '/'));
                    forms.Add(variant.Replace('/', '\\'));
                }
            }
        }

        // Longest first, so a whole location wins over a part of it; an optional trailing slash goes with it
        var alternatives = forms.Where(f => f.Length > 0).OrderByDescending(f => f.Length).Select(Regex.Escape).ToList();
        if (alternatives.Count == 0)
        {
            return text;
        }

        var pattern = new Regex(
            "(?:" + string.Join('|', alternatives) + @")[/\\]?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        return pattern.Replace(text, _ => name);
    }

    /// <summary>
    ///     Builds the process description of a git call without starting it.
    /// </summary>
    /// <param name="arguments">The git arguments (subcommand and options), added after the hardening options. Must not be null.</param>
    /// <param name="access">The repository whose credentials the call uses, or <c>null</c> for a call that needs none.</param>
    /// <returns>The process description; the token, if any, is only in its environment.</returns>
    internal ProcessStartInfo BuildStartInfo(IReadOnlyList<string> arguments, RepositoryAccess? access)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        // Git is deliberately found through PATH: the administrator installs and updates it, and the version is checked
#pragma warning disable S4036
        var info = new ProcessStartInfo(_executable)
#pragma warning restore S4036
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // Hardening first: no monitors, hooks, external protocols, redirects, renames or credential helpers
        foreach (var setting in new[]
        {
            "core.fsmonitor=false",
            $"core.hooksPath={nullDevice}",
            "protocol.ext.allow=never",
            "http.followRedirects=false",
            "core.quotepath=false",
            "diff.renames=false",
            "credential.helper="
        })
        {
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(setting);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var environment = info.Environment;
        environment.Clear();
        foreach (var name in InheritedVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }

        var home = HomeFolder();
        environment["HOME"] = home;
        environment["USERPROFILE"] = home;
        environment["XDG_CONFIG_HOME"] = home;
        environment["GIT_TERMINAL_PROMPT"] = "0";
        environment["GIT_ASKPASS"] = string.Empty;
        environment["SSH_ASKPASS"] = string.Empty;
        environment["GIT_CONFIG_NOSYSTEM"] = "1";
        environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        environment["GIT_ALLOW_PROTOCOL"] = "file:http:https";
        environment["GCM_INTERACTIVE"] = "never";
        environment["LC_ALL"] = "C";

        if (access?.Token is not null && access.Username is not null)
        {
            // The only place the token goes: the environment of this one child process
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{access.Username}:{access.Token}"));
            environment["GIT_CONFIG_COUNT"] = "1";
            environment["GIT_CONFIG_KEY_0"] = $"http.{access.Location}.extraHeader";
            environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}";
        }

        return info;
    }

    /// <summary>
    ///     Runs git and returns its result, whatever the exit code.
    /// </summary>
    /// <param name="arguments">The git arguments (subcommand and options). Must not be null.</param>
    /// <param name="access">The repository whose credentials the call uses, or <c>null</c>.</param>
    /// <param name="maxOutputBytes">The most standard output to accept; more is an error.</param>
    /// <param name="cancellationToken">Cancels the call and stops git.</param>
    /// <returns>The result, with standard error already redacted.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.GitUnavailable"/> when git cannot be run or is too old,
    ///     <see cref="DomainRule.GitTimeout"/> when the time limit passes, or <see cref="DomainRule.GitAccessFailed"/>
    ///     when the output is larger than allowed.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    internal async Task<GitResult> RunAsync(
        IReadOnlyList<string> arguments,
        RepositoryAccess? access,
        long maxOutputBytes,
        CancellationToken cancellationToken)
    {
        if (!_versionChecked)
        {
            await CheckVersionAsync(cancellationToken);
        }

        return await ExecuteAsync(arguments, access, maxOutputBytes, cancellationToken);
    }

    /// <summary>
    ///     Runs git and turns a failure into a domain exception with a message that names the repository.
    /// </summary>
    /// <param name="arguments">The git arguments. Must not be null.</param>
    /// <param name="access">The repository the call is about. Must not be null.</param>
    /// <param name="maxOutputBytes">The most standard output to accept.</param>
    /// <param name="cancellationToken">Cancels the call and stops git.</param>
    /// <returns>The standard output of the successful call.</returns>
    /// <exception cref="DomainRuleException">Thrown as for <see cref="RunAsync"/>, or with <see cref="DomainRule.GitAccessFailed"/> for a failing call.</exception>
    internal async Task<byte[]> RunCheckedAsync(
        IReadOnlyList<string> arguments,
        RepositoryAccess access,
        long maxOutputBytes,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, access, maxOutputBytes, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw FailureFor(result, access);
        }

        return result.Output;
    }

    /// <summary>
    ///     Describes a failed call as a domain exception that is safe to show.
    /// </summary>
    /// <param name="result">The failed result; its error text is normally already redacted, and is redacted again here.</param>
    /// <param name="access">The repository the call was about.</param>
    /// <returns>The exception to throw; its message names the repository, never its location.</returns>
    internal static DomainRuleException FailureFor(GitResult result, RepositoryAccess access)
    {
        var error = Redact(result.Error, access.Username, access.Token, access.Location, access.Name);
        var denied = new[]
        {
            "authentication failed", "could not read username", "terminal prompts disabled",
            "returned error: 401", "returned error: 403", "http basic: access denied", "invalid credentials"
        };
        if (denied.Any(d => error.Contains(d, StringComparison.OrdinalIgnoreCase)))
        {
            return new DomainRuleException(
                DomainRule.GitAccessFailed,
                $"Access to repository '{access.Name}' was denied. An administrator must check the user name and token.");
        }

        var firstLine = error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? $"git exited with code {result.ExitCode}";
        if (firstLine.Length > 300)
        {
            firstLine = firstLine[..300];
        }

        return new DomainRuleException(
            DomainRule.GitAccessFailed,
            $"Repository '{access.Name}' could not be read: {firstLine}");
    }

    /// <summary>
    ///     Runs <c>git --version</c> once and checks that it is new enough.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The version text that git reported, without the trailing line break.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.GitUnavailable"/> when git is missing or too old.</exception>
    /// <remarks>
    ///     The self-validation calls this directly, so that it proves the same check the server makes before its
    ///     first git call with one process instead of two.
    /// </remarks>
    internal async Task<string> CheckVersionAsync(CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(["--version"], null, 4096, cancellationToken);
        var text = Encoding.UTF8.GetString(result.Output);
        var match = Regex.Match(text, @"(\d+)\.(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (result.ExitCode != 0
            || !match.Success
            || new Version(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)) < MinimumVersion)
        {
            throw Unavailable();
        }

        _versionChecked = true;
        return text.Trim();
    }

    /// <summary>
    ///     Starts git, reads its output within the time limit and stops it when the limit passes.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="access">The credentials, or <c>null</c>.</param>
    /// <param name="maxOutputBytes">The most standard output to accept.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    private async Task<GitResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        RepositoryAccess? access,
        long maxOutputBytes,
        CancellationToken cancellationToken)
    {
        var info = BuildStartInfo(arguments, access);
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // The exception text is not chained: it is not needed and it names the environment of the call
            throw Unavailable();
        }

        process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        var exceeded = false;
        var outputTask = ReadOutputAsync(process.StandardOutput.BaseStream, maxOutputBytes, () =>
        {
            exceeded = true;
            Kill(process);
        }, timeout.Token);
        var errorTask = ReadTailAsync(process.StandardError.BaseStream, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var captured = await errorTask;
            if (exceeded)
            {
                throw new DomainRuleException(DomainRule.GitAccessFailed, "Git returned more data than the server accepts.");
            }

            return new GitResult(
                process.ExitCode,
                output,
                BuildError(captured, access?.Username, access?.Token, access?.Location, access?.Name));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await DrainAsync(outputTask, errorTask);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new DomainRuleException(
                DomainRule.GitTimeout,
                $"Git did not finish within {_options.Timeout.TotalSeconds:0} seconds and was stopped.");
        }
        catch (DomainRuleException)
        {
            await DrainAsync(outputTask, errorTask);
            throw;
        }
    }

    /// <summary>
    ///     Waits for the reader tasks to end after the process was stopped, ignoring their failures.
    /// </summary>
    /// <param name="tasks">The reader tasks.</param>
    /// <returns>A task that completes when the readers are done.</returns>
    private static async Task DrainAsync(params Task[] tasks)
    {
        foreach (var task in tasks)
        {
            try
            {
                await task;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // The process was killed, so a broken or cancelled read is expected
            }
        }
    }

    /// <summary>
    ///     Stops a process and all its child processes.
    /// </summary>
    /// <param name="process">The process.</param>
    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process has already ended
        }
    }

    /// <summary>
    ///     Reads standard output up to a cap.
    /// </summary>
    /// <param name="stream">The output stream.</param>
    /// <param name="maxBytes">The cap.</param>
    /// <param name="onExceeded">Called once when the output passes the cap; it stops the process.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes read.</returns>
    private static async Task<byte[]> ReadOutputAsync(
        Stream stream,
        long maxBytes,
        Action onExceeded,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                onExceeded();
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    /// <summary>
    ///     Reads standard error, keeping only the last <see cref="RetainedErrorBytes"/> bytes.
    /// </summary>
    /// <param name="stream">The error stream.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The retained end of the error output, and whether earlier output was dropped.</returns>
    internal static async Task<CapturedError> ReadTailAsync(Stream stream, CancellationToken cancellationToken)
    {
        var tail = new List<byte>();
        var dropped = false;
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            tail.AddRange(chunk.AsSpan(0, read).ToArray());
            if (tail.Count > RetainedErrorBytes)
            {
                tail.RemoveRange(0, tail.Count - RetainedErrorBytes);
                dropped = true;
            }
        }

        return new CapturedError(tail.ToArray(), dropped);
    }

    /// <summary>
    ///     Turns the captured standard error into the text of a result: the whole captured text is redacted first and
    ///     only then cut to the last <see cref="MaxErrorBytes"/> bytes, so a secret that crosses the cut cannot leave
    ///     a fragment behind.
    /// </summary>
    /// <param name="captured">The captured end of standard error.</param>
    /// <param name="username">The user name of the credentials, or <c>null</c>.</param>
    /// <param name="token">The token of the credentials, or <c>null</c>.</param>
    /// <param name="location">The registered location of the repository, or <c>null</c>.</param>
    /// <param name="name">The repository name, or <c>null</c>.</param>
    /// <returns>The redacted text, at most <see cref="MaxErrorBytes"/> bytes in UTF-8.</returns>
    /// <remarks>
    ///     When earlier output was dropped while reading, a secret may start before the first retained byte, and the
    ///     redaction of the whole text leaves its first part. That part is within the first
    ///     <see cref="DroppedMarginChars"/> characters (more than any secret form is long), so the text is redacted a
    ///     second time without them and only the end that both passes share is kept: it is redacted correctly
    ///     whatever the redaction shrank the text to.
    /// </remarks>
    internal static string BuildError(CapturedError captured, string? username, string? token, string? location, string? name)
    {
        var text = Encoding.UTF8.GetString(captured.Bytes);
        var redacted = Redact(text, username, token, location, name);
        if (captured.Dropped)
        {
            var inner = Redact(text[Math.Min(DroppedMarginChars, text.Length)..], username, token, location, name);
            var shared = 0;
            while (shared < inner.Length
                && shared < redacted.Length
                && inner[inner.Length - 1 - shared] == redacted[redacted.Length - 1 - shared])
            {
                shared++;
            }

            redacted = redacted[^shared..];
        }

        var encoded = Encoding.UTF8.GetBytes(redacted);
        return encoded.Length <= MaxErrorBytes
            ? redacted
            : Encoding.UTF8.GetString(encoded, encoded.Length - MaxErrorBytes, MaxErrorBytes);
    }

    /// <summary>
    ///     Gets the private, empty folder used as the home of every git call, so no user configuration is found.
    /// </summary>
    /// <returns>The folder path; it is created when missing.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.GitUnavailable"/> when the folder cannot be created or its path is malformed.
    /// </exception>
    private string HomeFolder()
    {
        var root = string.IsNullOrWhiteSpace(_options.CachePath) ? Path.GetTempPath() : _options.CachePath;
        var home = Path.Combine(root, ".git-home");
        try
        {
            Directory.CreateDirectory(home);
        }
        catch (Exception ex) when (IsCacheFolderError(ex))
        {
            // The raw message names server paths; the exception is deliberately not chained
            throw CacheFolderUnwritable();
        }

        return home;
    }

    /// <summary>
    ///     Tells whether an exception means that a cache folder path cannot be used: it cannot be created or written
    ///     (<see cref="IOException"/>, which includes a path that is too long, and <see cref="UnauthorizedAccessException"/>),
    ///     or the path is malformed or not supported (<see cref="ArgumentException"/>, for example a null character,
    ///     <see cref="NotSupportedException"/>, for example a colon in the wrong place on Windows, and
    ///     <see cref="SecurityException"/>).
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><c>true</c> when the exception is a cache folder path or permission problem.</returns>
    internal static bool IsCacheFolderError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;

    /// <summary>
    ///     Creates the exception for a cache folder that cannot be written or whose path is not valid. The message names the setting, not the path.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    internal static DomainRuleException CacheFolderUnwritable() =>
        new(
            DomainRule.GitUnavailable,
            "The server cannot write its git cache folder. Ask an administrator to check the Git:CachePath setting and the folder permissions.");

    /// <summary>
    ///     Creates the exception for a missing or too old git.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException Unavailable() =>
        new(
            DomainRule.GitUnavailable,
            "The server cannot run git. Ask an administrator to install git 2.31 or newer.");
}
