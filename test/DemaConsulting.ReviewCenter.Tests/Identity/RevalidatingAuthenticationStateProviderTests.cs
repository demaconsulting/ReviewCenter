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
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Unit tests for <see cref="RevalidatingAuthenticationStateProvider"/>: the rule that decides whether an open
///     page keeps its user, the fail-closed and cancellation behavior, and the framework loop that applies it.
/// </summary>
public class RevalidatingAuthenticationStateProviderTests
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     A check interval far shorter than the configured minimum, so the loop tests are fast.
    /// </summary>
    private static readonly TimeSpan ShortInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     How long a loop test waits for the state to change before it gives up.
    /// </summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Test that an enabled user with the same role and display name stays valid.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_EnabledUserSameRole_ReturnsTrue()
    {
        // Arrange: a stored reviewer and a state signed in as that user
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var provider = NewProvider(users);

        // Act: check the state
        var current = await provider.IsCurrentAsync(StateOf("ann", "Ann", UserRole.Reviewer), TestContext.Current.CancellationToken);

        // Assert: still valid
        Assert.True(current);
    }

    /// <summary>
    ///     Test that a user who is disabled after sign-in is no longer valid.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_UserDisabled_ReturnsFalse()
    {
        // Arrange: a stored reviewer who is then disabled
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        var provider = NewProvider(users);

        // Act: check the state
        var current = await provider.IsCurrentAsync(StateOf("ann", "Ann", UserRole.Reviewer), TestContext.Current.CancellationToken);

        // Assert: no longer valid
        Assert.False(current);
    }

    /// <summary>
    ///     Test that a user who is not in the store at all is not valid.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_UserDeleted_ReturnsFalse()
    {
        // Arrange: a store that has no user for the signed-in name
        var provider = NewProvider(NewUsers());

        // Act: check the state
        var current = await provider.IsCurrentAsync(StateOf("ghost", "Ghost", UserRole.Reviewer), TestContext.Current.CancellationToken);

        // Assert: no longer valid
        Assert.False(current);
    }

    /// <summary>
    ///     Test that a changed role, in either direction, makes the state not valid.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin, UserRole.Reviewer)]
    [InlineData(UserRole.Reviewer, UserRole.Admin)]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_RoleChanged_ReturnsFalse(UserRole signedInRole, UserRole storedRole)
    {
        // Arrange: a user stored with a different role than the one in the state
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann", storedRole, Password, TestContext.Current.CancellationToken);
        var provider = NewProvider(users);

        // Act: check the state
        var current = await provider.IsCurrentAsync(StateOf("ann", "Ann", signedInRole), TestContext.Current.CancellationToken);

        // Assert: no longer valid, so the user signs in again and gets the current role
        Assert.False(current);
    }

    /// <summary>
    ///     Test that a changed display name makes the state not valid, as it does for the cookie.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_DisplayNameChanged_ReturnsFalse()
    {
        // Arrange: a user stored with another display name than the one in the state
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann B.", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var provider = NewProvider(users);

        // Act: check the state
        var current = await provider.IsCurrentAsync(StateOf("ann", "Ann", UserRole.Reviewer), TestContext.Current.CancellationToken);

        // Assert: no longer valid
        Assert.False(current);
    }

    /// <summary>
    ///     Test that a state without a user name is not valid and does not reach the store.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_NoNameClaim_ReturnsFalse()
    {
        // Arrange: a store that records whether it was read, and a state without a name
        var read = false;
        var store = new ScriptedUserStore((_, _) =>
        {
            read = true;
            return Task.FromResult<User?>(null);
        });
        var provider = NewProvider(new UserService(store, new FakePasswordHasher(), TimeProvider.System));
        var state = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Reviewer")], "Test")));

        // Act: check the state
        var current = await provider.IsCurrentAsync(state, TestContext.Current.CancellationToken);

        // Assert: not valid, and the store was not read
        Assert.False(current);
        Assert.False(read);
    }

    /// <summary>
    ///     Test that a failing lookup ends the state (fail closed) instead of keeping the user or throwing.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_LookupThrows_ReturnsFalseFailClosed()
    {
        // Arrange: a store that fails, for example because the database is unavailable, and a captured log
        var store = new ScriptedUserStore((_, _) => throw new InvalidOperationException("The database is unavailable."));
        var logs = new CapturingLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var provider = new RevalidatingAuthenticationStateProvider(
            loggers, new UserService(store, new FakePasswordHasher(), TimeProvider.System), ShortInterval);

        // Act: check the state
        var current = await provider.IsCurrentAsync(StateOf("ann", "Ann", UserRole.Reviewer), TestContext.Current.CancellationToken);

        // Assert: an unverifiable user is not valid, and no exception reaches the framework
        Assert.False(current);

        // Assert: exactly one warning names the user, and it carries no secret (no password, hash, or claim value)
        var entry = Assert.Single(logs.Entries, e => e.Category.EndsWith(nameof(RevalidatingAuthenticationStateProvider), StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("'ann'", entry.Message, StringComparison.Ordinal);
        Assert.Equal("ann", entry.Properties["Username"]);
        Assert.DoesNotContain("password", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ann", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a cancelled lookup is not treated as a failure: it is rethrown as the exception, carrying the
    ///     token, that the framework loop treats as a clean stop.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_IsCurrentAsync_CancelledLookup_ThrowsTaskCanceledWithToken()
    {
        // Arrange: a store whose lookup is cancelled, as when the circuit closes
        using var cancellation = new CancellationTokenSource();
        var store = new ScriptedUserStore((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<User?>(null);
        });
        var provider = NewProvider(new UserService(store, new FakePasswordHasher(), TimeProvider.System));

        // Act: check the state
        var exception = await Assert.ThrowsAsync<TaskCanceledException>(
            () => provider.IsCurrentAsync(StateOf("ann", "Ann", UserRole.Reviewer), cancellation.Token));

        // Assert: the exception carries the token that was cancelled
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    /// <summary>
    ///     Test that the loop turns the state anonymous soon after the user is disabled.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_Loop_UserDisabled_StateBecomesAnonymous()
    {
        // Arrange: a signed-in state that is checked every 50 milliseconds
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        using var provider = NewProvider(users);
        provider.SetAuthenticationState(Task.FromResult(StateOf("ann", "Ann", UserRole.Reviewer)));

        // Act: disable the user and wait for the state to change
        await users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        var authenticated = await WaitUntilAnonymousAsync(provider);

        // Assert: nobody is signed in any more
        Assert.False(authenticated);
    }

    /// <summary>
    ///     Test that the loop turns the state anonymous when the lookup fails (fail closed).
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_Loop_LookupFails_StateBecomesAnonymous()
    {
        // Arrange: a signed-in state over a store that fails
        var store = new ScriptedUserStore((_, _) => throw new InvalidOperationException("The database is unavailable."));
        using var provider = NewProvider(new UserService(store, new FakePasswordHasher(), TimeProvider.System));

        // Act: sign in and wait for the state to change
        provider.SetAuthenticationState(Task.FromResult(StateOf("ann", "Ann", UserRole.Reviewer)));
        var authenticated = await WaitUntilAnonymousAsync(provider);

        // Assert: nobody is signed in any more
        Assert.False(authenticated);
    }

    /// <summary>
    ///     Test that the loop leaves a valid user signed in over several checks.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_Loop_UserStaysValid_StateStaysAuthenticated()
    {
        // Arrange: a signed-in state of a valid user
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        using var provider = NewProvider(users);
        provider.SetAuthenticationState(Task.FromResult(StateOf("ann", "Ann", UserRole.Reviewer)));

        // Act: let about ten checks pass
        await Task.Delay(ShortInterval * 10, TestContext.Current.CancellationToken);
        var state = await provider.GetAuthenticationStateAsync();

        // Assert: still signed in
        Assert.True(state.User.Identity?.IsAuthenticated);
    }

    /// <summary>
    ///     Test that disposing the provider stops the loop, so a later change is not applied.
    /// </summary>
    [Fact]
    public async Task RevalidatingAuthenticationStateProvider_Dispose_StopsLoop()
    {
        // Arrange: a signed-in state whose provider is disposed (the circuit closed)
        var users = NewUsers();
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var provider = NewProvider(users);
        provider.SetAuthenticationState(Task.FromResult(StateOf("ann", "Ann", UserRole.Reviewer)));
        ((IDisposable)provider).Dispose();

        // Act: disable the user and let several intervals pass
        await users.SetEnabledAsync("ann", false, TestContext.Current.CancellationToken);
        await Task.Delay(ShortInterval * 10, TestContext.Current.CancellationToken);
        var state = await provider.GetAuthenticationStateAsync();

        // Assert: nothing checked the user, so the state is unchanged
        Assert.True(state.User.Identity?.IsAuthenticated);
    }

    /// <summary>
    ///     Test that the provider refuses missing arguments.
    /// </summary>
    [Fact]
    public void RevalidatingAuthenticationStateProvider_Constructor_NullArguments_Throw()
    {
        // Arrange: a valid user service
        var users = NewUsers();

        // Act and assert: a missing user service or options is refused
        Assert.Throws<ArgumentNullException>(() => new RevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, null!, ShortInterval));
        Assert.Throws<ArgumentNullException>(() => new RevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, users, (IOptions<AuthOptions>)null!));
    }

    /// <summary>
    ///     Creates a user service over an in-memory store.
    /// </summary>
    /// <returns>The user service.</returns>
    private static UserService NewUsers()
    {
        return new UserService(new FakeUserStore(), new FakePasswordHasher(), TimeProvider.System);
    }

    /// <summary>
    ///     Creates the provider under test with a short interval.
    /// </summary>
    /// <param name="users">The user service.</param>
    /// <returns>The provider; the caller disposes it when the loop runs.</returns>
    private static RevalidatingAuthenticationStateProvider NewProvider(UserService users)
    {
        return new RevalidatingAuthenticationStateProvider(NullLoggerFactory.Instance, users, ShortInterval);
    }

    /// <summary>
    ///     Builds the state of a signed-in user with the claims the application issues.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="role">The role in the state.</param>
    /// <returns>The state.</returns>
    private static AuthenticationState StateOf(string username, string displayName, UserRole role)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, username),
                new Claim(SignIn.DisplayNameClaimType, displayName),
                new Claim(ClaimTypes.Role, role == UserRole.Admin ? SignIn.AdminRole : "Reviewer")
            ],
            "Test");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    ///     Waits until the provider reports nobody signed in, or the timeout passes.
    /// </summary>
    /// <param name="provider">The provider to poll.</param>
    /// <returns>Whether somebody is still signed in when the wait ends.</returns>
    private static async Task<bool> WaitUntilAnonymousAsync(AuthenticationStateProvider provider)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (DateTime.UtcNow < deadline)
        {
            var state = await provider.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            await Task.Delay(ShortInterval, TestContext.Current.CancellationToken);
        }

        return true;
    }

    /// <summary>
    ///     A user store whose lookup is a delegate, to script failures and cancellation.
    /// </summary>
    /// <param name="get">The lookup of a user by name.</param>
    private sealed class ScriptedUserStore(Func<string, CancellationToken, Task<User?>> get) : IUserStore
    {
        /// <inheritdoc />
        public Task<User> AddAsync(
            User user,
            Func<User, AuditDraft> audit,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<User> AddToProjectAsync(
            User user,
            int projectId,
            ProjectRole role,
            Func<User, AuditDraft> audit,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            return get(username, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public Task UpdateAsync(User user, AuditDraft? audit, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
