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
///     Subsystem tests of the Web subsystem that need a layout engine: what the pages look like in a real browser (the
///     width of a diff on a phone, the line comment control on hover, focus, and touch, and the hover fill of the
///     transparent buttons in the dark theme, and the size of the link to a thread), measured on the rendered page
///     instead of read from the stylesheet.
/// </summary>
/// <remarks>
///     The in-memory host of the unit test project serves no static files and has no layout engine, so these tests
///     run the real server and a real browser through the Puppeting library, like the browser system tests. They are
///     skipped locally when Chromium or git is missing, and fail in CI, where <c>REVIEWCENTER_REQUIRE_CHROMIUM=true</c>.
/// </remarks>
[Collection("Sequential")]
public class WebSubsystemBrowserTests
{
    /// <summary>
    ///     The second line of the sample file at the head: a change with a line far wider than a phone, without a
    ///     space to break at.
    /// </summary>
    private static readonly string LongLine = "BETA" + new string('x', 400);

    /// <summary>
    ///     Test that a diff on a phone keeps the page inside the width of the screen, by stacking the two sides of a
    ///     changed line and wrapping a long line inside its cell, while a wide screen shows the sides next to each other.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task WebSubsystem_DiffView_PhoneWidth_StacksSidesAndPageDoesNotScrollSideways()
    {
        // Arrange: a review of a change with a long line, open in the diff view of a wide screen
        var cancellation = TestContext.Current.CancellationToken;
        await using var fixture = await DiffFixture.StartAsync(cancellation);
        var browser = fixture.Browser;
        const string sides =
            "(() => { const pair = [...document.querySelectorAll('.diff-pair')].find(p => p.querySelector('.diff-row.left[data-kind=removed]') && p.querySelector('.diff-row.right[data-kind=added]'));"
            + " const l = pair.querySelector('.diff-row.left').getBoundingClientRect(); const r = pair.querySelector('.diff-row.right').getBoundingClientRect();"
            + " return Math.abs(l.top - r.top) < 1 ? 'side-by-side' : (r.top >= l.bottom - 1 ? 'stacked' : 'overlapping'); })()";
        const string pageFits = "document.documentElement.scrollWidth <= document.documentElement.clientWidth";
        const string longLineFits =
            "[...document.querySelectorAll('[data-testid^=diff-text-]')].every(e => e.getBoundingClientRect().right <= document.documentElement.clientWidth)";
        var wide = await browser.ValueOfAsync(sides);

        // Act: the same page on the screen of a phone
        await browser.SetViewportAsync("390x844");
        await browser.WaitUntilAsync(async () => await browser.ValueOfAsync(sides) != wide, "the layout of the phone");

        // Assert: side by side on the wide screen; on the phone the sides are stacked, the long line wraps inside the
        // screen, and the page does not scroll sideways
        Assert.Equal("side-by-side", wide);
        Assert.Equal("stacked", await browser.ValueOfAsync(sides));
        Assert.Equal("true", await browser.ValueOfAsync(longLineFits));
        Assert.Equal("true", await browser.ValueOfAsync(pageFits));
    }

