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
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Api;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Integration tests of the two-step sign-in with a time-based one-time password, exercised over the in-memory
///     application: the first step must never issue a session, every failure of the second step must look the same,
///     and the code, the secret and the challenge must never be recorded.
/// </summary>
public class TotpSignInTests
{
    /// <summary>
    ///     The header a test sets to make the server see another client address.
    /// </summary>
    private const string AddressHeader = "X-Test-Address";

    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the password step of an enrolled account sets only the challenge cookie (HttpOnly, SameSite
    ///     Strict, five minutes) and sends the browser to the code page: no session cookie, no access.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_PasswordStep_EnrolledAccount_IssuesChallengeCookieAndNoSession()
    {
        // Arrange: an enrolled account
        await using var world = await World.StartAsync();
        await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);

        // Act: the right password, then a page that needs a session
        var response = await browser.LoginAsync("ann", TestBrowser.Password, "/projects");
        var account = await browser.Client.GetAsync("/account", Token);

        // Assert: redirected to the code page, a safe challenge cookie, and no session of any kind
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login/totp", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
        var challenge = Assert.Single(ChallengeCookies(browser));
        Assert.Contains("HttpOnly", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=300", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(HttpStatusCode.OK, account.StatusCode);
    }

    /// <summary>
    ///     Test that the challenge cookie is marked Secure when the browser uses https.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_PasswordStep_OverHttps_ChallengeCookieIsSecure()
    {
        // Arrange: an enrolled account and a browser that uses https
        await using var world = await World.StartAsync();
        await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App, https: true);

        // Act: the password step
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Assert: Secure
        Assert.Contains("secure", Assert.Single(ChallengeCookies(browser)), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that an account without a second factor still signs in with the password alone and gets no challenge.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_PasswordStep_AccountWithoutSecondFactor_SignsInAsBefore()
    {
        // Arrange: a plain account
        await using var world = await World.StartAsync();
        await world.AddUserAsync("ann");
        using var browser = new TestBrowser(world.App);

        // Act: sign in
        var response = await browser.LoginAsync("ann", TestBrowser.Password, "/projects");

        // Assert: signed in, no challenge
        Assert.Equal("/projects", response.Headers.Location?.OriginalString);
        Assert.Single(AuthCookies(browser));
        Assert.Empty(ChallengeCookies(browser));
    }

    /// <summary>
    ///     Test that a right code completes the sign-in: the session cookie appears, the challenge cookie is removed,
    ///     the browser lands on the requested place, and the page of the account opens.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_RightCode_SignsInAndRedirects()
    {
        // Arrange: an enrolled account and a browser past the password step
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password, "/projects");

        // Act: the code of the current step
        var response = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: redirected to the requested place with a session; the challenge is gone
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/projects", response.Headers.Location?.OriginalString);
        var session = Assert.Single(AuthCookies(browser));
        Assert.Contains("HttpOnly", session, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(TotpSignIn.CookieName + "=;", ChallengeCookies(browser)[^1], StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/account", Token)).StatusCode);
        var audit = await world.AuditTextAsync();
        Assert.Contains("second factor: authenticator code", audit, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a code one step before and one step after the current one is accepted (clock drift).
    /// </summary>
    /// <param name="offset">The steps away from the current one.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task TotpSignIn_CodeStep_NeighboringStep_SignsIn(int offset)
    {
        // Arrange: an enrolled account and a browser past the password step
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        world.Clock.Advance(TimeSpan.FromSeconds(30));
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: the code of the neighboring step
        var response = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret, offset)));

        // Assert: signed in
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Single(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a wrong code, an out-of-window code, a code that was already used, and malformed input all get
    ///     exactly the same answer and no session, so the answer does not say why the code was refused.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_EveryKindOfFailure_GivesTheSameAnswerAndNoSession()
    {
        // Arrange: ann signed in once with the code of this step, and a second browser past the password step
        await using var world = await World.StartAsync(generousThrottle: true);
        var secret = await world.AddEnrolledUserAsync("ann");
        var used = world.CodeNow(secret);
        using (var first = new TestBrowser(world.App))
        {
            await first.LoginAsync("ann", TestBrowser.Password);
            Assert.Equal(HttpStatusCode.Redirect, (await first.PostFormAsync("/auth/login-totp", Code(used))).StatusCode);
        }

        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: the used code, a wrong code, a code from far away, and input that is not a code (four failures: one
        // short of the lockout)
        var attempts = new[] { used, world.WrongCode(secret), world.CodeNow(secret, 5), "abcdef" };
        var locations = new List<string?>();
        foreach (var code in attempts)
        {
            var response = await browser.PostFormAsync("/auth/login-totp", Code(code));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            locations.Add(response.Headers.Location?.OriginalString);
        }

        // Assert: one and the same redirect, and no session was issued to the second browser
        Assert.All(locations, location => Assert.Equal("/login/totp?error=1", location));
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that an empty form and a form without the code field are answered like a wrong code.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_MissingOrEmptyCode_IsAnsweredLikeAWrongCode()
    {
        // Arrange: a browser past the password step
        await using var world = await World.StartAsync();
        await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: no field and an empty field
        var none = await browser.PostFormAsync("/auth/login-totp", []);
        var empty = await browser.PostFormAsync("/auth/login-totp", Code(string.Empty));

        // Assert: the same redirect as for a wrong code
        Assert.Equal("/login/totp?error=1", none.Headers.Location?.OriginalString);
        Assert.Equal("/login/totp?error=1", empty.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a recovery code signs in once and is refused the second time, and the sign-in is recorded as
    ///     one with a recovery code.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_RecoveryCode_WorksOnce()
    {
        // Arrange: an enrolled account with its recovery codes
        await using var world = await World.StartAsync();
        await world.AddEnrolledUserAsync("ann");
        var recovery = world.RecoveryCodes[0];

        // Act: use the code in two sign-ins
        using var first = new TestBrowser(world.App);
        await first.LoginAsync("ann", TestBrowser.Password);
        var accepted = await first.PostFormAsync("/auth/login-totp", Code(recovery));
        using var second = new TestBrowser(world.App);
        await second.LoginAsync("ann", TestBrowser.Password);
        var refused = await second.PostFormAsync("/auth/login-totp", Code(recovery));

        // Assert: accepted once, refused like any wrong code, and recorded
        Assert.Equal("/", accepted.Headers.Location?.OriginalString);
        Assert.Single(AuthCookies(first));
        Assert.Equal("/login/totp?error=1", refused.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(second));
        var audit = await world.AuditTextAsync();
        Assert.Contains("auth.totp-recovery-used", audit, StringComparison.Ordinal);
        Assert.Contains("second factor: recovery code", audit, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a code spent by a sign-in is refused for the next sign-in in the same time step (replay).
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_CodeUsedBefore_IsRefusedAndTheNextStepWorks()
    {
        // Arrange: a sign-in that used the code of the current step
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using (var first = new TestBrowser(world.App))
        {
            await first.LoginAsync("ann", TestBrowser.Password);
            await first.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));
        }

        // Act: a new sign-in in the same step, then in the next one
        using var second = new TestBrowser(world.App);
        await second.LoginAsync("ann", TestBrowser.Password);
        var replay = await second.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));
        world.Clock.Advance(TimeSpan.FromSeconds(30));
        var next = await second.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: the replay failed (and the challenge was kept), the next step's code signed in
        Assert.Equal("/login/totp?error=1", replay.Headers.Location?.OriginalString);
        Assert.Equal("/", next.Headers.Location?.OriginalString);
        Assert.Single(AuthCookies(second));
    }

    /// <summary>
    ///     Test that a code request without a challenge cookie is sent back to the login page and issues nothing.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_NoChallenge_RedirectsToLoginAndIssuesNothing()
    {
        // Arrange: an enrolled account, but a browser that never did the password step
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);

        // Act: post a right code anyway
        var response = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: expired answer and no session
        Assert.Equal("/login?error=totp-expired", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a made-up challenge cookie is refused like a missing one.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_ForgedChallengeCookie_IsRefused()
    {
        // Arrange: an enrolled account and a request that carries a guessed token
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        var form = new List<KeyValuePair<string, string>>(Code(world.CodeNow(secret)))
        {
            new("__RequestVerificationToken", await browser.GetTokenAsync())
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login-totp")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.TryAddWithoutValidation("Cookie", $"{TotpSignIn.CookieName}={new string('A', 43)}");

        // Act: post
        var response = await browser.Client.SendAsync(request, Token);

        // Assert: refused, nothing issued
        Assert.Equal("/login?error=totp-expired", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a challenge used from another client address is refused, and that the refusal does not use up
    ///     the challenge of the owner.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_ChallengeFromAnotherAddress_IsRefusedAndNotConsumed()
    {
        // Arrange: a browser past the password step from the default address
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);
        var challenges = world.App.Services.GetRequiredService<TotpChallengeStore>();

        // Act: the cookie is presented from somewhere else with a right code
        browser.Client.DefaultRequestHeaders.Add(AddressHeader, "203.0.113.9");
        var foreign = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: the foreign attempt got the expired answer and no session, and the challenge is still pending
        Assert.Equal("/login?error=totp-expired", foreign.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
        Assert.Equal(1, challenges.Count());
    }

    /// <summary>
    ///     Test that a challenge that is older than its lifetime is refused even with a right code.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_ExpiredChallenge_IsRefused()
    {
        // Arrange: a browser past the password step
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: wait longer than the challenge lives, then send the code of that time
        world.Clock.Advance(TotpChallengeStore.Lifetime + TimeSpan.FromSeconds(1));
        var response = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: expired answer and no session
        Assert.Equal("/login?error=totp-expired", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a challenge can only be used once: after a successful sign-in, the same cookie is worthless.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_ChallengeAfterSuccess_CannotBeUsedAgain()
    {
        // Arrange: a completed sign-in; the challenge cookie value is kept
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);
        var challengeValue = ChallengeCookies(browser)[0].Split(';')[0];
        await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));
        world.Clock.Advance(TimeSpan.FromSeconds(30));

        // Act: present the old cookie again with the new step's code
        using var other = new TestBrowser(world.App);
        var form = new List<KeyValuePair<string, string>>(Code(world.CodeNow(secret)))
        {
            new("__RequestVerificationToken", await other.GetTokenAsync())
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login-totp")
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.TryAddWithoutValidation("Cookie", challengeValue);
        var response = await other.Client.SendAsync(request, Token);

        // Assert: refused, no session for the other browser
        Assert.Equal("/login?error=totp-expired", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(other));
    }

    /// <summary>
    ///     Test that the code form needs the antiforgery token.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_WithoutAntiforgeryToken_IsRefused()
    {
        // Arrange: a browser past the password step
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: post a right code without the token
        var response = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)), withToken: false);

        // Assert: refused and no session
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that five wrong codes lock the account like five wrong passwords, and that a locked account cannot
    ///     pass the password step even with the right password.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_FiveWrongCodes_LockTheAccount()
    {
        // Arrange: a browser past the password step, and generous throttle limits so only the lockout acts
        await using var world = await World.StartAsync(generousThrottle: true);
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: five wrong codes, then a new sign-in with the right password and the right code
        for (var i = 0; i < 5; i++)
        {
            await browser.PostFormAsync("/auth/login-totp", Code(world.WrongCode(secret)));
        }

        using var later = new TestBrowser(world.App);
        var password = await later.LoginAsync("ann", TestBrowser.Password);
        var challengesAfterPassword = ChallengeCookies(later).Count;
        var code = await later.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: locked: the password step fails like a wrong password and nothing is issued
        Assert.Equal("/login?error=1", password.Headers.Location?.OriginalString);
        Assert.Equal(0, challengesAfterPassword);
        Assert.Equal("/login?error=totp-expired", code.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(later));
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that password failures and code failures share one counter: three wrong passwords and two wrong
    ///     codes lock the account.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_PasswordAndCodeFailures_ShareOneCounter()
    {
        // Arrange: an enrolled account with three wrong passwords behind it
        await using var world = await World.StartAsync(generousThrottle: true);
        var secret = await world.AddEnrolledUserAsync("ann");
        for (var i = 0; i < 3; i++)
        {
            using var wrong = new TestBrowser(world.App);
            await wrong.LoginAsync("ann", "wrong password here");
        }

        // Act: the password step passes (it does not reset the counter), then two wrong codes
        using var browser = new TestBrowser(world.App);
        var step = await browser.LoginAsync("ann", TestBrowser.Password);
        await browser.PostFormAsync("/auth/login-totp", Code(world.WrongCode(secret)));
        await browser.PostFormAsync("/auth/login-totp", Code(world.WrongCode(secret)));
        using var after = new TestBrowser(world.App);
        var locked = await after.LoginAsync("ann", TestBrowser.Password);

        // Assert: the first step passed, afterwards the account is locked
        Assert.Equal("/login/totp", step.Headers.Location?.OriginalString);
        Assert.Equal("/login?error=1", locked.Headers.Location?.OriginalString);
    }

    /// <summary>
    ///     Test that a complete sign-in resets the failure counter, so earlier failures do not add up over time.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_Success_ResetsTheFailureCounter()
    {
        // Arrange: four wrong codes in a row would be one short of the lockout; a success follows
        await using var world = await World.StartAsync(generousThrottle: true);
        var secret = await world.AddEnrolledUserAsync("ann");
        using var first = new TestBrowser(world.App);
        await first.LoginAsync("ann", TestBrowser.Password);
        for (var i = 0; i < 4; i++)
        {
            await first.PostFormAsync("/auth/login-totp", Code(world.WrongCode(secret)));
        }

        var success = await first.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Act: four more wrong codes after the success
        world.Clock.Advance(TimeSpan.FromSeconds(30));
        using var second = new TestBrowser(world.App);
        await second.LoginAsync("ann", TestBrowser.Password);
        for (var i = 0; i < 4; i++)
        {
            await second.PostFormAsync("/auth/login-totp", Code(world.WrongCode(secret)));
        }

        var again = await second.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: both sign-ins finished; the counter started from zero the second time
        Assert.Equal("/", success.Headers.Location?.OriginalString);
        Assert.Equal("/", again.Headers.Location?.OriginalString);
    }

    /// <summary>
    ///     Test that the sign-in throttle limits the code attempts too: past the limit even a right code is refused
    ///     with the throttled answer.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_TooManyAttempts_AreThrottledEvenForARightCode()
    {
        // Arrange: a limit of three attempts per name and a browser past the password step
        await using var world = await World.StartAsync(perName: 3);
        var secret = await world.AddEnrolledUserAsync("ann");
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Act: wrong codes until the throttle answers, then a right code
        var answers = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            var response = await browser.PostFormAsync("/auth/login-totp", Code(world.WrongCode(secret)));
            answers.Add(response.Headers.Location?.OriginalString);
        }

        var right = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: failures first, then the throttled answer; the right code gets no session either
        Assert.Equal("/login/totp?error=1", answers[0]);
        Assert.Equal("/login/totp?error=throttled", answers[^1]);
        Assert.Equal("/login/totp?error=throttled", right.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that a correct password does not use up the quota of the client address: many enrolled users can
    ///     pass the password step from one address.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_PasswordStep_CorrectPassword_DoesNotUseUpTheAddressQuota()
    {
        // Arrange: an address limit of two and three enrolled accounts
        await using var world = await World.StartAsync(perName: 2, perAddress: 2);
        foreach (var name in new[] { "ann", "bob", "cyd" })
        {
            await world.AddEnrolledUserAsync(name);
        }

        // Act: all three pass the password step from the same address
        var locations = new List<string?>();
        foreach (var name in new[] { "ann", "bob", "cyd" })
        {
            using var browser = new TestBrowser(world.App);
            locations.Add((await browser.LoginAsync(name, TestBrowser.Password)).Headers.Location?.OriginalString);
        }

        // Assert: none was throttled
        Assert.All(locations, location => Assert.Equal("/login/totp", location));
    }

    /// <summary>
    ///     Test that disabling or resetting the second factor ends a pending challenge, so the code step no longer
    ///     signs anybody in.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_CodeStep_AfterAdministratorReset_ChallengeIsDead()
    {
        // Arrange: a browser past the password step, and a reset by the administrator
        await using var world = await World.StartAsync();
        var secret = await world.AddEnrolledUserAsync("ann");
        await world.App.Services.GetRequiredService<UserService>()
            .CreateAsync("root", "Root", true, TestBrowser.Password, Token);
        using var browser = new TestBrowser(world.App);
        await browser.LoginAsync("ann", TestBrowser.Password);
        await world.App.Services.GetRequiredService<TotpService>().ResetAsync(new Actor("root", "Root"), "ann", Token);

        // Act: the code step
        var response = await browser.PostFormAsync("/auth/login-totp", Code(world.CodeNow(secret)));

        // Assert: refused and no session
        Assert.Equal("/login?error=totp-expired", response.Headers.Location?.OriginalString);
        Assert.Empty(AuthCookies(browser));
    }

    /// <summary>
    ///     Test that after a reset the account signs in with the password alone (it has no second factor any more).
    /// </summary>
    [Fact]
    public async Task TotpSignIn_PasswordStep_AfterAdministratorReset_SignsInWithPasswordOnly()
    {
        // Arrange: an enrolled account that the administrator resets
        await using var world = await World.StartAsync();
        await world.AddEnrolledUserAsync("ann");
        await world.App.Services.GetRequiredService<UserService>()
            .CreateAsync("root", "Root", true, TestBrowser.Password, Token);
        await world.App.Services.GetRequiredService<TotpService>().ResetAsync(new Actor("root", "Root"), "ann", Token);

        // Act: sign in
        using var browser = new TestBrowser(world.App);
        var response = await browser.LoginAsync("ann", TestBrowser.Password);

        // Assert: a session and no challenge
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Single(AuthCookies(browser));
        Assert.Empty(ChallengeCookies(browser));
    }

    /// <summary>
    ///     Test that with the feature switched off the code endpoint does not exist, and an enrolled account signs in
    ///     with the password alone (the enrollment stays dormant).
    /// </summary>
    [Fact]
    public async Task TotpSignIn_FeatureDisabled_NoEndpointAndPasswordOnlySignIn()
    {
        // Arrange: an account enrolled while the feature was on, then the same database with the feature off
        var connectionString = TestDatabase.NewConnectionString();
        await using (var first = await World.StartAsync(connectionString: connectionString))
        {
            await first.AddEnrolledUserAsync("ann");
        }

        await using var world = await World.StartAsync(connectionString: connectionString, enabled: false);
        using var browser = new TestBrowser(world.App);

        // Act: sign in and post to the code endpoint
        var login = await browser.LoginAsync("ann", TestBrowser.Password);
        var code = await browser.Client.PostAsync(
            "/auth/login-totp", new FormUrlEncodedContent(Code("123456")), Token);

        // Assert: password-only sign-in, and the endpoint is not there
        Assert.Equal("/", login.Headers.Location?.OriginalString);
        Assert.Single(AuthCookies(browser));
        Assert.Empty(ChallengeCookies(browser));
        Assert.Equal(HttpStatusCode.NotFound, code.StatusCode);
    }

    /// <summary>
    ///     Test that a personal access token of an enrolled account keeps working (a token never needs a code) and
    ///     that the account's session cookie is not needed for it.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_PersonalAccessToken_EnrolledAccount_IsUnaffected()
    {
        // Arrange: an enrolled account with a token
        await using var world = await World.StartAsync();
        await world.AddEnrolledUserAsync("ann");
        var created = await ApiTestSupport.CreateTokenAsync(world.App, "ann");
        using var client = ApiTestSupport.ClientFor(world.App, created.Value);

        // Act: call the API with the token alone
        var response = await client.GetAsync("/api/projects", Token);

        // Assert: accepted
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     Test that neither the secret, the codes, the recovery codes nor the challenge token appear in the logs or
    ///     in the audit trail over a full life cycle: enrollment, sign-in, failures, recovery and reset.
    /// </summary>
    [Fact]
    public async Task TotpSignIn_FullLifeCycle_NeverRecordsSecretsCodesOrTheChallenge()
    {
        // Arrange: an enrolled account; the secrets are the things that must never be recorded
        await using var world = await World.StartAsync(generousThrottle: true);
        var secret = await world.AddEnrolledUserAsync("ann");
        var forbidden = new List<string> { TotpOracle.ToBase32(secret), Convert.ToHexString(secret), world.OtpauthUri };
        forbidden.AddRange(world.RecoveryCodes);
        forbidden.AddRange(world.RecoveryCodes.Select(c => c.Replace("-", string.Empty, StringComparison.Ordinal)));

        // Act: failures, a code sign-in, a recovery sign-in and a reset
        var typed = new List<string>();
        using (var browser = new TestBrowser(world.App))
        {
            await browser.LoginAsync("ann", TestBrowser.Password);
            forbidden.Add(ChallengeCookies(browser)[0].Split(';')[0].Split('=')[1]);
            var wrong = world.WrongCode(secret);
            typed.Add(wrong);
            await browser.PostFormAsync("/auth/login-totp", Code(wrong));
            var right = world.CodeNow(secret);
            typed.Add(right);
            await browser.PostFormAsync("/auth/login-totp", Code(right));
        }

        using (var browser = new TestBrowser(world.App))
        {
            await browser.LoginAsync("ann", TestBrowser.Password);
            await browser.PostFormAsync("/auth/login-totp", Code(world.RecoveryCodes[1]));
        }

        await world.App.Services.GetRequiredService<UserService>()
            .CreateAsync("root", "Root", true, TestBrowser.Password, Token);
        await world.App.Services.GetRequiredService<TotpService>().ResetAsync(new Actor("root", "Root"), "ann", Token);
        forbidden.AddRange(typed);

        // Assert: nothing is in the audit trail or in any log entry
        var recorded = await world.AuditTextAsync()
            + "\n" + string.Join('\n', world.Logs.Entries.Select(e => e.Message + " " + string.Join(' ', e.Properties.Values)));
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, recorded, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("auth.totp-failed", recorded, StringComparison.Ordinal);
        Assert.Contains("auth.totp-reset", recorded, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the form fields of a code post.
    /// </summary>
    /// <param name="code">The code as typed.</param>
    /// <returns>The fields.</returns>
    private static List<KeyValuePair<string, string>> Code(string code)
    {
        return [new("code", code)];
    }

    /// <summary>
    ///     Gets the <c>Set-Cookie</c> values that set the session cookie.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The values; empty when no session cookie was issued.</returns>
    private static List<string> AuthCookies(TestBrowser browser)
    {
        return browser.SetCookieHeaders
            .Where(h => h.StartsWith(SignIn.CookieName + "=", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    ///     Gets the <c>Set-Cookie</c> values that set or remove the challenge cookie.
    /// </summary>
    /// <param name="browser">The browser.</param>
    /// <returns>The values in the order they were sent.</returns>
    private static List<string> ChallengeCookies(TestBrowser browser)
    {
        return browser.SetCookieHeaders
            .Where(h => h.StartsWith(TotpSignIn.CookieName + "=", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    ///     A started application whose clock a test moves, with helpers that arrange accounts and codes.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        /// <summary>
        ///     The application.
        /// </summary>
        private readonly WebApplication _app;

        /// <summary>
        ///     Initializes a new instance of the <see cref="World"/> class.
        /// </summary>
        /// <param name="app">The started application.</param>
        /// <param name="clock">The clock of the application.</param>
        /// <param name="logs">The captured logs.</param>
        private World(WebApplication app, MovableTimeProvider clock, CapturingLoggerProvider logs)
        {
            _app = app;
            Clock = clock;
            Logs = logs;
        }

        /// <summary>
        ///     Gets the application.
        /// </summary>
        public WebApplication App => _app;

        /// <summary>
        ///     Gets the clock of the application.
        /// </summary>
        public MovableTimeProvider Clock { get; }

        /// <summary>
        ///     Gets the captured logs.
        /// </summary>
        public CapturingLoggerProvider Logs { get; }

        /// <summary>
        ///     Gets the recovery codes of the account enrolled last.
        /// </summary>
        public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

        /// <summary>
        ///     Gets the setup address of the account enrolled last.
        /// </summary>
        public string OtpauthUri { get; private set; } = string.Empty;

        /// <summary>
        ///     Starts an application on a clock that a test can move.
        /// </summary>
        /// <param name="connectionString">The database; <c>null</c> for a new one.</param>
        /// <param name="enabled">Whether the second factor is switched on.</param>
        /// <param name="perName">The throttle limit per name; <c>null</c> for the default.</param>
        /// <param name="perAddress">The throttle limit per address; <c>null</c> for the default.</param>
        /// <param name="generousThrottle"><c>true</c> to raise both throttle limits so only the lockout acts.</param>
        /// <returns>The world; the caller disposes it.</returns>
        public static async Task<World> StartAsync(
            string? connectionString = null,
            bool enabled = true,
            int? perName = null,
            int? perAddress = null,
            bool generousThrottle = false)
        {
            var settings = new Dictionary<string, string?> { ["Identity:Totp:Enabled"] = enabled ? "true" : "false" };
            if (generousThrottle)
            {
                perName = 1000;
                perAddress = 1000;
            }

            if (perName is not null)
            {
                settings["Identity:Throttle:MaxFailuresPerName"] = perName.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            settings["Identity:Throttle:MaxFailuresPerAddress"] = (perAddress ?? 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var clock = new MovableTimeProvider(DateTimeOffset.UtcNow);
            var logs = new CapturingLoggerProvider();
            var app = await TestWebApp.StartAsync(
                logs,
                connectionString,
                ["serve", "--silent"],
                settings,
                services =>
                {
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddTransient<IStartupFilter, AddressFilter>();
                });
            return new World(app, clock, logs);
        }

        /// <summary>
        ///     Creates a plain account with the standard test password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <returns>A task that completes when the account exists.</returns>
        public async Task AddUserAsync(string username)
        {
            await _app.Services.GetRequiredService<UserService>()
                .CreateAsync(username, username, UserRole.Reviewer, TestBrowser.Password, Token);
        }

        /// <summary>
        ///     Creates an account and enrolls it in the second factor.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <returns>The secret of the account.</returns>
        public async Task<byte[]> AddEnrolledUserAsync(string username)
        {
            await AddUserAsync(username);
            var totp = _app.Services.GetRequiredService<TotpService>();
            var actor = new Actor(username, username);
            var enrollment = await totp.BeginEnrollmentAsync(actor, TestBrowser.Password, Token);
            var secret = TotpOracle.FromBase32(enrollment.SecretBase32);
            RecoveryCodes = await totp.ConfirmEnrollmentAsync(actor, CodeNow(secret), Token);
            OtpauthUri = enrollment.OtpauthUri;

            // The enrollment used the code of this step; the sign-in tests start with the next one
            Clock.Advance(TimeSpan.FromSeconds(30));
            return secret;
        }

        /// <summary>
        ///     Computes the code of the current time step, or one that is some steps away.
        /// </summary>
        /// <param name="secret">The secret.</param>
        /// <param name="stepOffset">The steps before (negative) or after (positive) the current one.</param>
        /// <returns>The six digit code.</returns>
        public string CodeNow(byte[] secret, int stepOffset = 0)
        {
            return TotpOracle.CodeAt(secret, Clock.GetUtcNow(), stepOffset);
        }

        /// <summary>
        ///     Computes a six digit code that is not valid in any step the server accepts now.
        /// </summary>
        /// <param name="secret">The secret.</param>
        /// <returns>The code.</returns>
        public string WrongCode(byte[] secret)
        {
            var valid = new[] { -1, 0, 1 }.Select(o => CodeNow(secret, o)).ToHashSet();
            return Enumerable.Range(100000, 100)
                .Select(candidate => candidate.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .First(text => !valid.Contains(text));
        }

        /// <summary>
        ///     Reads the audit trail as one text.
        /// </summary>
        /// <returns>The action, detail and actor of every entry.</returns>
        public async Task<string> AuditTextAsync()
        {
            var entries = await OidcTestApp.AuditAsync(_app, null);
            return string.Join('\n', entries.Select(e => $"{e.Action} {e.Detail} {e.ActorName}"));
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    ///     A start-up filter that lets a test choose the client address of a request with a header.
    /// </summary>
    private sealed class AddressFilter : IStartupFilter
    {
        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(AddressHeader, out var value)
                        && IPAddress.TryParse(value.ToString(), out var address))
                    {
                        context.Connection.RemoteIpAddress = address;
                    }

                    return nextMiddleware(context);
                });
                next(app);
            };
        }
    }
}
