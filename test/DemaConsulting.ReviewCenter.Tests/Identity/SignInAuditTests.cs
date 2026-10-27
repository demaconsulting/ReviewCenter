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
    ///     Test that a failed login for an unknown user records the typed name (cleaned) and never the password.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_UnknownUser_RecordsCleanedTypedNameWithoutPassword()
    {
        // Arrange
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);
        const string TypedPassword = "typed-secret-password-1";

        // Act
        await browser.LoginAsync("mallory\r\nforged line", TypedPassword);

        // Assert: the name is on one line, the actor is the plain unknown user, the password is nowhere
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-in-failed"));
        Assert.Equal("unknown user", entry.ActorName);
        Assert.Equal("unknown user, name 'mallory  forged line'", entry.Detail);
        Assert.Null(entry.ActorUserId);
        await AssertNoSecretsAsync(app, TypedPassword);
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
    ///     Test that repeated failures record the lockout once, and that a login during the lockout is refused.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_RepeatedFailures_RecordsLockoutOnce()
    {
        // Arrange: a high throttle limit so that the account lockout is what stops the sixth attempt
        await using var app = await StartAsync(perName: 100);
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
    ///     Test that logins on a locked account, however many, are recorded as one locked entry per minute.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_LockedAccount_RecordsOneSignInLockedEntry()
    {
        // Arrange: a locked account and a high throttle limit
        await using var app = await StartAsync(perName: 100);
        await CreateUserAsync(app, "ann");
        using var browser = new TestBrowser(app);
        for (var i = 0; i < 5; i++)
        {
            await browser.LoginAsync("ann", "typo-attempt-" + i);
        }

        // Act: three logins while the account is locked
        for (var i = 0; i < 3; i++)
        {
            await browser.LoginAsync("ann", TestBrowser.Password);
        }

        // Assert: one entry for the user, without the password
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-in-locked"));
        Assert.Equal("Ann (ann)", entry.ActorName);
        Assert.Equal("account locked", entry.Detail);
        await AssertNoSecretsAsync(app, TestBrowser.Password);
    }

    /// <summary>
    ///     Test that refusals by the throttle are recorded as one entry per minute and name, with the cleaned typed name.
    /// </summary>
    [Fact]
    public async Task SignIn_Login_Throttled_RecordsOneThrottledEntryPerWindow()
    {
        // Arrange: a limit of two attempts per name
        await using var app = await StartAsync(perName: 2);
        using var browser = new TestBrowser(app);

        // Act: two attempts that pass, then four that are refused
        for (var i = 0; i < 6; i++)
        {
            await browser.LoginAsync("Mallory", "typed-secret-password-3");
        }

        // Assert: one throttled entry, and the two attempts that went through were recorded as unknown-user failures once
        var entry = Assert.Single(await EntriesAsync(app, "auth.sign-in-throttled"));
        Assert.Equal("not signed in", entry.ActorName);
        Assert.Equal("too many attempts, name 'Mallory'", entry.Detail);
        Assert.Single(await EntriesAsync(app, "auth.sign-in-failed"));
        await AssertNoSecretsAsync(app, "typed-secret-password-3");
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
    /// <param name="perName">The throttle limit per name; <c>null</c> for the default.</param>
    /// <returns>The application.</returns>
    private static async Task<WebApplication> StartAsync(int? perName = null)
    {
        var settings = perName is null
            ? null
            : new Dictionary<string, string?>
            {
                ["Identity:Throttle:MaxFailuresPerName"] = perName.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Identity:Throttle:MaxFailuresPerAddress"] = "1000"
            };
        return await TestWebApp.StartAsync(new CapturingLoggerProvider(), TestDatabase.NewConnectionString(), ["serve", "--silent"], settings);
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
