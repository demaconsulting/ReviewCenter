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
///     Unit tests for the steps-file parsing (<see cref="StepFile"/>) and step execution (<see cref="StepRunner"/>).
/// </summary>
public class StepRunnerTests
{
    /// <summary>
    ///     Test that a steps file with every kind of step is parsed with all of its fields.
    /// </summary>
    [Fact]
    public void StepFile_Parse_AllStepKinds_ReturnsSteps()
    {
        // Arrange: one of each kind, with a comment and a trailing comma, which are tolerated
        const string json = """
            [
              // open the page
              { "kind": "goto", "url": "/" },
              { "kind": "click", "selector": "hello-button" },
              { "kind": "fill", "selector": "#name", "value": "" },
              { "kind": "text", "selector": "hello-message" },
              { "kind": "screenshot", "file": "home", "fullPage": true },
              { "kind": "screenshot" },
            ]
            """;

        // Act: parse it
        var steps = StepFile.Parse(json, "steps.json");

        // Assert: every step has its fields and the screenshot names are completed
        Assert.Equal(6, steps.Count);
        Assert.Equal(new Step(1, "goto", "/", null, null, null, false), steps[0]);
        Assert.Equal(new Step(2, "click", null, "hello-button", null, null, false), steps[1]);
        Assert.Equal(new Step(3, "fill", null, "#name", string.Empty, null, false), steps[2]);
        Assert.Equal(new Step(4, "text", null, "hello-message", null, null, false), steps[3]);
        Assert.Equal(new Step(5, "screenshot", null, null, null, "home.png", true), steps[4]);
        Assert.Equal(new Step(6, "screenshot", null, null, null, "step-6.png", false), steps[5]);
    }

    /// <summary>
    ///     Test that text that is not valid JSON is reported with the file name.
    /// </summary>
    [Fact]
    public void StepFile_Parse_InvalidJson_ThrowsPuppetException()
    {
        // Arrange: truncated JSON
        const string json = "[ { \"kind\": ";

        // Act: parse it
        var exception = Assert.Throws<PuppetException>(() => StepFile.Parse(json, "broken.json"));

        // Assert: the message names the file and the problem
        Assert.Contains("'broken.json' is not valid JSON", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that invalid steps are rejected with a message that names the step and the problem.
    /// </summary>
    /// <param name="json">The steps file text.</param>
    /// <param name="expectedMessage">A fragment of the expected message.</param>
    [Theory]
    [InlineData("""[{ "kind": "jump" }]""", "step 1 (jump): unknown kind")]
    [InlineData("""[{ "kind": "goto", "url": "home" }]""", "step 1 (goto): 'url'")]
    [InlineData("""[{ "kind": "goto", "url": "/" }, { "kind": "click" }]""", "step 2 (click): 'selector'")]
    [InlineData("""[{ "kind": "fill", "selector": "a" }]""", "step 1 (fill): 'value'")]
    [InlineData("""[{ "kind": "screenshot", "file": "../x" }]""", "step 1 (screenshot): 'file'")]
    [InlineData("""[{ "kind": "click", "bogus": "a" }]""", "bogus")]
    [InlineData("[]", "at least one step")]
    public void StepFile_Parse_InvalidSteps_ThrowsPuppetExceptionNamingTheProblem(string json, string expectedMessage)
    {
        // Arrange: invalid step data
        // Act: parse it
        var exception = Assert.Throws<PuppetException>(() => StepFile.Parse(json, "steps.json"));

        // Assert: the message names the problem
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every kind of step is executed in order and reported.
    /// </summary>
    [Fact]
    public async Task StepRunner_Run_AllStepKinds_ExecutesInOrderAndReportsResults()
    {
        // Arrange: a step list covering every kind, a fake page, and an output folder
        using var directory = new TempDirectory();
        var page = new FakeBrowserPage { TextResult = "42" };
        var steps = StepFile.Parse(
            """
            [
              { "kind": "goto", "url": "/page" },
              { "kind": "fill", "selector": "name", "value": "Ada" },
              { "kind": "click", "selector": "#go" },
              { "kind": "text", "selector": "answer" },
              { "kind": "screenshot", "file": "shot" }
            ]
            """,
            "steps.json");
        var results = new List<StepResult>();

        // Act: run them
        var allOk = await StepRunner.RunAsync(steps, page, "http://localhost:5000/", directory.Path, 5000, results.Add, TestContext.Current.CancellationToken);

        // Assert: browser calls are in order with resolved selectors and the joined URL
        Assert.True(allOk);
        Assert.Equal(
            [
                "goto http://localhost:5000/page",
                "fill [data-testid=\"name\"] Ada",
                "click #go",
                "text [data-testid=\"answer\"]",
                "screenshot fullPage=False"
            ],
            page.Calls);

        // Assert: one successful result per step, carrying the text and the screenshot path, and the PNG was written
        Assert.Equal([1, 2, 3, 4, 5], results.Select(r => r.Step));
        Assert.All(results, r => Assert.True(r.Ok));
        Assert.Equal("42", results[3].Text);
        Assert.Equal(Path.Combine(directory.Path, "shot.png"), results[4].File);
        Assert.True(File.Exists(results[4].File));
    }

    /// <summary>
    ///     Test that a failed step stops the run and produces a failure screenshot.
    /// </summary>
    [Fact]
    public async Task StepRunner_Run_StepFails_StopsAndCapturesFailureScreenshot()
    {
        // Arrange: a click that fails on the second step
        using var directory = new TempDirectory();
        var page = new FakeBrowserPage { FailClickOn = "missing" };
        var steps = StepFile.Parse(
            """[{ "kind": "goto", "url": "/" }, { "kind": "click", "selector": "missing" }, { "kind": "text", "selector": "later" }]""",
            "steps.json");
        var results = new List<StepResult>();

        // Act: run them
        var allOk = await StepRunner.RunAsync(steps, page, "http://localhost:5000", directory.Path, 5000, results.Add, TestContext.Current.CancellationToken);

        // Assert: the run stopped at the failure, which carries the error and a failure screenshot
        Assert.False(allOk);
        Assert.Equal(2, results.Count);
        Assert.False(results[1].Ok);
        Assert.Contains("missing", results[1].Error, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(directory.Path, "failure-step-2.png"), results[1].FailureScreenshot);
        Assert.True(File.Exists(results[1].FailureScreenshot));
        Assert.DoesNotContain(page.Calls, c => c.StartsWith("text", StringComparison.Ordinal));
    }
}
