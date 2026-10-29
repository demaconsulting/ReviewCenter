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
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.TestResults;

namespace DemaConsulting.ReviewCenter.SelfTest;

/// <summary>
///     One named self-validation check: the name that appears in the report and the work that proves it.
/// </summary>
/// <remarks>
///     The check returns a short detail text on success (what it saw, never a secret). It reports a failed
///     expectation by throwing <see cref="ValidationFailedException"/> and an intentional skip by throwing
///     <see cref="ValidationSkippedException"/>; any other exception is also a failure. Keeping the checks as plain
///     delegates lets <see cref="ValidationRunner"/> own all timing, outcome and reporting code once.
/// </remarks>
/// <param name="Name">The test name, such as <c>ReviewCenter_GitAvailable</c>; it is the name in the TRX and JUnit files.</param>
/// <param name="RunAsync">The work. It must stop when the supplied token is cancelled.</param>
internal sealed record ValidationCheck(string Name, Func<CancellationToken, Task<string>> RunAsync);

/// <summary>
///     Thrown by a check when what it verified does not hold; the message is shown in the report.
/// </summary>
/// <remarks>
///     The message must be a clear, secret-free statement of what is wrong and, where possible, how to fix it,
///     because it is the text an operator reads in the report.
/// </remarks>
/// <param name="message">The failure message.</param>
public sealed class ValidationFailedException(string message) : Exception(message)
{
    /// <summary>
    ///     Throws a <see cref="ValidationFailedException"/> when a condition does not hold; it keeps the checks
    ///     free of repeated <c>if</c>/<c>throw</c> blocks.
    /// </summary>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="message">The failure message shown in the report.</param>
    /// <exception cref="ValidationFailedException">Thrown when <paramref name="condition"/> is false.</exception>
    public static void ThrowUnless(bool condition, string message)
    {
        if (!condition)
        {
            throw new ValidationFailedException(message);
        }
    }
}

/// <summary>
///     Thrown by a check that cannot apply in the current environment; the run records it as skipped, not failed.
/// </summary>
/// <param name="message">The reason the check was skipped.</param>
public sealed class ValidationSkippedException(string message) : Exception(message);

/// <summary>
///     The outcome of one check: the result for the results file and the line for the report.
/// </summary>
/// <param name="Result">The recorded test result, with outcome, duration and message.</param>
/// <param name="Line">The report line, without a line terminator.</param>
internal sealed record CheckOutcome(TestResult Result, string Line);

/// <summary>
///     Runs one <see cref="ValidationCheck"/>: times it and turns its result or exception into a test outcome, then
///     records the outcome and prints its line of the report.
/// </summary>
/// <remarks>
///     All the try/catch, stopwatch and outcome code of the self-validation lives here once, so a check contains
///     only the thing it proves. A skip is recorded as <see cref="TestOutcome.NotExecuted"/> because the test
///     results library has no separate skipped outcome. The class is stateless and thread-safe.
/// </remarks>
internal static class ValidationRunner
{
    /// <summary>
    ///     The longest a single check may take before it is stopped and reported as failed; a hung git process or
    ///     host must not hang the whole validation.
    /// </summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Runs a check under the default <see cref="CheckTimeout"/>, times it and turns its result or exception into
    ///     an outcome; nothing is printed yet.
    /// </summary>
    /// <param name="check">The check to run. Must not be null.</param>
    /// <returns>The recorded result and the report line for it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="check"/> is null.</exception>
    /// <remarks>
    ///     Never throws for a failing check: every exception becomes a failed result. The elapsed time is that of
    ///     the check itself, so it includes any slowdown from checks that run at the same time.
    /// </remarks>
    public static Task<CheckOutcome> ExecuteAsync(ValidationCheck check)
    {
        return ExecuteAsync(check, CheckTimeout, CancellationToken.None);
    }

