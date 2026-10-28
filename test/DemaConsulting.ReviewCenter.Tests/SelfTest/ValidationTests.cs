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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.SelfTest;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Unit tests for the Validation class. The report, the summary and the results files are tested with fast
///     stand-in checks; the real checks run once in the shared real run (see <see cref="RealValidationRun"/>).
/// </summary>
[Collection("Sequential")]
public class ValidationTests
{
    /// <summary>
    ///     The text a run left behind.
    /// </summary>
    /// <param name="ExitCode">The exit code of the run.</param>
    /// <param name="Log">The report, as written to the log file.</param>
    /// <param name="Results">The text of the results file, or an empty string when none was requested.</param>
    private sealed record RunOutput(int ExitCode, string Log, string Results);

    /// <summary>
    ///     Runs the supplied checks through <c>Validation.Run</c> in silent mode and reads the outputs back.
    /// </summary>
    /// <param name="checks">The checks to run.</param>
    /// <param name="resultsExtension">The extension of the results file to request, or <c>null</c> for none.</param>
    /// <returns>The exit code, the report and the results file text.</returns>
    private static RunOutput Run(IReadOnlyList<ValidationCheck> checks, string? resultsExtension = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"validation_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var logFile = Path.Combine(folder, "report.log");
            var resultsFile = Path.Combine(folder, "results" + resultsExtension);
            var args = new List<string> { "--silent", "--log", logFile };
            if (resultsExtension != null)
            {
                args.AddRange(["--results", resultsFile]);
            }

            int exitCode;
            using (var context = Context.Create([.. args]))
            {
                Validation.Run(context, checks);
                exitCode = context.ExitCode;
            }

            return new RunOutput(
                exitCode,
                File.ReadAllText(logFile),
                resultsExtension != null && File.Exists(resultsFile) ? File.ReadAllText(resultsFile) : string.Empty);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that Run throws ArgumentNullException when context is null.
    /// </summary>
    [Fact]
    public void Validation_Run_NullContext_ThrowsArgumentNullException()
    {
        // Arrange: setup test conditions
        // No setup required — null is the input under test.

        // Act: invoke Run with null context
        var exception = Record.Exception(() => Validation.Run(null!));

        // Assert: verify ArgumentNullException is thrown
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>
    ///     Test that Run throws ArgumentNullException when the check list is null.
    /// </summary>
    [Fact]
    public void Validation_Run_NullChecks_ThrowsArgumentNullException()
    {
        // Arrange: a silent context
        using var context = Context.Create(["--silent"]);

        // Act: invoke Run with a null check list
        var exception = Record.Exception(() => Validation.Run(context, null!));

        // Assert: verify ArgumentNullException is thrown
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>
    ///     Test that Run prints a summary containing total, passed, skipped, failed counts and the total time.
    /// </summary>
    [Fact]
    public void Validation_Run_WithSilentContext_PrintsSummary()
    {
        // Arrange: one passing and one skipped check
        var checks = new[] { FastValidationChecks.Passing(), FastValidationChecks.Skipped() };

        // Act: run validation with silent context and a log file
        var output = Run(checks);

        // Assert: verify summary lines are written to the log file
        Assert.Contains("Total Tests: 2", output.Log);
        Assert.Contains("Passed: 1", output.Log);
        Assert.Contains("Skipped: 1", output.Log);
        Assert.Contains("Failed: 0", output.Log);
        Assert.Matches(@"Total Time: \d+\.\d s", output.Log);
    }

    /// <summary>
    ///     Test that Run exits with code zero when all self-validation tests pass.
    /// </summary>
    [Fact]
    public void Validation_Run_WithSilentContext_ExitCodeIsZero()
    {
        // Arrange: checks that all pass
        var checks = new[] { FastValidationChecks.Passing("ReviewCenter_One"), FastValidationChecks.Passing("ReviewCenter_Two") };

        // Act: run validation
        var output = Run(checks);

        // Assert: verify exit code is zero indicating successful validation
        Assert.Equal(0, output.ExitCode);
    }

    /// <summary>
    ///     Test that Run writes a valid TRX file when the results path ends with .trx.
    /// </summary>
    [Fact]
    public void Validation_Run_WithTrxResultsFile_WritesTrxFile()
    {
        // Arrange: a passing check named like a real one
        var checks = new[] { FastValidationChecks.Passing("ReviewCenter_Fast") };

        // Act: run validation with TRX results output
        var output = Run(checks, ".trx");

        // Assert: verify the TRX file has the expected content, with the test name and outcome
        Assert.Contains("<TestRun", output.Results);
        Assert.Contains("ReviewCenter_Fast", output.Results);
        Assert.Contains("Passed", output.Results);
    }

    /// <summary>
    ///     Test that Run writes a valid JUnit XML file when the results path ends with .xml.
    /// </summary>
    [Fact]
    public void Validation_Run_WithXmlResultsFile_WritesXmlFile()
    {
        // Arrange: a passing check named like a real one
        var checks = new[] { FastValidationChecks.Passing("ReviewCenter_Fast") };

        // Act: run validation with XML results output
        var output = Run(checks, ".xml");

        // Assert: verify the XML file has JUnit format content with the test name
        Assert.Contains("<testsuites", output.Results);
        Assert.Contains("ReviewCenter_Fast", output.Results);
    }

    /// <summary>
    ///     Test that Run does not write a results file when the extension is unsupported.
    /// </summary>
    [Fact]
    public void Validation_Run_WithUnsupportedResultsFormat_DoesNotWriteFile()
    {
        // Arrange: a passing check and an unsupported file extension
        var checks = new[] { FastValidationChecks.Passing() };

        // Act: run validation with unsupported results file extension
        var output = Run(checks, ".json");

        // Assert: no results file, a non-zero exit code and the error in the log
        Assert.Equal(string.Empty, output.Results);
        Assert.Equal(1, output.ExitCode);
        Assert.Contains("Unsupported results file format", output.Log);
    }

    /// <summary>
    ///     Test that a failed check is reported, counted and turns the exit code non-zero, while the other checks
    ///     still run.
    /// </summary>
    [Fact]
    public void Validation_Run_FailingCheck_ReportsFailureAndSetsExitCode()
    {
        // Arrange: a failing check between two passing ones
        var checks = new[]
        {
            FastValidationChecks.Passing("ReviewCenter_Before"),
            FastValidationChecks.Failing("ReviewCenter_Failing", "it is wrong"),
            FastValidationChecks.Passing("ReviewCenter_After")
        };

        // Act: run validation with a TRX file
        var output = Run(checks, ".trx");

        // Assert: the failure and its message are reported, the run fails and every check was recorded
        Assert.Equal(1, output.ExitCode);
        Assert.Contains("✗ ReviewCenter_Failing - Failed", output.Log);
        Assert.Contains("it is wrong", output.Log);
        Assert.Contains("Total Tests: 3", output.Log);
        Assert.Contains("Failed: 1", output.Log);
        Assert.Contains("ReviewCenter_After", output.Results);
        Assert.Contains("Failed", output.Results);
    }

    /// <summary>
    ///     Test that an unexpected exception is recorded as a failed check with the exception type in the message.
    /// </summary>
    [Fact]
    public void Validation_Run_ThrowingCheck_RecordsFailureWithExceptionType()
    {
        // Arrange: a check that throws an exception that is not a validation failure
        var checks = new[] { FastValidationChecks.Throwing() };

        // Act: run validation
        var output = Run(checks);

        // Assert: the run fails and the line names the exception type and message
        Assert.Equal(1, output.ExitCode);
        Assert.Contains("✗ ReviewCenter_Throwing - Failed", output.Log);
        Assert.Contains("InvalidOperationException: boom", output.Log);
    }

    /// <summary>
    ///     Test that a skipped check is reported as skipped, does not fail the run and is recorded as not executed.
    /// </summary>
    [Fact]
    public void Validation_Run_SkippedCheck_ReportsSkippedAndDoesNotFail()
    {
        // Arrange: a skipped check and a passing check
        var checks = new[] { FastValidationChecks.Skipped("ReviewCenter_Skipped", "nothing to check"), FastValidationChecks.Passing() };

        // Act: run validation with a TRX file and again with a JUnit file
        var trx = Run(checks, ".trx");
        var junit = Run(checks, ".xml");

        // Assert: the line says SKIPPED, the exit code is zero, and the results files record the skip
        Assert.Equal(0, trx.ExitCode);
        Assert.Contains("- ReviewCenter_Skipped - SKIPPED", trx.Log);
        Assert.Contains("nothing to check", trx.Log);
        Assert.Contains("Skipped: 1", trx.Log);
        Assert.Contains("NotExecuted", trx.Results);
        Assert.Contains("<skipped", junit.Results);
    }

    /// <summary>
    ///     Test that every line of the report carries the elapsed time of the check, measured from the check itself.
    /// </summary>
    [Fact]
    public void Validation_Run_SlowCheck_ReportsElapsedTime()
    {
        // Arrange: a check that takes at least 150 ms
        var checks = new[] { FastValidationChecks.Slow("ReviewCenter_Slow", TimeSpan.FromMilliseconds(150)) };

        // Act: run validation
        var output = Run(checks);

        // Assert: the line shows an elapsed time of at least 150 ms
        var match = Regex.Match(output.Log, @"ReviewCenter_Slow - Passed \((\d+) ms\)");
        Assert.True(match.Success, "The line should show the elapsed time");
        Assert.True(int.Parse(match.Groups[1].Value) >= 140, $"Elapsed time {match.Groups[1].Value} ms is too short");
    }

    /// <summary>
    ///     Test that the report lists the checks in their listed order even when they finish in another order.
    /// </summary>
    [Fact]
    public void Validation_Run_SlowFirstCheck_ReportsInListedOrder()
    {
        // Arrange: a slow check listed before a fast one, so the fast one finishes first
        var checks = new[]
        {
            FastValidationChecks.Slow("ReviewCenter_SlowFirst", TimeSpan.FromMilliseconds(150)),
            FastValidationChecks.Passing("ReviewCenter_FastSecond")
        };

        // Act: run validation
        var output = Run(checks);

        // Assert: the slow check is reported first
        Assert.True(
            output.Log.IndexOf("ReviewCenter_SlowFirst", StringComparison.Ordinal) <
            output.Log.IndexOf("ReviewCenter_FastSecond", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the default check list has the eight hermetic checks in their order, with the opt-in check absent.
    /// </summary>
    [Fact]
    public void Validation_CreateChecks_Default_ListsEightHermeticChecks()
    {
        // Arrange: no setup; the default list is asked for

        // Act: create the default check list
        var checks = Validation.CreateChecks(includeConfiguredDatabase: false);

        // Assert: eight checks, in order, all with the ReviewCenter_ prefix and no configured-database check
        Assert.Equal(RealValidationRun.CheckNames, checks.Select(check => check.Name));
    }

    /// <summary>
    ///     Test that the opt-in check list adds only the configured-database check, after the default ones.
    /// </summary>
    [Fact]
    public void Validation_CreateChecks_CheckConfig_AddsConfiguredDatabaseLast()
    {
        // Arrange: no setup; the opt-in list is asked for

        // Act: create the opt-in check list
        var checks = Validation.CreateChecks(includeConfiguredDatabase: true);

        // Assert: nine checks and the configured-database check is the last
        Assert.Equal(9, checks.Count);
        Assert.Equal("ReviewCenter_ConfiguredDatabase", checks[^1].Name);
    }
}
