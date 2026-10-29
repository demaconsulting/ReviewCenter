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
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Data.Sqlite;

[assembly: AssemblyFixture(typeof(DemaConsulting.ReviewCenter.Tests.RealValidationRunFixture))]

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Starts the two real self-validation runs when the test assembly starts, so that they overlap with the rest of
///     the suite instead of blocking the first test that reads them.
/// </summary>
/// <remarks>
///     An assembly fixture is created before the first test of the assembly runs. Its constructor only starts the
///     runs (see <see cref="RealValidationRun.Start"/>) and returns at once; the tests that need the result wait
///     for it when they read it.
/// </remarks>
public sealed class RealValidationRunFixture
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RealValidationRunFixture"/> class and starts the real runs.
    /// </summary>
    public RealValidationRunFixture()
    {
        RealValidationRun.Start();
    }
}

/// <summary>
///     The two real runs of the self-validation that the whole test suite shares: one in process and one as a
///     separate <c>dotnet</c> process.
/// </summary>
/// <remarks>
///     The real checks start git processes, build the production services and start a web host, which takes
///     seconds, so the suite runs them only here and lets every test that needs a real report read the same
///     outcome. Both runs are started together when the test assembly starts (see
///     <see cref="RealValidationRunFixture"/>), so their times overlap with each other and with the rest of the
///     suite. All other tests use the fast check list of
///     <c>Validation.Run(Context, IReadOnlyList{ValidationCheck})</c>.
///     The run goes through <see cref="Program.Run(Context)"/> with a results file, a log file and a heading depth,
///     so the dispatch, the report, the results file and the options are all exercised by it. The run happens at
///     most once per test process; the tests that read it only read the finished, immutable outcome.
/// </remarks>
internal static class RealValidationRun
{
    /// <summary>
    ///     The heading depth the run asks for, which tests read back from the report.
    /// </summary>
    public const int HeadingDepth = 3;

    /// <summary>
    ///     The names of the nine checks of the default run, in the order they are reported.
    /// </summary>
    public static readonly string[] CheckNames =
    [
        "ReviewCenter_GitAvailable",
        "ReviewCenter_GitRoundTrip",
        "ReviewCenter_ReviewWorkflow",
        "ReviewCenter_Fingerprint",
        "ReviewCenter_PasswordHash",
        "ReviewCenter_Totp",
        "ReviewCenter_AuditChain",
        "ReviewCenter_AttestationPdf",
        "ReviewCenter_HostStart"
    ];

    /// <summary>
    ///     The two real runs, started together so that their elapsed times overlap instead of adding up.
    /// </summary>
    private static readonly Lazy<(Task<Outcome> InProcess, Task<ProcessRun> Process)> _started = new(
        () => (Task.Run(Execute), Task.Run(ExecuteProcess)));

    /// <summary>
    ///     Starts both real runs without waiting for them; later calls do nothing.
    /// </summary>
    /// <remarks>
    ///     Called by <see cref="RealValidationRunFixture"/> when the test assembly starts, so the runs (about ten
    ///     seconds when started late, because they are on the critical path of the first test that reads them) overlap
    ///     with the rest of the suite and cost it almost nothing.
    /// </remarks>
    public static void Start()
    {
        _ = _started.Value;
    }

    /// <summary>
    ///     Gets the outcome of the in-process run, starting both real runs if they were not started yet.
    /// </summary>
    public static Outcome Result => _started.Value.InProcess.GetAwaiter().GetResult();

    /// <summary>
    ///     Gets the outcome of the real process run, starting both real runs if they were not started yet.
    /// </summary>
    internal static ProcessRun Process => _started.Value.Process.GetAwaiter().GetResult();

    /// <summary>
    ///     The outcome of the real <c>--validate</c> process run, which is made in a hostile environment.
    /// </summary>
    /// <param name="ExitCode">The process exit code.</param>
    /// <param name="Output">The console output.</param>
    /// <param name="ResultsContent">The text of the results file; empty when none was written.</param>
    /// <param name="PoisonedPort">The port that every poisoned listen address of the environment named.</param>
    /// <param name="SentinelEntries">
    ///     What exists in the folder that the poisoned database, key, git and attestation settings pointed at, after
    ///     the run; empty when nothing read the poisoned settings.
    /// </param>
    /// <param name="PrivateTempEntries">
    ///     What the run left in its private temporary folder, which was its <c>TEMP</c>; empty when every check
    ///     removed everything it created.
    /// </param>
    internal sealed record ProcessRun(
        int ExitCode,
        string Output,
        string ResultsContent,
        int PoisonedPort,
        IReadOnlyList<string> SentinelEntries,
        IReadOnlyList<string> PrivateTempEntries);

