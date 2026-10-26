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
using Microsoft.AspNetCore.Http.Features;
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
///     changed account has to sign in again; an open page is checked by the same rule, <see cref="IsCurrent"/>,
///     every <see cref="AuthOptions.RevalidationSeconds"/>. The class is stateless and therefore thread-safe.
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
    ///     The registration of <see cref="RevalidatingAuthenticationStateProvider"/> replaces the framework default and
    ///     therefore has to be made after the Razor components are added. The settings are checked when they are first
    ///     used (at start-up): <see cref="AuthOptions.SessionHours"/> must be above 0 and
    ///     <see cref="AuthOptions.RevalidationSeconds"/> at least <see cref="AuthOptions.MinimumRevalidationSeconds"/>.
    /// </remarks>
    public static IServiceCollection AddSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(options => options.SessionHours > 0, "Auth:SessionHours must be greater than 0.")
            .Validate(
                options => options.RevalidationSeconds >= AuthOptions.MinimumRevalidationSeconds,
                $"Auth:RevalidationSeconds must be at least {AuthOptions.MinimumRevalidationSeconds}.");

        // An open page (circuit) re-checks its user through this provider. A plain AddScoped is needed, not
        // TryAddScoped: AddRazorComponents().AddInteractiveServerComponents() registers the framework default with
        // TryAdd, and the host calls AddSignIn after it, so this last registration is the one that wins
        services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();

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
                cookie.Events.OnRedirectToLogin = context =>
                {
                    // The visitor comes back to the exact address that was requested: the raw request target keeps the
                    // escapes of a deep link as sent (the decoded path would turn a literal %2F or %20 in a file name
                    // into a separator or a space), and only a local path is accepted as the return address
                    var raw = context.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
                    var target = SafeReturnUrl(string.IsNullOrEmpty(raw)
                        ? EscapedPathAndQuery(context.Request)
                        : raw);
                    var retry = target == "/" ? string.Empty : $"?ReturnUrl={Uri.EscapeDataString(target)}";
                    context.Response.Redirect($"{LoginPath}{retry}");
                    return Task.CompletedTask;
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
                UserService users,
                AuditService audit) =>
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

                // Best effort: a failure to record is logged by the service and never stops the sign-in
                await audit.TryRecordAsync(
                    AuditDraft.By(new Actor(user.Username, user.DisplayName), "auth.sign-in", "signed in"),
                    http.RequestAborted);
                return Results.LocalRedirect(target);
            });

        // This endpoint binds no form field, so the framework does not check the token by itself
        endpoints.MapPost(
            LogoutEndpoint,
            async (HttpContext http, IAntiforgery antiforgery, AuditService audit) =>
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(http);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

                // The actor is read and the entry recorded before the cookie is cleared; nobody signed in means
                // nothing to record
                var username = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null;
                var displayName = http.User.FindFirstValue(DisplayNameClaimType);
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(displayName))
                {
                    await audit.TryRecordAsync(
                        AuditDraft.By(new Actor(username, displayName), "auth.sign-out", "signed out"),
                        http.RequestAborted);
                }

                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect("/");
            });

        return endpoints;
    }

    /// <summary>
    ///     Writes the path and query of a request as an escaped address, for a server that has no raw request target.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The address; each path segment is escaped, so a literal <c>%2F</c> in a name stays literal.</returns>
    private static string EscapedPathAndQuery(HttpRequest request)
    {
        var path = string.Join('/', (request.Path.Value ?? string.Empty).Split('/').Select(Uri.EscapeDataString));
        return path + request.QueryString;
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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> when nobody is signed in. This is what an action
    ///     on an open page meets when the sign-in state ended (the user was disabled, removed or changed) before
    ///     the page was replaced; the page shows the message next to the control that was used instead of failing.
    /// </exception>
    /// <remarks>
    ///     A rule violation, not a programming error, because an open page can legitimately lose its user between
    ///     two clicks. The method reads the state of the provider and changes nothing.
    /// </remarks>
    public static async Task<Actor> GetActorAsync(this AuthenticationStateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var principal = (await provider.GetAuthenticationStateAsync()).User;
        var username = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name : null;
        var displayName = principal.FindFirstValue(DisplayNameClaimType);
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(displayName))
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "You are no longer signed in, so nothing was saved. Reload the page to sign in again.");
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

        // The same rule decides for open pages (see RevalidatingAuthenticationStateProvider)
        if (!IsCurrent(principal, user))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>
    ///     Decides whether a signed-in principal still matches the stored user it was issued for.
    /// </summary>
    /// <param name="principal">The signed-in principal; may be null.</param>
    /// <param name="user">The user as stored now, or <c>null</c> when there is no such user.</param>
    /// <returns>
    ///     <c>true</c> only when both are present, the user is enabled, and the role and display name in the
    ///     principal are equal to the stored ones; otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    ///     This is the single rule behind both the cookie check on each request and the periodic check of an open
    ///     page, so the two cannot drift apart. A changed role or display name counts as not current so that
    ///     the user signs in again and gets the current values. The method is pure and thread-safe.
    /// </remarks>
    internal static bool IsCurrent(ClaimsPrincipal? principal, User? user)
    {
        return principal is not null
            && user is { Enabled: true }
            && principal.FindFirstValue(ClaimTypes.Role) == user.Role.ToString()
            && principal.FindFirstValue(DisplayNameClaimType) == user.DisplayName;
    }
}
