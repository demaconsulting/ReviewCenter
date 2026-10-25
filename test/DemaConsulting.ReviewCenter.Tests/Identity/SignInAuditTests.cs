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
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests that sign-in, sign-out, failed sign-in and lockout are recorded in the audit trail of the real
///     (SQLite-backed, in-memory hosted) application, and that nothing secret ends up there.
/// </summary>
public class SignInAuditTests
{
    /// <summary>
    ///     Test that a successful login is recorded with the user.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_ValidCredentials_RecordsSignInEntry()
    {
        // Arrange
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);

        // Act
        await browser.LoginAsync("ann", TestBrowser.Password);

        // Assert
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-in"));
        Assert.Equal("signed in", entry.Detail);
        Assert.Equal("Ann (ann)", entry.ActorName);
        Assert.NotNull(entry.ActorUserId);
        Assert.True((await Verify(app)).IsIntact);
    }

    /// <summary>
    ///     Test that a logout is recorded with the user.
    /// </summary>
    [Fact]
    public async Task SignIn_Logout_SignedInUser_RecordsSignOutEntry()
    {
        // Arrange
        await using var app = await StartAsync();
        using var browser = await TestBrowser.SignedInAsync(app, "ann");

        // Act
        await browser.PostFormAsync(SignIn.LogoutEndpoint, []);

        // Assert
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-out"));
        Assert.Equal("signed out", entry.Detail);
        Assert.Contains("(ann)", entry.ActorName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failed login for an unknown user is recorded without echoing the typed name or password.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_UnknownUser_RecordsUnknownUserWithoutEchoingInput()
    {
        // Arrange
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);
        const string TypedPassword = "typed-secret-password-1";

        // Act
        await browser.LoginAsync("<i>mallory</i>", TypedPassword);

        // Assert
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-in-failed"));
        Assert.Equal("unknown user", entry.ActorName);
        Assert.Equal("unknown user", entry.Detail);
        await AssertNoSecretsAsync(app, "mallory", TypedPassword);
    }

    /// <summary>
    ///     Test that a failed login for an existing user records the user and the reason, never the password.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_WrongPassword_RecordsFailureForTheUserWithoutPassword()
    {
        // Arrange
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);
        const string TypedPassword = "typed-secret-password-2";

        // Act
        await browser.LoginAsync("ann", TypedPassword);

        // Assert
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-in-failed"));
        Assert.Equal("Ann (ann)", entry.ActorName);
        Assert.Equal("wrong password", entry.Detail);
        await AssertNoSecretsAsync(app, TypedPassword);
    }

    /// <summary>
    ///     Test that repeated failures record the lockout, and that a login during the lockout is refused and not recorded.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_RepeatedFailures_RecordsLockoutOnce()
    {
        // Arrange
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");

        // Act: five wrong passwords, then the right one while locked
        for (var i = 0; i < 5; i++)
        {
            using var browser = new TestBrowser(app);
            await browser.LoginAsync("ann", "typo-attempt-" + i);
        }

        using var locked = new TestBrowser(app);
        await locked.LoginAsync("ann", TestBrowser.Password);

        // Assert
        Assert.Equal(5, (await EntriesAsync(app, "auth.sign-in-failed")).Count);
        var lockout = Assert.Single(await EntriesAsync(app, "auth.locked-out"));
        Assert.Equal("locked for 5 minutes after 5 failed attempts", lockout.Detail);
        Assert.Empty(await EntriesAsync(app, "auth.sign-in"));
        await AssertNoSecretsAsync(app, "typo-attempt", TestBrowser.Password);
    }

    /// <summary>
    ///     Test that the trail of the whole flow is a verifiable chain.
    /// </summary>
    [Fact]
    public async Task SignIn_Flow_LoginFailureAndLogout_FormAnIntactChain()
    {
        // Arrange
        await using var app = await StartAsync();
        await CreateUserAsync(app, "ann");
        using var failing = new TestBrowser(app);
        await failing.LoginAsync("ann", "wrong password here");
        using var browser = await TestBrowser.SignedInAsync(app, "ann");

        // Act
        await browser.PostFormAsync(SignIn.LogoutEndpoint, []);

        // Assert
        var result = await Verify(app);
        Assert.True(result.IsIntact);
        Assert.True(result.Count >= 4);
    }

    /// <summary>
    ///     Reads the entries with an action, newest first.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="action">The action code.</param>
    /// <returns>The entries.</returns>
    private static async Task<IReadOnlyList<AuditEntry>> EntriesAsync(WebApplication app, string action)
    {
        var all = await AllAsync(app);
        return all.Where(e => e.Action == action).ToList();
    }

    /// <summary>
    ///     Reads every entry of the trail.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The entries.</returns>
    private static async Task<IReadOnlyList<AuditEntry>> AllAsync(WebApplication app)
    {
        var audit = app.Services.GetRequiredService<AuditService>();
        var result = new List<AuditEntry>();
        long? before = null;
        while (true)
        {
            var page = await audit.ListAsync(null, null, before, null, TestContext.Current.CancellationToken);
            result.AddRange(page.Entries);
            if (!page.HasOlder)
            {
                return result;
            }

            before = page.OlderBefore;
        }
    }

    /// <summary>
    ///     Verifies the chain of the application.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The result.</returns>
    private static Task<AuditVerificationResult> Verify(WebApplication app)
    {
        return app.Services.GetRequiredService<AuditService>().VerifyAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Asserts that none of the texts appears in any field of any entry.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="secrets">The texts that must not appear.</param>
    private static async Task AssertNoSecretsAsync(WebApplication app, params string[] secrets)
    {
        foreach (var entry in await AllAsync(app))
        {
            var text = $"{entry.ActorName}|{entry.Action}|{entry.Detail}|{entry.PreviousHash}|{entry.Hash}";
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    ///     Starts the application over a temporary database.
    /// </summary>
    /// <returns>The application.</returns>
    private static async Task<WebApplication> StartAsync()
    {
        return await TestWebApp.StartAsync(new CapturingLoggerProvider(), TestDatabase.NewConnectionString(), ["serve", "--silent"]);
    }

    /// <summary>
    ///     Creates a user with the password the test browser uses.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="username">The user name.</param>
    private static async Task CreateUserAsync(WebApplication app, string username)
    {
        await app.Services.GetRequiredService<UserService>()
            .CreateAsync(username, char.ToUpperInvariant(username[0]) + username[1..], UserRole.Reviewer, TestBrowser.Password, Actor.CommandLine, TestContext.Current.CancellationToken);
    }
}
