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

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.SelfTest.Checks;
using DemaConsulting.TestResults;
using DemaConsulting.TestResults.IO;

namespace DemaConsulting.ReviewCenter.SelfTest;

/// <summary>
///     Provides self-validation functionality for the ReviewCenter: it runs the checks, prints the report and
///     writes the optional results file.
/// </summary>
/// <remarks>
///     The default run is hermetic: it never reads or changes the configured database or configuration and never
///     binds the configured port (see <see cref="Checks"/>). The opt-in exceptions are the two read-only checks of
///     the configured database and key ring that <c>--check-config</c> adds. Timing, outcome and reporting of each check are
///     done by <see cref="ValidationRunner"/>; this class only decides which checks run and summarizes them. The
///     checks run at the same time, each in its own temporary folder, and are reported in their listed order.
///     The class is stateless and therefore thread-safe.
/// </remarks>
internal static class Validation
{
    /// <summary>
    ///     Gets the checks of the default run, in the order they are executed.
    /// </summary>
    /// <param name="includeConfigured">
    ///     <c>true</c> to append the opt-in read-only checks of the configured database and of the configured key
    ///     ring and its secrets (<c>--check-config</c>).
    /// </param>
    /// <returns>The checks; the default run has nine, the opt-in run eleven.</returns>
    public static IReadOnlyList<ValidationCheck> CreateChecks(bool includeConfigured)
    {
        var checks = new List<ValidationCheck>
        {
            new(GitAvailableCheck.Name, GitAvailableCheck.RunAsync),
            new(GitRoundTripCheck.Name, GitRoundTripCheck.RunAsync),
            new(ReviewWorkflowCheck.Name, ReviewWorkflowCheck.RunAsync),
            new(FingerprintCheck.Name, FingerprintCheck.RunAsync),
            new(PasswordHashCheck.Name, PasswordHashCheck.RunAsync),
            new(TotpCheck.Name, TotpCheck.RunAsync),
            new(AuditChainCheck.Name, AuditChainCheck.RunAsync),
            new(AttestationPdfCheck.Name, AttestationPdfCheck.RunAsync),
            new(HostStartCheck.Name, HostStartCheck.RunAsync)
        };

        // The configured database and key ring are the only things outside the temporary folders, so they are opt-in
        if (includeConfigured)
        {
            checks.Add(new(ConfiguredDatabaseCheck.Name, ConfiguredDatabaseCheck.RunAsync));
            checks.Add(new(ConfiguredKeysCheck.Name, ConfiguredKeysCheck.RunAsync));
        }

        return checks;
    }

    /// <summary>
    ///     Runs self-validation tests and optionally writes results to a file.
    /// </summary>
    /// <param name="context">The context containing command line arguments and program state.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     If any self-test fails, <c>context.WriteError</c> is called for each failure, which sets
    ///     <c>context.ExitCode</c> to 1 as a side-effect. A skipped check does not fail the run. If a results file
    ///     is requested and its extension is unsupported, <c>context.WriteError</c> is also called, resulting in a
    ///     non-zero exit code.
    /// </remarks>
    public static void Run(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Run(context, CreateChecks(context.CheckConfig));
    }

    /// <summary>
    ///     Runs the supplied checks, prints the report and optionally writes the results file.
    /// </summary>
    /// <param name="context">The context containing command line arguments and program state.</param>
    /// <param name="checks">The checks to run, in order. Must not be null.</param>
    /// <param name="checkTimeout">
    ///     The deadline of each check, or <c>null</c> for <see cref="ValidationRunner.CheckTimeout"/>; tests pass a
    ///     short one to prove that a hung check is reported instead of hanging the run.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    ///     The seam that lets tests exercise the report, the summary and the results files with fast checks, so
    ///     the suite does not pay for the real checks more than a few times.
    /// </remarks>
    internal static void Run(Context context, IReadOnlyList<ValidationCheck> checks, TimeSpan? checkTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checks);
        var timeout = checkTimeout ?? ValidationRunner.CheckTimeout;

        // Print validation header
        PrintValidationHeader(context);