    /// <summary>
    ///     Test that the <c>+</c> control of a diff line is hidden until its row is pointed at or has keyboard focus,
    ///     is always shown on a screen without hover, and is never taken out of the layout or the tab order.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task WebSubsystem_DiffView_LineCommentControl_ShowsOnHoverFocusAndTouch()
    {
        // Arrange: a review of a change, open in the diff view, with the mouse away from the rows
        var cancellation = TestContext.Current.CancellationToken;
        await using var fixture = await DiffFixture.StartAsync(cancellation);
        var browser = fixture.Browser;
        const string control = "document.querySelector('[data-testid=diff-comment-2]')";
        Task OpacityBecomesAsync(string expected, string what) => browser.WaitUntilAsync(
            async () => await browser.ValueOfAsync($"getComputedStyle({control}).opacity") == expected, what);
        await browser.MouseMoveAsync(0, 0);

        // Act and assert: hidden at rest, but still in the layout and in the tab order
        await OpacityBecomesAsync("0", "the + to be hidden at rest");
        Assert.Equal(
            "true",
            await browser.ValueOfAsync(
                $"(() => {{ const s = getComputedStyle({control}); return s.display !== 'none' && s.visibility !== 'hidden' && {control}.tabIndex >= 0; }})()"));

        // Act and assert: pointing at the row reveals it, and moving away hides it again
        await browser.HoverAsync("diff-row-2");
        await OpacityBecomesAsync("1", "the + to show on hover");
        await browser.MouseMoveAsync(0, 0);
        await OpacityBecomesAsync("0", "the + to hide after the hover");

        // Act and assert: keyboard focus reveals it
        await browser.EvalAsync($"{control}.focus({{ focusVisible: true }})");
        await OpacityBecomesAsync("1", "the + to show with keyboard focus");
        await browser.EvalAsync($"{control}.blur()");
        await OpacityBecomesAsync("0", "the + to hide after the focus");

        // Act and assert: on a touch screen, which cannot hover, it is always shown
        await browser.SetTouchAsync(touch: true);
        await browser.SignInAsync(SystemTestHost.UserName);
        await DiffFixture.OpenDiffAsync(browser);
        Assert.Equal("true", await browser.ValueOfAsync("matchMedia('(hover: none)').matches"));
        await OpacityBecomesAsync("1", "the + to show on a screen without hover");
    }

    /// <summary>
    ///     Test that in the dark theme the line comment control of a diff and the section toggle of the checklist fill
    ///     with the soft accent on hover, not with the hover accent of the other buttons, which left their text close to
    ///     unreadable, and that the section toggle keeps its text color.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task WebSubsystem_TransparentButtons_DarkThemeHover_FillWithTheSoftAccent()
    {
        // Arrange: a review of a change, open in the diff view
        var cancellation = TestContext.Current.CancellationToken;
        await using var fixture = await DiffFixture.StartAsync(cancellation);
        var browser = fixture.Browser;

        // The computed value of a color variable of the theme, read through a probe element
        async Task<string> ThemeColorAsync(string variable, string property) => await browser.ValueOfAsync(
            "(() => { const p = document.createElement('span'); p.style." + property + " = 'var(" + variable + ")';"
            + " document.body.appendChild(p); const v = getComputedStyle(p)." + property + "; p.remove(); return v; })()");

        // Arrange: switch to the dark theme and wait until the page has applied it
        var lightSoftAccent = await ThemeColorAsync("--accent-soft", "backgroundColor");
        await browser.SetColorSchemeAsync(dark: true);
        await browser.WaitUntilAsync(
            async () => await ThemeColorAsync("--accent-soft", "backgroundColor") != lightSoftAccent, "the dark theme");
        var softAccent = await ThemeColorAsync("--accent-soft", "backgroundColor");
        var hoverAccent = await ThemeColorAsync("--accent-hover", "backgroundColor");

        // Hovers an element and returns its computed background once it is the soft accent of the dark theme (the
        // fill fades in), or the last value seen when it never is
        async Task<string> HoverBackgroundAsync(string testId, string selector)
        {
            await browser.MouseMoveAsync(0, 0);
            await browser.HoverAsync(testId);
            var value = string.Empty;
            for (var attempt = 0; attempt < 100 && value != softAccent; attempt++)
            {
                value = await browser.ValueOfAsync($"getComputedStyle(document.querySelector('{selector}')).backgroundColor");
                if (value != softAccent)
                {
                    await Task.Delay(100, cancellation);
                }
            }

            return value;
        }

        // Act: hover the + of a diff line, then the first section toggle of the checklist
        var lineComment = await HoverBackgroundAsync("diff-comment-2", "[data-testid=diff-comment-2]");
        await browser.OpenPageAsync($"/projects/1/reviews/{DiffFixture.ReviewId}/checklist", "checklist-page");
        await browser.WaitUntilAsync(
            async () => await ThemeColorAsync("--accent-soft", "backgroundColor") == softAccent, "the dark theme");
        var toggleId = await browser.ValueOfAsync("document.querySelector('.section-toggle').dataset.testid");
        var toggle = await HoverBackgroundAsync(toggleId, ".section-toggle");
        var toggleText = await browser.ValueOfAsync("getComputedStyle(document.querySelector('.section-toggle')).color");

        // Assert: in the dark theme both fill with the soft accent and not the hover accent, and the toggle keeps the
        // text color
        Assert.NotEqual(lightSoftAccent, softAccent);
        Assert.NotEqual(softAccent, hoverAccent);
        Assert.Equal(softAccent, lineComment);
        Assert.Equal(softAccent, toggle);
        Assert.Equal(await ThemeColorAsync("--text", "color"), toggleText);
    }

