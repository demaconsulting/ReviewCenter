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

using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     What the fake identity provider answers with for the next sign-in; every value can be changed by a test.
/// </summary>
internal sealed class IdpScenario
{
    /// <summary>
    ///     Gets or sets the subject (<c>sub</c>) of the person who signs in.
    /// </summary>
    public string Subject { get; set; } = "subject-1";

    /// <summary>
    ///     Gets or sets the full name (<c>name</c>) claim; <c>null</c> to leave it out.
    /// </summary>
    public string? Name { get; set; } = "Olive Oidc";

    /// <summary>
    ///     Gets or sets the <c>preferred_username</c> claim; <c>null</c> to leave it out.
    /// </summary>
    public string? PreferredUsername { get; set; } = "olive";

    /// <summary>
    ///     Gets or sets the <c>email</c> claim; <c>null</c> to leave it out.
    /// </summary>
    public string? Email { get; set; } = "olive@example.com";

    /// <summary>
    ///     Gets or sets the <c>email_verified</c> claim; <c>null</c> to leave it out.
    /// </summary>
    public bool? EmailVerified { get; set; } = true;

    /// <summary>
    ///     Gets or sets how long ago the person authenticated at the provider (<c>auth_time</c>); <c>null</c> to leave
    ///     the claim out.
    /// </summary>
    public TimeSpan? AuthenticatedAgo { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Gets or sets an error code the provider returns instead of a code (for example <c>access_denied</c>);
    ///     <c>null</c> for a normal sign-in.
    /// </summary>
    public string? AuthorizeError { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the token endpoint refuses the code.
    /// </summary>
    public bool RefuseCode { get; set; }

    /// <summary>
    ///     Gets or sets what is wrong with the identity token the token endpoint returns; the token is signed with the
    ///     real key of the provider, so only the application's checks of its content can refuse it.
    /// </summary>
    public IdTokenFault IdTokenFault { get; set; }
}

/// <summary>
///     What the fake identity provider changes in the identity token it returns.
/// </summary>
public enum IdTokenFault
{
    /// <summary>
    ///     Nothing: the token is as the provider made it.
    /// </summary>
    None,

    /// <summary>
    ///     The issuer (<c>iss</c>) is not the provider's own.
    /// </summary>
    WrongIssuer,

    /// <summary>
    ///     The audience (<c>aud</c>) is another client.
    /// </summary>
    WrongAudience,

    /// <summary>
    ///     The <c>nonce</c> is not the one of the authorization request.
    /// </summary>
    WrongNonce
}

/// <summary>
///     Shares one <see cref="FakeIdentityProvider"/> between the tests of a class (xUnit class fixture): the
///     provider starts once and each test sets its own <see cref="FakeIdentityProvider.Scenario"/>.
/// </summary>
public sealed class IdentityProviderFixture : IAsyncLifetime
{
    /// <summary>
    ///     Gets the provider; available after <see cref="InitializeAsync"/>.
    /// </summary>
    internal FakeIdentityProvider Idp { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        Idp = await FakeIdentityProvider.StartAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Idp.DisposeAsync();
    }
}

/// <summary>
///     An in-process OpenID Connect provider that answers every sign-in at once, as a person who is already known to
///     it: no login page, only the protocol. It is a real OpenIddict server on a loopback port, so the application's
///     OpenID Connect handler runs its true code against it.
/// </summary>
/// <remarks>
///     Create one per test class (it starts a listener and creates a key) and reset <see cref="Scenario"/> in each
///     test. The provider keeps no state besides the scenario and the list of requests it has seen.
/// </remarks>
internal sealed class FakeIdentityProvider : IAsyncDisposable
{
    /// <summary>
    ///     The issuer a <see cref="IdTokenFault.WrongIssuer"/> token carries.
    /// </summary>
    public const string ForgedIssuer = "https://other-issuer.example/";

    /// <summary>
    ///     The audience a <see cref="IdTokenFault.WrongAudience"/> token carries.
    /// </summary>
    public const string ForgedAudience = "some-other-client";

    /// <summary>
    ///     The nonce a <see cref="IdTokenFault.WrongNonce"/> token carries.
    /// </summary>
    public const string ForgedNonce = "forged-nonce";

    /// <summary>
    ///     The requests the authorization endpoint has seen, as parsed query strings, oldest first.
    /// </summary>
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> _authorizeRequests = new();

    /// <summary>
    ///     The running provider.
    /// </summary>
    private readonly WebApplication _app;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeIdentityProvider"/> class.
    /// </summary>
    /// <param name="app">The started provider.</param>
    private FakeIdentityProvider(WebApplication app)
    {
        _app = app;
        BaseUrl = app.Urls.First().TrimEnd('/');
        Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = new Uri(BaseUrl)
        };
    }

    /// <summary>
    ///     Gets the address of the provider without a trailing slash; this is the authority to configure.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    ///     Gets the issuer the provider puts into its tokens (the base address with a trailing slash).
    /// </summary>
    public string Issuer => BaseUrl + "/";

    /// <summary>
    ///     Gets the client a browser would use to talk to the provider; it does not follow redirects.
    /// </summary>
    public HttpClient Client { get; }

    /// <summary>
    ///     Gets or sets the answer for the next sign-in.
    /// </summary>
    public IdpScenario Scenario { get; set; } = new();

    /// <summary>
    ///     Gets the requests the authorization endpoint has seen so far, oldest first.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> AuthorizeRequests => _authorizeRequests.ToArray();

    /// <summary>
    ///     Starts a provider on a free loopback port.
    /// </summary>
    /// <returns>The started provider; the caller disposes it.</returns>
    public static async Task<FakeIdentityProvider> StartAsync()
    {
        using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = "fake-idp-key" };
        FakeIdentityProvider? self = null;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddOpenIddict().AddServer(options =>
        {
            options.SetAuthorizationEndpointUris("/connect/authorize").SetTokenEndpointUris("/connect/token");
            options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
            options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email);
            options.AddSigningKey(signingKey);
            options.AddEncryptionKey(new SymmetricSecurityKey(new byte[32]));
            options.DisableAccessTokenEncryption();

            // No clients, no stored grants: the provider accepts any request and keeps nothing
            options.EnableDegradedMode().DisableAuthorizationStorage().DisableTokenStorage();
            options.UseAspNetCore()
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough()
                .DisableTransportSecurityRequirement();
            options.AddEventHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>(
                handler => handler.UseInlineHandler(_ => default));
            options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(
                handler => handler.UseInlineHandler(_ => default));

            // The scenario may ask for a wrong issuer, audience or nonce: the token is re-signed with the real key
            options.AddEventHandler<OpenIddictServerEvents.ApplyTokenResponseContext>(
                handler => handler.UseInlineHandler(context =>
                {
                    var fault = self?.Scenario.IdTokenFault ?? IdTokenFault.None;
                    if (fault != IdTokenFault.None && context.Response.IdToken is { } token)
                    {
                        context.Response.IdToken = Tamper(token, fault, signingKey);
                    }

                    return default;
                }));
        });