        // Run every check; the runner records each outcome and duration
        var testResults = new DemaConsulting.TestResults.TestResults
        {
            Name = "ReviewCenter Self-Validation"
        };
        // The checks are independent and hermetic, so they run at the same time: the slow ones (the review
        // workflow, the host start) overlap instead of adding up. The report is still printed in the listed order.
        // Every check has its own deadline, enforced by the runner, so one hung check cannot hang the run.
        var watch = Stopwatch.StartNew();
        var running = checks
            .Select(check => Task.Run(() => ValidationRunner.ExecuteAsync(check, timeout, CancellationToken.None)))
            .ToList();
        foreach (var task in running)
        {
            ValidationRunner.Report(context, testResults, task.GetAwaiter().GetResult());
        }

        // Print summary
        PrintSummary(context, testResults, watch.Elapsed);

        // Write results file if requested
        if (context.ResultsFile != null)
        {
            WriteResultsFile(context, testResults);
        }
    }

    /// <summary>
    ///     Prints the totals of the run.
    /// </summary>
    /// <param name="context">The context for output.</param>
    /// <param name="testResults">The recorded results.</param>
    /// <param name="elapsed">The time the whole run took.</param>
    private static void PrintSummary(Context context, DemaConsulting.TestResults.TestResults testResults, TimeSpan elapsed)
    {
        var failed = testResults.Results.Count(t => t.Outcome == TestOutcome.Failed);
        context.WriteLine("");
        context.WriteLine($"Total Tests: {testResults.Results.Count}");
        context.WriteLine($"Passed: {testResults.Results.Count(t => t.Outcome == TestOutcome.Passed)}");
        context.WriteLine($"Skipped: {testResults.Results.Count(t => t.Outcome == TestOutcome.NotExecuted)}");
        if (failed > 0)
        {
            context.WriteError($"Failed: {failed}");
        }
        else
        {
            context.WriteLine($"Failed: {failed}");
        }

        context.WriteLine($"Total Time: {elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
    }

    /// <summary>
    ///     Prints the validation header with system information.
    /// </summary>
    /// <param name="context">The context for output.</param>
    private static void PrintValidationHeader(Context context)
    {
        var heading = new string('#', context.HeadingDepth);
        context.WriteLine($"{heading} DEMA Consulting ReviewCenter");
        context.WriteLine("");
        context.WriteLine("| Information         | Value                                              |");
        context.WriteLine("| :------------------ | :------------------------------------------------- |");
        context.WriteLine($"| Tool Version        | {Program.Version,-50} |");
        context.WriteLine($"| Machine Name        | {Environment.MachineName,-50} |");
        context.WriteLine($"| OS Version          | {RuntimeInformation.OSDescription,-50} |");
        context.WriteLine($"| DotNet Runtime      | {RuntimeInformation.FrameworkDescription,-50} |");
        context.WriteLine($"| Time Stamp          | {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC{"",-29} |");
        context.WriteLine("");
    }

    /// <summary>
    ///     Writes test results to a file in TRX or JUnit format.
    /// </summary>
    /// <param name="context">The context for output.</param>
    /// <param name="testResults">The test results to write.</param>
    private static void WriteResultsFile(Context context, DemaConsulting.TestResults.TestResults testResults)
    {
        if (context.ResultsFile == null)
        {
            return;
        }

        try
        {
            var extension = Path.GetExtension(context.ResultsFile).ToLowerInvariant();
            string content;

            if (extension == ".trx")
            {
                content = TrxSerializer.Serialize(testResults);
            }
            else if (extension == ".xml")
            {
                // Assume JUnit format for .xml extension
                content = JUnitSerializer.Serialize(testResults);
            }
            else
            {
                context.WriteError($"Error: Unsupported results file format '{extension}'. Use .trx or .xml extension.");
                return;
            }

            File.WriteAllText(context.ResultsFile, content);
            context.WriteLine($"Results written to {context.ResultsFile}");
        }
        // Generic catch is justified here as a top-level handler to log file write errors
        catch (Exception ex)
        {
            context.WriteError($"Error: Failed to write results file: {ex.Message}");
        }
    }
}
