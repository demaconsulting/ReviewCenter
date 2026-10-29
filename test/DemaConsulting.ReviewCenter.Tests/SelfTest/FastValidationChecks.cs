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

using DemaConsulting.ReviewCenter.SelfTest;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     Instant stand-ins for the real self-validation checks, so that the report, the summary and the results
///     files can be tested without starting git, a database or a web host.
/// </summary>
internal static class FastValidationChecks
{
    /// <summary>
    ///     Creates a check that passes with a detail text.
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <param name="detail">The detail the check returns.</param>
    /// <returns>The check.</returns>
    public static ValidationCheck Passing(string name = "ReviewCenter_Fast", string detail = "all good") =>
        new(name, _ => Task.FromResult(detail));

    /// <summary>
    ///     Creates a check that fails with a message.
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <param name="message">The failure message.</param>
    /// <returns>The check.</returns>
    public static ValidationCheck Failing(string name = "ReviewCenter_Failing", string message = "it is wrong") =>
        new(name, _ => throw new ValidationFailedException(message));

    /// <summary>
    ///     Creates a check that is skipped with a reason.
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <param name="reason">The reason for the skip.</param>
    /// <returns>The check.</returns>
    public static ValidationCheck Skipped(string name = "ReviewCenter_Skipped", string reason = "nothing to check") =>
        new(name, _ => throw new ValidationSkippedException(reason));

    /// <summary>
    ///     Creates a check that throws an unexpected exception.
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <returns>The check.</returns>
    public static ValidationCheck Throwing(string name = "ReviewCenter_Throwing") =>
        new(name, _ => throw new InvalidOperationException("boom"));

    /// <summary>
    ///     Creates a check that passes after a delay.
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <param name="delay">How long the check takes.</param>
    /// <returns>The check.</returns>
    public static ValidationCheck Slow(string name, TimeSpan delay) =>
        new(name, async token =>
        {
            await Task.Delay(delay, token);
            return "done";
        });
}
