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

using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     Token authentication of the API (bearer token, or Basic with the token as the password): the authentication
///     scheme, the <c>Api</c> authorization policy and the
///     handler that turns a personal access token into the signed-in principal of its owner.
/// </summary>
/// <remarks>
///     The scheme is not the default scheme: the cookie stays the default for the web pages. Only the endpoints that
///     use the <see cref="PolicyName"/> policy run this handler, and the policy lists this scheme alone, so a session
///     cookie never authenticates an API call (which also closes cross-site request forgery for the API) and a token
///     never authenticates a web page, the sign-in endpoints or the page route of the attestation. Every refusal
///     (no header, wrong, malformed, expired, revoked token, disabled or locked owner) answers the same generic
///     <c>401</c> problem document advertising <c>Bearer</c> and <c>Basic</c>, so a caller learns nothing about which
///     tokens exist; the audit trail records the reason (never the token). Attempts are counted per client address with
///     <see cref="SignInThrottle"/>; an address over its limit is answered <c>429</c>. The static members are
///     stateless and thread-safe; see <see cref="ApiTokenHandler"/> for the per-request work.
/// </remarks>
internal static class ApiTokenAuthentication
{
    /// <summary>
    ///     The name of the authentication scheme.
    /// </summary>
    public const string Scheme = "ReviewCenterToken";

    /// <summary>
    ///     The name of the authorization policy that requires a personal access token.
    /// </summary>
    public const string PolicyName = "Api";

    /// <summary>
    ///     The claim type that tells how the principal was authenticated.
    /// </summary>
    public const string AuthMethodClaimType = "rc_auth";

    /// <summary>
    ///     The value of <see cref="AuthMethodClaimType"/> for a personal access token.
    /// </summary>
    public const string TokenAuthMethod = "pat";

    /// <summary>
    ///     The claim type that carries the non-secret display id of the token used.
    /// </summary>
    public const string TokenIdClaimType = "rc_token_id";

    /// <summary>
    ///     Registers the token authentication scheme and the <c>Api</c> policy.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     Call after <c>AddSignIn</c>: the cookie scheme stays the default scheme and the default challenge scheme.
    ///     <see cref="SignInThrottle"/>, <see cref="PersonalAccessTokenService"/> and <see cref="AuditService"/>
    ///     must be registered.
    /// </remarks>
    public static IServiceCollection AddApiTokenAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiTokenHandler>(Scheme, null);
        services.AddAuthorizationBuilder().AddPolicy(
            PolicyName,
            policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
        return services;
    }
}

