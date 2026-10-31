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

using System.Security.Claims;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Mvc;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     The download endpoints of an audit export: the audit trail of one project, and the whole trail.
/// </summary>
/// <remarks>
///     <para>
///         <c>GET /projects/{projectId}/audit/export</c> and <c>GET /audit/export</c> are minimal endpoints that belong
///         to the signed-in web application (cookie authentication), not to the token API, so they are not part of
///         the OpenAPI document. Both stream a file of everything that matches the filter in the query: <c>format</c>
///         (<c>csv</c>, the default, or <c>json</c>; anything else is a 400) and the page's filter values
///         <c>actor</c>, <c>action</c>, <c>review</c>, <c>from</c>, <c>to</c> (and <c>project</c> on the whole-trail
///         route). The values are read with <see cref="AuditFilterState.FromQuery"/>, so a value that is not usable
///         is dropped exactly as on the page and never fails the request; <c>size</c>, <c>before</c> and
///         <c>after</c> are not used.
///     </para>
///     <para>
///         Access: somebody who is not signed in is sent to the sign-in page by the authentication scheme. The
///         project route needs the right to read the project's audit trail (<see cref="IProjectAccess.RequireAuditAsync"/>:
///         a project administrator or a system administrator): 404 when the project is not visible to the caller or
///         does not exist, 403 when it is visible but the caller may not read its audit trail. The whole-trail route is
///         for system administrators only; anybody else gets 403, as for the whole-trail page.
///     </para>
///     <para>
///         The answer carries the file type, <c>Content-Disposition: attachment</c> with a name that holds the scope,
///         the time and the chain status, <c>Cache-Control: no-store</c>, <c>X-Content-Type-Options: nosniff</c> and
///         the chain status in <c>X-ReviewCenter-Audit-Chain</c> (<c>intact</c> or <c>broken</c>, with the first
///         broken sequence in <c>X-ReviewCenter-Audit-Chain-Broken-At</c> for a broken chain). The status is known
///         before the first byte because <see cref="AuditService.PrepareExportAsync"/> verifies the chain first.
///         The body is streamed in batches and follows <see cref="HttpContext.RequestAborted"/>; the export is audited
///         by the service when the stream ends.
///     </para>
/// </remarks>
internal static class AuditExportEndpoints
{
    /// <summary>
    ///     The route template of the export of one project's audit trail.
    /// </summary>
    public const string ProjectRoute = "/projects/{projectId:int}/audit/export";

    /// <summary>
    ///     The route of the export of the whole audit trail.
    /// </summary>
    public const string GlobalRoute = "/audit/export";

    /// <summary>
    ///     The response header that tells whether the audit chain was intact.
    /// </summary>
    public const string ChainHeader = "X-ReviewCenter-Audit-Chain";

    /// <summary>
    ///     The response header that names the first broken sequence; present only for a broken chain.
    /// </summary>
    public const string BrokenAtHeader = "X-ReviewCenter-Audit-Chain-Broken-At";

    /// <summary>
    ///     Maps the two audit export endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapAuditExport(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(
            ProjectRoute,
            async (
                int projectId,
                HttpContext http,
                IProjectAccess access,
                AuditService audit,
                [FromQuery] string? format,
                [FromQuery] string? actor,
                [FromQuery] string? action,
                [FromQuery] string? review,
                [FromQuery(Name = "from")] string? fromDay,
                [FromQuery(Name = "to")] string? toDay) =>
            {
                var user = ActorOf(http.User);
                try
                {
                    await access.RequireAuditAsync(user, projectId, http.RequestAborted);
                }
                catch (DomainRuleException ex)
                {
                    return Refuse(ex);
                }

                // The project is the one of the address, never one from the query
                var filter = AuditFilterState.FromQuery(actor, action, review, fromDay, toDay, null, null)
                    .ToFilter(projectId);
                return await StreamAsync(http, audit, filter, format, user);
            }).RequireAuthorization();

        endpoints.MapGet(
            GlobalRoute,
            async (
                HttpContext http,
                IProjectAccess access,
                AuditService audit,
                [FromQuery] string? format,
                [FromQuery] string? actor,
                [FromQuery] string? action,
                [FromQuery] string? review,
                [FromQuery(Name = "from")] string? fromDay,
                [FromQuery(Name = "to")] string? toDay,
                [FromQuery] string? project) =>
            {
                var user = ActorOf(http.User);
                try
                {
                    await access.RequireSystemAdminAsync(user, http.RequestAborted);
                }
                catch (DomainRuleException ex)
                {
                    return Refuse(ex);
                }

                var filter = AuditFilterState.FromQuery(actor, action, review, fromDay, toDay, null, project)
                    .ToFilter(null);
                return await StreamAsync(http, audit, filter, format, user);
            }).RequireAuthorization();
        return endpoints;
    }

    /// <summary>
    ///     Builds the actor from the cookie principal of the request.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The actor with the user name and the display name of the signed-in user.</returns>
    private static Actor ActorOf(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name ?? string.Empty;
        return new Actor(name, principal.FindFirstValue(SignIn.DisplayNameClaimType) ?? name);
    }

    /// <summary>
    ///     Turns a refused access check into the answer.
    /// </summary>
    /// <param name="exception">The refusal.</param>
    /// <returns>
    ///     An empty 404 for a project the caller cannot see (the page host shows its "not found" page for it, so the
    ///     answer does not reveal whether the project exists); the problem document of the rule otherwise (403).
    /// </returns>
    private static IResult Refuse(DomainRuleException exception)
    {
        return exception.Rule == DomainRule.ProjectNotFound ? Results.NotFound() : ApiProblems.ToResult(exception);
    }

    /// <summary>
    ///     Prepares the export, sets the headers and streams the body.
    /// </summary>
    /// <param name="http">The request context; its response is written.</param>
    /// <param name="audit">The audit service.</param>
    /// <param name="filter">The filter to export; valid by construction.</param>
    /// <param name="format">The raw <c>format</c> value, or <c>null</c> for CSV.</param>
    /// <param name="user">The exporting user.</param>
    /// <returns>A 400 problem for an unknown format; otherwise an empty result after the body was written.</returns>
    internal static async Task<IResult> StreamAsync(
        HttpContext http,
        AuditService audit,
        AuditFilter filter,
        string? format,
        Actor user)
    {
        var parsed = (format?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "csv" => AuditExportFormat.Csv,
            "json" => AuditExportFormat.Json,
            _ => (AuditExportFormat?)null
        };
        if (parsed is not { } exportFormat)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "The format must be csv or json.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var aborted = http.RequestAborted;
        try
        {
            // The chain is verified before the first byte, so its status can be a header
            var export = await audit.PrepareExportAsync(filter, exportFormat, user, aborted);
            var headers = http.Response.Headers;
            http.Response.ContentType = export.ContentType;
            headers.ContentDisposition = $"attachment; filename=\"{export.FileName}\"";
            headers.CacheControl = "no-store";
            headers.XContentTypeOptions = "nosniff";
            headers[ChainHeader] = export.ChainStatus;
            if (export.FirstBrokenSequence is { } broken)
            {
                headers[BrokenAtHeader] = broken.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            await audit.ExportAsync(export, http.Response.Body, aborted);
        }
        catch (Exception exception) when (aborted.IsCancellationRequested && exception is OperationCanceledException or IOException)
        {
            // The client went away (a cancelled read or a write to a closed connection); the service already audited
            // the export as incomplete, and a cancelled verification leaves no entry because no data was sent
        }

        return Results.Empty;
    }
}
