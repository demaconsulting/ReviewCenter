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

using System.Globalization;
using System.Security.Claims;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     Sign-in with an OpenID Connect provider: the registration of the provider, the three endpoints that start
///     and finish a sign-in and a confirmation of identity, and the checks that make the confirmation safe.
/// </summary>
/// <remarks>
///     Everything here exists only when <see cref="OidcOptions.Enabled"/> is set; otherwise nothing is registered and
///     the endpoints are not mapped. The flow is the authorization code flow with PKCE, handled by the framework
///     handler, which checks state, nonce, signature, issuer, audience and lifetime of the identity token. The
///     handler hands the validated claims to a short-lived cookie (<see cref="ExternalCookieScheme"/>), never to the
///     session. <see cref="CompleteEndpoint"/> then turns them into an account through
///     <see cref="UserService.SignInExternalAsync"/> and issues the normal session cookie, so everything after the
///     sign-in is identical for every kind of account. A confirmation of identity for a signature (see
///     <see cref="ReauthEndpoint"/>) asks the provider for a fresh sign-in (<c>prompt=login</c>, <c>max_age=0</c>);
///     because the handler does not check <c>auth_time</c>, <see cref="VerifyReauth"/> does, together with the
///     issuer and the subject, and every doubt refuses (the check fails closed). Only a verified confirmation
///     issues a single-use <see cref="ReauthProofStore"/> proof for exactly one user, review and slot, and the
///     session is left as it was. Callbacks are counted per client address only, with no name counter, by <see cref="SignInThrottle"/>, so bad
///     callbacks from one shared address cannot lock out another person behind it before the address limit. Only a
///     fixed reason class is ever recorded for a failure, never a token, a claim, an error text or the secret, and
///     every address a visitor can influence is reduced to a local one by <see cref="SignIn.SafeReturnUrl"/> both
///     when the sign-in starts and when it ends. The class is stateless and therefore thread-safe.
/// </remarks>
internal static class OidcSignIn
{
    /// <summary>
    ///     The name of the short-lived cookie scheme that carries the validated claims to
    ///     <see cref="CompleteEndpoint"/>.
    /// </summary>
    public const string ExternalCookieScheme = "ReviewCenter.External";

    /// <summary>
    ///     The name of the scheme of the provider.
    /// </summary>
    public const string ProviderScheme = "oidc";

    /// <summary>
    ///     The endpoint that receives the form of the button "Sign in with ..." and sends the browser to the provider.
    /// </summary>
    public const string LoginEndpoint = "/auth/oidc/login";

    /// <summary>
    ///     The endpoint the provider flow ends at; it creates the session.
    /// </summary>
    public const string CompleteEndpoint = "/auth/oidc/complete";

    /// <summary>
    ///     The endpoint that receives the form of the button "Confirm identity" and sends the browser to the provider.
    /// </summary>
    public const string ReauthEndpoint = "/auth/oidc/reauth";

    /// <summary>
    ///     The address the provider sends the browser back to; handled by the framework handler.
    /// </summary>
    public const string CallbackPath = "/signin-oidc";

    /// <summary>
    ///     The value of the <c>error</c> query of the login page that asks for the message about the provider.
    /// </summary>
    public const string ErrorValue = "oidc";

    /// <summary>
    ///     The name of the query value that tells a page how a confirmation of identity ended.
    /// </summary>
    public const string ReauthQuery = "reauth";

    /// <summary>
    ///     The value of <see cref="ReauthQuery"/> after a confirmed identity.
    /// </summary>
    public const string ReauthOk = "ok";

    /// <summary>
    ///     The value of <see cref="ReauthQuery"/> after a confirmation that failed.
    /// </summary>
    public const string ReauthFailed = "failed";

    /// <summary>
    ///     The value of <see cref="ReauthQuery"/> after a confirmation that failed because the provider did not report
    ///     a usable moment of sign-in (<c>auth_time</c>).
    /// </summary>
    public const string ReauthMissingAuthTime = "missing-auth-time";

    /// <summary>
    ///     The value of <see cref="ReauthQuery"/> after a confirmation that failed because the reported moment of
    ///     sign-in (<c>auth_time</c>) was not recent enough.
    /// </summary>
    public const string ReauthStaleAuthTime = "stale-auth-time";

    /// <summary>
    ///     The property that marks a challenge as a confirmation of identity for a signature.
    /// </summary>
    private const string ReauthItem = "rc_reauth";

