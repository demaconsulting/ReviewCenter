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
///     Unit tests for <see cref="UserService"/> using an in-memory store, a trivial password hasher and a
///     clock the tests can move.
/// </summary>
public class UserServiceTests
{
    /// <summary>
    ///     The instant at which each test's clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates a service over fresh fakes.
    /// </summary>
    /// <returns>The service, its store and its clock.</returns>
    private static (UserService Service, FakeUserStore Store, MovableTimeProvider Clock) Create()
    {
        var store = new FakeUserStore();
        var clock = new MovableTimeProvider(_now);
        return (new UserService(store, new FakePasswordHasher(), clock), store, clock);
    }

    /// <summary>
    ///     Test that valid user names, at the length limits and with every allowed character, are accepted.
    /// </summary>
    /// <param name="username">The user name.</param>
    [Theory]
    [InlineData("abc")]
    [InlineData("A.b_c-9")]
    [InlineData("12345678901234567890123456789012345678901234567890")]
    public async Task UserService_CreateAsync_ValidUsername_Creates(string username)
    {
        // Arrange: a service
        var (service, _, _) = Create();

        // Act: create the user
        var user = await service.CreateAsync(username, "Name", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Assert: created enabled, with the clock time
        Assert.Equal(username, user.Username);
        Assert.True(user.Enabled);
        Assert.Equal(_now, user.Created);
    }

    /// <summary>
    ///     Test that invalid user names are refused and nothing is stored.
    /// </summary>
    /// <param name="username">The invalid user name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("a b")]
    [InlineData("a@b")]
    [InlineData("\u00e9\u00e9\u00e9")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task UserService_CreateAsync_InvalidUsername_ThrowsUserNameInvalid(string? username)
    {
        // Arrange: a service
        var (service, store, _) = Create();

        // Act: create the user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(username, "Name", UserRole.Reviewer, Password, TestContext.Current.CancellationToken));

        // Assert: the rule is named and nothing is stored
        Assert.Equal(DomainRule.UserNameInvalid, exception.Rule);
        Assert.Empty(await store.ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a user name that exists, in any case, is refused.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_DuplicateUsernameOtherCase_ThrowsUserNameTaken()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        await service.CreateAsync("Ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Act: create another user whose name differs only in case
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ANN", "Other", UserRole.Reviewer, Password, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.UserNameTaken, exception.Rule);
    }

    /// <summary>
    ///     Test that the display name is trimmed, must not be empty and has an inclusive length limit.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_DisplayName_IsTrimmedAndLimited()
    {
        // Arrange: a service
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create users with padded, blank, longest and too long display names
        var padded = await service.CreateAsync("padded", "  Ann Lee  ", UserRole.Reviewer, Password, cancellationToken);
        var blank = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("blank", "   ", UserRole.Reviewer, Password, cancellationToken));
        var missing = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("missing", null, UserRole.Reviewer, Password, cancellationToken));
        var longest = await service.CreateAsync(
            "longest", new string('n', ReviewLimits.MaxDisplayNameLength), UserRole.Reviewer, Password, cancellationToken);
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(
                "too.long", new string('n', ReviewLimits.MaxDisplayNameLength + 1), UserRole.Reviewer, Password, cancellationToken));

        // Assert: trimmed, the rule is named for the bad ones, and the longest allowed one is stored
        Assert.Equal("Ann Lee", padded.DisplayName);
        Assert.Equal(DomainRule.DisplayNameInvalid, blank.Rule);
        Assert.Equal(DomainRule.DisplayNameInvalid, missing.Rule);
        Assert.Equal(DomainRule.DisplayNameInvalid, tooLong.Rule);
        Assert.Equal(ReviewLimits.MaxDisplayNameLength, longest.DisplayName.Length);
    }

    /// <summary>
    ///     Test the password policy: 12 to 128 characters, no other rule, and not trimmed.
    /// </summary>
    /// <param name="length">The password length.</param>
    /// <param name="valid">Whether the password is accepted.</param>
    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task UserService_CreateAsync_PasswordLength_FollowsPolicy(int length, bool valid)
    {
        // Arrange: a service and a password of the given length
        var (service, _, _) = Create();
        var password = new string('p', length);

        // Act: create the user
        var task = service.CreateAsync("ann", "Ann", UserRole.Reviewer, password, TestContext.Current.CancellationToken);

        // Assert: accepted or refused for the password
        if (valid)
        {
            Assert.Equal("ann", (await task).Username);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<DomainRuleException>(() => task);
            Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
        }
    }

    /// <summary>
    ///     Test that a missing password and spaces-only padding are handled by the length rule alone.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_MissingPasswordOrWhiteSpacePadding_FollowsLengthRule()
    {
        // Arrange: a service
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create with no password, and with a 12-character password made of spaces
        var missing = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", UserRole.Reviewer, null, cancellationToken));
        var spaces = await service.CreateAsync("bob", "Bob", UserRole.Reviewer, new string(' ', 12), cancellationToken);

        // Assert: the missing one is refused, the spaces are taken as they are
        Assert.Equal(DomainRule.PasswordInvalid, missing.Rule);
        Assert.Equal("bob", spaces.Username);
        Assert.NotNull(await service.AuthenticateAsync("bob", new string(' ', 12), cancellationToken));
    }

    /// <summary>
    ///     Test that the store receives only a hash, never the password.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_StoresHashNotPassword()
    {
        // Arrange: a service
        var (service, store, _) = Create();

        // Act: create a user
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Assert: the stored value is the hasher's output
        var stored = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!;
        Assert.Equal(new FakePasswordHasher().Hash(Password), stored.PasswordHash);
        Assert.NotEqual(Password, stored.PasswordHash);
    }

    /// <summary>
    ///     Test that the right password signs in, in any case of the user name, and a wrong password, an unknown
    ///     user and a missing password do not.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_RightWrongAndUnknown_OnlyRightSucceeds()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("Ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: try the right password, a wrong one, an unknown user and no password
        var right = await service.AuthenticateAsync("ann", Password, cancellationToken);
        var wrong = await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        var unknown = await service.AuthenticateAsync("nobody", Password, cancellationToken);
        var none = await service.AuthenticateAsync("ann", null, cancellationToken);

        // Assert: only the right password gives the user
        Assert.Equal("Ann", right?.Username);
        Assert.Null(wrong);
        Assert.Null(unknown);
        Assert.Null(none);
    }

