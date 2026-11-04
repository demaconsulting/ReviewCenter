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
///     Tests of the password checks of <see cref="UserService"/> for accounts with a second factor: the two-step
///     answers, the fail-closed legacy checks and the deferred reset of the failure counter.
/// </summary>
public class UserServiceTotpTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the right password of an enrolled account asks for the second factor.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_EnrolledUser_AsksForTheSecondFactor()
    {
        // Arrange: ann is enrolled
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");

        // Act: the right password
        var ann = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: ann must give a code
        Assert.True(ann.SecondFactorRequired);
        Assert.Equal("ann", ann.User!.Username);
    }

    /// <summary>
    ///     Test that the right password of an enrolled account leaves the count of failures as it is.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_EnrolledUser_KeepsTheCountOfFailures()
    {
        // Arrange: ann is enrolled with two wrong passwords on record
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);

        // Act: the right password
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: the code is asked for and the stored count of failures is still two
        Assert.True(step.SecondFactorRequired);
        Assert.Equal(2, (await world.Users.GetByUsernameAsync("ann", Token))!.FailedLoginCount);
    }

    /// <summary>
    ///     Test that the legacy password-only sign-in refuses an enrolled account (fail closed) and records why.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_EnrolledUser_FailsClosed()
    {
        // Arrange: ann is enrolled
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");

        // Act: the legacy sign-in with the right password
        var signIn = await world.UserService.AuthenticateAsync("ann", TotpTestWorld.Password, Token);

        // Assert: refused, and the audit trail says a second factor is required
        Assert.Null(signIn);
        var entry = Assert.Single(world.AuditStore.Entries, e => e.Action == "auth.sign-in-failed");
        Assert.Contains("second factor required", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the legacy password-only re-authentication refuses an enrolled account (fail closed) and records
    ///     why.
    /// </summary>
    [Fact]
    public async Task UserService_ReauthenticateAsync_EnrolledUser_FailsClosed()
    {
        // Arrange: ann is enrolled
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");

        // Act: the legacy re-authentication with the right password
        var reauth = await world.UserService.ReauthenticateAsync("ann", TotpTestWorld.Password, Token);

        // Assert: refused, and the audit trail says a second factor is required
        Assert.Null(reauth);
        var entry = Assert.Single(world.AuditStore.Entries, e => e.Action == "signature.reauth-failed");
        Assert.Contains("second factor required", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the legacy checks still accept an account without a second factor, and an enrolled account when
    ///     the feature is switched off.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_NoSecondFactorOrFeatureOff_SignsInWithThePassword()
    {
        // Arrange: bob has none; ann is enrolled, and a second user service shares the stores but has the feature off
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.AddUserAsync("bob");
        await world.EnrollAsync("ann");
        var off = new UserService(
            world.Users,
            new FakePasswordHasher(),
            world.Clock,
            world.Audit,
            world.Store,
            new TotpSettings(false, TotpSettings.DefaultIssuer));

        // Act: sign in
        var bob = await world.UserService.AuthenticateAsync("bob", TotpTestWorld.Password, Token);
        var annOff = await off.AuthenticateAsync("ann", TotpTestWorld.Password, Token);
        var annStep = await off.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: both are in, and no second factor is asked for
        Assert.NotNull(bob);
        Assert.NotNull(annOff);
        Assert.False(annStep.SecondFactorRequired);
    }

    /// <summary>
    ///     Test that a user service built without a store of second factors behaves as before.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_WithoutTotpStore_NeverAsksForTheSecondFactor()
    {
        // Arrange: a plain user service
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), new MovableTimeProvider(TotpTestWorld.Start));
        await users.CreateAsync("ann", "Ann", UserRole.Reviewer, TotpTestWorld.Password, Token);

        // Act: check the password
        var step = await users.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: in, with no second factor
        Assert.NotNull(step.User);
        Assert.False(step.SecondFactorRequired);
    }

    /// <summary>
    ///     Test that failures of every kind (wrong password, unknown user) look the same for an enrolled account and a
    ///     plain one.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_WrongPassword_LooksTheSameWithAndWithoutSecondFactor()
    {
        // Arrange: ann is enrolled; bob is not
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.AddUserAsync("bob");
        await world.EnrollAsync("ann");

        // Act: wrong password for both, and an unknown user
        var ann = await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        var bob = await world.UserService.AuthenticatePasswordAsync("bob", "wrong password", Token);
        var nobody = await world.UserService.AuthenticatePasswordAsync("nobody", "wrong password", Token);

        // Assert: identical results
        Assert.Equal(new PasswordStep(null, false), ann);
        Assert.Equal(ann, bob);
        Assert.Equal(ann, nobody);
    }

    /// <summary>
    ///     Test that the password confirmation works for an enrolled account without asking for a code and without
    ///     resetting the counter, and refuses wrong passwords, external accounts and locked accounts.
    /// </summary>
    [Fact]
    public async Task UserService_ConfirmPasswordAsync_ChecksThePasswordOnly()
    {
        // Arrange: ann is enrolled with two failures; an external user exists
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        await world.UserService.SignInExternalAsync("https://idp.example/", "sub-1", "olive", "Olive", Token);

        // Act: confirm with the right and a wrong password, and for the external account
        var right = await world.UserService.ConfirmPasswordAsync("ann", TotpTestWorld.Password, Token);
        var afterRight = (await world.Users.GetByUsernameAsync("ann", Token))!.FailedLoginCount;
        var wrong = await world.UserService.ConfirmPasswordAsync("ann", "wrong password", Token);
        var afterWrong = (await world.Users.GetByUsernameAsync("ann", Token))!.FailedLoginCount;
        var external = await world.UserService.ConfirmPasswordAsync("olive", TotpTestWorld.Password, Token);

        // Assert: the right password is accepted and leaves the counter, a wrong one counts, external is refused
        Assert.NotNull(right);
        Assert.Equal(2, afterRight);
        Assert.Null(wrong);
        Assert.Equal(3, afterWrong);
        Assert.Null(external);
    }

    /// <summary>
    ///     Test that a locked account is refused a password confirmation until the lockout ends.
    /// </summary>
    [Fact]
    public async Task UserService_ConfirmPasswordAsync_LockedAccount_IsRefused()
    {
        // Arrange: five wrong passwords lock ann
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        for (var i = 0; i < 5; i++)
        {
            await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        }

        // Act: confirm with the right password
        var confirmed = await world.UserService.ConfirmPasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: refused while locked, and accepted again after the lockout ends
        Assert.Null(confirmed);
        world.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.NotNull(await world.UserService.ConfirmPasswordAsync("ann", TotpTestWorld.Password, Token));
    }

    /// <summary>
    ///     Test that a right password of a disabled enrolled account is refused like any other disabled account.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_DisabledEnrolledUser_IsRefused()
    {
        // Arrange: ann is enrolled and then disabled
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        await world.UserService.CreateAsync("root", "Root", UserRole.Admin, TotpTestWorld.Password, Token);
        await world.UserService.SetEnabledAsync("ann", false, new Actor("root", "Root"), Token);

        // Act: check the password
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: no user and no request for a code
        Assert.Equal(new PasswordStep(null, false), step);
    }

    /// <summary>
    ///     Test that the password-only change fails closed for an enrolled account: refused, the hash and the failure
    ///     counter stay as they were, and the audit trail says a second factor is required.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_EnrolledUser_FailsClosedAndKeepsHashAndCounter()
    {
        // Arrange: ann is enrolled with three failures on record
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        for (var i = 0; i < 3; i++)
        {
            await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        }

        var before = (await world.Users.GetByUsernameAsync("ann", Token))!.PasswordHash;

        // Act: the legacy change with the right password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.UserService.ChangeOwnPasswordAsync("ann", TotpTestWorld.Password, "another long passphrase", Token));

        // Assert: refused, nothing changed (the counter is neither reset nor raised), and the reason is on record
        var after = (await world.Users.GetByUsernameAsync("ann", Token))!;
        Assert.Equal(DomainRule.TotpCodeIncorrect, exception.Rule);
        Assert.Equal(before, after.PasswordHash);
        Assert.Equal(3, after.FailedLoginCount);
        Assert.Contains(world.AuditStore.Entries, e => e.Action == TotpService.FailedAction && e.Detail.Contains("second factor required", StringComparison.Ordinal));
        Assert.DoesNotContain(world.Users.Audits, a => a.Action == "user.password-changed");
    }

    /// <summary>
    ///     Test that the legacy change counts a wrong current password for an enrolled account like any wrong password.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_EnrolledUser_WrongPasswordCounts()
    {
        // Arrange: ann is enrolled
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");

        // Act: a wrong current password
        var wrong = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.UserService.ChangeOwnPasswordAsync("ann", "wrong password", "another long passphrase", Token));

        // Assert: the wrong password is a plain wrong password and is counted once
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, wrong.Rule);
        Assert.Equal(1, (await world.Users.GetByUsernameAsync("ann", Token))!.FailedLoginCount);
    }

    /// <summary>
    ///     Test that the legacy change still works for an enrolled account while the second factor is switched off.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_EnrolledUserWithTheFeatureOff_ChangesThePassword()
    {
        // Arrange: ann is enrolled; a second user service shares the stores but has the feature off
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");
        var off = new UserService(
            world.Users,
            new FakePasswordHasher(),
            world.Clock,
            world.Audit,
            world.Store,
            new TotpSettings(false, TotpSettings.DefaultIssuer));

        // Act: the right current password with the feature off
        await off.ChangeOwnPasswordAsync("ann", TotpTestWorld.Password, "another long passphrase", Token);

        // Assert: the stored hash is the new one and no failure is on record
        var stored = (await world.Users.GetByUsernameAsync("ann", Token))!;
        Assert.Equal("hash:another long passphrase", stored.PasswordHash);
        Assert.Equal(0, stored.FailedLoginCount);
    }

    /// <summary>
    ///     Test that recording a complete sign-in stores its time and clears the count of failures.
    /// </summary>
    [Fact]
    public async Task UserService_RecordSignInAsync_KnownUser_StoresTheTimeOfTheSignIn()
    {
        // Arrange: ann has two wrong passwords on record and an hour has passed
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        world.Clock.Advance(TimeSpan.FromHours(1));

        // Act: record the sign-in under another spelling of the name
        await world.UserService.RecordSignInAsync("ANN", Token);

        // Assert: the stored time is the clock and the count of failures is cleared
        var stored = (await world.Users.GetByUsernameAsync("ann", Token))!;
        Assert.Equal(TotpTestWorld.Start.AddHours(1), stored.LastSignInUtc);
        Assert.Equal(0, stored.FailedLoginCount);
    }

    /// <summary>
    ///     Test that the right password of an enrolled account, which only asks for the code, stores no sign-in time.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticatePasswordAsync_EnrolledUser_StoresNoSignInTime()
    {
        // Arrange: ann is enrolled
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.EnrollAsync("ann");

        // Act: the right password
        var step = await world.UserService.AuthenticatePasswordAsync("ann", TotpTestWorld.Password, Token);

        // Assert: the code is still required and no sign-in time is stored
        Assert.True(step.SecondFactorRequired);
        Assert.Null((await world.Users.GetByUsernameAsync("ann", Token))!.LastSignInUtc);
    }

    /// <summary>
    ///     Test that wrong codes count with wrong passwords in one counter that locks the account on the fifth failure.
    /// </summary>
    [Fact]
    public async Task UserService_RegisterSecondFactorFailureAsync_WrongCodesAndPasswords_ShareOneCounter()
    {
        // Arrange: three wrong passwords are on record
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        for (var i = 0; i < 3; i++)
        {
            await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        }

        // Act: two wrong codes
        var fourth = await world.UserService.RegisterSecondFactorFailureAsync(
            "ann", "auth.totp-failed", "wrong code", Token);
        var afterFourth = (await world.Users.GetByUsernameAsync("ann", Token))!;
        var countAfterFourth = afterFourth.FailedLoginCount;
        var lockoutAfterFourth = afterFourth.LockoutUntil;
        var fifth = await world.UserService.RegisterSecondFactorFailureAsync(
            "ann", "auth.totp-failed", "wrong code", Token);

        // Assert: the fourth failure counts without a lockout; the fifth locks (the count restarts at zero)
        Assert.False(fourth);
        Assert.Equal(4, countAfterFourth);
        Assert.Null(lockoutAfterFourth);
        Assert.True(fifth);
        var stored = (await world.Users.GetByUsernameAsync("ann", Token))!;
        Assert.Equal(TotpTestWorld.Start.AddMinutes(5), stored.LockoutUntil);
        Assert.Equal(0, stored.FailedLoginCount);
    }

    /// <summary>
    ///     Test that the check whether an account is locked out is true for a locked account and false for a free or
    ///     unknown one.
    /// </summary>
    [Fact]
    public async Task UserService_IsLockedOutAsync_LockedAndFreeAccounts_TellsWhetherLocked()
    {
        // Arrange: five wrong passwords lock ann; bob is free
        var world = new TotpTestWorld();
        await world.AddUserAsync("ann");
        await world.AddUserAsync("bob");
        for (var i = 0; i < 5; i++)
        {
            await world.UserService.AuthenticatePasswordAsync("ann", "wrong password", Token);
        }

        // Act: ask for each
        var ann = await world.UserService.IsLockedOutAsync("ann", Token);
        var bob = await world.UserService.IsLockedOutAsync("bob", Token);
        var nobody = await world.UserService.IsLockedOutAsync("nobody", Token);

        // Assert: only the locked account is reported as locked
        Assert.True(ann);
        Assert.False(bob);
        Assert.False(nobody);
    }
}
