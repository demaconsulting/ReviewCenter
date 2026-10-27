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

using System.Collections.Concurrent;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     A captured log entry: the category, level, rendered message, and structured properties.
/// </summary>
/// <param name="Category">Logger category name.</param>
/// <param name="Level">Severity of the entry.</param>
/// <param name="Message">The fully rendered message text.</param>
/// <param name="Properties">The named structured properties, including <c>{OriginalFormat}</c>.</param>
internal sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>
///     Logger provider that records every entry in memory so tests can assert on structured logging.
/// </summary>
/// <remarks>
///     Thread-safe: entries are appended to a concurrent collection from whatever thread logs.
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    /// <summary>
    ///     The entries captured so far.
    /// </summary>
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    /// <summary>
    ///     Gets a snapshot of the captured entries in the order they were written.
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

    /// <summary>
    ///     Creates a logger that records into this provider.
    /// </summary>
    /// <param name="categoryName">Category the logger writes under.</param>
    /// <returns>The capturing logger.</returns>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    /// <summary>
    ///     Releases the provider; nothing to release because entries are plain memory.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    ///     Logger that appends to the owning provider's queue.
    /// </summary>
    /// <param name="category">Category name recorded with each entry.</param>
    /// <param name="entries">Destination queue.</param>
    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        /// <summary>
        ///     Scopes are not needed by the tests, so none is created.
        /// </summary>
        /// <typeparam name="TState">Scope state type.</typeparam>
        /// <param name="state">Scope state (ignored).</param>
        /// <returns>Always <c>null</c>.</returns>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <summary>
        ///     Reports every level as enabled so nothing is filtered out of the capture.
        /// </summary>
        /// <param name="logLevel">Level being queried.</param>
        /// <returns>Always <c>true</c>.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        ///     Records one entry, extracting the structured properties when the state provides them.
        /// </summary>
        /// <typeparam name="TState">State type.</typeparam>
        /// <param name="logLevel">Severity.</param>
        /// <param name="eventId">Event id (ignored).</param>
        /// <param name="state">The log state; structured states expose key/value pairs.</param>
        /// <param name="exception">Exception (ignored).</param>
        /// <param name="formatter">Renders the state to text.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    properties[pair.Key] = pair.Value;
                }
            }

            entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception), properties));
        }
    }
}

/// <summary>
///     Starts the real ReviewCenter pipeline on an in-memory test server.
/// </summary>
internal static class TestWebApp
{
    /// <summary>
    ///     Builds the application through <see cref="ServerHost.CreateApplication"/> with the
    ///     in-memory server and a capturing log provider, then starts it.
    /// </summary>
    /// <param name="logs">Receives the capturing provider attached to the application.</param>
    /// <param name="arguments">Command-line arguments used to create the context.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    public static async Task<WebApplication> StartAsync(CapturingLoggerProvider logs, params string[] arguments)
    {
        return await StartAsync(logs, connectionString: null, arguments);
    }

    /// <summary>
    ///     Builds and starts the application like <see cref="StartAsync(CapturingLoggerProvider, string[])"/>
    ///     but against a specific database.
    /// </summary>
    /// <param name="logs">Receives the capturing provider attached to the application.</param>
    /// <param name="connectionString">
    ///     Database connection string, or <c>null</c> to use a new temporary database for this application.
    /// </param>
    /// <param name="arguments">Command-line arguments used to create the context.</param>
    /// <param name="settings">Extra configuration values (for example <c>Identity:Throttle:MaxFailuresPerName</c>); <c>null</c> for none.</param>
    /// <param name="configureServices">Changes to the services, applied after the application registered its own; <c>null</c> for none.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    public static async Task<WebApplication> StartAsync(
        CapturingLoggerProvider logs,
        string? connectionString,
        string[] arguments,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        using var context = Context.Create(arguments);
        var app = ServerHost.CreateApplication(context, new ServerHostOptions
        {
            // The in-memory host has no static web asset manifest of its own
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Logging.AddProvider(logs);
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Every in-memory application gets its own database so parallel tests never share a file
                    ["Database:ConnectionString"] = connectionString ?? TestDatabase.NewConnectionString()
                });
                if (settings is not null)
                {
                    builder.Configuration.AddInMemoryCollection(settings);
                }

                configureServices?.Invoke(builder.Services);
            }
        });
        await app.StartAsync();
        return app;
    }
}