/// <summary>
///     The authentication handler of the <c>ReviewCenterToken</c> scheme.
/// </summary>
/// <remarks>
///     A new instance serves each request. The handler reads the bearer token (or the password of a Basic credential),
///     counts the attempt in the throttle,
///     asks <see cref="PersonalAccessTokenService"/> to check it and builds the principal from the current data of the
///     owner, so a changed role applies at once. Nothing about the header is logged; a refusal is written to the audit
///     trail on a best-effort basis, coalesced per address so a flood cannot fill it.
/// </remarks>
internal sealed class ApiTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    ///     The key under which a throttled request is marked, so the challenge answers 429 instead of 401.
    /// </summary>
    private const string ThrottledItem = "rc.api.throttled";

    /// <summary>
    ///     The prefix of the pseudo user name that the throttle counts API attempts under, followed by the address.
    /// </summary>
    /// <remarks>
    ///     The name is per address on purpose: one shared name would let anybody block every client's token use with a
    ///     few bad attempts. The address counter of the throttle still limits one address that varies its attempts.
    /// </remarks>
    private const string ThrottleNamePrefix = "api-token:";

    /// <summary>
    ///     The address text shown when the client address is not known.
    /// </summary>
    private const string UnknownAddress = "unknown";

    /// <summary>
    ///     Checks the presented tokens.
    /// </summary>
    private readonly PersonalAccessTokenService _tokens;

    /// <summary>
    ///     Limits the attempts per client address.
    /// </summary>
    private readonly SignInThrottle _throttle;

    /// <summary>
    ///     Records refusals in the audit trail.
    /// </summary>
    private readonly AuditService _audit;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ApiTokenHandler"/> class.
    /// </summary>
    /// <param name="options">The scheme options monitor.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    /// <param name="tokens">The token service. Must not be null.</param>
    /// <param name="throttle">The attempt throttle. Must not be null.</param>
    /// <param name="audit">The audit service. Must not be null.</param>
    public ApiTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PersonalAccessTokenService tokens,
        SignInThrottle throttle,
        AuditService audit)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
        _throttle = throttle;
        _audit = audit;
    }

    /// <summary>
    ///     Authenticates the request from its bearer token or Basic password.
    /// </summary>
    /// <returns>
    ///     No result when the request carries no usable token (the challenge answers 401); a failure when the
    ///     attempt is throttled or the token is refused; otherwise success with the principal of the token's owner.
    /// </returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // A bearer token or the password of a Basic credential is an attempt; anything else is no credentials
        var presented = ExtractToken(Request.Headers.Authorization.ToString());
        if (presented is null)
        {
            return AuthenticateResult.NoResult();
        }

        // Count the attempt before anything is checked; over the limit nothing is checked at all
        var address = SignIn.ClientAddress(Context);
        var shownAddress = address ?? UnknownAddress;
        var reservation = _throttle.TryBegin(address, ThrottleNamePrefix + shownAddress);
        if (reservation is null)
        {
            Context.Items[ThrottledItem] = true;
            await _audit.TryRecordCoalescedAsync(
                new AuditDraft(
                    "token.use-throttled",
                    null,
                    "not signed in",
                    null,
                    null,
                    $"API token attempts from {shownAddress} were throttled"),
                $"token-throttled|{shownAddress}",
                Context.RequestAborted);
            return AuthenticateResult.Fail("throttled");
        }

        var validation = await _tokens.AuthenticateAsync(presented, Context.RequestAborted);
        if (!validation.Succeeded)
        {
            // The failure stays counted; the reason goes to the trail (with the token's display id only when a stored
            // token matched), never to the caller
            var which = validation.Token is null ? string.Empty : $" (token {validation.Token.DisplayId})";
            await _audit.TryRecordCoalescedAsync(
                new AuditDraft(
                    "token.use-failed",
                    null,
                    "not signed in",
                    null,
                    null,
                    $"API token rejected from {shownAddress}: {Describe(validation.Failure)}{which}"),
                $"token-failed|{shownAddress}",
                Context.RequestAborted);
            return AuthenticateResult.Fail("rejected");
        }

        // A good token does not use up the budget of its address
        _throttle.Succeeded(reservation);
        return AuthenticateResult.Success(new AuthenticationTicket(BuildPrincipal(validation), Scheme.Name));
    }

    /// <summary>
    ///     Extracts the token text from an <c>Authorization</c> header.
    /// </summary>
    /// <param name="authorization">The header value; may be empty.</param>
    /// <returns>
    ///     The bearer token, or the password of a Basic credential (the user name is ignored, so tools that can send
    ///     only Basic, such as ReviewMark, can present the token); null when the header holds neither or is
    ///     malformed.
    /// </returns>
    private static string? ExtractToken(string authorization)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return null;
        }

        if (string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return header.Parameter;
        }

        if (!string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // "user:password": split at the first colon, so the password may contain colons
        try
        {
            var decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(header.Parameter));
            var colon = decoded.IndexOf(':');
            var password = colon < 0 ? string.Empty : decoded[(colon + 1)..];
            return string.IsNullOrWhiteSpace(password) ? null : password;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Answers a request that needs a token and has none that was accepted.
    /// </summary>
    /// <param name="properties">The challenge properties; not used.</param>
    /// <returns>A task that completes when the answer is written.</returns>
    /// <remarks>
    ///     One generic <c>401</c> problem document for every reason, advertising <c>Bearer</c> and <c>Basic</c>;
    ///     <c>429</c>
    ///     when the address is throttled.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(ThrottledItem))
        {
            return Results.Problem(
                title: "Too many requests",
                detail: "Too many attempts with an API token; try again later.",
                statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(Context);
        }

        Response.Headers.WWWAuthenticate = new[] { "Bearer", "Basic realm=\"ReviewCenter\", charset=\"UTF-8\"" };
        return Results.Problem(
            title: "Unauthorized",
            statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(Context);
    }

    /// <summary>
    ///     Answers a request whose user is not allowed to do what it asks.
    /// </summary>
    /// <param name="properties">The challenge properties; not used.</param>
    /// <returns>A task that completes when the answer is written.</returns>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        return Results.Problem(
            title: "Forbidden",
            statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(Context);
    }

    /// <summary>
    ///     Builds the principal of an accepted token.
    /// </summary>
    /// <param name="validation">The accepted validation; carries the owner and the token.</param>
    /// <returns>
    ///     A principal with the same name, display name and administrator-role claims as the cookie principal (so every
    ///     claims-based rule applies unchanged), marked as token-authenticated with the token's display id.
    /// </returns>
    private ClaimsPrincipal BuildPrincipal(TokenValidation validation)
    {
        var user = validation.User!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(SignIn.DisplayNameClaimType, user.DisplayName),
            new(ApiTokenAuthentication.AuthMethodClaimType, ApiTokenAuthentication.TokenAuthMethod),
            new(ApiTokenAuthentication.TokenIdClaimType, validation.Token!.DisplayId)
        };
        if (user.IsSystemAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, SignIn.AdminRole));
        }

        var identity = new ClaimsIdentity(
            claims,
            Scheme.Name,
            ClaimTypes.Name,
            ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    ///     Describes a refusal for the audit trail.
    /// </summary>
    /// <param name="failure">The reason.</param>
    /// <returns>A short phrase; contains no token value.</returns>
    private static string Describe(TokenFailure failure)
    {
        return failure switch
        {
            TokenFailure.Malformed => "not a token",
            TokenFailure.Expired => "token expired",
            TokenFailure.Revoked => "token revoked",
            TokenFailure.OwnerDisabled => "owner disabled",
            TokenFailure.OwnerLockedOut => "owner locked out",
            TokenFailure.OwnerMustChangePassword => "owner must change password",
            _ => "unknown token"
        };
    }
}
