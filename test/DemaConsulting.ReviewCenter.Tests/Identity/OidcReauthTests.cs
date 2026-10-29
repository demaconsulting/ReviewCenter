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
using System.Net;
using System.Security.Claims;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Tests.Evidence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests of the confirmation of identity for a signature through an OpenID Connect provider
///     (<see cref="OidcSignIn"/>): a fresh sign-in at the fake provider, checked against the account, that gives a
///     single-use proof the signing service accepts in place of a password.
/// </summary>
public class OidcReauthTests : IClassFixture<IdentityProviderFixture>
{
    /// <summary>
    ///     The review the tests sign.
    /// </summary>
    private const string ReviewId = "rev-1";

    /// <summary>
    ///     The fake provider shared by the tests of this class.
    /// </summary>
    private readonly FakeIdentityProvider _idp;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OidcReauthTests"/> class; every test starts with the default
    ///     person at the provider.
    /// </summary>
    /// <param name="fixture">The shared provider.</param>
    public OidcReauthTests(IdentityProviderFixture fixture)
    {
        _idp = fixture.Idp;
        _idp.Scenario = new IdpScenario();
    }

    /// <summary>
    ///     Test that a fresh sign-in of the right person gives a proof that is accepted for exactly one signature, and
    ///     that the provider was asked for a new sign-in.
    /// </summary>
    [Fact]
    public async Task OidcReauth_FreshSignInOfSamePerson_AllowsExactlyOneSignature()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);

        // Act
        var landing = await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert: back at the review, and the provider was asked to sign the person in again
        Assert.Equal($"{world.Route}?reauth=ok", landing.Headers.Location?.OriginalString);
        var request = _idp.AuthorizeRequests[^1];
        Assert.Equal("login", request["prompt"]);
        Assert.Equal("0", request["max_age"]);
        Assert.True(await world.Signing.HasReauthProofAsync(
            world.Actor, world.ProjectId, ReviewId, world.SlotId, TestContext.Current.CancellationToken));

        // Act: sign without a password
        await world.SignAsync();

        // Assert: the proof was used up, and the slot is signed
        Assert.False(await world.Signing.HasReauthProofAsync(
            world.Actor, world.ProjectId, ReviewId, world.SlotId, TestContext.Current.CancellationToken));
        var approval = await world.Signing.GetApprovalAsync(world.ProjectId, ReviewId, TestContext.Current.CancellationToken);
        Assert.Equal(SlotSignatureState.Signed, Assert.Single(approval!.Slots).State);
    }

    /// <summary>
    ///     Test that the person stays signed in as the same user after the confirmation.
    /// </summary>
    [Fact]
    public async Task OidcReauth_Success_KeepsTheSession()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);

        // Act
        await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert
        using var account = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.Single(await app.Services.GetRequiredService<UserService>().ListAsync(TestContext.Current.CancellationToken), user => user.Username == "olive");
    }

    /// <summary>
    ///     Test that a sign-in at the provider that is too old, from the future, or without a time is refused, with the
    ///     reason class recorded and no proof given.
    /// </summary>
    [Theory]
    [InlineData(600, "stale-auth-time")]
    [InlineData(-600, "stale-auth-time")]
    [InlineData(null, "missing-auth-time")]
    public async Task OidcReauth_AuthTimeNotInWindow_IsRefusedFailClosed(int? secondsAgo, string reason)
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        _idp.Scenario = new IdpScenario { AuthenticatedAgo = secondsAgo is { } seconds ? TimeSpan.FromSeconds(seconds) : null };

        // Act
        var landing = await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert
        await world.AssertRefusedAsync(landing, reason);
    }

    /// <summary>
    ///     Test that an <c>auth_time</c> that no date can hold (past the largest or below the smallest date, beyond
    ///     what a whole number holds, or negative) is refused like a missing one: the reason class is recorded, no proof
    ///     is given and nothing throws, so the person is sent back to the review and not to an error page.
    /// </summary>
    /// <param name="seconds">The <c>auth_time</c> the provider sends, in seconds since 1970.</param>
    /// <param name="reason">The expected reason class.</param>
    [Theory]
    [InlineData(long.MaxValue, "missing-auth-time")]
    [InlineData(long.MinValue, "missing-auth-time")]
    [InlineData(-1L, "missing-auth-time")]
    [InlineData(253402300800L, "missing-auth-time")]
    [InlineData(253402300799L, "stale-auth-time")]
    public async Task OidcReauth_AuthTimeOutsideTheRangeOfDates_IsRefusedWithoutError(long seconds, string reason)
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        _idp.Scenario = new IdpScenario { RawAuthTimeSeconds = seconds };

        // Act
        var landing = await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert: a normal refusal with its audit entry (AssertRefusedAsync checks the redirect, the single
        // signature.reauth-failed entry with the reason class, the missing proof and the refused signing)
        await world.AssertRefusedAsync(landing, reason);
        using var account = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
    }

    /// <summary>
    ///     Test that an identity token with a wrong issuer, audience or nonce gives no proof for a signature: the
    ///     failure is recorded as a failed confirmation with the reason class only, signing stays refused, no account
    ///     is created, the session is unchanged and no token value reaches the log or the audit trail.
    /// </summary>
    /// <param name="fault">What is wrong with the identity token.</param>
    /// <param name="reason">The expected reason class.</param>
    [Theory]
    [InlineData(IdTokenFault.WrongIssuer, "token-invalid")]
    [InlineData(IdTokenFault.WrongAudience, "token-invalid")]
    [InlineData(IdTokenFault.WrongNonce, "state-or-nonce")]
    public async Task OidcReauth_WrongIssuerAudienceOrNonce_IsRefusedFailClosed(IdTokenFault fault, string reason)
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        await using var app = await OidcTestApp.StartAsync(_idp, logs);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        _idp.Scenario = new IdpScenario { IdTokenFault = fault };
        var usersBefore = (await app.Services.GetRequiredService<UserService>()
            .ListAsync(TestContext.Current.CancellationToken)).Count;

        // Act
        var landing = await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert
        await world.AssertRefusedAsync(landing, reason);
        var users = await app.Services.GetRequiredService<UserService>().ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(usersBefore, users.Count);
        Assert.Empty(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
        using var account = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        await OidcTestApp.AssertNoTokenValuesRecordedAsync(app, logs);
    }

    /// <summary>
    ///     Test that a confirmation that comes back after the session ended (the person signed out or the account was
    ///     disabled while they were at the provider) gives no proof and is recorded as a failed sign-in with the
    ///     reason class <c>session-ended</c>.
    /// </summary>
    /// <param name="signOut">Whether the session ends by signing out (otherwise the account is disabled).</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OidcReauth_SessionEndedAtTheProvider_IsRefusedWithoutProof(bool signOut)
    {
        // Arrange: a confirmation that is started, and a session that ends before the answer of the provider arrives
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        var start = await OidcTestApp.StartReauthAsync(browser, world.ProjectId, ReviewId, world.SlotId, world.Route);
        if (signOut)
        {
            await browser.PostFormAsync(SignIn.LogoutEndpoint, []);
        }
        else
        {
            await app.Services.GetRequiredService<UserService>()
                .SetEnabledAsync("olive", false, TestContext.Current.CancellationToken);
        }

        // Act
        var landing = await OidcTestApp.FollowAsync(browser, _idp, start);

        // Assert: sent to the sign-in page, no proof, and the reason class recorded
        Assert.Equal("/login?error=oidc", landing.Headers.Location?.OriginalString);
        Assert.False(await world.Signing.HasReauthProofAsync(
            world.Actor, world.ProjectId, ReviewId, world.SlotId, TestContext.Current.CancellationToken));
        var entry = Assert.Single(await OidcTestApp.AuditAsync(app, "auth.oidc-sign-in-failed"));
        Assert.Equal("session-ended", entry.Detail);
        Assert.Empty(await OidcTestApp.AuditAsync(app, "signature.reauth-failed"));
    }

    /// <summary>
    ///     Test that another person who signs in at the provider, in a browser that is signed in as the user, gives no
    ///     proof, does not create an account and does not change the session.
    /// </summary>
    [Fact]
    public async Task OidcReauth_OtherSubject_IsRefusedAndCreatesNoAccount()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        _idp.Scenario = new IdpScenario { Subject = "somebody-else", PreferredUsername = "somebody" };

        // Act
        var landing = await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert
        await world.AssertRefusedAsync(landing, "subject-mismatch");
        var users = await app.Services.GetRequiredService<UserService>().ListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(users, user => user.ExternalSubject == "somebody-else");
        using var account = await browser.Client.GetAsync("/account", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
    }

    /// <summary>
    ///     Test that an error from the provider ends the confirmation at the review with a failure and no proof.
    /// </summary>
    [Fact]
    public async Task OidcReauth_ProviderRefuses_ReturnsToReviewWithoutProof()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        _idp.Scenario = new IdpScenario { AuthorizeError = "access_denied" };

        // Act
        var landing = await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert
        await world.AssertRefusedAsync(landing, "provider-error");
    }

    /// <summary>
    ///     Test that signing without a confirmation is refused for a user without a password, and recorded.
    /// </summary>
    [Fact]
    public async Task OidcReauth_SignWithoutProof_IsRefusedAndRecorded()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);

        // Act
        var failure = await Assert.ThrowsAsync<DomainRuleException>(() => world.SignAsync());

        // Assert
        Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        var entry = Assert.Single(await OidcTestApp.AuditAsync(app, "signature.reauth-failed"));
        Assert.Equal("oidc: no-proof", entry.Detail);
    }

    /// <summary>
    ///     Test that the confirmation needs a session, a local account has no confirmation to make, and a user who is
    ///     not a reviewer of the slot is refused: none of them is sent to the provider.
    /// </summary>
    [Fact]
    public async Task OidcReauth_NotAllowed_IsNeverSentToTheProvider()
    {
        // Arrange: a person without a session, a local member, and a user of the provider who is no reviewer
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        using var anonymous = new TestBrowser(app);
        using var local = await TestBrowser.SignedInAsync(app, "ann");
        _idp.Scenario = new IdpScenario { Subject = "subject-2", PreferredUsername = "other" };
        using var stranger = new TestBrowser(app);
        await OidcTestApp.SignInAsync(stranger, _idp);
        await world.AddMemberAsync("other");
        var requests = _idp.AuthorizeRequests.Count;

        // Act
        var anonymousAnswer = await OidcTestApp.StartReauthAsync(anonymous, world.ProjectId, ReviewId, world.SlotId, world.Route);
        var localAnswer = await OidcTestApp.StartReauthAsync(local, world.ProjectId, ReviewId, world.SlotId, world.Route);
        var strangerAnswer = await OidcTestApp.StartReauthAsync(stranger, world.ProjectId, ReviewId, world.SlotId, world.Route);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, anonymousAnswer.StatusCode);
        Assert.Contains("/login", anonymousAnswer.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal($"{world.Route}?reauth=failed", localAnswer.Headers.Location?.OriginalString);
        Assert.Equal($"{world.Route}?reauth=failed", strangerAnswer.Headers.Location?.OriginalString);
        Assert.Equal(requests, _idp.AuthorizeRequests.Count);
    }

    /// <summary>
    ///     Test that a return address that leaves the application is replaced by the home page, so the confirmation
    ///     cannot be used as an open redirect.
    /// </summary>
    [Fact]
    public async Task OidcReauth_ForeignReturnUrl_ReturnsToLocalAddress()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);

        // Act
        var landing = await OidcTestApp.ReauthAsync(
            browser, _idp, world.ProjectId, ReviewId, world.SlotId, "https://evil.example/steal");

        // Assert
        Assert.Equal("/?reauth=ok", landing.Headers.Location?.OriginalString);
    }

    /// <summary>
    ///     Test that the review page offers a button to confirm the identity (and no password field) to a user of the
    ///     provider, then the sign button after the confirmation, and shows the failure after a refused one.
    /// </summary>
    [Fact]
    public async Task OidcReauth_ReviewPage_OffersConfirmationThenSigning()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        var slot = world.SlotId;

        // Act, Assert: before the confirmation, the button and no password field
        var before = await browser.GetPageAsync(world.Route);
        var button = before.QuerySelector($"[data-testid='sign-reauth-{slot}']");
        Assert.Equal("Confirm identity with Test IdP", button?.TextContent.Trim());
        Assert.Null(before.QuerySelector($"[data-testid='sign-form-{slot}']"));
        Assert.Null(before.QuerySelector($"[data-testid='sign-reauth-form-{slot}'] input[type='password']"));
        Assert.Equal(
            "/auth/oidc/reauth",
            before.QuerySelector($"[data-testid='sign-reauth-form-{slot}']")?.GetAttribute("action"));

        // Act, Assert: after a refused confirmation, the failure is said next to the button
        var failed = await browser.GetPageAsync($"{world.Route}?reauth=failed");
        Assert.NotNull(failed.QuerySelector($"[data-testid='sign-error-{slot}']"));
        Assert.NotNull(failed.QuerySelector($"[data-testid='sign-reauth-{slot}']"));
        Assert.Contains(
            "We could not confirm your identity",
            failed.QuerySelector($"[data-testid='sign-error-{slot}']")?.TextContent);

        // Act, Assert: a value that is none of the known ones shows nothing
        var bogus = await browser.GetPageAsync($"{world.Route}?reauth=bogus");
        Assert.Null(bogus.QuerySelector($"[data-testid='sign-error-{slot}']"));

        // Act, Assert: after the confirmation, the sign button
        await OidcTestApp.ReauthAsync(browser, _idp, world.ProjectId, ReviewId, slot, world.Route);
        var after = await browser.GetPageAsync($"{world.Route}?reauth=ok");
        Assert.NotNull(after.QuerySelector($"[data-testid='sign-button-{slot}']"));
        Assert.NotNull(after.QuerySelector($"[data-testid='sign-confirmed-{slot}']"));
        Assert.Null(after.QuerySelector($"[data-testid='sign-reauth-{slot}']"));
    }

    /// <summary>
    ///     Test that after a refused confirmation the review page says why when the provider did not report when the
    ///     person signed in (and that an administrator must set it up), and when the sign-in was not recent enough,
    ///     and that the general text is not shown for those two causes.
    /// </summary>
    [Fact]
    public async Task OidcReauth_MissingOrStaleAuthTime_ReviewPageNamesTheCause()
    {
        // Arrange
        await using var app = await OidcTestApp.StartAsync(_idp);
        using var browser = new TestBrowser(app);
        var world = await ArrangeAsync(app, browser);
        var errorSelector = $"[data-testid='sign-error-{world.SlotId}']";

        // Act: the provider sends no auth_time, and the person follows the redirect back to the review
        _idp.Scenario = new IdpScenario { AuthenticatedAgo = null };
        var missing = await OidcTestApp.ReauthAsync(
            browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);
        var missingPage = await browser.GetPageAsync(missing.Headers.Location!.OriginalString);

        // Assert
        var missingText = missingPage.QuerySelector(errorSelector)?.TextContent;
        Assert.Contains("did not report when you signed in", missingText);
        Assert.Contains("auth_time", missingText);
        Assert.Contains("administrator", missingText);
        Assert.DoesNotContain("We could not confirm your identity", missingText);

        // Act: the provider sends an auth_time that is too old
        _idp.Scenario = new IdpScenario { AuthenticatedAgo = TimeSpan.FromSeconds(600) };
        var stale = await OidcTestApp.ReauthAsync(
            browser, _idp, world.ProjectId, ReviewId, world.SlotId, world.Route);
        var stalePage = await browser.GetPageAsync(stale.Headers.Location!.OriginalString);

        // Assert
        var staleText = stalePage.QuerySelector(errorSelector)?.TextContent;
        Assert.Contains("not recent enough", staleText);
        Assert.DoesNotContain("auth_time", staleText);
        Assert.DoesNotContain("We could not confirm your identity", staleText);
    }

    /// <summary>
    ///     Test that issuer and subject that differ from the account, and an account that is not external, are refused
    ///     with their own reason class by the check of the confirmation, and that the stored identity is accepted.
    /// </summary>
    /// <param name="issuer">The issuer of the confirmation.</param>
    /// <param name="subject">The subject of the confirmation.</param>
    /// <param name="external">Whether the account is an account at the provider.</param>
    /// <param name="reason">The expected reason class; <c>null</c> when the confirmation is accepted.</param>
    [Theory]
    [InlineData("https://other.example/", "sub-1", true, "issuer-mismatch")]
    [InlineData("https://idp.example/", "sub-2", true, "subject-mismatch")]
    [InlineData("https://idp.example/", "sub-1", false, "not-external")]
    [InlineData("https://idp.example/", "sub-1", true, null)]
    public void OidcReauth_VerifyReauth_ComparesIssuerAndSubjectOfTheAccount(
        string issuer,
        string subject,
        bool external,
        string? reason)
    {
        // Arrange
        var user = new User
        {
            Id = 7,
            Username = "olive",
            DisplayName = "Olive",
            ExternalIssuer = external ? "https://idp.example/" : null,
            ExternalSubject = external ? "sub-1" : null
        };
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("iss", issuer),
            new Claim("sub", subject),
            new Claim("auth_time", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        ]));
        var properties = new AuthenticationProperties();
        properties.Items["rc_user"] = "7";
        properties.Items["rc_project"] = "1";
        properties.Items["rc_review"] = ReviewId;
        properties.Items["rc_slot"] = "2";

        // Act
        var result = OidcSignIn.VerifyReauth(user, claims, properties, now, new OidcOptions());

        // Assert
        Assert.Equal(reason, result);
    }

    /// <summary>
    ///     Test that a confirmation started for another user, or without its binding, or without claims is refused.
    /// </summary>
    /// <param name="startedFor">The user the confirmation was started for; <c>null</c> for none.</param>
    /// <param name="withClaims">Whether the token has claims.</param>
    [Theory]
    [InlineData("8", true)]
    [InlineData(null, true)]
    [InlineData("7", false)]
    public void OidcReauth_VerifyReauth_WrongBinding_IsRefusedAsOther(string? startedFor, bool withClaims)
    {
        // Arrange
        var user = new User
        {
            Id = 7,
            Username = "olive",
            DisplayName = "Olive",
            ExternalIssuer = "https://idp.example/",
            ExternalSubject = "sub-1"
        };
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        ClaimsPrincipal? claims = withClaims
            ? new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("iss", "https://idp.example/"), new Claim("sub", "sub-1")]))
            : null;
        var properties = new AuthenticationProperties();
        if (startedFor is not null)
        {
            properties.Items["rc_user"] = startedFor;
        }

        properties.Items["rc_project"] = "1";
        properties.Items["rc_review"] = ReviewId;
        properties.Items["rc_slot"] = "2";

        // Act
        var result = OidcSignIn.VerifyReauth(user, claims, properties, now, new OidcOptions());

        // Assert
        Assert.Equal("other", result);
    }

    /// <summary>
    ///     Creates the scenario of a review: a user of the provider who signed in, a project the user is a member of,
    ///     and a review with every check answered and one slot that the user is the primary reviewer of.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="browser">The browser that signs the user in.</param>
    /// <returns>The prepared world.</returns>
    private async Task<World> ArrangeAsync(WebApplication app, TestBrowser browser)
    {
        await OidcTestApp.SignInAsync(browser, _idp);
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = app.Services.GetRequiredService<UserService>();
        var admin = await users.CreateAsync("boss", "Boss", UserRole.Admin, TestBrowser.Password, cancellationToken);
        var project = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var world = new World(app, project.Id, new Actor("boss", "Boss"), new Actor("olive", "Olive Oidc"));
        await world.AddMemberAsync("boss");
        await world.AddMemberAsync("olive");
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, ReviewId, "First review", [new("a.txt", TestContent.A)], cancellationToken);
        await EvidenceTestSupport.AnswerAllPassAsync(
            app.Services.GetRequiredService<ChecklistService>(), project.Id, ReviewId);
        var olive = await OidcTestApp.UserAsync(app, "olive");
        var slot = await world.Signing.AddSlotAsync(world.Boss, project.Id, ReviewId, olive.Id, null, cancellationToken);
        world.SlotId = slot.Id;
        Assert.NotEqual(0, admin.Id);
        return world;
    }

    /// <summary>
    ///     A review with one slot of the user of the provider, and the services to sign it.
    /// </summary>
    private sealed class World
    {
        /// <summary>
        ///     The application.
        /// </summary>
        private readonly WebApplication _app;

        /// <summary>
        ///     Initializes a new instance of the <see cref="World"/> class.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <param name="projectId">The project number.</param>
        /// <param name="boss">The administrator who sets the review up.</param>
        /// <param name="actor">The user of the provider.</param>
        public World(WebApplication app, int projectId, Actor boss, Actor actor)
        {
            _app = app;
            ProjectId = projectId;
            Boss = boss;
            Actor = actor;
        }

        /// <summary>
        ///     Gets the project number.
        /// </summary>
        public int ProjectId { get; }

        /// <summary>
        ///     Gets the administrator who sets the review up.
        /// </summary>
        public Actor Boss { get; }

        /// <summary>
        ///     Gets the user of the provider, as the actor of a request.
        /// </summary>
        public Actor Actor { get; }

        /// <summary>
        ///     Gets or sets the slot of the user.
        /// </summary>
        public int SlotId { get; set; }

        /// <summary>
        ///     Gets the address of the review page.
        /// </summary>
        public string Route => $"/projects/{ProjectId}/reviews/{ReviewId}";

        /// <summary>
        ///     Gets the signing service.
        /// </summary>
        public SigningService Signing => _app.Services.GetRequiredService<SigningService>();

        /// <summary>
        ///     Makes a user a member of the project.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <returns>A task that completes when the membership is stored.</returns>
        public async Task AddMemberAsync(string username)
        {
            var user = await OidcTestApp.UserAsync(_app, username);
            await _app.Services.GetRequiredService<IReviewStore>()
                .AddMembershipAsync(user.Id, ProjectId, TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Signs the slot as the user of the provider, without a password, for the current record.
        /// </summary>
        /// <returns>A task that completes when the slot is signed.</returns>
        public async Task SignAsync()
        {
            await EvidenceTestSupport.SignAsync(Signing, ProjectId, ReviewId, SlotId, Actor, string.Empty);
        }

        /// <summary>
        ///     Checks that a confirmation was refused for a reason: it returned to the review with a failure, the
        ///     failure is recorded with the reason class only, and signing is still refused.
        /// </summary>
        /// <param name="landing">The answer that ended the flow.</param>
        /// <param name="reason">The reason class.</param>
        /// <returns>A task that completes when everything is checked.</returns>
        public async Task AssertRefusedAsync(HttpResponseMessage landing, string reason)
        {
            Assert.Equal(
                $"{Route}?reauth={(reason is "missing-auth-time" or "stale-auth-time" ? reason : "failed")}",
                landing.Headers.Location?.OriginalString);
            var entry = Assert.Single(await OidcTestApp.AuditAsync(_app, "signature.reauth-failed"));
            Assert.Equal($"oidc: {reason}", entry.Detail);
            Assert.False(await Signing.HasReauthProofAsync(
                Actor, ProjectId, ReviewId, SlotId, TestContext.Current.CancellationToken));
            var failure = await Assert.ThrowsAsync<DomainRuleException>(() => SignAsync());
            Assert.Equal(DomainRule.ReauthenticationFailed, failure.Rule);
        }
    }
}

