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

using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Hosting;

/// <summary>
///     Unit tests for the <see cref="ServerHost"/> composition root.
/// </summary>
public class ServerHostTests
{
    /// <summary>
    ///     Test that the health route answers 200 with the body "Healthy".
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_HealthRoute_Returns200Healthy()
    {
        // Arrange: start the real pipeline on an in-memory server
        var logs = new CapturingLoggerProvider();
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Act: request the health route
        using var client = app.GetTestClient();
        var response = await client.GetAsync(ServerHost.HealthPath, TestContext.Current.CancellationToken);

        // Assert: 200 and the plain-text body
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that start-up writes one structured entry carrying the listen URL as a property.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_StartupLogging_EmitsStructuredEntry()
    {
        // Arrange: create a capturing logger provider
        var logs = new CapturingLoggerProvider();

        // Act: start the pipeline with an explicit URL
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--urls", "http://127.0.0.1:6123", "--silent");

        // Assert: the entry uses a message template with a named Urls property, not a pre-formatted string
        var entry = Assert.Single(logs.Entries, e => e.Category == typeof(ServerHost).FullName);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("http://127.0.0.1:6123", entry.Properties["Urls"]);
        Assert.Contains("{Urls}", (string)entry.Properties["{OriginalFormat}"]!);
        Assert.Contains("http://127.0.0.1:6123", entry.Message);
    }

    /// <summary>
    ///     Test that silent mode leaves the console logger provider out.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_Silent_OmitsConsoleProvider()
    {
        // Arrange: create a capturing logger provider
        var logs = new CapturingLoggerProvider();

        // Act: start the pipeline silently
        await using var app = await TestWebApp.StartAsync(logs, "serve", "--silent");

        // Assert: no console provider is registered
        var providers = app.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().Name);
        Assert.DoesNotContain("ConsoleLoggerProvider", providers);
    }

    /// <summary>
    ///     Test that without silent mode the console logger provider is registered.
    /// </summary>
    [Fact]
    public async Task ServerHost_CreateApplication_NotSilent_IncludesConsoleProvider()
    {
        // Arrange: create a capturing logger provider
        var logs = new CapturingLoggerProvider();

        // Act: start the pipeline with console logging on
        await using var app = await TestWebApp.StartAsync(logs, "serve");

        // Assert: the console provider is registered
        var providers = app.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().Name);
        Assert.Contains("ConsoleLoggerProvider", providers);
    }

    /// <summary>
    ///     Test that a null context is rejected.
    /// </summary>
    [Fact]
    public void ServerHost_CreateApplication_NullContext_ThrowsArgumentNullException()
    {
        // Arrange: no setup required; null is the input under test

        // Act: create the application with a null context
        var exception = Record.Exception(() => ServerHost.CreateApplication(null!));

        // Assert: an ArgumentNullException is thrown
        Assert.IsType<ArgumentNullException>(exception);
    }
}
