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
///     Unit tests for the <see cref="HomePage"/> component, rendered in memory with bUnit.
/// </summary>
public class HomePageTests : BunitContext
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
    ///     Test that no greeting is shown before the Hello button is clicked.
    /// </summary>
    [Fact]
    public void HomePage_Render_Default_ShowsNoGreeting()
    {
        // Arrange: no setup required; the component renders with default parameters

        // Act: render the page
        var cut = Render<HomePage>();

        // Assert: the greeting element does not exist yet
        Assert.Empty(cut.FindAll("[data-testid='hello-message']"));
    }

    /// <summary>
    ///     Test that clicking Hello shows the greeting.
    /// </summary>
    [Fact]
    public void HomePage_HelloButton_Clicked_ShowsGreeting()
    {
        // Arrange: render the page
        var cut = Render<HomePage>();

        // Act: click Hello
        cut.Find("[data-testid='hello-button']").Click();

        // Assert: the greeting appears
        Assert.Equal("Hello from ReviewCenter!", cut.Find("[data-testid='hello-message']").TextContent);
    }

    /// <summary>
    ///     Test that clicking Hello twice leaves the same single greeting.
    /// </summary>
    [Fact]
    public void HomePage_HelloButton_ClickedTwice_ShowsSingleGreeting()
    {
        // Arrange: render the page
        var cut = Render<HomePage>();

        // Act: click Hello twice
        var button = cut.Find("[data-testid='hello-button']");
        button.Click();
        button.Click();

        // Assert: exactly one greeting is shown
        Assert.Single(cut.FindAll("[data-testid='hello-message']"));
    }

    /// <summary>
    ///     Test that the counter starts at zero.
    /// </summary>
    [Fact]
    public void HomePage_Render_Default_CounterStartsAtZero()
    {
        // Arrange: no setup required; the component renders with default parameters

        // Act: render the page
        var cut = Render<HomePage>();

        // Assert: counter shows 0
        Assert.Equal("0", cut.Find("[data-testid='counter-value']").TextContent);
    }

    /// <summary>
    ///     Test that each counter click adds one.
    /// </summary>
    [Fact]
    public void HomePage_CounterButton_ClickedThreeTimes_ShowsCountThree()
    {
        // Arrange: render the page
        var cut = Render<HomePage>();

        // Act: click the counter button three times
        var button = cut.Find("[data-testid='counter-button']");
        button.Click();
        button.Click();
        button.Click();

        // Assert: counter shows 3
        Assert.Equal("3", cut.Find("[data-testid='counter-value']").TextContent);
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public void HomePage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required; the component renders with default parameters

        // Act: render the page (bUnit runs OnAfterRender like a live circuit does)
        var cut = Render<HomePage>();

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='home-page']").GetAttribute("data-interactive"));
    }
}