    /// <summary>
    ///     The property that carries the identity of the user a confirmation is for.
    /// </summary>
    private const string UserItem = "rc_user";

    /// <summary>
    ///     The property that carries the project of the signature.
    /// </summary>
    private const string ProjectItem = "rc_project";

    /// <summary>
    ///     The property that carries the review of the signature.
    /// </summary>
    private const string ReviewItem = "rc_review";

    /// <summary>
    ///     The property that carries the slot of the signature.
    /// </summary>
    private const string SlotItem = "rc_slot";

    /// <summary>
    ///     The key under which the throttle reservation of a callback is kept for the length of the request.
    /// </summary>
    private const string ReservationKey = "ReviewCenter.Oidc.Reservation";

    /// <summary>
    ///     How far in the future the moment of the provider's sign-in may lie, to absorb a clock that runs fast.
    /// </summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long the hand-over cookie and a started sign-in stay valid.
    /// </summary>
    private static readonly TimeSpan HandOverLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Registers the provider and the hand-over cookie when sign-in with a provider is enabled.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Identity:Oidc</c> section. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     Only the <c>Enabled</c> switch is read here. The other values are read when the scheme is first used, after
    ///     <see cref="OidcOptionsValidator"/> has checked them at start-up, so an invalid value is reported by the
    ///     start-up check and not by the framework. Call it once, after the configuration is final.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddOidcSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetSection(OidcOptions.SectionName).GetValue<bool>(nameof(OidcOptions.Enabled)))
        {
            return services;
        }