    /// <summary>
    ///     Test that the link to a thread is drawn as an action button of the same size as the Copy link button beside
    ///     it, not as plain underlined text.
    /// </summary>
    /// <returns>A task that completes when the test has finished.</returns>
    [Fact]
    public async Task WebSubsystem_ThreadLink_LooksLikeTheActionButtonBesideIt()
    {
        // Arrange: a review of a change, open in the diff view, with a thread started on a line through the page
        var cancellation = TestContext.Current.CancellationToken;
        await using var fixture = await DiffFixture.StartAsync(cancellation);
        var browser = fixture.Browser;
        await browser.ClickAsync("diff-comment-2");
        await browser.WaitForAsync("css=[data-testid=diff-composer] [data-testid=new-thread-form]");
        await browser.FillAsync("thread-text-input", "Why this change?");
        await browser.ClickAsync("start-thread-button");
        await browser.WaitForAsync("css=[data-testid^=thread-link-]");

        // The computed box and text styles of the first element that a selector names
        const string styles =
            "(s => { const e = document.querySelector(s); const c = getComputedStyle(e); const r = e.getBoundingClientRect();"
            + " return [c.display, c.paddingTop, c.paddingRight, c.paddingBottom, c.paddingLeft, c.borderTopWidth,"
            + " c.borderTopStyle, c.fontSize, c.fontWeight, c.lineHeight, c.textDecorationLine, Math.round(r.height)].join(' | '); })";

        // Act: read the styles of the thread link and of the Copy link button of the same thread
        var link = await browser.ValueOfAsync(styles + "('[data-testid^=thread-link-]')");
        var button = await browser.ValueOfAsync(styles + "('[data-testid^=thread-copy-link-]')");

        // Assert: the link has the box, the text, and the height of the button, without an underline
        Assert.Equal(button, link);
        Assert.DoesNotContain("underline", link, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A running server with a review of a git change, and a signed-in browser that shows its diff.
    /// </summary>
    private sealed class DiffFixture : IAsyncDisposable
    {
        /// <summary>
        ///     The identifier of the review.
        /// </summary>
        public const string ReviewId = "diff-review";

        /// <summary>
        ///     The folder of the repository and the git cache.
        /// </summary>
        private readonly SystemTestHost.TemporaryFolder _cleanup;

        /// <summary>
        ///     The server.
        /// </summary>
        private readonly SystemTestHost.TestServer _server;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiffFixture"/> class.
        /// </summary>
        /// <param name="cleanup">The folder of the repository and the git cache.</param>
        /// <param name="server">The server.</param>
        /// <param name="browser">The browser.</param>
        private DiffFixture(SystemTestHost.TemporaryFolder cleanup, SystemTestHost.TestServer server, PuppetBrowser browser)
        {
            _cleanup = cleanup;
            _server = server;
            Browser = browser;
        }

        /// <summary>
        ///     Gets the browser, signed in and showing the diff of the changed file.
        /// </summary>
        public PuppetBrowser Browser { get; }

        /// <summary>
        ///     Creates a git repository with a base tag and a change of the second line to a long line, starts the
        ///     server and a browser, creates the project, the repository, and the review through the pages, and opens
        ///     the diff of the changed file.
        /// </summary>
        /// <param name="cancellation">Cancels the arrangement.</param>
        /// <returns>The fixture.</returns>
        public static async Task<DiffFixture> StartAsync(CancellationToken cancellation)
        {
            if (!await SystemTestHost.IsGitAvailableAsync(cancellation))
            {
                Assert.Skip("git is not available.");
            }

            var directory = Directory.CreateTempSubdirectory("reviewcenter-web-");
            var cleanup = new SystemTestHost.TemporaryFolder(directory);
            SystemTestHost.TestServer? server = null;
            PuppetBrowser? browser = null;
            try
            {
                var repository = Path.Combine(directory.FullName, "repo");
                Directory.CreateDirectory(Path.Combine(repository, "src"));
                await SystemTestHost.RunGitAsync(repository, cancellation, "init", "-b", "main");
                var sample = Path.Combine(repository, "src", "sample.txt");
                await File.WriteAllTextAsync(sample, "alpha\nbeta\ngamma\n", cancellation);
                await SystemTestHost.RunGitAsync(repository, cancellation, "add", "-A");
                await SystemTestHost.RunGitAsync(repository, cancellation, "commit", "-m", "base");
                await SystemTestHost.RunGitAsync(repository, cancellation, "tag", "base");
                await File.WriteAllTextAsync(sample, $"alpha\n{LongLine}\ngamma\n", cancellation);
                await SystemTestHost.RunGitAsync(repository, cancellation, "commit", "-a", "-m", "change");

                server = await SystemTestHost.TestServer.StartAsync(
                    cancellation, seedUser: true, gitCachePath: Path.Combine(directory.FullName, "git-cache"));
                browser = await SystemTestHost.StartBrowserAsync(server.Url);
                await browser.SignInAsync(SystemTestHost.UserName);

                await browser.OpenPageAsync("/projects/new", "new-project-page");
                await browser.FillAsync("project-name-input", "Web Project");
                await browser.ClickAsync("create-project-button");
                await browser.WaitForAsync("project-name");

                await browser.OpenPageAsync("/projects/1/repositories/new", "new-repository-page");
                await browser.FillAsync("new-repository-name-input", "web-repository");
                await browser.FillAsync("new-repository-location-input", repository);
                await browser.ClickAsync("create-repository-button");
                await browser.WaitForAsync("repository-row-1");

                await browser.OpenPageAsync("/projects/1/reviews/new", "new-review-page");
                await browser.FillAsync("review-id-input", ReviewId);
                await browser.FillAsync("review-title-input", "Web review");
                await browser.CheckAsync("source-repository-radio");
                await browser.SelectAsync("repository-select", "1");
                await browser.FillAsync("head-revision-input", "main");
                await browser.FillAsync("baseline-revision-input", "base");
                await browser.ClickAsync("create-review-button");
                await browser.WaitForAsync("css=[data-testid=review-page][data-interactive=true]", 30000);

                await OpenDiffAsync(browser);
                return new DiffFixture(cleanup, server, browser);
            }
            catch
            {
                if (browser is not null)
                {
                    await browser.DisposeAsync();
                }

                server?.Dispose();
                cleanup.Dispose();
                throw;
            }
        }

        /// <summary>
        ///     Opens the review and shows the diff of the changed file.
        /// </summary>
        /// <param name="browser">The signed-in browser.</param>
        /// <returns>A task that completes when the diff is shown.</returns>
        public static async Task OpenDiffAsync(PuppetBrowser browser)
        {
            await browser.OpenPageAsync($"/projects/1/reviews/{ReviewId}", "review-page");
            await browser.ClickAsync("file-select-0");
            await browser.ClickAsync("view-diff-button");
            await browser.WaitForAsync("diff-comment-2");
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            _server.Dispose();
            _cleanup.Dispose();
        }
    }
}
