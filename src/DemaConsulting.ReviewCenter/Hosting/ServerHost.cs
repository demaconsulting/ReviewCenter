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
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     Composition root for the ReviewCenter web server: builds, configures, and runs the ASP.NET
///     Core application behind the <c>serve</c> verb.
/// </summary>
/// <remarks>
///     A single composition root keeps service registration, logging, and endpoint mapping in one
///     reviewable place, and the <see cref="CreateApplication"/> seam lets tests exercise exactly
///     the pipeline that ships. The class is stateless and therefore thread-safe; every call builds
///     an independent application instance.
/// </remarks>
internal static class ServerHost
{
    /// <summary>
    ///     The route of the health endpoint used by operators, load balancers, and the Puppet tool
    ///     to find out whether the server is ready to serve requests.
    /// </summary>
    public const string HealthPath = "/health";

    /// <summary>
    ///     The body returned by the health endpoint when the server is running.
    /// </summary>
    public const string HealthyText = "Healthy";

    /// <summary>
    ///     Builds the web application for the supplied command-line context without starting it.
    /// </summary>
    /// <param name="context">
    ///     Parsed command-line context; supplies the listen URL and whether console logging is
    ///     suppressed. Must not be null.
    /// </param>
    /// <param name="options">
    ///     Optional build adjustments; <c>null</c> selects the production defaults.
    /// </param>
    /// <returns>A configured, not yet started <see cref="WebApplication"/>. The caller owns and must dispose it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     Building is separated from running so tests can start the application against an
    ///     in-memory server. Building has no side effects outside the returned application: nothing
    ///     is bound to a network port until the application is started.
    /// </remarks>
    public static WebApplication CreateApplication(Context context, ServerHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        options ??= new ServerHostOptions();

        // Anchor the content root at the application directory rather than the working directory so
        // the server finds its static web asset manifest no matter where the operator launches it
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ServerHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        // Listen where the operator asked; the default is loopback-only (see Context.DefaultUrls)
        builder.WebHost.UseUrls(context.Urls);

        // Structured logging: Microsoft.Extensions.Logging message templates with named
        // placeholders. Silent mode removes the console provider so that --silent holds for the
        // server just as it does for every other verb
        builder.Logging.ClearProviders();
        if (!context.Silent)
        {
            builder.Logging.AddConsole();
        }

        // Blazor Server: server-side Razor components rendered over an interactive circuit
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // Allow the caller (tests) to replace the server or add logger providers
        options.ConfigureBuilder?.Invoke(builder);

        // Outside the Development environment ASP.NET Core does not load the static web asset
        // manifest on its own, which would leave the Blazor client script empty when the server is
        // started with 'dotnet <dll>'. Load it explicitly (a no-op when the manifest is absent)
        if (options.MapStaticAssets)
        {
            builder.WebHost.UseStaticWebAssets();
        }

        var app = builder.Build();

        // Antiforgery must run before the Razor component endpoints it protects
        app.UseAntiforgery();

        // Serve the Blazor client script and other static web assets; skipped only when the host
        // has no asset manifest (in-memory test servers)
        if (options.MapStaticAssets)
        {
            app.MapStaticAssets();
        }

        // Liveness endpoint: plain-text 200 so that it is trivial for any probe to consume
        app.MapGet(HealthPath, () => Results.Text(HealthyText));

        // The UI: every page lives under the App root component
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // Emit one structured start-up entry once the server is accepting requests
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ServerHost).FullName!);
        var urls = context.Urls;
        app.Lifetime.ApplicationStarted.Register(() => LogStarted(logger, urls));

        return app;
    }

    /// <summary>
    ///     Builds and runs the web server until it is stopped.
    /// </summary>
    /// <param name="context">Parsed command-line context. Must not be null.</param>
    /// <param name="cancellationToken">
    ///     Requests a graceful shutdown when cancelled; Ctrl+C / SIGTERM also stop the server.
    /// </param>
    /// <returns>
    ///     0 when the server ran and shut down cleanly; 1 when it could not start (for example
    ///     because the port is already in use), in which case the reason was reported through
    ///     <see cref="Context.WriteError"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     Start-up failures caused by the environment (an address that is in use or not bindable)
    ///     are expected operator errors and are reported in the same way as bad arguments, instead
    ///     of surfacing as an unhandled exception with a stack trace. Side effects: binds a network
    ///     port and writes log entries for the lifetime of the call.
    /// </remarks>
    public static async Task<int> RunAsync(Context context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var app = CreateApplication(context);
        try
        {
            await app.RunAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancellation that arrives before start-up completes is still a requested shutdown
            return 0;
        }
        catch (IOException ex)
        {
            // Kestrel reports an unavailable address as an IOException
            context.WriteError($"Failed to start the server on {context.Urls}: {ex.Message}");
            return 1;
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>
    ///     Writes the structured "server started" log entry.
    /// </summary>
    /// <param name="logger">Logger to write to.</param>
    /// <param name="urls">The URL the server was asked to listen on; becomes the <c>Urls</c> log property.</param>
    private static void LogStarted(ILogger logger, string urls)
    {
        // A message template with a named placeholder (not string interpolation) keeps the URL
        // available as a structured property for any log sink that understands them
#pragma warning disable CA1848 // Single start-up entry; a LoggerMessage delegate adds no value here
        logger.LogInformation("ReviewCenter server started and listening on {Urls}", urls);
#pragma warning restore CA1848
    }
}
