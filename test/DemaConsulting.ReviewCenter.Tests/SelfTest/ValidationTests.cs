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
public partial class ValidationTests
{
    /// <summary>
    ///     Matches the report line of the slow check and captures its elapsed milliseconds.
    /// </summary>
    /// <returns>The pattern; group 1 is the elapsed time.</returns>
    [GeneratedRegex(@"ReviewCenter_Slow - Passed \((\d+) ms\)")]
    private static partial Regex SlowCheckLine();

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
    /// <param name="checkTimeout">The deadline of each check, or <c>null</c> for the production default.</param>
    /// <returns>The exit code, the report and the results file text.</returns>
    private static RunOutput Run(
        IReadOnlyList<ValidationCheck> checks,
        string? resultsExtension = null,
        TimeSpan? checkTimeout = null)
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
                Validation.Run(context, checks, checkTimeout);
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
        var match = SlowCheckLine().Match(output.Log);
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
    ///     Test that a check that blocks synchronously and ignores its token is failed at the deadline, without
    ///     the runner waiting for it. Before the deadline was enforced by the runner this call never returned.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_CheckBlocksAndIgnoresToken_FailsAsTimedOutWithoutWaiting()
    {
        // Arrange: a check that blocks its thread until the test releases it, and never looks at its token
        using var release = new ManualResetEventSlim(false);
        var check = new ValidationCheck("ReviewCenter_Blocked", _ =>
        {
            release.Wait(CancellationToken.None);
            return Task.FromResult("never reported");
        });

        try
        {
            // Act: run it with a 100 ms deadline; WaitAsync turns a hang into a failure after a much longer wait
            var outcome = await ValidationRunner
                .ExecuteAsync(check, TimeSpan.FromMilliseconds(100), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Assert: the runner returned a failed outcome with the timeout message while the check still blocks
            Assert.Equal(DemaConsulting.TestResults.TestOutcome.Failed, outcome.Result.Outcome);
            Assert.Equal("Timed out after 0.1 s", outcome.Result.ErrorMessage);
            Assert.Contains("✗ ReviewCenter_Blocked - Failed", outcome.Line);
            Assert.True(outcome.Result.Duration < TimeSpan.FromSeconds(5), $"Took {outcome.Result.Duration}");
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    ///     Test that the token of a check that is still running is cancelled when the deadline passes, so a
    ///     cooperative check stops.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_DeadlinePasses_CancelsTheTokenOfTheCheck()
    {
        // Arrange: a check that waits for its token to be cancelled and records that it saw the cancellation
        var sawCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new ValidationCheck("ReviewCenter_Cooperative", async token =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                sawCancellation.TrySetResult(true);
                throw;
            }

            return "unreachable";
        });

        // Act: run it with a short deadline
        var outcome = await ValidationRunner.ExecuteAsync(check, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert: it is reported as timed out and its token was cancelled
        Assert.Equal("Timed out after 0.1 s", outcome.Result.ErrorMessage);
        Assert.True(await sawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a check that cancels itself is reported as cancelled, not as a timeout.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_CheckThrowsCancellation_ReportsCancelledNotTimedOut()
    {
        // Arrange: a check that throws OperationCanceledException long before the deadline
        var check = new ValidationCheck("ReviewCenter_SelfCancelled", _ => throw new OperationCanceledException());

        // Act: run it with the default deadline
        var outcome = await ValidationRunner.ExecuteAsync(check);

        // Assert: the failure says cancelled and does not claim a timeout
        Assert.Equal(DemaConsulting.TestResults.TestOutcome.Failed, outcome.Result.Outcome);
        Assert.Equal("Cancelled", outcome.Result.ErrorMessage);
        Assert.DoesNotContain("Timed out", outcome.Line);
    }

    /// <summary>
    ///     Test that a cancellation requested by the caller is reported as cancelled even when the check ignores it.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_CallerCancels_ReportsCancelledWithoutWaitingForTheCheck()
    {
        // Arrange: a blocking check that ignores its token and a caller that cancels after a moment
        using var release = new ManualResetEventSlim(false);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var check = new ValidationCheck("ReviewCenter_Ignoring", _ =>
        {
            release.Wait(CancellationToken.None);
            return Task.FromResult("never reported");
        });

        try
        {
            // Act: run it with a long deadline, so only the caller's cancellation can end the wait
            var outcome = await ValidationRunner
                .ExecuteAsync(check, TimeSpan.FromMinutes(5), source.Token)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // Assert: the runner returned "Cancelled"
            Assert.Equal("Cancelled", outcome.Result.ErrorMessage);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    ///     Test that a TimeoutException thrown by the check itself is not mistaken for the deadline of the runner.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_CheckThrowsTimeoutException_ReportsTheCheckOwnFailure()
    {
        // Arrange: a check that fails with its own timeout exception
        var check = new ValidationCheck("ReviewCenter_OwnTimeout", _ => throw new TimeoutException("git hung"));

        // Act: run it
        var outcome = await ValidationRunner.ExecuteAsync(check);

        // Assert: the message is the check's own
        Assert.Equal("TimeoutException: git hung", outcome.Result.ErrorMessage);
    }

    /// <summary>
    ///     Test that an exception thrown later by an abandoned check does not surface as an unobserved task exception.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_AbandonedCheckThrowsLater_ExceptionIsObserved()
    {
        // Arrange: a check that ignores its deadline and then throws when released
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            lock (unobserved)
            {
                unobserved.AddRange(args.Exception.InnerExceptions);
            }
        }

        using var release = new ManualResetEventSlim(false);
        using var finishedWork = new ManualResetEventSlim(false);
        var check = new ValidationCheck("ReviewCenter_ThrowsLater", _ =>
        {
            release.Wait(CancellationToken.None);
            try
            {
                throw new InvalidOperationException("late failure of an abandoned check");
            }
            finally
            {
                finishedWork.Set();
            }
        });
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var outcome = await ValidationRunner.ExecuteAsync(check, TimeSpan.FromMilliseconds(100), CancellationToken.None);
            Assert.Equal("Timed out after 0.1 s", outcome.Result.ErrorMessage);

            // Act: let the abandoned check throw, then collect so that an unobserved exception would be raised
            release.Set();
            while (!finishedWork.IsSet)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
            for (var i = 0; i < 2; i++)
            {
                ForcedGarbageCollection.CollectAndFinalize();
            }

            // Assert: nothing was left unobserved
            lock (unobserved)
            {
                Assert.DoesNotContain(unobserved, ex => ex.Message.Contains("late failure", StringComparison.Ordinal));
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    /// <summary>
    ///     Test that the runner refuses a deadline that is not positive.
    /// </summary>
    [Fact]
    public async Task ValidationRunner_ExecuteAsync_NonPositiveTimeout_ThrowsArgumentOutOfRangeException()
    {
        // Act: run a check with a zero deadline
        var exception = await Record.ExceptionAsync(
            () => ValidationRunner.ExecuteAsync(FastValidationChecks.Passing(), TimeSpan.Zero, CancellationToken.None));

        // Assert: refused
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    /// <summary>
    ///     Test that a hung check does not hang the whole run: it is reported as timed out, the other checks pass and
    ///     the exit code is non-zero.
    /// </summary>
    [Fact]
    public async Task Validation_Run_HungCheck_ReportsTimeoutAndOtherChecksStillPass()
    {
        // Arrange: a check that blocks until released and ignores its token, between two passing checks
        using var release = new ManualResetEventSlim(false);
        var hung = new ValidationCheck("ReviewCenter_Hung", _ =>
        {
            release.Wait(CancellationToken.None);
            return Task.FromResult("never reported");
        });
        var checks = new[]
        {
            FastValidationChecks.Passing("ReviewCenter_Before"),
            hung,
            FastValidationChecks.Passing("ReviewCenter_After")
        };

        try
        {
            // Act: run validation with a 150 ms deadline on a worker; WaitAsync turns a hang into a failure
            var output = await Task
                .Run(() => Run(checks, checkTimeout: TimeSpan.FromMilliseconds(150)), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

            // Assert: the hung check is the only failure and the run fails
            Assert.Equal(1, output.ExitCode);
            Assert.Contains("✗ ReviewCenter_Hung - Failed", output.Log);
            Assert.Contains("Timed out after 0.15 s", output.Log);
            Assert.Contains("Passed: 2", output.Log);
            Assert.Contains("Failed: 1", output.Log);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>
    ///     Test that the default check list has the nine hermetic checks in their order, with the opt-in checks absent.
    /// </summary>
    [Fact]
    public void Validation_CreateChecks_Default_ListsNineHermeticChecks()
    {
        // Arrange: no setup; the default list is asked for

        // Act: create the default check list
        var checks = Validation.CreateChecks(includeConfigured: false);

        // Assert: nine checks, in order, all with the ReviewCenter_ prefix and no configured-database or key check
        Assert.Equal(RealValidationRun.CheckNames, checks.Select(check => check.Name));
    }

    /// <summary>
    ///     Test that the opt-in check list adds only the configured-database and key checks, after the default ones.
    /// </summary>
    [Fact]
    public void Validation_CreateChecks_CheckConfig_AddsConfiguredDatabaseAndKeysLast()
    {
        // Arrange: no setup; the opt-in list is asked for

        // Act: create the opt-in check list
        var checks = Validation.CreateChecks(includeConfigured: true);

        // Assert: eleven checks; the configured-database check and then the configured-keys check come last
        Assert.Equal(11, checks.Count);
        Assert.Equal("ReviewCenter_ConfiguredDatabase", checks[^2].Name);
        Assert.Equal("ReviewCenter_ConfiguredKeys", checks[^1].Name);
    }
}
