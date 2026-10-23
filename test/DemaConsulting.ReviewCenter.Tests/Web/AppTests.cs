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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the App unit: the HTML shell (<c>App.razor</c>) and the router (<c>Routes.razor</c>).
/// </summary>
public class AppTests : BunitContext
{
    /// <summary>
    ///     Test that the HTML shell declares a base href, the head outlet output, and the Blazor client script.
    /// </summary>
    [Fact]
    public async Task App_Render_ServedAtRoot_ContainsShellElements()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the root document
        var html = await client.GetStringAsync("/", Xunit.TestContext.Current.CancellationToken);

        // Assert: shell elements are present
        Assert.Contains("<base href=\"/\">", html);
        Assert.Contains("<title>ReviewCenter</title>", html);
        Assert.Contains("_framework/blazor.web.js", html);
    }

    /// <summary>
    ///     Test that the router renders the home page for the root URL.
    /// </summary>
    [Fact]
    public void Routes_Render_RootUrl_RendersHomePage()
    {
        // Arrange: navigate to the root URL
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the home page is shown
        Assert.NotEmpty(cut.FindAll("[data-testid='home-page']"));
    }

    /// <summary>
    ///     Test that the router shows a not-found message for an unknown URL.
    /// </summary>
    [Fact]
    public void Routes_Render_UnknownUrl_ShowsNotFoundMessage()
    {
        // Arrange: navigate to a URL no page handles
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/does-not-exist");

        // Act: render the router
        var cut = Render<Routes>();

        // Assert: the not-found message is shown
        Assert.Contains("Page not found.", cut.Markup);
    }
}