    /// <summary>
    ///     Runs a check with its own deadline, times it and turns its result or exception into an outcome; nothing is
    ///     printed yet.
    /// </summary>
    /// <param name="check">The check to run. Must not be null.</param>
    /// <param name="timeout">The longest the check may take; must be positive.</param>
    /// <param name="cancellationToken">Cancels the check on behalf of the caller (for example Ctrl+C).</param>
    /// <returns>The recorded result and the report line for it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="check"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is not positive.</exception>
    /// <remarks>
    ///     <para>
    ///         The deadline is enforced by the runner itself, not by the check: the check starts on the thread pool
    ///         and the runner waits for whichever comes first, the check, the deadline or the caller's
    ///         cancellation. A check that blocks synchronously or never looks at its token therefore cannot hang
    ///         the validation. When the deadline passes the token is cancelled, the outcome is recorded as failed
    ///         ("Timed out after N s") and the runner returns without waiting for the check.
    ///     </para>
    ///     <para>
    ///         Limitation: .NET cannot abort a running delegate, so a check that ignores its token keeps running
    ///         in the background until it ends on its own (or the process exits). Whatever it later throws is
    ///         observed and dropped, so it cannot surface as an unobserved task exception. A check that is
    ///         cancelled by the caller, or that throws <see cref="OperationCanceledException"/> by itself, is
    ///         reported as "Cancelled", which is distinct from a timeout.
    ///     </para>
    ///     <para>
    ///         Never throws for a failing check: every exception becomes a failed result. The elapsed time is that of
    ///         the check itself, so it includes any slowdown from checks that run at the same time.
    ///     </para>
    /// </remarks>
    internal static async Task<CheckOutcome> ExecuteAsync(
        ValidationCheck check,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var test = new TestResult { Name = check.Name, ClassName = "Validation", CodeBase = "ReviewCenter" };
        var watch = Stopwatch.StartNew();
        string line;
        try
        {
            var detail = await RunWithDeadlineAsync(check, timeout, cancellationToken);
            if (detail is null)
            {
                // The runner stopped waiting: this is the deadline, not a failure of the check itself
                test.Outcome = TestOutcome.Failed;
                test.ErrorMessage = $"Timed out after {timeout.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s";
                line = Format("✗", check.Name, "Failed", watch, test.ErrorMessage);
            }
            else
            {
                test.Outcome = TestOutcome.Passed;
                test.SystemOutput = detail;
                line = Format("✓", check.Name, "Passed", watch, detail);
            }
        }
        catch (ValidationSkippedException ex)
        {
            test.Outcome = TestOutcome.NotExecuted;
            test.SystemOutput = ex.Message;
            line = Format("-", check.Name, "SKIPPED", watch, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the caller or by the check itself, which is not a timeout
            test.Outcome = TestOutcome.Failed;
            test.ErrorMessage = "Cancelled";
            line = Format("✗", check.Name, "Failed", watch, test.ErrorMessage);
        }
        // Generic catch is justified here as this is a test framework - any exception, expected or not, is
        // recorded as a failed test so that the remaining checks still run and the report is complete.
        catch (Exception ex)
        {
            test.Outcome = TestOutcome.Failed;
            test.ErrorMessage = ex is ValidationFailedException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            line = Format("✗", check.Name, "Failed", watch, test.ErrorMessage);
        }

        test.Duration = watch.Elapsed;
        return new CheckOutcome(test, line);
    }

    /// <summary>
    ///     Starts a check on the thread pool and waits for it, for the deadline or for the caller's cancellation,
    ///     whichever comes first.
    /// </summary>
    /// <param name="check">The check to run.</param>
    /// <param name="timeout">The deadline.</param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    /// <returns>The detail text of the check, or <c>null</c> when the deadline passed before the check ended.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancelled or the check was cancelled.</exception>
    private static async Task<string?> RunWithDeadlineAsync(
        ValidationCheck check,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var checkSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timerSource = new CancellationTokenSource();
        var callerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => callerCancelled.TrySetResult());

        // The thread pool runs the check, so even code that blocks synchronously before its first await cannot
        // keep this method from reaching the deadline
        var token = checkSource.Token;
        var work = Task.Run(() => check.RunAsync(token), CancellationToken.None);
        var deadline = Task.Delay(timeout, timerSource.Token);
        var first = await Task.WhenAny(work, deadline, callerCancelled.Task);

        // Stop the timer; a finished check must not leave it running
        await timerSource.CancelAsync();

        if (ReferenceEquals(first, work) || work.IsCompleted)
        {
            return await work ?? string.Empty;
        }

        // Deadline or caller cancellation: tell the check to stop, but do not wait for it. Its exception, if it
        // ever ends with one, is observed here so that it does not become an unobserved task exception.
        await checkSource.CancelAsync();
        _ = work.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (ReferenceEquals(first, deadline))
        {
            // The deadline passed: no detail, which the caller reports as a timeout
            return null;
        }

        throw new OperationCanceledException(cancellationToken);
    }

    /// <summary>
    ///     Records an outcome and prints its line of the report.
    /// </summary>
    /// <param name="context">The context that receives the report line. Must not be null.</param>
    /// <param name="testResults">The collection the result is added to. Must not be null.</param>
    /// <param name="outcome">The outcome to report. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <remarks>
    ///     A failed check is reported with <see cref="Context.WriteError"/>, which makes the exit code non-zero;
    ///     a pass and a skip are ordinary output.
    /// </remarks>
    public static void Report(Context context, DemaConsulting.TestResults.TestResults testResults, CheckOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(testResults);
        ArgumentNullException.ThrowIfNull(outcome);

        testResults.Results.Add(outcome.Result);
        if (outcome.Result.Outcome == TestOutcome.Failed)
        {
            context.WriteError(outcome.Line);
        }
        else
        {
            context.WriteLine(outcome.Line);
        }
    }

    /// <summary>
    ///     Formats one line of the report.
    /// </summary>
    /// <param name="mark">The symbol that shows the outcome at a glance.</param>
    /// <param name="name">The test name.</param>
    /// <param name="outcome">The outcome word.</param>
    /// <param name="watch">The stopwatch of the check; its elapsed time is shown in milliseconds.</param>
    /// <param name="detail">What the check saw or why it failed; may be empty.</param>
    /// <returns>The line, without a line terminator.</returns>
    private static string Format(string mark, string name, string outcome, Stopwatch watch, string detail)
    {
        var text = $"{mark} {name} - {outcome} ({watch.ElapsedMilliseconds} ms)";
        return string.IsNullOrWhiteSpace(detail) ? text : $"{text}: {detail}";
    }
}
