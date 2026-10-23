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

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     End-to-end test that runs a steps file against the real ReviewCenter server in real headless Chromium.
/// </summary>
/// <remarks>
///     This test is requirement-linked evidence for the Puppet system requirements. Locally it is skipped
///     with a clear reason when Chromium is not installed. CI installs Chromium and sets
///     <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, which turns a missing or broken browser into a failure so
///     the evidence can never be skipped silently.
/// </remarks>
[Collection("Sequential")]
public class PuppetEndToEndTests
{
    /// <summary>
    ///     The environment variable that turns a missing Chromium from a skip into a failure.
    /// </summary>
    private const string RequireChromiumVariable = "REVIEWCENTER_REQUIRE_CHROMIUM";

    /// <summary>
    ///     Test that a steps file driving the hello page and the counter shows the greeting and the count.
    /// </summary>
    [Fact]
    public async Task PuppetEndToEnd_StepsFile_HelloAndCounter_WritesScreenshotAndReportsText()
    {
        // Arrange: the steps file of the Puppet README, an output folder, and the real services
        using var directory = new TempDirectory();
        var steps = directory.WriteFile(
            "steps.json",
            """
            [
              { "kind": "goto", "url": "/" },
              { "kind": "click", "selector": "hello-button" },
              { "kind": "text", "selector": "hello-message" },
              { "kind": "click", "selector": "counter-button" },
              { "kind": "click", "selector": "counter-button" },
              { "kind": "text", "selector": "counter-value" },
              { "kind": "screenshot", "file": "home.png" }
            ]
            """);
        var output = new StringWriter();
        var error = new StringWriter();

        // Act: run it with a real server process and a real browser
        var exitCode = await PuppetProgram.RunAsync(
            ["run", steps, "--out", directory.Path, "--timeout", "30"],
            output,
            error,
            PuppetServices.CreateDefault(),
            TestContext.Current.CancellationToken);

        // Assert: a missing browser is a skip locally and a failure where Chromium is required
        if (exitCode == 2 && error.ToString().Contains("Chromium", StringComparison.Ordinal))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable(RequireChromiumVariable), "true", StringComparison.OrdinalIgnoreCase),
                $"{RequireChromiumVariable} is true but Chromium is unavailable: {error}");
            Assert.Skip(error.ToString());
        }

        // Assert: every step worked, the page showed the greeting and the count, and the screenshot is a PNG
        Assert.True(exitCode == 0, $"Exit code {exitCode}.{Environment.NewLine}{output}{error}");
        var results = output.ToString();
        Assert.Contains("\"text\":\"Hello from ReviewCenter!\"", results, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"2\"", results, StringComparison.Ordinal);
        var png = await File.ReadAllBytesAsync(Path.Combine(directory.Path, "home.png"), TestContext.Current.CancellationToken);
        Assert.True(png.Length > 1000);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
    }
}
