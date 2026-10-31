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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of the temporary password of an account created for a project
///     (<see cref="User.MustChangePassword"/>): what the account can do until the password is changed, and what the
///     change does.
/// </summary>
public class MustChangePasswordTests
{
    /// <summary>
    ///     The temporary password of the arranged account.
    /// </summary>
    private const string Temporary = "temporary password 1";

    /// <summary>
    ///     The password the account chooses.
    /// </summary>
    private const string Chosen = "a much better password";

    /// <summary>
    ///     The instant at which the clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that an account with a temporary password signs in, so that it can reach the page that changes it.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_TemporaryPassword_SignsIn()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var user = await world.Users.AuthenticateAsync("newbie", Temporary, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(user);
        Assert.True(user.MustChangePassword);
    }

    /// <summary>
    ///     Test that the temporary password cannot confirm a signature: the re-authentication fails, and the failure
    ///     is audited with its reason.
    /// </summary>
    [Fact]
    public async Task UserService_ReauthenticateAsync_TemporaryPassword_IsRefusedAndAudited()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var user = await world.Users.ReauthenticateAsync("newbie", Temporary, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(user);
        var entry = Assert.Single(world.AuditStore.Entries, e => e.Action == "signature.reauth-failed");
        Assert.Contains("password change pending", entry.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(Temporary, entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an account with a temporary password has no access to any project, whatever its memberships
    ///     or its system administrator role.
    /// </summary>
    [Fact]
    public async Task ProjectAccess_TemporaryPassword_GivesNoAccessAtAll()
    {
        // Arrange: the account is a project administrator and a system administrator
        var world = await WorldAsync(isSystemAdmin: true);
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("newbie", "Newbie");

        // Act, Assert
        Assert.False(await world.Access.IsSystemAdminAsync(actor, cancellationToken));
        Assert.False(await world.Access.CanViewAsync(actor, world.Project, cancellationToken));
        Assert.False(await world.Access.CanAdministerAsync(actor, world.Project, cancellationToken));
        Assert.Empty(await world.Access.ListVisibleProjectsAsync(actor, cancellationToken));
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Access.RequireViewAsync(actor, world.Project, cancellationToken));
        Assert.Equal(DomainRule.ProjectNotFound, refused.Rule);
    }

    /// <summary>
    ///     Test that an account with a temporary password cannot create, list or revoke a personal access token, and
    ///     that a token it already has does not authenticate.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenService_TemporaryPassword_RefusesEveryUse()
    {
        // Arrange: the account had a token before it was made to change its password
        var world = await WorldAsync(flagged: false);
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("newbie", "Newbie");
        var token = await world.Tokens.CreateAsync(actor, "ci", 30, cancellationToken);
        await world.SetFlagAsync(true);

        // Act
        var create = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Tokens.CreateAsync(actor, "more", 30, cancellationToken));
        var list = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Tokens.ListAsync(actor, cancellationToken));
        var revoke = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Tokens.RevokeAsync(actor, token.Token.Id, cancellationToken));
        var authenticated = await world.Tokens.AuthenticateAsync(token.Value, cancellationToken);

        // Assert
        Assert.All([create, list, revoke], e => Assert.Equal(DomainRule.PasswordChangeRequired, e.Rule));
        Assert.Equal(TokenFailure.OwnerMustChangePassword, authenticated.Failure);
        Assert.False(authenticated.Succeeded);
    }

    /// <summary>
    ///     Test that choosing a new password clears the flag, and that everything the flag blocked then works.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_NewPassword_ClearsTheFlagAndGivesAccess()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor("newbie", "Newbie");

        // Act
        await world.Users.ChangeOwnPasswordAsync("newbie", Temporary, Chosen, cancellationToken);

        // Assert: the flag is gone, the new password re-authenticates, the project and the tokens are available
        Assert.False((await world.UserStore.GetByUsernameAsync("newbie", cancellationToken))!.MustChangePassword);
        Assert.NotNull(await world.Users.ReauthenticateAsync("newbie", Chosen, cancellationToken));
        Assert.True(await world.Access.CanViewAsync(actor, world.Project, cancellationToken));
        Assert.Single(await world.Access.ListVisibleProjectsAsync(actor, cancellationToken));
        Assert.Empty(await world.Tokens.ListAsync(actor, cancellationToken));
    }

    /// <summary>
    ///     Test that the temporary password cannot be chosen again as the new password, and that nothing changes.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_SameAsTemporary_IsRefusedAndKeepsTheFlag()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: the temporary password is long enough to pass the policy, but it is no replacement
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Users.ChangeOwnPasswordAsync("newbie", Temporary, Temporary, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
        Assert.True((await world.UserStore.GetByUsernameAsync("newbie", cancellationToken))!.MustChangePassword);
    }

    /// <summary>
    ///     Test that an administrator who sets the password of another account makes it temporary, also for an
    ///     account that was not flagged, and that setting the own password does not.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_ByAdministrator_MakesThePasswordTemporaryExceptOwn()
    {
        // Arrange: a flagged account and a normal one
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.Users.CreateAsync("plain", "Plain", false, Temporary, Actor.CommandLine, cancellationToken);

        // Act
        await world.Users.SetPasswordAsync("newbie", Chosen, Actor.CommandLine, cancellationToken);
        await world.Users.SetPasswordAsync("plain", Chosen, Actor.CommandLine, cancellationToken);
        var other = (await world.UserStore.GetByUsernameAsync("plain", cancellationToken))!.MustChangePassword;
        await world.Users.ChangeOwnPasswordAsync("plain", Chosen, "a password of my own", cancellationToken);
        await world.Users.SetPasswordAsync("plain", "my own second password", new Actor("plain", "Plain"), cancellationToken);

        // Assert: the flag stays set for the flagged account and is set for the normal one; an own change leaves it off
        Assert.True((await world.UserStore.GetByUsernameAsync("newbie", cancellationToken))!.MustChangePassword);
        Assert.True(other);
        Assert.False((await world.UserStore.GetByUsernameAsync("plain", cancellationToken))!.MustChangePassword);
    }

    /// <summary>
    ///     Arranges an account that is a project administrator and has a temporary password.
    /// </summary>
    /// <param name="flagged">Whether the account has to change its password.</param>
    /// <param name="isSystemAdmin">Whether the account is also a system administrator.</param>
    /// <returns>The arranged world.</returns>
    private static async Task<World> WorldAsync(bool flagged = true, bool isSystemAdmin = false)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore(store);
        var auditStore = new FakeAuditStore(clock);
        var users = new UserService(userStore, new FakePasswordHasher(), clock, new AuditService(auditStore));
        var user = await users.CreateAsync(
            "newbie", "Newbie", isSystemAdmin, Temporary, Actor.CommandLine, cancellationToken);
        var project = await new ProjectService(
            store, userStore, new AllowAllProjectAccess(store), new FakePasswordHasher(), clock)
            .CreateAsync("Apollo", null, cancellationToken);
        await store.AddMembershipAsync(user.Id, project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        var world = new World(
            users,
            userStore,
            auditStore,
            new ProjectAccess(userStore, store),
            new PersonalAccessTokenService(new FakePersonalAccessTokenStore(userStore), userStore, clock),
            project.Id);
        await world.SetFlagAsync(flagged);
        return world;
    }

    /// <summary>
    ///     The arranged services and project.
    /// </summary>
    /// <param name="Users">The user service.</param>
    /// <param name="UserStore">The user store.</param>
    /// <param name="AuditStore">The store behind the audit service of <paramref name="Users"/>.</param>
    /// <param name="Access">The access service over the real rules.</param>
    /// <param name="Tokens">The personal access token service.</param>
    /// <param name="Project">The project the account administers.</param>
    private sealed record World(
        UserService Users,
        FakeUserStore UserStore,
        FakeAuditStore AuditStore,
        ProjectAccess Access,
        PersonalAccessTokenService Tokens,
        int Project)
    {
        /// <summary>
        ///     Sets or clears the flag of the account.
        /// </summary>
        /// <param name="value">The new value.</param>
        /// <returns>A task that completes when the flag is stored.</returns>
        public async Task SetFlagAsync(bool value)
        {
            var user = (await UserStore.GetByUsernameAsync("newbie", TestContext.Current.CancellationToken))!;
            user.MustChangePassword = value;
            await UserStore.UpdateAsync(user, null, TestContext.Current.CancellationToken);
        }
    }
}
