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

using System.Net;
using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests of sign-in with an OpenID Connect provider (<see cref="OidcSignIn"/>): the real application, with its
///     real OpenID Connect handler, talks to the in-process fake provider over loopback http.
/// </summary>
public partial class OidcSignInTests : IClassFixture<IdentityProviderFixture>
{
    /// <summary>
    ///     Matches the <c>state</c> parameter of an authorization response URL.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("state=[^&]+")]
    private static partial Regex StateParameter();

    /// <summary>
    ///     The client secret in the settings of the test application; it must never be logged or recorded.
    /// </summary>
    private const string ClientSecret = "test-client-secret-value";

    /// <summary>
    ///     The fake provider shared by the tests of this class.
    /// </summary>
    private readonly FakeIdentityProvider _idp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OidcSignInTests"/> class; every test starts with the
    ///     default person at the provider.
    /// </summary>
    /// <param name="fixture">The shared provider.</param>
    public OidcSignInTests(IdentityProviderFixture fixture)
    {
        _idp = fixture.Idp;
        _idp.Scenario = new IdpScenario();
    }

    /// <summary>
    ///     Test that a first sign-in creates an enabled account that is no administrator, has no password and no
    ///     membership, signs the person in and returns to the requested local address.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_FirstSignIn_CreatesPlainAccountAndSignsIn()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp, "/projects");