    /// <summary>
    ///     Runs <c>dotnet ReviewCenter.dll --validate</c> once as a real process, in a hostile environment.
    /// </summary>
    /// <returns>The exit code, console output, results file content and the traces of the hostile environment.</returns>
    /// <remarks>
    ///     <para>
    ///         It uses the legacy <c>--result</c> alias with a JUnit XML file and heading depth 3, so that one run
    ///         serves every option the integration tests check.
    ///     </para>
    ///     <para>
    ///         The program runs from a private copy of the build output, so its <c>appsettings.json</c> can be
    ///         replaced by invalid JSON (another broken one is in the working directory), and with an environment
    ///         that points every listen address, the environment name, the database, the key ring, the git cache and
    ///         the attestation key at places the run must never use: all-interface listen addresses on one port and
    ///         paths in a sentinel folder. <c>TEMP</c> is a private empty folder, which shows what a run leaves
    ///         behind. A self-validation that read any of these would fail, bind the wrong address or leave a trace.
    ///     </para>
    /// </remarks>
    private static ProcessRun ExecuteProcess()
    {
        using var work = new TemporaryDirectory();
        using var sentinel = new TemporaryDirectory();
        using var privateTemp = new TemporaryDirectory();

        // A private copy of the program with a broken appsettings.json, and a broken one in the working directory
        var appFolder = Path.Combine(work.DirectoryPath, "app");
        var workingFolder = Path.Combine(work.DirectoryPath, "cwd");
        CopyApplication(AppContext.BaseDirectory, appFolder);
        Directory.CreateDirectory(workingFolder);
        File.WriteAllText(Path.Combine(appFolder, "appsettings.json"), "{ this is not valid json");
        File.WriteAllText(Path.Combine(workingFolder, "appsettings.json"), "{ this is not valid json either");

        // The hostile environment
        var port = FindFreePort();
        var everyInterface = $"http://0.0.0.0:{port}";
        var environment = new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = everyInterface,
            ["ASPNETCORE_HTTP_PORTS"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ASPNETCORE_Kestrel__Endpoints__Default__Url"] = everyInterface,
            ["Kestrel__Endpoints__Default__Url"] = everyInterface,
            ["DOTNET_URLS"] = everyInterface,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["Database__Provider"] = "Oracle",
            [TestDatabase.ConnectionStringVariable] = $"Data Source={Path.Combine(sentinel.DirectoryPath, "ambient.db")}",
            ["DataProtection__KeysPath"] = Path.Combine(sentinel.DirectoryPath, "keys"),
            ["Git__CachePath"] = Path.Combine(sentinel.DirectoryPath, "git"),
            ["Attestation__SelfSignedFolder"] = Path.Combine(sentinel.DirectoryPath, "attestation"),
            ["TEMP"] = privateTemp.DirectoryPath,
            ["TMP"] = privateTemp.DirectoryPath,
            ["TMPDIR"] = privateTemp.DirectoryPath
        };

        var resultsFile = Path.Combine(work.DirectoryPath, "validation-results.xml");
        var exitCode = Runner.RunInFolder(
            out var output,
            workingFolder,
            environment,
            "dotnet",
            Path.Combine(appFolder, "DemaConsulting.ReviewCenter.dll"),
            "--validate",
            "--depth",
            "3",
            "--result",
            resultsFile);

        var content = File.Exists(resultsFile) ? File.ReadAllText(resultsFile) : string.Empty;
        return new ProcessRun(
            exitCode,
            output,
            content,
            port,
            Directory.GetFileSystemEntries(sentinel.DirectoryPath),
            Directory.GetFileSystemEntries(privateTemp.DirectoryPath));
    }

