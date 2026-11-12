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

using DemaConsulting.ReviewCenter.Puppeting;

namespace DemaConsulting.ReviewCenter.SystemTests;

/// <summary>
///     System tests that drive the real pages of the real server in a real browser: the live connection of the pages,
///     the browser operations the system tests rely on, and the hover-revealed controls of a review diff.
/// </summary>
/// <remarks>
///     The browser runs in-process through the Puppeting library. Like the other system tests these tests are skipped
///     locally when Chromium (or git) is missing, and fail in CI, where <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>, so the
///     evidence cannot be skipped silently.
/// </remarks>
[Collection("Sequential")]
public class BrowserSystemTests
{
    /// <summary>
    ///     Test that a real browser loads the pages of the real server, hovers, fills, clicks, and reads them, that a
    ///     form submitted over the live connection of a page reaches the server, that the emulated color scheme of the
    ///     operating system applies, and that a screenshot is a PNG.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task ReviewCenter_RealBrowser_SignInCreateProjectAndTheme_WorksOverTheLiveConnection()
    {
        // Arrange: the real server with one administrator, a browser, and a private screenshot folder
        var cancellation = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("reviewcenter-browser-");
        using var cleanup = new SystemTestHost.TemporaryFolder(directory);
        using var server = await SystemTestHost.TestServer.StartAsync(cancellation, seedUser: true);
        await using var browser = await SystemTestHost.StartBrowserAsync(server.Url);

        // Act and assert: the home page loads and shows the sign-in link of the header (nobody is signed in)
        Assert.Equal("HTTP 200", await browser.GotoAsync("/"));
        Assert.True(await browser.ExistsAsync("signin-link"));

        // Act and assert: a hover stays until the mouse moves
        const string isHovered = "document.querySelector('[data-testid=signin-link]').matches(':hover')";
        await browser.HoverAsync("signin-link");
        Assert.Equal("true", await browser.EvalAsync(isHovered));
        await browser.MouseMoveAsync(0, 0);
        Assert.Equal("false", await browser.EvalAsync(isHovered));

        // Act and assert: a wrong password shows its message; the right password signs in
        await browser.GotoAsync("/login");
        await browser.FillAsync("login-username-input", "nobody");
        await browser.FillAsync("login-password-input", "wrong-password");
        await browser.ClickAsync("login-button");
        await browser.WaitForAsync("login-error");
        Assert.StartsWith("Invalid user name or password.", await browser.TextAsync("login-error"), StringComparison.Ordinal);
        await browser.SignInAsync(SystemTestHost.UserName);
        Assert.StartsWith("Puppet Admin", await browser.TextAsync("current-user"), StringComparison.Ordinal);

        // Act and assert: the header leads to the projects page, and a form submitted over the live connection of the
        // new project page creates the project
        await browser.ClickAsync("projects-link");
        await browser.WaitForAsync("css=[data-testid=projects-page][data-interactive=true]");
        await browser.ClickAsync("new-project-button");
        await browser.WaitForAsync("css=[data-testid=new-project-page][data-interactive=true]");
        await browser.FillAsync("project-name-input", "Browser Project");
        await browser.ClickAsync("create-project-button");
        await browser.WaitForAsync("project-name");
        Assert.StartsWith("Browser Project", await browser.TextAsync("project-name"), StringComparison.Ordinal);

        // Act and assert: the emulated color scheme of the operating system applies to the page
        await browser.SetColorSchemeAsync(dark: true);
        Assert.Equal("true", await browser.EvalAsync("matchMedia('(prefers-color-scheme: dark)').matches"));
        await browser.SetColorSchemeAsync(dark: false);
        Assert.Equal("false", await browser.EvalAsync("matchMedia('(prefers-color-scheme: dark)').matches"));

        // Act and assert: the screenshot of the page and of one element is a PNG
        var shot = Path.Combine(directory.FullName, "project.png");
        await browser.ScreenshotAsync(shot);
        var png = await File.ReadAllBytesAsync(shot, cancellation);
        Assert.True(png.Length > 1000);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        var elementShot = Path.Combine(directory.FullName, "name.png");
        await browser.ScreenshotAsync(elementShot, selector: "project-name");
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], (await File.ReadAllBytesAsync(elementShot, cancellation))[..4]);

        // Act and assert: a missing element fails fast with a message that names it
        var missing = await Assert.ThrowsAsync<PuppetingException>(() => browser.ClickAsync("no-such-element"));
        Assert.Contains("no-such-element", missing.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a hover-revealed control of a real review diff is reached in a real browser: the <c>+</c> of a
    ///     diff line is hidden until the mouse is on its row, hovering the row reveals it, and clicking it opens the
    ///     comment composer.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task PuppetEndToEnd_ReviewDiff_HoverRevealsLineCommentAndClickOpensComposer_WorksAgainstRealServer()
    {
        // Arrange: a local git repository with a base tag and one changed line; the test needs git, as CI has it
        var cancellation = TestContext.Current.CancellationToken;
        if (!await SystemTestHost.IsGitAvailableAsync(cancellation))
        {
            Assert.Skip("git is not available.");
        }

        var directory = Directory.CreateTempSubdirectory("reviewcenter-diff-");
        using var cleanup = new SystemTestHost.TemporaryFolder(directory);
        var repository = Path.Combine(directory.FullName, "repo");
        var gitCache = Path.Combine(directory.FullName, "git-cache");
        Directory.CreateDirectory(repository);
        await SystemTestHost.RunGitAsync(repository, cancellation, "init", "-b", "main");
        Directory.CreateDirectory(Path.Combine(repository, "src"));
        var sample = Path.Combine(repository, "src", "sample.txt");
        await File.WriteAllTextAsync(sample, "alpha\nbeta\ngamma\n", cancellation);
        await SystemTestHost.RunGitAsync(repository, cancellation, "add", "-A");
        await SystemTestHost.RunGitAsync(repository, cancellation, "commit", "-m", "base");
        await SystemTestHost.RunGitAsync(repository, cancellation, "tag", "base");
        await File.WriteAllTextAsync(sample, "alpha\nBETA\ngamma\n", cancellation);
        await SystemTestHost.RunGitAsync(repository, cancellation, "commit", "-a", "-m", "change");

        using var server = await SystemTestHost.TestServer.StartAsync(cancellation, seedUser: true, gitCachePath: gitCache);
        await using var browser = await SystemTestHost.StartBrowserAsync(server.Url);

        // The computed opacity of the + of the added line (the 0 to 1 transition takes a moment, so poll briefly)
        async Task<bool> OpacityBecomesAsync(string expected)
        {
            const string opacity = "getComputedStyle(document.querySelector('[data-testid=diff-comment-2]')).opacity";
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await browser.ValueOfAsync(opacity) == expected)
                {
                    return true;
                }

                await Task.Delay(100, cancellation);
            }

            return false;
        }

        // Act: sign in, register the repository, create a project and a review of the change
        await browser.SignInAsync(SystemTestHost.UserName);

        await browser.GotoAsync("/projects/new");
        await browser.WaitForAsync("css=[data-testid=new-project-page][data-interactive=true]");
        await browser.FillAsync("project-name-input", "Puppet Project");
        await browser.ClickAsync("create-project-button");
        await browser.WaitForAsync("project-name");

        await browser.GotoAsync("/projects/1/repositories/new");
        await browser.WaitForAsync("css=[data-testid=new-repository-page][data-interactive=true]");
        await browser.FillAsync("new-repository-name-input", "puppet-repository");
        await browser.FillAsync("new-repository-location-input", repository);
        await browser.ClickAsync("create-repository-button");
        await browser.WaitForAsync("repository-row-1");

        await browser.GotoAsync("/projects/1/reviews/new");
        await browser.WaitForAsync("css=[data-testid=new-review-page][data-interactive=true]");
        await browser.FillAsync("review-id-input", "diff-review");
        await browser.FillAsync("review-title-input", "Diff review");
        await browser.CheckAsync("source-repository-radio");
        await browser.SelectAsync("repository-select", "1");
        await browser.FillAsync("head-revision-input", "main");
        await browser.FillAsync("baseline-revision-input", "base");
        await browser.ClickAsync("create-review-button");
        await browser.WaitForAsync("css=[data-testid=review-page][data-interactive=true]", 30000);

        // Act and assert: the changed file shows its diff, and the + of the added line is hidden at first
        await browser.ClickAsync("file-select-0");
        await browser.ClickAsync("view-diff-button");
        await browser.WaitForAsync("diff-view");
        await browser.WaitForAsync("diff-comment-2");
        await browser.MouseMoveAsync(0, 0);
        Assert.True(await OpacityBecomesAsync("0"), "The + of a diff line must be hidden until its row is pointed at.");

        // Act and assert: pointing at the row reveals the +, and the hover persists until the next mouse operation
        await browser.HoverAsync("diff-row-2");
        Assert.True(await OpacityBecomesAsync("1"), "Hovering a diff row must reveal its + control.");

        // Act and assert: clicking the revealed + opens the comment composer under the line
        await browser.ClickAsync("diff-comment-2");
        await browser.WaitForAsync("css=[data-testid=diff-composer] [data-testid=new-thread-form]");
    }
}