        // Assert: signed in and sent on
        Assert.Equal(HttpStatusCode.Redirect, landing.StatusCode);
        Assert.Equal("/projects", landing.Headers.Location?.OriginalString);
        var cookie = Assert.Single(OidcTestApp.SessionCookies(browser));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);

        // Assert: the account is plain, bound to the identity and has no password and no project
        var user = await OidcTestApp.UserAsync(app, "olive");
        Assert.Equal("Olive Oidc", user.DisplayName);
        Assert.True(user.Enabled);
        Assert.False(user.IsSystemAdmin);
        Assert.Equal(_idp.Issuer, user.ExternalIssuer);
        Assert.Equal("subject-1", user.ExternalSubject);
        Assert.Equal(string.Empty, user.PasswordHash);
        var projects = await app.Services.GetRequiredService<ProjectService>()
            .ListAsync(new Actor(user.Username, user.DisplayName), TestContext.Current.CancellationToken);
        Assert.Empty(projects);

        // Assert: the session works
        using var page = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    /// <summary>
    ///     Test that the sign-in and the creation of the account are recorded in the audit trail with the host of the
    ///     issuer only.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_FirstSignIn_RecordsCreationAndSignIn()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        var created = Assert.Single(await OidcTestApp.AuditAsync(app, "account.oidc-created"));
        var signedIn = Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in"));
        Assert.Equal("issuer 127.0.0.1", created.Detail);
        Assert.Equal("issuer 127.0.0.1", signedIn.Detail);
        Assert.Equal("Olive Oidc (olive)", created.ActorName);
        Assert.Equal("Olive Oidc (olive)", signedIn.ActorName);
    }

    /// <summary>
    ///     Test that a second sign-in of the same identity uses the same account and creates none.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_SecondSignIn_UsesTheSameAccount()
    {
        // Arrange: a first sign-in, then the provider reports a changed name and e-mail address
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var first = new TestBrowser(app);
        await OidcTestApp.SignInAsync(first, _idp);
        _idp.Scenario = new IdpScenario
        {
            PreferredUsername = "renamed",
            Name = "Renamed Person",
            Email = "r@example.com"
        };
        using var second = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(second, _idp);

        // Assert: signed in, one account, the stored names did not change
        Assert.Equal("/", landing.Headers.Location?.OriginalString);
        Assert.Single(OidcTestApp.SessionCookies(second));
        var users = await app.Services.GetRequiredService<UserService>()
            .ListAsync(TestContext.Current.CancellationToken);
        var user = Assert.Single(users);
        Assert.Equal("olive", user.Username);
        Assert.Equal("Olive Oidc", user.DisplayName);
        Assert.Single(await OidcTestApp.AuditAsync(app, "account.oidc-created"));
        Assert.Equal(2, (await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in")).Count);
    }

    /// <summary>
    ///     Test that an account is found by the pair of issuer and subject only: a second person who proposes the same
    ///     user name gets an account of their own with a numbered name.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_OtherSubjectSameName_CreatesSeparateAccountWithUniqueName()
    {
        // Arrange: a first person
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var first = new TestBrowser(app);
        await OidcTestApp.SignInAsync(first, _idp);
        _idp.Scenario = new IdpScenario { Subject = "subject-2", Name = "Other Olive" };
        using var second = new TestBrowser(app);

        // Act
        await OidcTestApp.SignInAsync(second, _idp);

        // Assert
        var other = await OidcTestApp.UserAsync(app, "olive-2");
        Assert.Equal("subject-2", other.ExternalSubject);
        Assert.Equal("subject-1", (await OidcTestApp.UserAsync(app, "olive")).ExternalSubject);
    }

    /// <summary>
    ///     Test that a provider can never take over a local account: a person who proposes the name of a local account
    ///     gets a new account and the local account stays as it is.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_NameOfLocalAccount_NeverLinksToIt()
    {
        // Arrange: a local administrator called "olive", with the same e-mail and display name the provider sends
        await using var app = await OidcTestApp.StartAsync(_idp);
        await app.Services.GetRequiredService<UserService>().CreateAsync(
            "olive", "Olive Oidc", UserRole.Admin, TestBrowser.Password, TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);

        // Act
        await OidcTestApp.SignInAsync(browser, _idp);

        // Assert: a separate, plain account; the local one is untouched
        var external = await OidcTestApp.UserAsync(app, "olive-2");
        Assert.False(external.IsSystemAdmin);
        Assert.True(external.IsExternal);
        var local = await OidcTestApp.UserAsync(app, "olive");
        Assert.True(local.IsSystemAdmin);
        Assert.False(local.IsExternal);
        Assert.Null(local.ExternalSubject);
    }

    /// <summary>
    ///     Test that the account is derived from the e-mail address when the provider sends no user name, and that a
    ///     hostile name is reduced to safe characters and a hostile display name to one clean line.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_HostileNames_AreSanitized()
    {
        // Arrange: no preferred user name, a display name with control, bidirectional and extra white space
        _idp.Scenario = new IdpScenario
        {
            PreferredUsername = null,
            Email = "<b>mallory</b>@example.com",
            Name = "  Eve\r\n\u202EMal\u0007lory   Jr  "
        };
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        var user = Assert.Single(await app.Services.GetRequiredService<UserService>()
            .ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("bmalloryb", user.Username); // cspell:disable-line
        Assert.Equal("Eve Mal lory Jr", user.DisplayName);
    }

    /// <summary>
    ///     Test that a disabled account cannot sign in, gets the same answer as any other failure, and that the audit
    ///     trail names the reason.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_DisabledAccount_IsRefusedLikeAnyFailure()
    {
        // Arrange: an account that is disabled after its first sign-in
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var first = new TestBrowser(app);
        await OidcTestApp.SignInAsync(first, _idp);
        await app.Services.GetRequiredService<UserService>()
            .SetEnabledAsync("olive", false, TestContext.Current.CancellationToken);
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
        var entry = Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
        Assert.Equal("account disabled", entry.Detail);
    }

    /// <summary>
    ///     Test that an account of a provider has no password: no password is accepted, not even an empty one.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("correct horse battery")]
    public async Task OidcSignIn_PasswordSignInOfExternalAccount_IsRefused(string password)
    {
        // Arrange: an account that exists through the provider
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var first = new TestBrowser(app);
        await OidcTestApp.SignInAsync(first, _idp);
        using var browser = new TestBrowser(app);

        // Act
        var response = await browser.LoginAsync("olive", password);

        // Assert: the generic failure and no session
        Assert.Equal("/login?error=1", response.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
    }

    /// <summary>
    ///     Test that the provider's error answer (for example a refusal of the person) ends at the sign-in page with
    ///     the message about the provider, records the reason class only and creates no account.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_ProviderRefuses_ShowsFailureAndRecordsReasonClass()
    {
        // Arrange
        _idp.Scenario = new IdpScenario { AuthorizeError = "access_denied" };
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
        Assert.Empty(await app.Services.GetRequiredService<UserService>().ListAsync(TestContext.Current.CancellationToken));
        var entry = Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
        Assert.Equal("provider-error", entry.Detail);
        Assert.Equal("not signed in", entry.ActorName);
    }

    /// <summary>
    ///     Test that a refused code exchange ends at the sign-in page without a session.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_ProviderRefusesTheCode_EndsWithoutSession()
    {
        // Arrange
        _idp.Scenario = new IdpScenario { RefuseCode = true };
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
        Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
    }

    /// <summary>
    ///     Test that an answer with a state the application did not issue is refused.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_TamperedState_IsRefused()
    {
        // Arrange: a started sign-in whose answer comes back with another state
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var start = await browser.PostFormAsync("/auth/oidc/login", []);
        var answer = await _idp.Client.GetAsync(start.Headers.Location, TestContext.Current.CancellationToken);
        var forged = answer.Headers.Location!.ToString();
        forged = StateParameter().Replace(forged, "state=forged");

        // Act
        var callback = await browser.Client.GetAsync(forged, TestContext.Current.CancellationToken);
        var landing = await OidcTestApp.FollowAsync(browser, _idp, callback);

        // Assert
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
        var entry = Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
        Assert.Equal("state-or-nonce", entry.Detail);
    }

    /// <summary>
    ///     Test that an identity token with a wrong issuer, audience or nonce, although validly signed by the provider,
    ///     is refused: no session, no account, a failure recorded with the reason class only and no token value in the
    ///     log or the audit trail.
    /// </summary>
    /// <param name="fault">What is wrong with the identity token.</param>
    /// <param name="reason">The expected reason class.</param>
    [Theory]
    [InlineData(IdTokenFault.WrongIssuer, "token-invalid")]
    [InlineData(IdTokenFault.WrongAudience, "token-invalid")]
    [InlineData(IdTokenFault.WrongNonce, "state-or-nonce")]
    public async Task OidcSignIn_WrongIssuerAudienceOrNonce_FailsClosed(IdTokenFault fault, string reason)
    {
        // Arrange
        _idp.Scenario = new IdpScenario { IdTokenFault = fault };
        var logs = new CapturingLoggerProvider();
        await using var app = await OidcTestApp.StartAsync(_idp, logs);
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp);

        // Assert: no session and no account
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
        Assert.Empty(await app.Services.GetRequiredService<UserService>()
            .ListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in"));
        Assert.Empty(await OidcTestApp.AuditAsync(app, "account.oidc-created"));

        // Assert: the reason class only, and no value of the token anywhere
        var entry = Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
        Assert.Equal(reason, entry.Detail);
        await OidcTestApp.AssertNoTokenValuesRecordedAsync(app, logs);
    }

    /// <summary>
    ///     Test that a session that was open before the account was disabled at the application is ended: the next
    ///     request is sent to the sign-in page.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_AccountDisabledWhileSessionIsOpen_EndsTheSession()
    {
        // Arrange: a signed-in person whose account is then disabled
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        await OidcTestApp.SignInAsync(browser, _idp);
        using var before = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<UserService>()
            .SetEnabledAsync("olive", false, TestContext.Current.CancellationToken);

        // Act
        using var after = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    /// <summary>
    ///     Test that an unverified e-mail address is refused when the settings ask for a verified one, and accepted
    ///     otherwise.
    /// </summary>
    [Theory]
    [InlineData(true, "/login?error=oidc")]
    [InlineData(false, "/")]
    public async Task OidcSignIn_UnverifiedEmail_IsRefusedOnlyWhenRequired(bool required, string expected)
    {
        // Arrange
        _idp.Scenario = new IdpScenario { EmailVerified = false };
        await using var app = await OidcTestApp.StartAsync(
            _idp,
            settings: new Dictionary<string, string?>
            {
                ["Identity:Oidc:RequireEmailVerified"] = required ? "true" : "false"
            });
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        Assert.Equal(expected, landing.Headers.Location?.OriginalString);
        Assert.Equal(required, (await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed")).Count == 1);
    }

    /// <summary>
    ///     Test that the callback is throttled per client address only: after the address limit of failures a correct
    ///     sign-in is refused too, without an account.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_TooManyCallbacks_AreThrottled()
    {
        // Arrange: a limit of two failures, used up by two refusals of the provider
        _idp.Scenario = new IdpScenario { AuthorizeError = "access_denied" };
        await using var app = await OidcTestApp.StartAsync(
            _idp,
            settings: new Dictionary<string, string?>
            {
                ["Identity:Throttle:MaxFailuresPerName"] = "2",
                ["Identity:Throttle:MaxFailuresPerAddress"] = "2"
            });
        using var browser = new TestBrowser(app);
        await OidcTestApp.SignInAsync(browser, _idp);
        await OidcTestApp.SignInAsync(browser, _idp);
        _idp.Scenario = new IdpScenario();

        // Act: a correct sign-in
        var landing = await OidcTestApp.SignInAsync(browser, _idp);

        // Assert
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.Empty(OidcTestApp.SessionCookies(browser));
        Assert.Empty(await app.Services.GetRequiredService<UserService>().ListAsync(TestContext.Current.CancellationToken));
        var reasons = (await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed")).Select(entry => entry.Detail);
        Assert.Contains("throttled", reasons);
    }

    /// <summary>
    ///     Test that the callback is counted per client address only: more bad callbacks than the per-name limit from
    ///     one address do not block a legitimate sign-in from that address while it is below the address limit.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_BadCallbacksBelowAddressLimit_DoNotBlockAnotherPerson()
    {
        // Arrange: a per-name limit of two and an address limit of ten, and three refusals by the provider
        _idp.Scenario = new IdpScenario { AuthorizeError = "access_denied" };
        await using var app = await OidcTestApp.StartAsync(
            _idp,
            settings: new Dictionary<string, string?>
            {
                ["Identity:Throttle:MaxFailuresPerName"] = "2",
                ["Identity:Throttle:MaxFailuresPerAddress"] = "10"
            });
        using var attacker = new TestBrowser(app);
        for (var i = 0; i < 3; i++)
        {
            await OidcTestApp.SignInAsync(attacker, _idp);
        }

        _idp.Scenario = new IdpScenario();
        using var other = new TestBrowser(app);

        // Act: a legitimate sign-in from the same address
        var landing = await OidcTestApp.SignInAsync(other, _idp);

        // Assert: it works, and the refusals were recorded as failures of the provider, not as throttled
        Assert.Equal("/", landing.Headers.Location?.OriginalString);
        Assert.Single(OidcTestApp.SessionCookies(other));
        var reasons = (await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed")).Select(entry => entry.Detail);
        Assert.DoesNotContain("throttled", reasons);
    }

    /// <summary>
    ///     Test that a successful callback gives its slot back to the address: with an address limit of two, one
    ///     failure and one success leave room for another success.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_SuccessfulCallback_HandsTheAddressSlotBack()
    {
        // Arrange: an address limit of two, one refusal of the provider
        _idp.Scenario = new IdpScenario { AuthorizeError = "access_denied" };
        await using var app = await OidcTestApp.StartAsync(
            _idp,
            settings: new Dictionary<string, string?>
            {
                ["Identity:Throttle:MaxFailuresPerName"] = "2",
                ["Identity:Throttle:MaxFailuresPerAddress"] = "2"
            });
        using var failing = new TestBrowser(app);
        await OidcTestApp.SignInAsync(failing, _idp);
        _idp.Scenario = new IdpScenario();
        using var first = new TestBrowser(app);
        using var second = new TestBrowser(app);

        // Act: two sign-ins, which would be one too many if the first success kept its slot
        var firstLanding = await OidcTestApp.SignInAsync(first, _idp);
        var secondLanding = await OidcTestApp.SignInAsync(second, _idp);

        // Assert
        Assert.Equal("/", firstLanding.Headers.Location?.OriginalString);
        Assert.Equal("/", secondLanding.Headers.Location?.OriginalString);
        Assert.Single(OidcTestApp.SessionCookies(second));
    }

    /// <summary>
    ///     Test that only a local address is followed after the sign-in.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    public async Task OidcSignIn_ForeignReturnUrl_IsReplacedByHome(string returnUrl)
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        var landing = await OidcTestApp.SignInAsync(browser, _idp, returnUrl);

        // Assert
        Assert.Equal("/", landing.Headers.Location?.OriginalString);
    }

    /// <summary>
    ///     Test that the sign-in endpoints do not exist when sign-in with a provider is off, and the sign-in page
    ///     offers no button.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_Disabled_EndpointsAreNotMappedAndNoButtonIsShown()
    {
        // Arrange: the application without the provider settings
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        using var browser = new TestBrowser(app);

        // Act
        var login = await browser.PostFormAsync("/auth/oidc/login", []);
        var page = await browser.GetPageAsync("/login");
        var callback = await browser.Client.GetAsync("/signin-oidc", TestContext.Current.CancellationToken);
        var callbackBody = await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: the callback address is reserved, so it gives a plain 404 without the page frame
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, callback.StatusCode);
        Assert.DoesNotContain("Page not found", callbackBody, StringComparison.Ordinal);
        Assert.Null(page.QuerySelector("[data-testid='oidc-login']"));
    }

    /// <summary>
    ///     Test that the sign-in page offers the button beside the local form and shows the message about the provider.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_Enabled_SignInPageOffersButtonBesideLocalForm()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);

        // Act
        var page = await browser.GetPageAsync("/login?error=oidc&ReturnUrl=/projects");

        // Assert
        var button = page.QuerySelector("[data-testid='oidc-login']");
        Assert.NotNull(button);
        Assert.Equal("Sign in with Test IdP", button.TextContent.Trim());
        Assert.NotNull(page.QuerySelector("[data-testid='login-form']"));
        Assert.Contains(
            "did not work",
            page.QuerySelector("[data-testid='oidc-error']")?.TextContent,
            StringComparison.Ordinal);
        Assert.Null(page.QuerySelector("[data-testid='login-error']"));
        Assert.Equal(
            "/projects",
            page.QuerySelector("[data-testid='oidc-form'] input[name='returnUrl']")?.GetAttribute("value"));
    }

    /// <summary>
    ///     Test that signing out is local: the application does not call the provider, and the session ends.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_Logout_IsLocalOnly()
    {
        // Arrange: a signed-in person and the number of requests the provider has seen
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        await OidcTestApp.SignInAsync(browser, _idp);
        var seen = _idp.AuthorizeRequests.Count;

        // Act
        var response = await browser.PostFormAsync(SignIn.LogoutEndpoint, []);

        // Assert: redirected to a local page, the provider saw nothing, the session is gone
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.Location!.IsAbsoluteUri);
        Assert.Equal(seen, _idp.AuthorizeRequests.Count);
        using var account = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, account.StatusCode);
    }

    /// <summary>
    ///     Test that neither the client secret nor the code of the provider reaches the log or the audit trail, and
    ///     that the provider was asked for the authorization code flow with PKCE.
    /// </summary>
    [Fact]
    public async Task OidcSignIn_Flow_NeverLogsSecretsAndUsesCodeFlowWithPkce()
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        await using var app = await OidcTestApp.StartAsync(_idp, logs);
        using var browser = new TestBrowser(app);
        _idp.Scenario = new IdpScenario { RefuseCode = true };
        await OidcTestApp.SignInAsync(browser, _idp);
        _idp.Scenario = new IdpScenario();

        // Act
        await OidcTestApp.SignInAsync(browser, _idp);

        // Assert: no secret in the log, and the request asked for a code with a code challenge
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(ClientSecret, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Properties.Values.Any(
                value => value?.ToString()?.Contains(ClientSecret, StringComparison.Ordinal) == true));
        var request = _idp.AuthorizeRequests[^1];
        Assert.Equal("code", request["response_type"]);
        Assert.Equal("S256", request["code_challenge_method"]);
        Assert.False(request.ContainsKey("prompt"));
        foreach (var entry in await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"))
        {
            Assert.DoesNotContain(ClientSecret, entry.Detail, StringComparison.Ordinal);
        }
    }
}
