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
using Microsoft.Playwright;

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Finds out once per test run whether headless Chromium can be launched on this machine.
/// </summary>
internal static class PlaywrightAvailability
{
    /// <summary>
    ///     The environment variable that turns a missing Chromium from a skip into a failure. CI sets it to
    ///     <c>true</c> after installing Chromium, so the end-to-end evidence can never be skipped silently.
    /// </summary>
    internal const string RequireChromiumVariable = "REVIEWCENTER_REQUIRE_CHROMIUM";

    /// <summary>
    ///     The cached outcome of the one-time launch attempt.
    /// </summary>
    private static readonly Lazy<Task<string?>> Result = new(ProbeAsync);

    /// <summary>
    ///     Gets a value indicating whether the environment requires Chromium to be available.
    /// </summary>
    /// <returns><c>true</c> when <see cref="RequireChromiumVariable"/> is set to <c>true</c>.</returns>
    public static bool IsChromiumRequired()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(RequireChromiumVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Gets the reason Chromium is unavailable, or <c>null</c> when it can be launched.
    /// </summary>
    /// <returns>The skip reason, or <c>null</c> when Chromium works.</returns>
    public static Task<string?> GetUnavailableReasonAsync()
    {
        return Result.Value;
    }

    /// <summary>
    ///     Tries to launch headless Chromium, retrying a few times because a browser start can fail transiently
    ///     while the machine is busy (for example when other test projects run in parallel).
    /// </summary>
    /// <returns>The skip reason, or <c>null</c> when a launch succeeded.</returns>
    private static async Task<string?> ProbeAsync()
    {
        const int attempts = 3;
        PlaywrightException? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
                return null;
            }
            catch (PlaywrightException ex)
            {
                last = ex;
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        var firstLine = last!.Message.Split('\n')[0].Trim();
        return "Headless Chromium cannot be launched here (" + firstLine + "). Install it once with: " +
               "pwsh <test output folder>/playwright.ps1 install chromium";
    }
}

/// <summary>
///     End-to-end test that runs the real Puppet executable against the real ReviewCenter server in headless Chromium.
/// </summary>
/// <remarks>
///     This test is requirement-linked evidence for the Puppet system requirements for the session commands, the
///     session lifecycle, and error reporting: it runs the real executable, real server, and real headless Chromium.
///     Locally it is skipped with a clear reason when Chromium is not installed. CI installs Chromium and sets
///     <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, which turns a missing or broken browser into a failure so the
///     evidence can never be skipped silently.
/// </remarks>
[Collection("Sequential")]
public class PuppetEndToEndTests
{
    /// <summary>
    ///     The result of one Puppet process run.
    /// </summary>
    /// <param name="ExitCode">The process exit code.</param>
    /// <param name="Output">Standard output.</param>
    /// <param name="Error">Standard error.</param>
    private sealed record PuppetResult(int ExitCode, string Output, string Error);

    /// <summary>
    ///     Runs the Puppet executable as a separate process, the way a developer or coding agent would.
    /// </summary>
    /// <param name="args">The Puppet arguments.</param>
    /// <returns>The exit code and output.</returns>
    private static async Task<PuppetResult> RunPuppetAsync(params string[] args)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "DemaConsulting.ReviewCenter.Puppet.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(dll);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Puppet.");
        var output = process.StandardOutput.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(Xunit.TestContext.Current.CancellationToken);
        return new PuppetResult(process.ExitCode, await output, await error);
    }

    /// <summary>
    ///     Test that the hello and counter buttons of the real page work when driven through Puppet commands.
    /// </summary>
    [Fact]
    public async Task PuppetEndToEnd_HelloAndCounter_DrivenThroughCommands_ShowGreetingAndCount()
    {
        // Arrange: skip with a clear reason when Chromium cannot run here, unless the environment requires it
        var unavailable = await PlaywrightAvailability.GetUnavailableReasonAsync();
        if (unavailable is not null && PlaywrightAvailability.IsChromiumRequired())
        {
            Assert.Fail(PlaywrightAvailability.RequireChromiumVariable + " is true but Chromium is unavailable: " + unavailable);
        }

        Assert.SkipUnless(unavailable is null, unavailable ?? string.Empty);
        var session = "e2e" + Guid.NewGuid().ToString("N")[..8];
        var screenshot = Path.Combine(Path.GetTempPath(), session + ".png");

        try
        {
            // Act: start a session and drive the page
            var start = await RunPuppetAsync("start", "--session", session);
            var navigate = await RunPuppetAsync("goto", "/", "--session", session, "--timeout", "30");
            var hello = await RunPuppetAsync("click", "hello-button", "--session", session);
            var message = await RunPuppetAsync("text", "hello-message", "--session", session);
            await RunPuppetAsync("click", "counter-button", "--session", session);
            await RunPuppetAsync("click", "counter-button", "--session", session);
            var count = await RunPuppetAsync("text", "counter-value", "--session", session);
            var shot = await RunPuppetAsync("screenshot", "--out", screenshot, "--session", session);
            var missing = await RunPuppetAsync("click", "no-such-element", "--session", session, "--timeout", "1");
            var stop = await RunPuppetAsync("stop", "--session", session);
            var list = await RunPuppetAsync("list");

            // Assert: every step worked, errors are reported with exit code 1, and nothing is left behind
            Assert.True(start.ExitCode == 0, start.Error);
            Assert.True(navigate.ExitCode == 0, navigate.Error);
            Assert.Equal(0, hello.ExitCode);
            Assert.Contains("Hello from ReviewCenter!", message.Output, StringComparison.Ordinal);
            Assert.Equal("2", count.Output.Trim());
            Assert.Equal(0, shot.ExitCode);
            Assert.True(new FileInfo(screenshot).Length > 1000, "The screenshot should be a real PNG image.");
            Assert.Equal(1, missing.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(missing.Error), "A failed command should explain the failure on standard error.");
            Assert.Equal(0, stop.ExitCode);
            Assert.DoesNotContain(session, list.Output, StringComparison.Ordinal);
        }
        finally
        {
            // Make sure no daemon, server, or browser survives a failed assertion
            await RunPuppetAsync("stop", "--session", session);
            File.Delete(screenshot);
        }
    }
}
