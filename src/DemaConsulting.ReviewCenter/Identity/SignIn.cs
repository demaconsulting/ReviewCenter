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
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     Cookie sign-in for the web server: the authentication setup, the login and logout endpoints, the
///     return-URL check, and the conversion between a signed-in principal and an <see cref="Actor"/>.
/// </summary>
/// <remarks>
///     Signing in is plain HTTP, not a Blazor circuit event: the login page posts a normal form to
///     <see cref="LoginEndpoint"/>, which checks the password with <see cref="UserService"/> and issues the
///     authentication cookie. Both endpoints require the antiforgery token. One generic failure redirect covers
///     an unknown user, a wrong password, a disabled account and a lockout, so the response does not reveal
///     which accounts exist. On every request the cookie is checked against the user store, so a disabled or
///     changed account has to sign in again. The class is stateless and therefore thread-safe.
/// </remarks>
internal static class SignIn
{
    /// <summary>
    ///     The name of the authentication cookie.
    /// </summary>
    public const string CookieName = "ReviewCenter.Auth";

    /// <summary>
    ///     The page that shows the login form; anonymous requests for protected pages are redirected here.
    /// </summary>
    public const string LoginPath = "/login";

    /// <summary>
    ///     The endpoint that receives the login form.
    /// </summary>
    public const string LoginEndpoint = "/auth/login";

    /// <summary>
    ///     The endpoint that ends the session.
    /// </summary>
    public const string LogoutEndpoint = "/auth/logout";

    /// <summary>
    ///     The claim type that carries the display name.
    /// </summary>
    public const string DisplayNameClaimType = "display_name";

    /// <summary>
    ///     The role name that is required for the user administration page.
    /// </summary>
    public const string AdminRole = nameof(UserRole.Admin);

    /// <summary>
    ///     Registers cookie authentication and authorization.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Auth</c> section. Must not be null.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     The cookie is HttpOnly, SameSite=Lax and Secure when the request is https (<c>SameAsRequest</c>, so
    ///     it also works on plain http during development); it is a session cookie whose ticket slides over
    ///     <see cref="AuthOptions.SessionHours"/>. An anonymous request for a protected page is redirected to
    ///     <see cref="LoginPath"/>; a signed-in user without the needed role gets a 403 with a short "Access denied" page.
    /// </remarks>
    public static IServiceCollection AddSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(options => options.SessionHours > 0, "Auth:SessionHours must be greater than 0.");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.LoginPath = LoginPath;
                cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    // A signed-in user without the needed role gets a short explanation instead of a blank page
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    return context.Response.WriteAsync(
                        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />" +
                        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />" +
                        "<title>Access denied - ReviewCenter</title><link rel=\"stylesheet\" href=\"/app.css\" /></head>" +
                        "<body><main class=\"page\" data-testid=\"access-denied\"><h1>Access denied</h1>" +
                        "<p>Your account does not have permission to open this page.</p>" +
                        "<p><a href=\"/projects\" data-testid=\"access-denied-projects-link\">Back to projects</a></p>" +
                        "</main></body></html>");
                };
                cookie.Events.OnValidatePrincipal = ValidatePrincipalAsync;
            });

        // The session length comes from the options, which are read when the cookie options are first needed
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((cookie, auth) =>
                cookie.ExpireTimeSpan = TimeSpan.FromHours(auth.Value.SessionHours));

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    ///     Maps the login and logout endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder. Must not be null.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapSignIn(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The login form fields are bound with [FromForm], which makes the framework require the antiforgery token
        endpoints.MapPost(
            LoginEndpoint,
            async (
                [FromForm] string? username,
                [FromForm] string? password,
                [FromForm] string? returnUrl,
                HttpContext http,
                UserService users) =>
            {
                var target = SafeReturnUrl(returnUrl);
                var user = await users.AuthenticateAsync(username, password, http.RequestAborted);

                // One generic answer for every failure; the query keeps the (safe) place the user wanted to go to
                if (user is null)
                {
                    var retry = target == "/" ? string.Empty : $"&ReturnUrl={Uri.EscapeDataString(target)}";
                    return Results.Redirect($"{LoginPath}?error=1{retry}");
                }

                await http.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    BuildPrincipal(user),
                    new AuthenticationProperties { IsPersistent = false });
                return Results.LocalRedirect(target);
            });

        // This endpoint binds no form field, so the framework does not check the token by itself
        endpoints.MapPost(
            LogoutEndpoint,
            async (HttpContext http, IAntiforgery antiforgery) =>
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(http);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect("/");
            });

        return endpoints;
    }

    /// <summary>
    ///     Reduces a requested return URL to one that stays on this site.
    /// </summary>
    /// <param name="returnUrl">The requested URL; may be null, empty or hostile.</param>
    /// <returns>
    ///     The URL when it is a site-relative path (starts with a single <c>/</c> and holds no control
    ///     characters); otherwise <c>/</c>. This stops the login from being used to bounce a user to another
    ///     site (an open redirect).
    /// </returns>
    public static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return "/";
        }

        // "//host" and "/\host" are read by browsers as another host
        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return "/";
        }

        return returnUrl.Any(char.IsControl) ? "/" : returnUrl;
    }

    /// <summary>
    ///     Gets the signed-in user as an <see cref="Actor"/> for the Core services.
    /// </summary>
    /// <param name="provider">The authentication state provider of the page. Must not be null.</param>
    /// <returns>The actor built from the claims of the current user.</returns>
    /// <exception cref="InvalidOperationException">Thrown when nobody is signed in (a protected page was reached anonymously).</exception>
    public static async Task<Actor> GetActorAsync(this AuthenticationStateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var principal = (await provider.GetAuthenticationStateAsync()).User;
        var username = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name : null;
        var displayName = principal.FindFirstValue(DisplayNameClaimType);
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(displayName))
        {
            throw new InvalidOperationException("Nobody is signed in.");
        }

        return new Actor(username, displayName);
    }

    /// <summary>
    ///     Builds the principal that is stored in the authentication cookie.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>A principal with the user name, display name and role; never the password or its hash.</returns>
    private static ClaimsPrincipal BuildPrincipal(User user)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(DisplayNameClaimType, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            ],
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    ///     Rejects the cookie of a user who is gone, disabled or changed since the cookie was issued.
    /// </summary>
    /// <param name="context">The validation context of the request.</param>
    /// <returns>A task that completes when the cookie is accepted or rejected.</returns>
    /// <remarks>
    ///     The user store is read on each request. A changed role or display name also ends the session, so the
    ///     user signs in again and gets the current values.
    /// </remarks>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserService>();
        var principal = context.Principal;
        var username = principal?.Identity?.Name;
        var user = username is null ? null : await users.GetAsync(username, context.HttpContext.RequestAborted);

        if (principal is null
            || user is null
            || !user.Enabled
            || principal.FindFirstValue(ClaimTypes.Role) != user.Role.ToString()
            || principal.FindFirstValue(DisplayNameClaimType) != user.DisplayName)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