    /// <summary>
    ///     Test that a disabled user cannot sign in, even with the right password, until enabled again.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_DisabledUser_ReturnsNullUntilEnabled()
    {
        // Arrange: a service with an administrator and a disabled reviewer
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        await service.SetEnabledAsync("ann", false, cancellationToken);

        // Act: sign in while disabled, then after enabling
        var disabled = await service.AuthenticateAsync("ann", Password, cancellationToken);
        await service.SetEnabledAsync("ann", true, cancellationToken);
        var enabled = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: refused while disabled
        Assert.Null(disabled);
        Assert.NotNull(enabled);
    }

    /// <summary>
    ///     Test that the fifth consecutive failure locks the account for five minutes, that a locked account
    ///     refuses even the right password, and that it works again afterwards.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_FiveFailures_LockForFiveMinutes()
    {
        // Arrange: a service with a user
        var (service, _, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: fail four times (still not locked), once more, and try the right password while locked
        for (var i = 0; i < 4; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var beforeLock = await service.AuthenticateAsync("ann", Password, cancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var locked = await service.AuthenticateAsync("ann", Password, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        var almost = await service.AuthenticateAsync("ann", Password, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        var after = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: four failures do not lock (and the right password resets them), five do, the lock lasts
        // exactly five minutes
        Assert.NotNull(beforeLock);
        Assert.Null(locked);
        Assert.Null(almost);
        Assert.NotNull(after);
    }

    /// <summary>
    ///     Test that a successful sign-in resets the failure counter.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_SuccessBetweenFailures_ResetsCounter()
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: fail three times, succeed, fail three more times, then sign in
        for (var i = 0; i < 3; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        await service.AuthenticateAsync("ann", Password, cancellationToken);
        var afterSuccess = (await store.GetByUsernameAsync("ann", cancellationToken))!.FailedLoginCount;
        for (var i = 0; i < 3; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var stillOpen = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: the counter was reset, so six failures in total did not lock
        Assert.Equal(0, afterSuccess);
        Assert.NotNull(stillOpen);
    }

    /// <summary>
    ///     Test that the last enabled administrator cannot be disabled or demoted, while another one can.
    /// </summary>
    [Fact]
    public async Task UserService_SetEnabledAsync_SetRoleAsync_LastAdmin_IsProtected()
    {
        // Arrange: a service with one administrator
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);

        // Act: disable and demote the only administrator, add a second one, then do both to the first
        var disable = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetEnabledAsync("root", false, cancellationToken));
        var demote = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetRoleAsync("root", UserRole.Reviewer, cancellationToken));
        await service.CreateAsync("second", "Second", UserRole.Admin, Password, cancellationToken);
        await service.SetRoleAsync("root", UserRole.Reviewer, cancellationToken);
        var lastAgain = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetEnabledAsync("second", false, cancellationToken));

        // Assert: the first two were refused, the demotion with a second administrator worked, and the second
        // one is now protected
        Assert.Equal(DomainRule.LastAdmin, disable.Rule);
        Assert.Equal(DomainRule.LastAdmin, demote.Rule);
        Assert.Equal(DomainRule.LastAdmin, lastAgain.Rule);
        Assert.False((await service.GetAsync("root", cancellationToken))!.IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a disabled administrator does not count as a remaining administrator.
    /// </summary>
    [Fact]
    public async Task UserService_SetEnabledAsync_OtherAdminDisabled_StillProtectsLastEnabledAdmin()
    {
        // Arrange: two administrators, one of them disabled
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        await service.CreateAsync("second", "Second", UserRole.Admin, Password, cancellationToken);
        await service.SetEnabledAsync("second", false, cancellationToken);

        // Act: disable the remaining enabled administrator
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetEnabledAsync("root", false, cancellationToken));

        // Assert: refused
        Assert.Equal(DomainRule.LastAdmin, exception.Rule);
    }

    /// <summary>
    ///     Test that changing the own password needs the current one and applies the policy.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_NeedsCurrentPasswordAndPolicy()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: change with a wrong current password, with a short new password, and correctly
        var wrong = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("ann", "wrong password!", "another long password", cancellationToken));
        var tooShort = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("ann", Password, "short", cancellationToken));
        await service.ChangeOwnPasswordAsync("ann", Password, "another long password", cancellationToken);

        // Assert: only the correct change took effect
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, wrong.Rule);
        Assert.Equal(DomainRule.PasswordInvalid, tooShort.Rule);
        Assert.Null(await service.AuthenticateAsync("ann", Password, cancellationToken));
        Assert.NotNull(await service.AuthenticateAsync("ann", "another long password", cancellationToken));
    }

    /// <summary>
    ///     Test that wrong current passwords count towards the lockout, which then refuses the change.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_RepeatedWrongPassword_LocksOut()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: guess the current password five times, then give the right one
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(
                () => service.ChangeOwnPasswordAsync("ann", "wrong password!", "another long password", cancellationToken));
        }

        var locked = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("ann", Password, "another long password", cancellationToken));

        // Assert: the right password is refused while locked, and the old password is still the password
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, locked.Rule);
        Assert.Null(await service.AuthenticateAsync("ann", Password, cancellationToken));
    }

    /// <summary>
    ///     Test that an administrator can set a password without the current one, and that it ends a lockout.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_NewPassword_WorksAndEndsLockout()
    {
        // Arrange: a locked-out user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        // Act: set a short password, then a valid one
        var tooShort = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetPasswordAsync("ann", "short", cancellationToken));
        await service.SetPasswordAsync("ann", "another long password", cancellationToken);

        // Assert: the policy applies, and the user signs in with the new password at once
        Assert.Equal(DomainRule.PasswordInvalid, tooShort.Rule);
        Assert.NotNull(await service.AuthenticateAsync("ann", "another long password", cancellationToken));
    }

    /// <summary>
    ///     Test that every change of an unknown user is refused as not found.
    /// </summary>
    [Fact]
    public async Task UserService_Changes_UnknownUser_ThrowUserNotFound()
    {
        // Arrange: a service without users
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: change an unknown user in each way
        var enabled = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetEnabledAsync("nobody", false, cancellationToken));
        var role = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetRoleAsync("nobody", UserRole.Admin, cancellationToken));
        var own = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("nobody", Password, Password, cancellationToken));
        var set = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetPasswordAsync("nobody", Password, cancellationToken));

        // Assert: all name the missing user
        Assert.All([enabled, role, own, set], exception => Assert.Equal(DomainRule.UserNotFound, exception.Rule));
    }

    /// <summary>
    ///     Test that a promotion is stored and that the last enabled system administrator cannot be demoted,
    ///     disabled or locked out of the role.
    /// </summary>
    [Fact]
    public async Task UserService_SetSystemAdminAsync_PromoteAndDemote_LastAdminIsKept()
    {
        // Arrange: a service with one system administrator and a plain user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);

        // Act: the only administrator cannot be demoted or disabled
        var demote = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSystemAdminAsync("root", false, Actor.CommandLine, cancellationToken));
        var disable = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetEnabledAsync("root", false, Actor.CommandLine, cancellationToken));

        // Act: with a second administrator the first can be demoted
        await service.SetSystemAdminAsync("ann", true, Actor.CommandLine, cancellationToken);
        await service.SetSystemAdminAsync("root", false, Actor.CommandLine, cancellationToken);

        // Assert
        Assert.Equal(DomainRule.LastAdmin, demote.Rule);
        Assert.Equal(DomainRule.LastAdmin, disable.Rule);
        Assert.True((await service.GetAsync("ann", cancellationToken))!.IsSystemAdmin);
        Assert.False((await service.GetAsync("root", cancellationToken))!.IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a successful password sign-in stores the time, that a failed one and a re-authentication do
    ///     not, and that a user who never signed in has none.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_SuccessOnly_RecordsLastSignIn()
    {
        // Arrange: a user who never signed in, and a clock that moves
        var (service, store, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        var created = (await store.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;

        // Act: a wrong password, a re-authentication, then a sign-in five minutes later
        await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        var afterFailure = (await store.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;
        await service.ReauthenticatePasswordAsync("ann", Password, cancellationToken);
        var afterReauth = (await store.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;
        clock.Advance(TimeSpan.FromMinutes(5));
        var auditsBefore = store.Audits.Count;
        await service.AuthenticateAsync("ann", Password, cancellationToken);
        var afterSuccess = (await store.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;

        // Assert: only the sign-in stored the time of the sign-in, and storing it added no audit entry
        Assert.Null(created);
        Assert.Null(afterFailure);
        Assert.Null(afterReauth);
        Assert.Equal(_now + TimeSpan.FromMinutes(5), afterSuccess);
        Assert.Equal(auditsBefore, store.Audits.Count);
    }

    /// <summary>
    ///     Test that an OIDC sign-in stores the time, and that a refused one of a disabled account does not.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_AcceptedOnly_RecordsLastSignIn()
    {
        // Arrange: an account that is created by its first sign-in
        var (service, store, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: the first sign-in, a later one, and one after the account was disabled
        var first = (await service.SignInExternalAsync("https://idp", "sub-1", "ann", "Ann", cancellationToken)).User!;
        clock.Advance(TimeSpan.FromHours(1));
        await service.SignInExternalAsync("https://idp", "sub-1", "ann", "Ann", cancellationToken);
        var afterLater = (await store.GetByUsernameAsync(first.Username, cancellationToken))!.LastSignInUtc;
        await service.SetEnabledAsync(first.Username, false, Actor.CommandLine, cancellationToken);
        clock.Advance(TimeSpan.FromHours(1));
        var refused = await service.SignInExternalAsync("https://idp", "sub-1", "ann", "Ann", cancellationToken);
        var afterRefused = (await store.GetByUsernameAsync(first.Username, cancellationToken))!.LastSignInUtc;

        // Assert
        Assert.Equal(_now + TimeSpan.FromHours(1), afterLater);
        Assert.Null(refused.User);
        Assert.Equal(afterLater, afterRefused);
    }

    /// <summary>
    ///     Test that a lockout counts as locked exactly until its end time.
    /// </summary>
    [Fact]
    public void UserService_IsLocked_Lockout_IsLockedUntilItsEnd()
    {
        // Arrange: users with no lockout, a running one and an ended one
        var none = new User();
        var running = new User { LockoutUntil = _now + TimeSpan.FromMinutes(1) };
        var ended = new User { LockoutUntil = _now };

        // Act and assert: only the running lockout counts; the end instant itself is no longer locked
        Assert.False(UserService.IsLocked(none, _now));
        Assert.True(UserService.IsLocked(running, _now));
        Assert.False(UserService.IsLocked(ended, _now));
        Assert.True(UserService.IsLocked(ended, _now - TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    ///     Test that a system administrator can unlock a user, that the counter and the lockout are cleared, that
    ///     the change is audited and that the user can sign in at once.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_SystemAdmin_ClearsLockoutAndAudits()
    {
        // Arrange: a locked-out user and an administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var locked = (await store.GetByUsernameAsync("ann", cancellationToken))!;

        // Act
        await service.UnlockAsync(new Actor(root.Username, root.DisplayName), ann.Id, cancellationToken);

        // Assert: cleared, audited with the actor, and the right password works again
        var unlocked = (await store.GetByUsernameAsync("ann", cancellationToken))!;
        Assert.NotNull(locked.LockoutUntil);
        Assert.Null(unlocked.LockoutUntil);
        Assert.Equal(0, unlocked.FailedLoginCount);
        var audit = Assert.Single(store.Audits, entry => entry.Action == "user.unlocked");
        Assert.Equal("root", audit.ActorUsername);
        Assert.Equal("user 'ann'", audit.Detail);
        Assert.NotNull(await service.AuthenticateAsync("ann", Password, cancellationToken));
    }

    /// <summary>
    ///     Test that only an enabled system administrator who is not signed in through an API token and has no
    ///     temporary password can unlock, and that nothing is audited or changed otherwise.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_NotAllowedOrUnknown_IsRefused()
    {
        // Arrange: a locked user, an ordinary user, an administrator with a temporary password
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        await service.CreateAsync("tmp", "Tmp", true, Password, Actor.CommandLine, cancellationToken);
        await service.SetPasswordAsync("tmp", Password, Actor.CommandLine, cancellationToken);
        for (var i = 0; i < 5; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        // Act: a non-admin, an unknown actor, the command line, an API token and a temporary password
        var refused = new List<DomainRuleException>
        {
            await Assert.ThrowsAsync<DomainRuleException>(() => service.UnlockAsync(new Actor("ann", "Ann"), ann.Id, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => service.UnlockAsync(new Actor("nobody", "Nobody"), ann.Id, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => service.UnlockAsync(Actor.CommandLine, ann.Id, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => service.UnlockAsync(new Actor("root", "Root") { ApiTokenId = "t1" }, ann.Id, cancellationToken)),
            await Assert.ThrowsAsync<DomainRuleException>(() => service.UnlockAsync(new Actor("tmp", "Tmp"), ann.Id, cancellationToken))
        };
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.UnlockAsync(new Actor("root", "Root"), 9999, cancellationToken));

        // Assert: every refusal names the right rule, and the user is still locked and nothing was audited
        Assert.All(refused, exception => Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule));
        Assert.Equal(DomainRule.UserNotFound, unknown.Rule);
        Assert.NotNull((await store.GetByUsernameAsync("ann", cancellationToken))!.LockoutUntil);
        Assert.DoesNotContain(store.Audits, entry => entry.Action == "user.unlocked");
    }

    /// <summary>
    ///     Test that a password set by somebody else is temporary, that setting the own password is not, and that a
    ///     later change of the own password ends the temporary state.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_OtherUserOrOwn_OnlyOtherForcesAChange()
    {
        // Arrange: two users
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        var created = (await store.GetByUsernameAsync("ann", cancellationToken))!.MustChangePassword;

        // Act: the administrator sets the password of ann, and a command-line actor does the same for root
        await service.SetPasswordAsync("ann", "another long password", new Actor("root", "Root"), cancellationToken);
        var byAdmin = (await store.GetByUsernameAsync("ann", cancellationToken))!.MustChangePassword;
        await service.SetPasswordAsync("root", "yet another password", Actor.CommandLine, cancellationToken);
        var byCommandLine = (await store.GetByUsernameAsync("root", cancellationToken))!.MustChangePassword;
        await service.ChangeOwnPasswordAsync("ann", "another long password", "my own new password", cancellationToken);
        var afterOwnChange = (await store.GetByUsernameAsync("ann", cancellationToken))!.MustChangePassword;

        // Assert
        Assert.False(created);
        Assert.True(byAdmin);
        Assert.True(byCommandLine);
        Assert.False(afterOwnChange);
    }

    /// <summary>
    ///     Test that an administrator who sets their own password does not get a forced change.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_OwnRow_DoesNotForceAChange()
    {
        // Arrange: an administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);

        // Act: the administrator sets their own password, with the user name written in another case
        await service.SetPasswordAsync("root", "own chosen password", new Actor("ROOT", "Root"), cancellationToken);

        // Assert
        Assert.False((await store.GetByUsernameAsync("root", cancellationToken))!.MustChangePassword);
    }

    /// <summary>
    ///     Test that an API-token actor with the name of the user is not "the own row", and that an external user
    ///     cannot get a password.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_ApiTokenActorOrExternalUser_ForcesAChangeOrIsRefused()
    {
        // Arrange: a local user and an external one
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        var external = (await service.SignInExternalAsync("https://idp", "sub-1", "oidc", "Oidc", cancellationToken)).User!;

        // Act
        await service.SetPasswordAsync("ann", "another long password", new Actor("ann", "Ann") { ApiTokenId = "t1" }, cancellationToken);
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetPasswordAsync(external.Username, "another long password", Actor.CommandLine, cancellationToken));

        // Assert
        Assert.True((await store.GetByUsernameAsync("ann", cancellationToken))!.MustChangePassword);
        Assert.Equal(DomainRule.PasswordInvalid, refused.Rule);
        Assert.Equal(string.Empty, (await store.GetByUsernameAsync(external.Username, cancellationToken))!.PasswordHash);
    }

    /// <summary>
    ///     Test that the constructor refuses missing dependencies.
    /// </summary>
    [Fact]
    public void UserService_Constructor_NullArguments_Throws()
    {
        // Arrange, Act, Assert: each missing dependency is refused
        Assert.Throws<ArgumentNullException>(() => new UserService(null!, new FakePasswordHasher(), TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new UserService(new FakeUserStore(), null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new UserService(new FakeUserStore(), new FakePasswordHasher(), null!));
    }
}
