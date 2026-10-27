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
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="HomePage"/> component, rendered in memory with bUnit.
/// </summary>
public class HomePageTests : PageTestBase
{
    /// <summary>
    ///     Test that the page shows the product title heading and sets the document title.
    /// </summary>
    [Fact]
    public void HomePage_Render_Default_ShowsTitle()
    {
        // Arrange: no setup required; the component renders with default parameters

        // Act: render the page
        var cut = Render<HomePage>();

        // Assert: heading text is present
        Assert.Equal("ReviewCenter", cut.Find("h1").TextContent);
    }

    /// <summary>
    ///     Test that an anonymous visitor sees the tagline and no links.
    /// </summary>
    [Fact]
    public void HomePage_Render_Anonymous_ShowsTaglineAndNoLinks()
    {
        // Arrange: nobody is signed in
        SignOut();

        // Act: render the page
        var cut = Render<HomePage>();

        // Assert: the tagline is shown and nothing to follow
        Assert.Equal("Web-based code review for regulated projects.", cut.Find("p").TextContent);
        Assert.Empty(cut.FindAll("a"));
        Assert.Empty(cut.FindAll("[data-testid='home-get-started']"));
    }

    /// <summary>
    ///     Test that a signed-in reviewer sees the Get started section with only the Projects link.
    /// </summary>
    [Fact]
    public void HomePage_Render_Reviewer_ShowsGetStartedWithProjectsOnly()
    {
        // Arrange: a reviewer is signed in
        SignInAs("bea", "Bea");

        // Act: render the page
        var cut = Render<HomePage>();

        // Assert: one link, to the projects page
        Assert.Equal("/projects", cut.Find("[data-testid='home-card-projects']").GetAttribute("href"));
        Assert.Single(cut.FindAll("a"));
    }

    /// <summary>
    ///     Test that a signed-in administrator also sees the repositories, audit and users links.
    /// </summary>
    [Fact]
    public void HomePage_Render_Admin_ShowsGetStartedWithAdministrationLinks()
    {
        // Arrange: an administrator is signed in
        SignInAs("root", "Root", UserRole.Admin);

        // Act: render the page
        var cut = Render<HomePage>();

        // Assert: all four links, each to its page
        Assert.Equal(4, cut.FindAll("a").Count);
        Assert.Equal("/repositories", cut.Find("[data-testid='home-card-repositories']").GetAttribute("href"));
        Assert.Equal("/audit", cut.Find("[data-testid='home-card-audit']").GetAttribute("href"));
        Assert.Equal("/users", cut.Find("[data-testid='home-card-users']").GetAttribute("href"));
    }
}
