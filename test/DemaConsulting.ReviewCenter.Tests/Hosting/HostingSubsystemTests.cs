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

using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Subsystem tests for the Hosting subsystem: the server host and the endpoints it exposes.
/// </summary>
public class HostingSubsystemTests
{
    /// <summary>
    ///     Test that a health probe against the running host succeeds.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_HealthEndpoint_Requested_ReturnsHealthy()
    {
        // Arrange: start the host
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: probe health the way an operator or the Puppet tool does
        using var client = app.GetTestClient();
        var response = await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: success status and the agreed body
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(ServerHost.HealthyText, body);
    }

    /// <summary>
    ///     Test that logging configured by the host emits a structured start-up entry and request entries.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Startup_LoggingConfigured_EmitsStructuredEntry()
    {
        // Arrange: start the host and make one request so the framework logs too
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();
        await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: the application's own entry has a named property, and framework logging flows to the same sinks
        var started = Assert.Single(logs.Entries, e => e.Category == typeof(ServerHost).FullName);
        Assert.True(started.Properties.ContainsKey("Urls"), "Start-up entry should carry a Urls property");
        Assert.Contains(logs.Entries, e => e.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                                           && e.Level == LogLevel.Information);
    }

    /// <summary>
    ///     Test that the host serves the home route and the health route from one pipeline.
    /// </summary>
    [Fact]
    public async Task HostingSubsystem_Routes_BothRegistered_HomeAndHealthAnswer()
    {
        // Arrange: start the host
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");
        using var client = app.GetTestClient();

        // Act: request both routes
        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var health = await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: both answer 200
        Assert.Equal(System.Net.HttpStatusCode.OK, home.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