        var app = builder.Build();
        app.MapGet("/connect/authorize", http => self!.AuthorizeAsync(http));
        app.MapPost("/connect/token", http => self!.TokenAsync(http));
        await app.StartAsync();
        self = new FakeIdentityProvider(app);
        return self;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     Answers an authorization request: records it and signs the person of the scenario in at once, or returns
    ///     the error of the scenario to the client.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private async Task AuthorizeAsync(HttpContext http)
    {
        var query = http.Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        _authorizeRequests.Enqueue(query);
        var request = http.GetOpenIddictServerRequest()!;
        var scenario = Scenario;
        if (scenario.AuthorizeError is not null)
        {
            var separator = request.RedirectUri!.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            http.Response.Redirect(
                $"{request.RedirectUri}{separator}error={scenario.AuthorizeError}"
                + $"&state={Uri.EscapeDataString(request.State ?? string.Empty)}");
            return;
        }

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        AddClaim(identity, Claims.Subject, scenario.Subject);
        AddClaim(identity, Claims.Name, scenario.Name);
        AddClaim(identity, Claims.PreferredUsername, scenario.PreferredUsername);
        AddClaim(identity, Claims.Email, scenario.Email);
        if (scenario.EmailVerified is { } verified)
        {
            identity.AddClaim(new Claim(Claims.EmailVerified, verified ? "true" : "false", ClaimValueTypes.Boolean)
                .SetDestinations(Destinations.IdentityToken));
        }

        if (scenario.AuthenticatedAgo is { } ago)
        {
            var authTime = DateTimeOffset.UtcNow - ago;
            identity.AddClaim(new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
                .SetDestinations(Destinations.IdentityToken));
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(
            http, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
    }

    /// <summary>
    ///     Answers a token request: exchanges the code from the authorization response for the tokens, or refuses.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private async Task TokenAsync(HttpContext http)
    {
        if (Scenario.RefuseCode)
        {
            http.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await http.Response.WriteAsJsonAsync(new { error = "invalid_grant" }, http.RequestAborted);
            return;
        }

        var result = await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.AuthenticateAsync(
            http, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(
            http, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, result.Principal!);
    }

    /// <summary>
    ///     Replaces one claim of an identity token and signs the token again with the key of the provider.
    /// </summary>
    /// <param name="token">The identity token as the provider made it.</param>
    /// <param name="fault">The fault to put in; not <see cref="IdTokenFault.None"/>.</param>
    /// <param name="signingKey">The signing key of the provider.</param>
    /// <returns>The changed, validly signed token.</returns>
    private static string Tamper(string token, IdTokenFault fault, SecurityKey signingKey)
    {
        var handler = new JsonWebTokenHandler();
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(handler.ReadJsonWebToken(token).EncodedPayload))!
            .AsObject();
        switch (fault)
        {
            case IdTokenFault.WrongIssuer:
                payload["iss"] = ForgedIssuer;
                break;
            case IdTokenFault.WrongAudience:
                payload["aud"] = ForgedAudience;
                break;
            default:
                payload["nonce"] = ForgedNonce;
                break;
        }

        return handler.CreateToken(
            payload.ToJsonString(), new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
    }

    /// <summary>
    ///     Adds a claim that goes into the identity token, unless the value is <c>null</c>.
    /// </summary>
    /// <param name="identity">The identity to add to.</param>
    /// <param name="type">The claim type.</param>
    /// <param name="value">The claim value, or <c>null</c> to add nothing.</param>
    private static void AddClaim(ClaimsIdentity identity, string type, string? value)
    {
        if (value is not null)
        {
            identity.AddClaim(new Claim(type, value).SetDestinations(Destinations.IdentityToken));
        }
    }
}
