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

using System.Data.Common;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the configured database cannot be prepared (see <see cref="InitializeDatabase"/>).
    /// </exception>
    /// <remarks>
    ///     Building is separated from running so tests can start the application against an
    ///     in-memory server. Building creates the database (folder, file and schema) when it does
    ///     not exist yet; nothing is bound to a network port until the application is started.
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

        // Persistence: the database is chosen by the 'Database' configuration section (appsettings.json or
        // environment variables). Options are read lazily so that callers adjusting the configuration in
        // ConfigureBuilder take effect, and the Core services are shared by all browser sessions
        builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection(DatabaseSetup.SectionName));
        builder.Services.AddDbContextFactory<ReviewCenterDbContext>((services, dbOptions) =>
            DatabaseSetup.Configure(dbOptions, services.GetRequiredService<IOptions<DatabaseOptions>>().Value));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IReviewStore, ReviewStore>();
        builder.Services.AddSingleton<ProjectService>();
        builder.Services.AddSingleton<ReviewService>();
        builder.Services.AddSingleton<ThreadService>();

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

        // Create the database folder and schema now, so that a configuration or file system problem stops the
        // start-up with a clear message instead of failing on the first page request
        InitializeDatabase(app);

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
    /// <param name="options">
    ///     Optional build adjustments passed to <see cref="CreateApplication"/>; <c>null</c> selects the
    ///     production defaults. Tests use it to point the server at a temporary database.
    /// </param>
    /// <returns>
    ///     0 when the server ran and shut down cleanly; 1 when it could not start (for example
    ///     because the port is already in use or the database could not be prepared), in which case
    ///     the reason was reported through <see cref="Context.WriteError"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     Start-up failures caused by the environment (an address that is in use or not bindable, an
    ///     unsupported or unusable database) are expected operator errors and are reported in the same
    ///     way as bad arguments, instead of surfacing as an unhandled exception with a stack trace.
    ///     Side effects: creates the database when it does not exist, binds a network port and writes
    ///     log entries for the lifetime of the call.
    /// </remarks>
    public static async Task<int> RunAsync(
        Context context,
        CancellationToken cancellationToken = default,
        ServerHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Building includes database preparation, whose failures are reported as InvalidOperationException
        WebApplication app;
        try
        {
            app = CreateApplication(context, options);
        }
        catch (InvalidOperationException ex)
        {
            context.WriteError(ex.Message);
            return 1;
        }

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
    ///     Creates the database folder and schema for a built application.
    /// </summary>
    /// <param name="app">The built application whose services provide the database settings.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the database cannot be prepared (unsupported provider, invalid connection string,
    ///     unwritable location, or a database error); the application is disposed before the exception
    ///     is thrown and the original failure is the inner exception.
    /// </exception>
    private static void InitializeDatabase(WebApplication app)
    {
        try
        {
            var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var factory = app.Services.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
            DatabaseSetup.EnsureCreated(factory, options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbException or IOException
                                       or UnauthorizedAccessException or ArgumentException
                                       or DllNotFoundException or TypeInitializationException)
        {
            // The application will never run, so release what building it acquired
            ((IDisposable)app).Dispose();

            // A missing native SQLite library surfaces as a type-initializer failure whose own message is
            // unhelpful; report the root cause (the library that could not be loaded) instead
            var detail = ex is TypeInitializationException ? ex.GetBaseException().Message : ex.Message;
            throw new InvalidOperationException($"Failed to prepare the database: {detail}", ex);
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
