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

using Bunit;
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="AboutPage"/> component, rendered in memory with bUnit.
/// </summary>
public class AboutPageTests : PageTestBase
{
    /// <summary>
    ///     Test that the page shows the full version, the copyright and the link to the project, to an anonymous visitor.
    /// </summary>
    [Fact]
    public void AboutPage_Render_Anonymous_ShowsVersionCopyrightAndProjectLink()
    {
        // Arrange: nobody is signed in
        SignOut();

        // Act: render the page
        var cut = Render<AboutPage>();

        // Assert: the version is the one the command line prints, the copyright is named, and the project is linked
        Assert.Equal(Program.Version, cut.Find("[data-testid='about-version']").TextContent);
        Assert.Contains("DEMA Consulting", cut.Find("[data-testid='about-page']").TextContent, StringComparison.Ordinal);
        Assert.Equal("https://github.com/demaconsulting/ReviewCenter", cut.Find("[data-testid='about-github-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that the page says in plain words what ReviewCenter does.
    /// </summary>
    [Fact]
    public void AboutPage_Render_Default_DescribesWhatReviewCenterDoes()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = Render<AboutPage>();

        // Assert: the main features are named
        var text = cut.Find("[data-testid='about-page']").TextContent;
        Assert.Contains("threaded comments", text, StringComparison.Ordinal);
        Assert.Contains("checklist", text, StringComparison.Ordinal);
        Assert.Contains("audit trail", text, StringComparison.Ordinal);
    }
}
