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
using System.Security.Cryptography;
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
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
    ///     The route of the "Page not found" page that answers a page request no page handles.
    /// </summary>
    public const string NotFoundPath = "/not-found";

    /// <summary>
    ///     The address prefixes that belong to machine clients and keep a plain 404 without the page frame.
    /// </summary>
    private static readonly string[] ReservedPrefixes = ["/api", "/_framework", "/_blazor", "/_content"];

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

        // Makes the signed-in user available to AuthorizeRouteView and the layout
        builder.Services.AddCascadingAuthenticationState();

        // Persistence and the Core services, shared with the user command
        AddServices(builder.Services, builder.Configuration);

        // Cookie sign-in and authorization (web only)
        builder.Services.AddSignIn(builder.Configuration);

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
        ValidateAuthOptions(app);
        ValidateDataProtection(app);
        ValidateAttestation(app);

        // A page request that no page handles is answered with the framed "Page not found" page. This comes first
        // so that its re-executed request is routed, authenticated, and authorized again like any other request;
        // routing is then added here explicitly, because the automatic routing would sit before this middleware
        UseNotFoundPage(app);
        app.UseRouting();

        // Authentication and authorization must run before the endpoints they protect, and antiforgery
        // after them and before the Razor component and form endpoints it protects
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        // Serve the Blazor client script and other static web assets; skipped only when the host
        // has no asset manifest (in-memory test servers)
        if (options.MapStaticAssets)
        {
            app.MapStaticAssets();
        }

        // Liveness endpoint: plain-text 200 so that it is trivial for any probe to consume
        app.MapGet(HealthPath, () => Results.Text(HealthyText));

        // Login and logout
        app.MapSignIn();

        // The signed PDF attestation of a completed review
        app.MapAttestation();

        // The UI: every page lives under the App root component
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        // Emit one structured start-up entry once the server is accepting requests
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ServerHost).FullName!);
        var urls = context.Urls;
        app.Lifetime.ApplicationStarted.Register(() => LogStarted(logger, urls));

        return app;
    }

    /// <summary>
    ///     Registers the persistence and the Core services that both the web server and the user command use.
    /// </summary>
    /// <param name="services">The service collection to add to. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Database</c> section. Must not be null.</param>
    /// <remarks>
    ///     The database is chosen by the <c>Database</c> configuration section (appsettings.json or environment
    ///     variables). Options are read lazily so that callers adjusting the configuration afterwards take
    ///     effect, and the Core services are shared by all browser sessions. <see cref="UserService"/> must stay
    ///     a single instance because it serializes the changes to users. The repository registry, the credential
    ///     protector (Data Protection with a persisted key ring) and the git content source are registered here too;
    ///     the <c>DataProtection</c> and <c>Git</c> sections default to folders beside the database.
    /// </remarks>
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseSetup.SectionName));
        services.AddDbContextFactory<ReviewCenterDbContext>((provider, dbOptions) =>
            DatabaseSetup.Configure(dbOptions, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditAppender>();
        services.AddSingleton<IAuditStore, AuditStore>();
        services.AddSingleton(provider => new AuditService(
            provider.GetRequiredService<IAuditStore>(),
            exception => LogAuditFailure(provider.GetRequiredService<ILoggerFactory>(), exception)));
        services.AddSingleton<IReviewStore, ReviewStore>();
        services.AddSingleton<IUserStore, UserStore>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<ReviewService>();
        services.AddSingleton<ThreadService>();
        services.AddSingleton<ChecklistService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<SigningService>();

        // The repository registry and the git content source. Tokens are encrypted with Data Protection, whose key
        // ring is persisted in a folder so that the tokens survive a restart
        services.AddOptions<DataProtectionSettings>().Bind(configuration.GetSection(DataProtectionSettings.SectionName));
        services.AddOptions<GitOptions>()
            .Bind(configuration.GetSection(GitOptions.SectionName))
            .PostConfigure<IOptions<DatabaseOptions>>((git, database) =>
            {
                if (string.IsNullOrWhiteSpace(git.CachePath))
                {
                    git.CachePath = Path.Combine(DatabaseSetup.ResolveDataDirectory(database.Value), "git-cache");
                }
            });
        services.AddDataProtection().SetApplicationName("DemaConsulting.ReviewCenter");
        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<DataProtectionSettings>, IOptions<DatabaseOptions>, ILoggerFactory>(
                (keys, settings, database, loggerFactory) =>
                    keys.XmlRepository = new FileSystemXmlRepository(
                        new DirectoryInfo(ResolveKeysPath(settings.Value, database.Value)),
                        loggerFactory));
        services.AddSingleton<ICredentialProtector, CredentialProtector>();
        services.AddSingleton<IRepositoryStore, RepositoryStore>();
        services.AddSingleton<RepositoryService>();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<GitOptions>>().Value);
        services.AddSingleton<GitRunner>();
        services.AddSingleton<IGitRepositoryProvider, GitSource>();
        services.AddSingleton<IReviewSetParser, ReviewMarkConfigParser>();
        services.AddSingleton<ReviewPlanner>();

        // The attestation: the signing key (SelfSigned by default, or a certificate from a store), the PDF
        // renderer and signer, and the service that ties them to the review. The self-signed key is kept in a
        // subfolder of the Data Protection keys folder, which is known to be writable and private to the server
        services.AddOptions<AttestationOptions>()
            .Bind(configuration.GetSection(AttestationOptions.SectionName))
            .PostConfigure<IOptions<DataProtectionSettings>, IOptions<DatabaseOptions>>((attestation, settings, database) =>
            {
                if (string.IsNullOrWhiteSpace(attestation.SelfSignedFolder))
                {
                    attestation.SelfSignedFolder = Path.Combine(
                        ResolveKeysPath(settings.Value, database.Value),
                        "attestation-selfsigned");
                }
            });
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AttestationOptions>>().Value);
        services.AddSingleton(provider => AttestationKeyProviderFactory.Create(
            provider.GetRequiredService<AttestationOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IAttestationRenderer, AttestationRenderer>();
        services.AddSingleton<IAttestationSigner, AttestationSigner>();
        services.AddSingleton<AttestationService>();
    }

    /// <summary>
    ///     Works out the folder of the Data Protection key ring.
    /// </summary>
    /// <param name="settings">The configured settings.</param>
    /// <param name="database">The database options, whose folder is the default base.</param>
    /// <returns>The absolute folder: the configured one, or <c>keys</c> beside the database.</returns>
    internal static string ResolveKeysPath(DataProtectionSettings settings, DatabaseOptions database) =>
        string.IsNullOrWhiteSpace(settings.KeysPath)
            ? Path.Combine(DatabaseSetup.ResolveDataDirectory(database), "keys")
            : Path.GetFullPath(settings.KeysPath);

    /// <summary>
    ///     Logs a failed best-effort audit entry; the user never sees the failure.
    /// </summary>
    /// <param name="loggerFactory">The factory that creates the logger.</param>
    /// <param name="exception">The failure; its message is logged, never shown to a user.</param>
    private static void LogAuditFailure(ILoggerFactory loggerFactory, Exception exception)
    {
#pragma warning disable CA1848 // A rare failure path; a LoggerMessage delegate adds no value here
        loggerFactory.CreateLogger(typeof(ServerHost).FullName!)
            .LogWarning(exception, "An audit entry could not be recorded");
#pragma warning restore CA1848
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
    ///     Shows the "Page not found" page, with the header and footer, for a page request that ended in 404.
    /// </summary>
    /// <param name="app">The application whose pipeline receives the middleware. Must not be null.</param>
    /// <remarks>
    ///     A URL that no page handles matches no endpoint, so the router never sees it and the response would be
    ///     empty. The middleware re-executes such a request against <see cref="NotFoundPath"/> with the 404 status
    ///     kept (the built-in re-execution is not used because it would answer every empty 4xx or 5xx response,
    ///     for example a 405 or a 400, with "Page not found"). Only GET and HEAD requests for a page are answered:
    ///     the reserved prefixes of machine clients (<c>/api</c>, <c>/_framework</c>, <c>/_blazor</c>,
    ///     <c>/_content</c>) and an address that ends in a file extension keep the plain 404. It must be placed
    ///     before routing so that the re-executed request is routed again.
    /// </remarks>
    private static void UseNotFoundPage(WebApplication app)
    {
        app.UseWhen(
            context => IsPageRequest(context.Request),
            branch => branch.UseStatusCodePages(async status =>
            {
                var http = status.HttpContext;
                if (http.Response.StatusCode != StatusCodes.Status404NotFound)
                {
                    return;
                }

                // Same steps as the framework re-execution: remember the original request, forget the endpoint
                // that was chosen for it, and run the rest of the pipeline for the not-found address in a new scope
                var originalPath = http.Request.Path;
                var originalQuery = http.Request.QueryString;
                var originalServices = http.RequestServices;

                // Blazor reads this feature to see that the page is the answer to an earlier 404 and keeps the status
                http.Features.Set<IStatusCodeReExecuteFeature>(new ReExecuteFeature
                {
                    OriginalPathBase = http.Request.PathBase.Value ?? string.Empty,
                    OriginalPath = originalPath.Value ?? string.Empty,
                    OriginalQueryString = originalQuery.HasValue ? originalQuery.Value : null,
                    OriginalStatusCode = http.Response.StatusCode,
                    Endpoint = http.GetEndpoint(),
                    RouteValues = http.Features.Get<IRouteValuesFeature>()?.RouteValues
                });
                http.SetEndpoint(null);
                http.Features.Get<IRouteValuesFeature>()?.RouteValues?.Clear();
                http.Request.Path = NotFoundPath;
                http.Request.QueryString = QueryString.Empty;
                using var scope = originalServices.CreateScope();
                http.RequestServices = scope.ServiceProvider;
                try
                {
                    await status.Next(http);
                }
                finally
                {
                    http.Request.Path = originalPath;
                    http.Request.QueryString = originalQuery;
                    http.RequestServices = originalServices;
                    http.Features.Set<IStatusCodeReExecuteFeature>(null);
                }
            }));
    }

    /// <summary>
    ///     Tells whether a request asks for a page, which may be answered by the not-found page.
    /// </summary>
    /// <param name="request">The request to classify. Must not be null.</param>
    /// <returns><c>true</c> for a GET or HEAD request outside the reserved prefixes and without a file extension.</returns>
    private static bool IsPageRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (ReservedPrefixes.Any(prefix => request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !Path.HasExtension(request.Path.Value);
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
    ///     Reads the sign-in options once so an invalid value stops the start-up with a clear message.
    /// </summary>
    /// <param name="app">The built application whose services provide the options.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the <c>Auth</c> settings are invalid; the application is disposed before the exception is thrown.
    /// </exception>
    private static void ValidateAuthOptions(WebApplication app)
    {
        try
        {
            _ = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        }
        catch (OptionsValidationException ex)
        {
            ((IDisposable)app).Dispose();
            throw new InvalidOperationException($"Invalid sign-in settings: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Checks that the Data Protection key ring folder is writable and that a token can be encrypted and read back.
    /// </summary>
    /// <param name="app">The built application whose services provide the settings and the protector.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the key ring cannot be written; the application is disposed before the exception is thrown.
    ///     The message names the folder and the <c>DataProtection:KeysPath</c> setting.
    /// </exception>
    /// <remarks>
    ///     A key ring that cannot be saved would silently fall back to keys that are lost at the next restart,
    ///     and every stored token would become unreadable, so the server refuses to start instead.
    /// </remarks>
    private static void ValidateDataProtection(WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
        var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var folder = ResolveKeysPath(settings, database);
        try
        {
            Directory.CreateDirectory(folder);
            if (!OperatingSystem.IsWindows())
            {
                // The keys decrypt the stored tokens: only the server account may read them
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var probe = Path.Combine(folder, $".probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);

            var protector = app.Services.GetRequiredService<ICredentialProtector>();
            ProbeRoundTrip(protector);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or DomainRuleException or ArgumentException or NotSupportedException)
        {
            ((IDisposable)app).Dispose();
            var cause = ex.InnerException is null ? string.Empty : $" ({ex.InnerException.Message})";
            throw new InvalidOperationException(
                $"The Data Protection key ring folder '{folder}' is not writable: {ex.Message}{cause} " +
                "Set DataProtection:KeysPath to a folder the server can write to and keep.",
                ex);
        }
    }

    /// <summary>
    ///     Checks at start-up that the attestation signing key can be used, so that a wrong setting stops the
    ///     start-up with a clear message instead of failing when a user asks for an attestation.
    /// </summary>
    /// <param name="app">The built application; it is disposed before the exception is thrown.</param>
    /// <exception cref="InvalidOperationException">Thrown when the key provider is unknown or the key is not usable.</exception>
    /// <remarks>
    ///     This resolves the key provider (an unknown <c>Attestation:Provider</c> fails here) and loads the key; it
    ///     applies the <c>Attestation:RequireTrustedKey</c> policy. The key never signs anything at start-up, so a
    ///     smart card is not asked for its PIN.
    /// </remarks>
    private static void ValidateAttestation(WebApplication app)
    {
        try
        {
            app.Services.GetRequiredService<IAttestationKeyProvider>().GetSigningKey();
        }
        catch (AttestationKeyException ex)
        {
            ((IDisposable)app).Dispose();
            throw new InvalidOperationException($"The attestation signing key is not usable: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Encrypts and decrypts a probe value, which creates the first key in the key ring. A failure is tried
    ///     once more after a short pause, because a freshly written key file can be briefly locked by a virus
    ///     scanner or the indexer; a folder that really cannot be written fails both times.
    /// </summary>
    /// <param name="protector">The credential protector to probe.</param>
    private static void ProbeRoundTrip(ICredentialProtector protector)
    {
        for (var attempt = 1; attempt < 3; attempt++)
        {
            try
            {
                RoundTrip(protector);
                return;
            }
            catch (Exception ex) when (ex is IOException or CryptographicException or DomainRuleException)
            {
                Thread.Sleep(300);
            }
        }

        RoundTrip(protector);
    }

    /// <summary>
    ///     Encrypts and decrypts the probe value once.
    /// </summary>
    /// <param name="protector">The credential protector to probe.</param>
    private static void RoundTrip(ICredentialProtector protector)
    {
        if (protector.Unprotect(protector.Protect("probe")) != "probe")
        {
            throw new CryptographicException("The probe value did not survive a round trip.");
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

    /// <summary>
    ///     The record of the original request that <see cref="UseNotFoundPage"/> publishes while it re-executes.
    /// </summary>
    private sealed class ReExecuteFeature : IStatusCodeReExecuteFeature
    {
        /// <inheritdoc />
        public string OriginalPathBase { get; set; } = string.Empty;

        /// <inheritdoc />
        public string OriginalPath { get; set; } = string.Empty;

        /// <inheritdoc />
        public string? OriginalQueryString { get; set; }

        /// <inheritdoc />
        public int OriginalStatusCode { get; set; }

        /// <inheritdoc />
        public Endpoint? Endpoint { get; set; }

        /// <inheritdoc />
        public RouteValueDictionary? RouteValues { get; set; }
    }
}
