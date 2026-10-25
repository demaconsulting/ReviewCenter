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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Hosting;

/// <summary>
///     The <c>audit verify</c> verb: checks the hash chain of the audit trail in the configured database.
/// </summary>
/// <remarks>
///     The command builds its configuration and services the same way the server and the <c>user add</c> command
///     do (the same <c>appsettings.json</c> and environment variables, the same registration), so it reads the
///     same database. It streams the trail through <see cref="AuditService.VerifyAsync"/> and reports the result
///     through the context: an intact chain is an informational line and exit code 0; a broken chain names the
///     first bad entry and makes the exit code 1, so scripts and scheduled jobs can alert on it. The command
///     never changes the trail. The class is stateless and therefore thread-safe.
/// </remarks>
internal static class AuditCommand
{
    /// <summary>
    ///     Verifies the audit trail described by the command-line context.
    /// </summary>
    /// <param name="context">The parsed command line; results and errors are reported through it. Must not be null.</param>
    /// <param name="configure">
    ///     Optional adjustment of the configuration before the services are built; tests use it to select a
    ///     temporary database.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    ///     An intact chain prints <c>Audit chain intact: N entries</c>. A broken chain, and a database that cannot
    ///     be opened, are reported with <see cref="Context.WriteError"/>, which makes the exit code 1. Side
    ///     effects: creates the (empty) database when it does not exist.
    /// </remarks>
    public static void Run(Context context, Action<ConfigurationManager>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Same configuration sources as the server (appsettings.json next to the program, environment variables)
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = typeof(AuditCommand).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });
        configure?.Invoke(builder.Configuration);

        // The command reports through the context, so the host logging stays off
        builder.Logging.ClearProviders();
        ServerHost.AddServices(builder.Services, builder.Configuration);

        try
        {
            using var host = builder.Build();
            DatabaseSetup.EnsureCreated(
                host.Services.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
                host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value);

            var result = host.Services.GetRequiredService<AuditService>()
                .VerifyAsync()
                .GetAwaiter()
                .GetResult();
            if (result.IsIntact)
            {
                context.WriteLine($"Audit chain intact: {result.Count} entries");
            }
            else
            {
                context.WriteError($"Audit chain BROKEN at entry {result.FirstBadSequence}");
            }
        }
        catch (Exception ex) when (ex is DbException or IOException or UnauthorizedAccessException)
        {
            context.WriteError($"Failed to read the audit trail: {ex.Message}");
        }
    }
}
