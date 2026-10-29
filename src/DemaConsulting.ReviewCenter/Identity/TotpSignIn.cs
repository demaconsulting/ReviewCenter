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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     The second step of a sign-in with a time-based one-time password: the settings registration and the
///     endpoint that receives the code.
/// </summary>
/// <remarks>
///     <para>
///     The first step (<see cref="SignIn.LoginEndpoint"/>) checks the password; for an account with a second factor
///     it issues no session at all. It sets the <see cref="CookieName"/> cookie, which holds a random token that
///     refers to a short-lived, single-use challenge in <see cref="TotpChallengeStore"/> (bound to the user and the
///     client address), and sends the browser to <see cref="LoginPath"/>. Only <see cref="CodeEndpoint"/>, with a
///     right code, issues the authentication cookie; every failure path leaves the browser without one.
///     </para>
///     <para>
///     The endpoint requires the antiforgery token (the code is bound with <c>[FromForm]</c>). Before the code is
///     checked, <see cref="SignInThrottle"/> counts the attempt per client address and account; a wrong, reused,
///     malformed or expired code is answered with the same redirect, and a wrong code counts toward the account
///     lockout like a wrong password. Nothing here is ever logged or audited: no code, no token. The class is
///     stateless and thread-safe.
///     </para>
/// </remarks>
internal static class TotpSignIn
{
    /// <summary>
    ///     The name of the cookie that carries the pending-challenge token.
    /// </summary>
    public const string CookieName = "ReviewCenter.TotpChallenge";

    /// <summary>
    ///     The page that asks for the code.
    /// </summary>
    public const string LoginPath = "/login/totp";

    /// <summary>
    ///     The endpoint that receives the code form.
    /// </summary>
    public const string CodeEndpoint = "/auth/login-totp";

    /// <summary>
    ///     The value of the <c>error</c> query of the login page that says the second step is no longer open.
    /// </summary>
    public const string ErrorExpired = "totp-expired";

    /// <summary>
    ///     Registers the settings and the services of the second factor.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Identity:Totp</c> section. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     The settings are checked when they are first used, at start-up (see <see cref="TotpOptionsValidator"/>).
    ///     The stores and the code algorithm are registered by the host, next to the other services.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IServiceCollection AddTotpSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<TotpOptions>().Bind(configuration.GetSection(TotpOptions.SectionName));
        services.AddSingleton<IValidateOptions<TotpOptions>, TotpOptionsValidator>();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TotpOptions>>().Value;
            return new TotpSettings(options.Enabled, options.Issuer.Trim());
        });
        return services;
    }

    /// <summary>
    ///     Maps the endpoint that receives the code, when the second factor is enabled.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>While the second factor is switched off nothing is mapped, so the address does not exist.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    public static IEndpointRouteBuilder MapTotpSignIn(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<TotpOptions>>().Value.Enabled)
        {
            return endpoints;
        }

        // The code is bound with [FromForm], which makes the framework require the antiforgery token
        endpoints.MapPost(
            CodeEndpoint,
            async (
                [FromForm] string? code,
                HttpContext http,
                TotpService totp,
                AuditService audit,
                SignInThrottle throttle) =>
            {
                var address = SignIn.ClientAddress(http);
                var token = http.Request.Cookies[CookieName];
                var challenge = totp.PeekChallenge(token, address);
                if (challenge is null)
                {
                    http.Response.Cookies.Delete(CookieName, CookieOptions(http));
                    return Results.Redirect($"{SignIn.LoginPath}?error={ErrorExpired}");
                }

                // The same limits as the password step: per client address and account
                var reservation = throttle.TryBegin(address, challenge.Username);
                if (reservation is null)
                {
                    await audit.TryRecordCoalescedAsync(
                        new AuditDraft(
                            "auth.sign-in-throttled",
                            null,
                            "not signed in",
                            null,
                            null,
                            $"too many attempts, second step, name '{AuditText.Clean(challenge.Username, ReviewLimits.MaxUsernameLength)}'"),
                        $"auth.sign-in-throttled|totp|{challenge.Username.ToLowerInvariant()}",
                        http.RequestAborted);
                    return Results.Redirect($"{LoginPath}?error={SignIn.ThrottledError}");
                }

                var result = await totp.CompleteSignInAsync(token, address, code, http.RequestAborted);
                if (result.Outcome != TotpSignInOutcome.Success || result.User is not { } user)
                {
                    // A wrong code keeps the challenge for another try; anything else ends the second step. No cookie
                    // is ever issued here
                    if (result.Outcome == TotpSignInOutcome.Failed)
                    {
                        return Results.Redirect($"{LoginPath}?error=1");
                    }

                    http.Response.Cookies.Delete(CookieName, CookieOptions(http));
                    return Results.Redirect($"{SignIn.LoginPath}?error={ErrorExpired}");
                }

                // The attempt does not count against the client any more
                throttle.Succeeded(reservation);
                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    SignIn.BuildPrincipal(user),
                    new AuthenticationProperties { IsPersistent = false });
                http.Response.Cookies.Delete(CookieName, CookieOptions(http));

                // Best effort: a failure to record is logged by the service and never stops the sign-in
                await audit.TryRecordAsync(
                    AuditDraft.By(
                        new Actor(user.Username, user.DisplayName),
                        "auth.sign-in",
                        result.UsedRecoveryCode ? "second factor: recovery code" : "second factor: authenticator code"),
                    http.RequestAborted);
                var target = SignIn.SafeReturnUrl(challenge.ReturnUrl);
                return Results.LocalRedirect(user.MustChangePassword ? SignIn.AccountPath : target);
            });
        return endpoints;
    }

    /// <summary>
    ///     Gets the options of the challenge cookie.
    /// </summary>
    /// <param name="http">The request context; decides whether the cookie is marked Secure.</param>
    /// <returns>
    ///     HttpOnly, SameSite=Strict (it is only needed by the form post from this site), Secure when the request is
    ///     https, valid for as long as a challenge lives, and sent for the whole site.
    /// </returns>
    internal static CookieOptions CookieOptions(HttpContext http)
    {
        // Secure follows the request scheme like the session cookie (SameAsRequest): the server may be reached over
        // plain http on a loopback address, where a Secure cookie would never be sent back
#pragma warning disable S2092
        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            MaxAge = TotpChallengeStore.Lifetime,
            Path = "/",
            IsEssential = true
        };
#pragma warning restore S2092
    }
}
