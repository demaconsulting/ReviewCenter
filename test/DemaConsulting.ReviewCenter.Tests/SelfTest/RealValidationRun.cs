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

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     The two real runs of the self-validation that the whole test suite shares: one in process and one as a
///     separate <c>dotnet</c> process.
/// </summary>
/// <remarks>
///     The real checks start git processes, build the production services and start a web host, which takes
///     seconds, so the suite runs them only here and lets every test that needs a real report read the same
///     outcome. Both runs are started together, on first use, so their times overlap and the suite pays for
///     one of them. All other tests use the fast check list of
///     <c>Validation.Run(Context, IReadOnlyList{ValidationCheck})</c>.
///     The run goes through <see cref="Program.Run(Context)"/> with a results file, a log file and a heading depth,
///     so the dispatch, the report, the results file and the options are all exercised by it. The run happens at
///     most once per test process, whichever test asks first; the tests that read it share the Sequential
///     collection, so the shared state is never touched from two threads.
/// </remarks>
internal static class RealValidationRun
{
    /// <summary>
    ///     The heading depth the run asks for, which tests read back from the report.
    /// </summary>
    public const int HeadingDepth = 3;

    /// <summary>
    ///     The names of the eight checks of the default run, in the order they are reported.
    /// </summary>
    public static readonly string[] CheckNames =
    [
        "ReviewCenter_GitAvailable",
        "ReviewCenter_GitRoundTrip",
        "ReviewCenter_ReviewWorkflow",
        "ReviewCenter_Fingerprint",
        "ReviewCenter_PasswordHash",
        "ReviewCenter_AuditChain",
        "ReviewCenter_AttestationPdf",
        "ReviewCenter_HostStart"
    ];

    /// <summary>
    ///     The two real runs, started together on first use so that their elapsed times overlap instead of adding up.
    /// </summary>
    private static readonly Lazy<(Task<Outcome> InProcess, Task<ProcessRun> Process)> _started = new(
        () => (Task.Run(Execute), Task.Run(ExecuteProcess)));

    /// <summary>
    ///     Gets the outcome of the in-process run, starting both real runs on first use.
    /// </summary>
    public static Outcome Result => _started.Value.InProcess.GetAwaiter().GetResult();

    /// <summary>
    ///     Gets the outcome of the real process run, starting both real runs on first use.
    /// </summary>
    internal static ProcessRun Process => _started.Value.Process.GetAwaiter().GetResult();

    /// <summary>
    ///     The outcome of the real <c>--validate</c> process run.
    /// </summary>
    /// <param name="ExitCode">The process exit code.</param>
    /// <param name="Output">The console output.</param>
    /// <param name="ResultsContent">The text of the results file; empty when none was written.</param>
    internal sealed record ProcessRun(int ExitCode, string Output, string ResultsContent);

    /// <summary>
    ///     Runs <c>dotnet ReviewCenter.dll --validate</c> once as a real process.
    /// </summary>
    /// <returns>The exit code, console output and results file content of the run.</returns>
    /// <remarks>
    ///     It uses the legacy <c>--result</c> alias with a JUnit XML file and heading depth 3, so that one run
    ///     serves every option the integration tests check.
    /// </remarks>
    private static ProcessRun ExecuteProcess()
    {
        var dllPath = PathHelpers.SafePathCombine(AppContext.BaseDirectory, "DemaConsulting.ReviewCenter.dll");
        using var temp = new TemporaryDirectory();
        var resultsFile = temp.GetFilePath("validation-results.xml");

        var exitCode = Runner.Run(
            out var output,
            "dotnet",
            dllPath,
            "--validate",
            "--depth",
            "3",
            "--result",
            resultsFile);

        var content = File.Exists(resultsFile) ? File.ReadAllText(resultsFile) : string.Empty;
        return new ProcessRun(exitCode, output, content);
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
