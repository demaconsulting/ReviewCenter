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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     The download endpoint of the signed PDF attestation of a completed review.
/// </summary>
/// <remarks>
///     <c>GET /projects/{projectId}/reviews/{reviewId}/attestation.pdf</c> is authorized like viewing the review: any
///     signed-in user may download it, and anybody else is sent to the sign-in page by the authentication scheme.
///     It answers 200 with the PDF, 404 when the review does not exist, 409 when the review is not complete and 500
///     (a problem document that names the setting to correct) when the signing key is not usable.
/// </remarks>
internal static class AttestationEndpoint
{
    /// <summary>
    ///     The route template of the endpoint.
    /// </summary>
    public const string Route = "/projects/{projectId:int}/reviews/{reviewId}/attestation.pdf";

    /// <summary>
    ///     Maps the attestation download endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapAttestation(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(
            Route,
            async (int projectId, string reviewId, HttpContext http, AttestationService attestations) =>
            {
                // The actor is the signed-in user; the authorization policy guarantees there is one
                var actor = new Actor(
                    http.User.Identity?.Name ?? string.Empty,
                    http.User.FindFirstValue(SignIn.DisplayNameClaimType) ?? http.User.Identity?.Name ?? string.Empty);
                var result = await attestations.GenerateAsync(projectId, reviewId, actor, http.RequestAborted);

                // A signed attestation is evidence of the moment it was made, so it is never cached
                http.Response.Headers.CacheControl = "no-store";
                return result.Status switch
                {
                    AttestationStatus.Ok => Results.File(result.Content!, "application/pdf", result.FileName),
                    AttestationStatus.NotFound => Results.NotFound(),
                    AttestationStatus.NotApproved => Results.Problem(
                        title: "The review is not complete",
                        detail: result.Message,
                        statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(
                        title: "Attestation signing key unavailable",
                        detail: result.Message,
                        statusCode: StatusCodes.Status500InternalServerError)
                };
            }).RequireAuthorization();
        return endpoints;
    }
}
