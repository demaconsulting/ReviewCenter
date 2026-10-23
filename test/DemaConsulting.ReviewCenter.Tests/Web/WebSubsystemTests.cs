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

using Microsoft.AspNetCore.TestHost;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Subsystem tests for the Web subsystem: the shell, router, and home page working together.
/// </summary>
public class WebSubsystemTests
{
    /// <summary>
    ///     Test that requesting the home route renders the title and all interactive controls.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_HomeRoute_Requested_RendersTitleAndControls()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the home route
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: 200, the document title, and every control the UI tests drive
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>ReviewCenter</title>", html);
        Assert.Contains("data-testid=\"hello-button\"", html);
        Assert.Contains("data-testid=\"counter-button\"", html);
        Assert.Contains("data-testid=\"counter-value\"", html);
        Assert.DoesNotContain("data-testid=\"hello-message\"", html);
    }

    /// <summary>
    ///     Test that the first server-rendered response is not yet marked interactive.
    /// </summary>
    [Fact]
    public async Task WebSubsystem_HomeRoute_InitialResponse_IsNotYetInteractive()
    {
        // Arrange: start the real pipeline in memory
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request the home route (no circuit exists for a plain HTTP request)
        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        // Assert: drivers must wait for the marker to flip before clicking
        Assert.Contains("data-interactive=\"false\"", html);
    }
}
