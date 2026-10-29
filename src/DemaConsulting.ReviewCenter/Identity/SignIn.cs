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
///     authentication cookie. Both endpoints require the antiforgery token. Before the password is checked,
///     <see cref="SignInThrottle"/> counts the attempt per client address and user name; a client over its limit
///     is sent back with a "too many attempts" message and nothing is checked, whatever the name or the
///     password. Otherwise one generic failure redirect covers an unknown user, a wrong password, a disabled
///     account and a lockout, so the response does not reveal which accounts exist. On every request the cookie
///     is checked against the user store, so a disabled or changed account has to sign in again; an open page is
///     checked by the same rule, <see cref="IsCurrent"/>, every <see cref="AuthOptions.RevalidationSeconds"/>.
///     The class is stateless and therefore thread-safe (the throttle state lives in the
///     <see cref="SignInThrottle"/> singleton).
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
    ///     The value of the <c>error</c> query of the login page that asks for the "too many attempts" message.
    /// </summary>
    public const string ThrottledError = "throttled";

    /// <summary>
    ///     The claim type that carries the display name.
    /// </summary>
    public const string DisplayNameClaimType = "display_name";

    /// <summary>
    ///     The claim type that marks an account that must change its temporary password before anything else.
    /// </summary>
    public const string MustChangePasswordClaimType = "must_change_password";

    /// <summary>
    ///     The page where the user changes the own password; the only page a user with a temporary password may use.
    /// </summary>
    public const string AccountPath = "/account";

    /// <summary>
    ///     The role name that is required for the pages only a system administrator may use.
    /// </summary>
    public const string AdminRole = "SystemAdmin";

    /// <summary>
    ///     Registers cookie authentication and authorization.
    /// </summary>
    /// <param name="services">The service collection. Must not be null.</param>
    /// <param name="configuration">The configuration that holds the <c>Auth</c> and <c>Identity:Throttle</c> sections. Must not be null.</param>
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
    ///     The throttle settings are checked the same way: each limit above 0, <see cref="SignInThrottleOptions.WindowSeconds"/>
    ///     at most <see cref="SignInThrottleOptions.MaximumWindowSeconds"/>, and
    ///     <see cref="SignInThrottleOptions.MaxFailuresPerAddress"/> not below <see cref="SignInThrottleOptions.MaxFailuresPerName"/>.
    ///     The throttle needs a <see cref="TimeProvider"/> in the service collection.
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

        services.AddOptions<SignInThrottleOptions>()
            .Bind(configuration.GetSection(SignInThrottleOptions.SectionName))
            .Validate(
                options => options.MaxFailuresPerName > 0,
                $"{SignInThrottleOptions.SectionName}:MaxFailuresPerName must be greater than 0.")
            .Validate(
                options => options.MaxFailuresPerAddress > 0,
                $"{SignInThrottleOptions.SectionName}:MaxFailuresPerAddress must be greater than 0.")
            .Validate(
                options => options.MaxFailuresPerAddress >= options.MaxFailuresPerName,
                $"{SignInThrottleOptions.SectionName}:MaxFailuresPerAddress must not be less than MaxFailuresPerName.")
            .Validate(
                options => options.WindowSeconds > 0 && options.WindowSeconds <= SignInThrottleOptions.MaximumWindowSeconds,
                $"{SignInThrottleOptions.SectionName}:WindowSeconds must be between 1 and {SignInThrottleOptions.MaximumWindowSeconds}.");
        services.AddSingleton<SignInThrottle>();

        // Sign-in with an OpenID Connect provider: only the settings are registered here, and they are not read
        // until start-up; the provider itself is added by AddOidcSignIn once the configuration is final
        services.AddOptions<OidcOptions>().Bind(configuration.GetSection(OidcOptions.SectionName));
        services.AddSingleton<IValidateOptions<OidcOptions>, OidcOptionsValidator>();

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
            (
                [FromForm] string? username,
                [FromForm] string? password,
                [FromForm] string? returnUrl,
                HttpContext http) => LoginAsync(username, password, returnUrl, http));

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
    ///     Handles a posted login form: throttles, checks the password and either issues the session, starts the
    ///     second-factor step or redirects back with one generic error.
    /// </summary>
    /// <remarks>
    ///     The services are taken from the request scope instead of the endpoint parameters, which keeps the
    ///     endpoint to the form fields and the context. The antiforgery token is still required because the form
    ///     fields of the endpoint are bound with <c>[FromForm]</c>.
    /// </remarks>
    /// <param name="username">The typed user name; may be null.</param>
    /// <param name="password">The typed password; may be null. Never stored or logged.</param>
    /// <param name="returnUrl">The requested return address; may be null or hostile.</param>
    /// <param name="http">The request context.</param>
    /// <returns>The redirect for the outcome.</returns>
    private static async Task<IResult> LoginAsync(string? username, string? password, string? returnUrl, HttpContext http)
    {
        var users = http.RequestServices.GetRequiredService<UserService>();
        var throttle = http.RequestServices.GetRequiredService<SignInThrottle>();
        var target = SafeReturnUrl(returnUrl);
        var retry = target == "/" ? string.Empty : $"&ReturnUrl={Uri.EscapeDataString(target)}";

        // Over the limit: nothing is checked (even a right password is refused), so the answer is the same
        // for every name; the refusal is recorded once per minute and name
        var address = ClientAddress(http);
        var reservation = throttle.TryBegin(address, username);
        if (reservation is null)
        {
            var typed = AuditText.Clean(username, ReviewLimits.MaxUsernameLength);
            await http.RequestServices.GetRequiredService<AuditService>().TryRecordCoalescedAsync(
                new AuditDraft("auth.sign-in-throttled", null, "not signed in", null, null, $"too many attempts, name '{typed}'"),
                $"auth.sign-in-throttled|{typed.ToLowerInvariant()}",
                http.RequestAborted);
            return Results.Redirect($"{LoginPath}?error={ThrottledError}{retry}");
        }

        var step = await users.AuthenticatePasswordAsync(username, password, http.RequestAborted);
        var user = step.User;

        // One generic answer for every failure; the query keeps the (safe) place the user wanted to go to
        if (user is null)
        {
            return Results.Redirect($"{LoginPath}?error=1{retry}");
        }

        // The password was right but the account has a second factor: no session yet. The browser gets a
        // short-lived challenge cookie and is sent to the code page; the failure counter stays as it is
        // until the code is right too. The attempt gives its address slot back (a correct password must not
        // use up the quota of the client); the name counter stays
        if (step.SecondFactorRequired)
        {
            throttle.ReleaseAddress(reservation);
            var token = http.RequestServices.GetRequiredService<TotpService>().BeginSignIn(user, address, target);
            http.Response.Cookies.Append(TotpSignIn.CookieName, token, TotpSignIn.CookieOptions(http));
            return Results.Redirect(TotpSignIn.LoginPath);
        }

        // The attempt does not count against the client any more
        throttle.Succeeded(reservation);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(user),
            new AuthenticationProperties { IsPersistent = false });

        // Best effort: a failure to record is logged by the service and never stops the sign-in
        await http.RequestServices.GetRequiredService<AuditService>().TryRecordAsync(
            AuditDraft.By(new Actor(user.Username, user.DisplayName), "auth.sign-in", "signed in"),
            http.RequestAborted);

        // A temporary password leads straight to the page that replaces it, whatever the user asked for
        return Results.LocalRedirect(user.MustChangePassword ? AccountPath : target);
    }

    /// <summary>
    ///     Gets the address of the client of a request as text.
    /// </summary>
    /// <param name="http">The request context.</param>
    /// <returns>
    ///     The address of the connection, or <c>null</c> when there is none. Behind a proxy this is the
    ///     forwarded address only when the forwarded headers are enabled for the server.
    /// </returns>
    internal static string? ClientAddress(HttpContext http)
    {
        var address = http.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
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
    ///     Gets the signed-in user as an <see cref="Actor"/>, or <c>null</c> when nobody is signed in.
    /// </summary>
    /// <param name="provider">The authentication state provider of the page. Must not be null.</param>
    /// <returns>The actor, or <c>null</c>; a page that loads data uses it so that an ended sign-in shows nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is null.</exception>
    public static async Task<Actor?> GetActorOrNullAsync(this AuthenticationStateProvider provider)
    {
        try
        {
            return await provider.GetActorAsync();
        }
        catch (DomainRuleException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Builds the principal that is stored in the authentication cookie.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>
    ///     A principal with the user name and display name, and the system administrator role when the user has it
    ///     (and no temporary password); a user with a temporary password gets the must-change-password claim
    ///     instead; never the password or its hash. Project roles are not in the cookie: they are read when needed,
    ///     so a change of membership takes effect at once.
    /// </returns>
    internal static ClaimsPrincipal BuildPrincipal(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(DisplayNameClaimType, user.DisplayName)
        };
        if (user.MustChangePassword)
        {
            claims.Add(new Claim(MustChangePasswordClaimType, "true"));
        }
        else if (user.IsSystemAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        }

        var identity = new ClaimsIdentity(
            claims,
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
    ///     <c>true</c> only when both are present, the user is enabled, and the system administrator role, the
    ///     must-change-password marker and the display name in the principal are equal to the stored ones;
    ///     otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    ///     This is the single rule behind both the cookie check on each request and the periodic check of an open
    ///     page, so the two cannot drift apart. A changed administrator flag, temporary-password state or display name
    ///     counts as not current so that the user signs in again and gets the current values. The method is pure and
    ///     thread-safe.
    /// </remarks>
    internal static bool IsCurrent(ClaimsPrincipal? principal, User? user)
    {
        // A user with a temporary password gets no administrator role in the principal, whatever the stored flag says
        return principal is not null
            && user is { Enabled: true }
            && principal.FindAll(ClaimTypes.Role).Any(claim => claim.Value == AdminRole)
            == (user.IsSystemAdmin && !user.MustChangePassword)
            && IsPasswordChangeRequired(principal) == user.MustChangePassword
            && principal.FindFirstValue(DisplayNameClaimType) == user.DisplayName;
    }

    /// <summary>
    ///     Tells whether a principal was issued for an account that still has to change its temporary password.
    /// </summary>
    /// <param name="principal">The signed-in principal; may be null.</param>
    /// <returns><c>true</c> when the principal carries the must-change-password claim.</returns>
    /// <remarks>
    ///     The claim is the only thing the layout reads to confine such a user to the account page, so no service
    ///     call is needed per navigation. It is part of <see cref="IsCurrent"/>: once the password is changed the
    ///     old cookie is stale and the user signs in again.
    /// </remarks>
    public static bool IsPasswordChangeRequired(ClaimsPrincipal? principal)
    {
        return principal?.FindFirstValue(MustChangePasswordClaimType) == "true";
    }
}
