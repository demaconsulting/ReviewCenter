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

using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Authorization;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     The requirement of the two API routes that a project can open to callers without a token: the caller either
///     presented a valid token, or the project allows anonymous access to what the route returns.
/// </summary>
/// <param name="Setting">Tells whether the evidence settings of the project allow anonymous access to the route.</param>
internal sealed record AnonymousEvidenceRequirement(Func<EvidenceSettings, bool> Setting) : IAuthorizationRequirement;

/// <summary>
///     Decides <see cref="AnonymousEvidenceRequirement"/> for a request.
/// </summary>
/// <remarks>
///     A request whose token was accepted always satisfies the requirement (the route then applies the normal project
///     rules to the token's owner). A request without a signed-in user satisfies it only when it presented no
///     <c>Authorization</c> header at all and the project named by the <c>projectId</c> route value has the setting
///     on, so a wrong or expired token is still refused instead of silently being treated as anonymous, and a project
///     that does not exist or has the setting off gets the same generic <c>401</c> as before: nothing tells the
///     caller whether the project exists. Only the routes that carry this requirement are reachable anonymously;
///     every other route of the API stays behind the <c>Api</c> policy. The handler is stateless and thread-safe.
/// </remarks>
internal sealed class AnonymousEvidenceHandler : AuthorizationHandler<AnonymousEvidenceRequirement>
{
    /// <summary>
    ///     Reads the evidence settings of a project.
    /// </summary>
    private readonly EvidenceSettingsService _settings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnonymousEvidenceHandler"/> class.
    /// </summary>
    /// <param name="settings">The evidence settings service. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    public AnonymousEvidenceHandler(EvidenceSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnonymousEvidenceRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
            return;
        }

        // Anonymous access is for callers that present nothing: any header means the caller tried to sign in
        if (context.Resource is not HttpContext http
            || http.Request.Headers.ContainsKey("Authorization")
            || !int.TryParse(http.GetRouteValue("projectId")?.ToString(), out var projectId))
        {
            return;
        }

        var settings = await _settings.GetEffectiveAsync(projectId, http.RequestAborted);
        if (settings is not null && requirement.Setting(settings))
        {
            context.Succeed(requirement);
        }
    }
}