        services.AddAuthentication()
            .AddCookie(ExternalCookieScheme, cookie =>
            {
                cookie.Cookie.Name = ExternalCookieScheme;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = HandOverLifetime;
                cookie.SlidingExpiration = false;
            })
            .AddOpenIdConnect(ProviderScheme, _ => { });
        services.AddOptions<OpenIdConnectOptions>(ProviderScheme)
            .Configure<IOptions<OidcOptions>>((handler, oidc) => ConfigureHandler(handler, oidc.Value));
        return services;
    }

    /// <summary>
    ///     Maps the endpoints of sign-in with a provider, when it is enabled.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     When sign-in with a provider is off the addresses are not mapped and are answered like any unknown
    ///     address. Both forms are bound with <c>[FromForm]</c>, which makes the framework require the antiforgery
    ///     token. <see cref="CompleteEndpoint"/> is a plain GET: it needs the hand-over cookie that only a completed
    ///     provider flow sets, and it changes nothing without it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    public static IEndpointRouteBuilder MapOidcSignIn(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<OidcOptions>>().Value.Enabled)
        {
            return endpoints;
        }

        endpoints.MapPost(
            LoginEndpoint,
            ([FromForm] string? returnUrl) =>
            {
                // The way back is reduced to a local address now and again when the sign-in ends
                var target = SignIn.SafeReturnUrl(returnUrl);
                var properties = new AuthenticationProperties
                {
                    RedirectUri = $"{CompleteEndpoint}?returnUrl={Uri.EscapeDataString(target)}"
                };
                return Results.Challenge(properties, [ProviderScheme]);
            });

        endpoints.MapGet(
            CompleteEndpoint,
            async (
                string? returnUrl,
                HttpContext http,
                UserService users,
                IOptions<OidcOptions> options) =>
            {
                var target = SignIn.SafeReturnUrl(returnUrl);
                var retry = target == "/" ? string.Empty : $"&ReturnUrl={Uri.EscapeDataString(target)}";
                var failure = Results.Redirect($"{SignIn.LoginPath}?error={ErrorValue}{retry}");

                // The hand-over cookie is used once: it is removed whatever happens next
                var handOver = await http.AuthenticateAsync(ExternalCookieScheme);
                await http.SignOutAsync(ExternalCookieScheme);
                if (!handOver.Succeeded || handOver.Principal is null)
                {
                    await users.RecordExternalSignInFailureAsync(
                        "no-hand-over",
                        SignIn.ClientAddress(http),
                        http.RequestAborted);
                    return failure;
                }

                var claims = handOver.Principal;
                if (options.Value.RequireEmailVerified
                    && !string.Equals(claims.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    await users.RecordExternalSignInFailureAsync(
                        "email-not-verified",
                        SignIn.ClientAddress(http),
                        http.RequestAborted);
                    return failure;
                }

                var candidate = UsernameCandidate(claims, options.Value.UsernameClaim);
                var displayName = claims.FindFirstValue("name") ?? candidate;
                var result = await users.SignInExternalAsync(
                    claims.FindFirstValue("iss"),
                    claims.FindFirstValue("sub"),
                    candidate,
                    displayName,
                    http.RequestAborted);

                // One answer for every refusal, so a disabled account is not revealed (the audit trail says why)
                if (result.User is null)
                {
                    if (result.Refusal == ExternalSignInRefusal.Invalid)
                    {
                        await users.RecordExternalSignInFailureAsync(
                            "invalid-identity",
                            SignIn.ClientAddress(http),
                            http.RequestAborted);
                    }

                    return failure;
                }

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    SignIn.BuildPrincipal(result.User),
                    new AuthenticationProperties { IsPersistent = false });
                return Results.LocalRedirect(target);
            });

        endpoints.MapPost(
            ReauthEndpoint,
            async (
                [FromForm] int projectId,
                [FromForm] string? reviewId,
                [FromForm] int slotId,
                [FromForm] string? returnUrl,
                HttpContext http,
                SigningService signing) =>
            {
                var target = SignIn.SafeReturnUrl(returnUrl);
                var username = http.User.Identity?.Name;
                var displayName = http.User.FindFirstValue(SignIn.DisplayNameClaimType);
                if (string.IsNullOrEmpty(username)
                    || string.IsNullOrEmpty(displayName)
                    || string.IsNullOrEmpty(reviewId)
                    || reviewId.Length > ReviewLimits.MaxReviewIdLength)
                {
                    return Results.LocalRedirect(WithReauth(target, ReauthFailed));
                }

                // The same rules as the signature itself, so no confirmation is asked for one the user could not make
                User user;
                try
                {
                    user = await signing.RequireConfirmableAsync(
                        new Actor(username, displayName),
                        projectId,
                        reviewId,
                        slotId,
                        http.RequestAborted);
                }
                catch (DomainRuleException)
                {
                    return Results.LocalRedirect(WithReauth(target, ReauthFailed));
                }

                // The properties travel in the protected state of the request, so the provider and the browser
                // cannot change them
                var properties = new AuthenticationProperties { RedirectUri = target };
                properties.Items[ReauthItem] = "1";
                properties.Items[UserItem] = user.Id.ToString(CultureInfo.InvariantCulture);
                properties.Items[ProjectItem] = projectId.ToString(CultureInfo.InvariantCulture);
                properties.Items[ReviewItem] = reviewId;
                properties.Items[SlotItem] = slotId.ToString(CultureInfo.InvariantCulture);
                return Results.Challenge(properties, [ProviderScheme]);
            }).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    ///     Adds or replaces the <c>reauth</c> query value of a local address.
    /// </summary>
    /// <param name="target">The local address, possibly with a query.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>The address with exactly one <c>reauth</c> value; any fragment is dropped.</returns>
    internal static string WithReauth(string target, string value)
    {
        var safe = SignIn.SafeReturnUrl(target);
        var hash = safe.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            safe = safe[..hash];
        }

        var question = safe.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? safe : safe[..question];
        var kept = question < 0
            ? []
            : safe[(question + 1)..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith(ReauthQuery + "=", StringComparison.Ordinal))
                .ToList();
        kept.Add($"{ReauthQuery}={value}");
        return $"{path}?{string.Join('&', kept)}";
    }

    /// <summary>
    ///     Picks the name that a new account is derived from.
    /// </summary>
    /// <param name="claims">The validated claims.</param>
    /// <param name="usernameClaim">The claim the administrator chose.</param>
    /// <returns>The chosen claim, else the part of the e-mail address before the <c>@</c>, else the subject.</returns>
    private static string? UsernameCandidate(ClaimsPrincipal claims, string usernameClaim)
    {
        var chosen = claims.FindFirstValue(usernameClaim);
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            return chosen;
        }

        var email = claims.FindFirstValue("email");
        var at = email?.IndexOf('@', StringComparison.Ordinal) ?? -1;
        return at > 0 ? email![..at] : claims.FindFirstValue("sub");
    }

    /// <summary>
    ///     Applies the settings and the event handlers to the framework handler.
    /// </summary>
    /// <param name="handler">The options of the handler.</param>
    /// <param name="settings">The validated settings.</param>
    private static void ConfigureHandler(OpenIdConnectOptions handler, OidcOptions settings)
    {
        handler.Authority = settings.Authority;
        handler.RequireHttpsMetadata = !settings.IsLoopbackHttp();
        handler.ClientId = settings.ClientId;
        handler.ClientSecret = settings.ClientSecret;
        handler.SignInScheme = ExternalCookieScheme;
        handler.CallbackPath = CallbackPath;

        // Authorization code flow with PKCE only; the claims come from the identity token alone and stay as sent
        handler.ResponseType = "code";
        handler.ResponseMode = "query";
        handler.UsePkce = true;
        handler.SaveTokens = false;
        handler.MapInboundClaims = false;
        handler.GetClaimsFromUserInfoEndpoint = false;

        // The handler drops the issuer claim by default; the account is bound to it, so it must stay
        handler.ClaimActions.Remove("iss");
        handler.RemoteAuthenticationTimeout = HandOverLifetime;
        handler.Scope.Clear();
        foreach (var scope in settings.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            handler.Scope.Add(scope);
        }

        // The cookies of a started sign-in work on plain http during development, like the session cookie
        handler.CorrelationCookie.Name = "ReviewCenter.Oidc.Correlation.";
        handler.CorrelationCookie.SameSite = SameSiteMode.Lax;
        handler.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        handler.NonceCookie.Name = "ReviewCenter.Oidc.Nonce.";
        handler.NonceCookie.SameSite = SameSiteMode.Lax;
        handler.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        handler.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = RedirectingAsync,
            OnMessageReceived = MessageReceivedAsync,
            OnTicketReceived = TicketReceivedAsync,
            OnRemoteFailure = RemoteFailureAsync
        };
    }

    /// <summary>
    ///     Asks the provider for a fresh sign-in when the request is a confirmation of identity.
    /// </summary>
    /// <param name="context">The context of the redirect.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    ///     <c>prompt=login</c> makes the provider ask the person to sign in again, and <c>max_age=0</c> says that
    ///     no earlier sign-in is good enough. A normal sign-in is left as it is.
    /// </remarks>
    private static Task RedirectingAsync(RedirectContext context)
    {
        if (context.Properties.Items.ContainsKey(ReauthItem))
        {
            context.ProtocolMessage.Prompt = "login";
            context.ProtocolMessage.MaxAge = "0";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Counts a callback in the throttle and refuses it when the client has made too many.
    /// </summary>
    /// <param name="context">The context of the received message.</param>
    /// <returns>A task that completes when the callback is counted or refused.</returns>
    private static async Task MessageReceivedAsync(MessageReceivedContext context)
    {
        var http = context.HttpContext;
        var throttle = http.RequestServices.GetRequiredService<SignInThrottle>();
        var reservation = throttle.TryBeginAddressOnly(SignIn.ClientAddress(http));
        if (reservation is null)
        {
            await FailAsync(http, context.Properties, "throttled");
            context.HandleResponse();
            return;
        }

        http.Items[ReservationKey] = reservation;
    }

    /// <summary>
    ///     Finishes a callback whose identity token was accepted by the handler.
    /// </summary>
    /// <param name="context">The context of the received ticket.</param>
    /// <returns>A task that completes when the callback is answered or handed on.</returns>
    /// <remarks>
    ///     A normal sign-in is handed on: the handler stores the claims in the hand-over cookie and goes to
    ///     <see cref="CompleteEndpoint"/>, and the callback no longer counts against the client. A confirmation of
    ///     identity is answered here, so that the claims never replace the session of the user. The address to return
    ///     to is <c>ReturnUri</c> of the context, because the handler clears it from the
    ///     properties of the ticket.
    /// </remarks>
    private static async Task TicketReceivedAsync(TicketReceivedContext context)
    {
        var http = context.HttpContext;
        var properties = context.Properties;
        if (properties is null || !properties.Items.ContainsKey(ReauthItem))
        {
            GiveBackThrottle(http);
            return;
        }

        context.HandleResponse();
        var users = http.RequestServices.GetRequiredService<UserService>();
        var target = SignIn.SafeReturnUrl(context.ReturnUri);
        var user = await CurrentUserAsync(http);
        if (user is not { Enabled: true })
        {
            // The session ended while the person was at the provider: nothing to confirm for
            await users.RecordExternalSignInFailureAsync(
                "session-ended",
                SignIn.ClientAddress(http),
                http.RequestAborted);
            context.Response.Redirect($"{SignIn.LoginPath}?error={ErrorValue}");
            return;
        }

        var reason = VerifyReauth(
            user,
            context.Principal,
            properties,
            http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            http.RequestServices.GetRequiredService<IOptions<OidcOptions>>().Value);
        if (reason is not null)
        {
            await users.RecordExternalReauthFailureAsync(user, reason, http.RequestAborted);

            // Only the two causes about the moment of sign-in reach the page, because an administrator can fix
            // them; every other reason stays the generic failure. The audit reason is the same either way
            var shown = reason is ReauthMissingAuthTime or ReauthStaleAuthTime ? reason : ReauthFailed;
            context.Response.Redirect(WithReauth(target, shown));
            return;
        }

        http.RequestServices.GetRequiredService<ReauthProofStore>().Issue(
            user.Id,
            int.Parse(properties.Items[ProjectItem]!, CultureInfo.InvariantCulture),
            properties.Items[ReviewItem]!,
            int.Parse(properties.Items[SlotItem]!, CultureInfo.InvariantCulture));
        GiveBackThrottle(http);
        context.Response.Redirect(WithReauth(target, ReauthOk));
    }

    /// <summary>
    ///     Answers a callback the handler could not accept.
    /// </summary>
    /// <param name="context">The context of the failure.</param>
    /// <returns>A task that completes when the failure is recorded and the browser is sent on.</returns>
    /// <remarks>The text of the failure is used only to choose a reason class and is never stored or shown.</remarks>
    private static async Task RemoteFailureAsync(RemoteFailureContext context)
    {
        context.HandleResponse();
        await FailAsync(context.HttpContext, context.Properties, ClassifyFailure(context.Failure));
    }

    /// <summary>
    ///     Records a failed sign-in or confirmation and sends the browser to the page that tells about it.
    /// </summary>
    /// <param name="http">The request context.</param>
    /// <param name="properties">The properties of the started sign-in; <c>null</c> when they are not known.</param>
    /// <param name="reason">The reason class; a fixed word.</param>
    /// <returns>A task that completes when the browser is redirected.</returns>
    /// <remarks>
    ///     A failed confirmation goes back to the page of the signature and is recorded as a failed
    ///     confirmation of that user. Every other failure goes to the login page with one generic message.
    /// </remarks>
    private static async Task FailAsync(HttpContext http, AuthenticationProperties? properties, string reason)
    {
        var users = http.RequestServices.GetRequiredService<UserService>();
        if (properties is not null && properties.Items.ContainsKey(ReauthItem))
        {
            var user = await CurrentUserAsync(http);
            if (user is { IsExternal: true })
            {
                await users.RecordExternalReauthFailureAsync(user, reason, http.RequestAborted);
            }
            else
            {
                await users.RecordExternalSignInFailureAsync(reason, SignIn.ClientAddress(http), http.RequestAborted);
            }

            http.Response.Redirect(WithReauth(SignIn.SafeReturnUrl(properties.RedirectUri), ReauthFailed));
            return;
        }

        await users.RecordExternalSignInFailureAsync(reason, SignIn.ClientAddress(http), http.RequestAborted);
        http.Response.Redirect($"{SignIn.LoginPath}?error={ErrorValue}");
    }

    /// <summary>
    ///     Chooses the reason class of a failure of the handler.
    /// </summary>
    /// <param name="failure">The failure; may be null.</param>
    /// <returns>
    ///     <c>provider-error</c> when the provider answered with an error, <c>state-or-nonce</c> when the request
    ///     does not match the one that was started, <c>token-invalid</c> when the identity token was refused, and
    ///     <c>other</c> for everything else.
    /// </returns>
    internal static string ClassifyFailure(Exception? failure)
    {
        var text = failure?.Message ?? string.Empty;
        if (text.Contains("Message contains error", StringComparison.Ordinal)
            || text.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return "provider-error";
        }

        if (text.Contains("Correlation failed", StringComparison.Ordinal)
            || text.Contains("State", StringComparison.Ordinal)
            || text.Contains("nonce", StringComparison.OrdinalIgnoreCase))
        {
            return "state-or-nonce";
        }

        return failure is Microsoft.IdentityModel.Tokens.SecurityTokenException
            || failure?.InnerException is Microsoft.IdentityModel.Tokens.SecurityTokenException
            ? "token-invalid"
            : "other";
    }

    /// <summary>
    ///     Checks that a fresh sign-in at the provider really is a confirmation of identity of the user who asked.
    /// </summary>
    /// <param name="user">The signed-in user as stored now.</param>
    /// <param name="claims">The validated claims of the identity token; may be null.</param>
    /// <param name="properties">The properties of the started confirmation.</param>
    /// <param name="now">The current time.</param>
    /// <param name="options">The settings.</param>
    /// <returns>
    ///     <c>null</c> when every condition holds; otherwise the reason class of the first one that does not
    ///     (<c>not-external</c>, <c>subject-mismatch</c>, <c>issuer-mismatch</c>, <c>missing-auth-time</c>,
    ///     <c>stale-auth-time</c> or <c>other</c>).
    /// </returns>
    /// <remarks>
    ///     The user must have an account at the provider, must be the user the confirmation was started for, and the
    ///     issuer and subject of the token must be the stored ones, not just some valid ones. The moment of the
    ///     sign-in (<c>auth_time</c>) must be present, a whole number of seconds, not older than
    ///     <see cref="OidcOptions.ReauthMaxAgeSeconds"/> and not more than a minute in the future. Anything else
    ///     refuses. The method reads and changes nothing else, so it is pure and thread-safe.
    /// </remarks>
    internal static string? VerifyReauth(
        User user,
        ClaimsPrincipal? claims,
        AuthenticationProperties properties,
        DateTimeOffset now,
        OidcOptions options)
    {
        if (!user.IsExternal)
        {
            return "not-external";
        }

        if (claims is null
            || !properties.Items.TryGetValue(UserItem, out var userText)
            || userText != user.Id.ToString(CultureInfo.InvariantCulture)
            || !IsWholeNumber(Item(properties, ProjectItem))
            || !IsWholeNumber(Item(properties, SlotItem))
            || string.IsNullOrEmpty(Item(properties, ReviewItem)))
        {
            return "other";
        }

        if (!string.Equals(claims.FindFirstValue("iss"), user.ExternalIssuer, StringComparison.Ordinal))
        {
            return "issuer-mismatch";
        }

        if (!string.Equals(claims.FindFirstValue("sub"), user.ExternalSubject, StringComparison.Ordinal))
        {
            return "subject-mismatch";
        }

        // The value must be a whole number of seconds that a date can hold: outside that range converting it to a
        // date would throw, so it is refused like a missing value and no exception can escape
        var authTime = claims.FindFirstValue("auth_time");
        if (!long.TryParse(authTime, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds()
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return ReauthMissingAuthTime;
        }

        var age = now - DateTimeOffset.FromUnixTimeSeconds(seconds);
        return age <= TimeSpan.FromSeconds(options.ReauthMaxAgeSeconds) && age >= -FutureTolerance
            ? null
            : ReauthStaleAuthTime;
    }

    /// <summary>
    ///     Reads a property of a started sign-in.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="key">The name of the property.</param>
    /// <returns>The value, or <c>null</c> when it is not there.</returns>
    private static string? Item(AuthenticationProperties properties, string key)
    {
        return properties.Items.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    ///     Tells whether a property holds a whole number.
    /// </summary>
    /// <param name="value">The value; may be null.</param>
    /// <returns><c>true</c> when it is a non-negative whole number.</returns>
    private static bool IsWholeNumber(string? value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    ///     Gives the throttle slot of the current callback back, because the callback was accepted.
    /// </summary>
    /// <param name="http">The request context.</param>
    private static void GiveBackThrottle(HttpContext http)
    {
        if (http.Items.TryGetValue(ReservationKey, out var item) && item is SignInThrottle.Reservation reservation)
        {
            http.RequestServices.GetRequiredService<SignInThrottle>().Succeeded(reservation);
        }
    }

    /// <summary>
    ///     Reads the stored user of the session cookie of the request.
    /// </summary>
    /// <param name="http">The request context.</param>
    /// <returns>The user, or <c>null</c> when nobody is signed in or the user is gone.</returns>
    /// <remarks>
    ///     The callback address is handled before the framework authenticates the request, so the session cookie is
    ///     read explicitly. The cookie check has the same rules as for any other request: a disabled or changed user
    ///     is rejected.
    /// </remarks>
    private static async Task<User?> CurrentUserAsync(HttpContext http)
    {
        var session = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var name = session.Succeeded ? session.Principal?.Identity?.Name : null;
        return name is null
            ? null
            : await http.RequestServices.GetRequiredService<UserService>().GetAsync(name, http.RequestAborted);
    }
}