    /// <summary>
    ///     Copies the program that the tests were built with, without the test frameworks, symbols and the native
    ///     libraries of other platforms.
    /// </summary>
    /// <param name="source">The build output folder.</param>
    /// <param name="target">The folder to copy to; created.</param>
    private static void CopyApplication(string source, string target)
    {
        Directory.CreateDirectory(target);
        string[] skippedPrefixes =
        [
            "xunit", "testhost", "Microsoft.Testing", "Microsoft.TestPlatform", "Microsoft.VisualStudio", "coverlet",
            "Coverlet", ".msCoverage", "DemaConsulting.ReviewCenter.Tests", "DemaConsulting.ReviewCenter.Puppet"
        ];
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(name);
            if (extension is ".pdb" or ".xml" or ".exe" or "" || skippedPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            {
                continue;
            }

            File.Copy(file, Path.Combine(target, name));
        }

        // Only the native libraries of this platform
        var family = "linux";
        if (OperatingSystem.IsWindows())
        {
            family = "win";
        }
        else if (OperatingSystem.IsMacOS())
        {
            family = "osx";
        }
        var runtimes = Path.Combine(source, "runtimes");
        foreach (var folder in Directory.Exists(runtimes) ? Directory.EnumerateDirectories(runtimes) : [])
        {
            var name = Path.GetFileName(folder);
            if (name == family || name == "unix" || name.StartsWith(family + "-", StringComparison.Ordinal))
            {
                CopyFolder(folder, Path.Combine(target, "runtimes", name));
            }
        }
    }

    /// <summary>
    ///     Copies a folder with everything in it.
    /// </summary>
    /// <param name="source">The folder to copy.</param>
    /// <param name="target">The folder to copy to; created.</param>
    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
    }

    /// <summary>
    ///     Finds a loopback port that is free right now.
    /// </summary>
    /// <returns>The port number.</returns>
    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    ///     What the real run produced.
    /// </summary>
    /// <param name="Validate">Whether the parsed options asked for the validation.</param>
    /// <param name="HeadingDepthParsed">The heading depth the parsed options carry.</param>
    /// <param name="ExitCode">The exit code of the run.</param>
    /// <param name="Log">The text of the log file: the whole report.</param>
    /// <param name="Trx">The text of the TRX results file.</param>
    /// <param name="Elapsed">How long the run took.</param>
    /// <param name="ConfiguredDatabaseCreated">
    ///     <c>true</c> when the database that the environment configures exists after the run but did not before.
    /// </param>
    public sealed record Outcome(
        bool Validate,
        int HeadingDepthParsed,
        int ExitCode,
        string Log,
        string Trx,
        TimeSpan Elapsed,
        bool ConfiguredDatabaseCreated);

    /// <summary>
    ///     Runs the validation once and reads everything back.
    /// </summary>
    /// <returns>The outcome.</returns>
    private static Outcome Execute()
    {
        var configured = ConfiguredDatabasePath();
        var existedBefore = configured != null && File.Exists(configured);
        var folder = Path.Combine(Path.GetTempPath(), "validation_run_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var trxFile = Path.Combine(folder, "results.trx");
            var logFile = Path.Combine(folder, "report.log");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool validate;
            int depth;
            int exitCode;
            using (var context = Context.Create(
                ["--validate", "--silent", "--depth", HeadingDepth.ToString(), "--results", trxFile, "--log", logFile]))
            {
                Program.Run(context);
                validate = context.Validate;
                depth = context.HeadingDepth;
                exitCode = context.ExitCode;
            }

            return new Outcome(
                validate,
                depth,
                exitCode,
                File.ReadAllText(logFile),
                File.Exists(trxFile) ? File.ReadAllText(trxFile) : string.Empty,
                watch.Elapsed,
                configured != null && !existedBefore && File.Exists(configured));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Gets the file of the database that the environment of the test process configures.
    /// </summary>
    /// <returns>The path, or <c>null</c> when none is configured.</returns>
    private static string? ConfiguredDatabasePath()
    {
        var connectionString = Environment.GetEnvironmentVariable(TestDatabase.ConnectionStringVariable);
        return string.IsNullOrWhiteSpace(connectionString)
            ? null
            : Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);
    }
}
