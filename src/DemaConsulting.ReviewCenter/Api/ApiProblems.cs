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

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     Maps the domain rules to the HTTP problem documents of the API, and builds the actor of an API call.
/// </summary>
/// <remarks>
///     One mapping keeps the status of every rule in a single place. The detail of a problem is the message of the
///     rule, which is already plain text that is safe to show to a user. The class is stateless and thread-safe.
/// </remarks>
internal static class ApiProblems
{
    /// <summary>
    ///     Gets the HTTP status that answers a failed domain rule.
    /// </summary>
    /// <param name="rule">The rule that failed.</param>
    /// <returns>
    ///     404 for a missing thing, 409 for a conflict with the current state (a stale record, a duplicate, or a
    ///     review that is locked by its approval, or that is not locked when a reopen is asked, or a change that would
    ///     make a signature out of date without being acknowledged, or a review rule of the project that refuses the
    ///     change: the author answering the checklist, a reopener signing again, a reopen that would strand a slot), 429 when too many
    ///     attempts were made, 502, 503 or 504 when git cannot be used, 403 when the caller is not allowed, 500 when
    ///     a stored credential cannot be read, otherwise 400 (a checklist definition that is not valid is 400 explicitly).
    /// </returns>
    public static int StatusOf(DomainRule rule)
    {
        return rule switch
        {
            DomainRule.ProjectNotFound
                or DomainRule.ThreadNotFound
                or DomainRule.ChecklistNotFound
                or DomainRule.ChecklistItemNotFound
                or DomainRule.UserNotFound
                or DomainRule.ReviewNotFound
                or DomainRule.SlotNotFound
                or DomainRule.RepositoryNotFound
                or DomainRule.RevisionNotFound
                or DomainRule.MembershipNotFound
                or DomainRule.TokenNotFound => StatusCodes.Status404NotFound,
            DomainRule.ReviewIdDuplicate
                or DomainRule.MembershipExists
                or DomainRule.LastProjectAdmin
                or DomainRule.MemberHasOpenReviewWork
                or DomainRule.RepositoryDisabled
                or DomainRule.ReviewLocked
                or DomainRule.ReviewNotLocked
                or DomainRule.RecordChanged
                or DomainRule.SignaturesWouldBecomeOutOfDate
                or DomainRule.AuthorCannotAnswer
                or DomainRule.ReopenerCannotSign
                or DomainRule.ReopenWouldStrandSlot => StatusCodes.Status409Conflict,
            DomainRule.TooManyAttempts => StatusCodes.Status429TooManyRequests,
            DomainRule.GitUnavailable => StatusCodes.Status503ServiceUnavailable,
            DomainRule.GitAccessFailed => StatusCodes.Status502BadGateway,
            DomainRule.GitTimeout => StatusCodes.Status504GatewayTimeout,
            DomainRule.ChecklistDefinitionInvalid or DomainRule.DueDateInvalid or DomainRule.FollowUpInvalid => StatusCodes.Status400BadRequest,
            DomainRule.ActorNotAllowed
                or DomainRule.SigningNotAllowed
                or DomainRule.PasswordChangeRequired => StatusCodes.Status403Forbidden,
            DomainRule.CredentialsUnreadable => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };
    }

    /// <summary>
    ///     Builds the problem document of a failed domain rule.
    /// </summary>
    /// <param name="exception">The failure. Must not be null.</param>
    /// <returns>A problem result with the mapped status, a short title and the rule message as detail.</returns>
    public static IResult ToResult(DomainRuleException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var status = StatusOf(exception.Rule);
        return Results.Problem(
            title: TitleOf(status),
            detail: exception.Message,
            statusCode: status);
    }

    /// <summary>
    ///     Builds the 404 problem document of something that does not exist.
    /// </summary>
    /// <param name="detail">What was not found; contains no secret.</param>
    /// <returns>A problem result with status 404.</returns>
    public static IResult NotFound(string detail)
    {
        return Results.Problem(
            title: TitleOf(StatusCodes.Status404NotFound),
            detail: detail,
            statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    ///     Builds the 415 problem document of a request body that is not JSON.
    /// </summary>
    /// <returns>A problem result with status 415.</returns>
    public static IResult UnsupportedMediaType()
    {
        return Results.Problem(
            title: TitleOf(StatusCodes.Status415UnsupportedMediaType),
            detail: "The request body must be JSON (content type application/json).",
            statusCode: StatusCodes.Status415UnsupportedMediaType);
    }

    /// <summary>
    ///     Builds the actor of an API call from the principal of its token.
    /// </summary>
    /// <param name="principal">The authenticated principal. Must not be null.</param>
    /// <returns>
    ///     The actor, marked as using an API token (so the signing rules refuse it) and carrying the display id of the
    ///     token for the audit trail.
    /// </returns>
    public static Actor ActorOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var username = principal.Identity?.Name ?? string.Empty;
        var displayName = principal.FindFirstValue(SignIn.DisplayNameClaimType) ?? username;

        // An API call is always authenticated by a token; a missing claim still counts as one, so the cannot-sign
        // rule can never be skipped by accident
        var tokenId = principal.FindFirstValue(ApiTokenAuthentication.TokenIdClaimType) ?? "unknown";
        return new Actor(username, displayName) { ApiTokenId = tokenId };
    }

    /// <summary>
    ///     Builds the actor of an API call that may also come without a token.
    /// </summary>
    /// <param name="principal">The principal of the request. Must not be null.</param>
    /// <returns>The token actor, or <c>null</c> when nobody was authenticated (an anonymous request).</returns>
    public static Actor? ActorOrAnonymous(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Identity?.IsAuthenticated == true ? ActorOf(principal) : null;
    }

    /// <summary>
    ///     Gives the short title of a status.
    /// </summary>
    /// <param name="status">The HTTP status.</param>
    /// <returns>A fixed short phrase.</returns>
    private static string TitleOf(int status)
    {
        return status switch
        {
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
            StatusCodes.Status429TooManyRequests => "Too Many Requests",
            StatusCodes.Status500InternalServerError => "Internal Server Error",
            StatusCodes.Status502BadGateway => "Bad Gateway",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            StatusCodes.Status504GatewayTimeout => "Gateway Timeout",
            _ => "Bad Request"
        };
    }
}
