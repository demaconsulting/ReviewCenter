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
    ///     Reads a user back from the store, as the service stored it.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The stored user.</returns>
    private static async Task<User> StoredAsync(FakeUserStore store, string username)
    {
        return (await store.GetByUsernameAsync(username, TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    ///     Fails a sign-in five times in a row, which locks the account.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="username">The user name.</param>
    private static async Task LockAsync(UserService service, string username)
    {
        for (var i = 0; i < 5; i++)
        {
            await service.AuthenticateAsync(username, "wrong password!", TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Test that user names made of the allowed characters are accepted and the user is created enabled.
    /// </summary>
    /// <param name="username">The user name.</param>
    [Theory]
    [InlineData("A.b_c-9")]
    [InlineData("Ann.Lee1")]
    [InlineData("ANN_lee-2")]
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
    ///     Test that a user name with a character other than letters, digits, full stops, underscores and hyphens
    ///     is refused with the user-name-invalid rule.
    /// </summary>
    /// <param name="username">The invalid user name.</param>
    [Theory]
    [InlineData("a b")]
    [InlineData("a@b")]
    [InlineData("\u00e9\u00e9\u00e9")]
    public async Task UserService_CreateAsync_InvalidUsername_ThrowsUserNameInvalid(string username)
    {
        // Arrange: a service
        var (service, _, _) = Create();

        // Act: create the user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(username, "Name", UserRole.Reviewer, Password, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.UserNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a user name of exactly 3 characters is accepted and stored.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_UsernameOfThreeCharacters_Creates()
    {
        // Arrange: a service
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create a user with a 3-character name
        var user = await service.CreateAsync("a.b", "Name", false, Password, cancellationToken);

        // Assert: the user is returned and exactly one user is stored under that name
        Assert.Equal("a.b", user.Username);
        var stored = Assert.Single(await store.ListAsync(cancellationToken));
        Assert.Equal("a.b", stored.Username);
    }

    /// <summary>
    ///     Test that a user name of exactly 50 characters is accepted and stored.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_UsernameOfFiftyCharacters_Creates()
    {
        // Arrange: a service and a 50-character name
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var username = new string('a', 50);

        // Act: create the user
        var user = await service.CreateAsync(username, "Name", false, Password, cancellationToken);

        // Assert: the user is returned and exactly one user is stored under that name
        Assert.Equal(username, user.Username);
        var stored = Assert.Single(await store.ListAsync(cancellationToken));
        Assert.Equal(username, stored.Username);
    }

    /// <summary>
    ///     Test that a missing, empty or too short user name is refused with the user-name-invalid rule.
    /// </summary>
    /// <param name="username">The user name that is shorter than 3 characters.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    public async Task UserService_CreateAsync_UsernameShorterThanThree_ThrowsUserNameInvalid(string? username)
    {
        // Arrange: a service
        var (service, _, _) = Create();

        // Act: create the user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(username, "Name", false, Password, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.UserNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a user name of 51 characters is refused with the user-name-invalid rule.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_UsernameLongerThanFifty_ThrowsUserNameInvalid()
    {
        // Arrange: a service
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create a user with a 51-character name
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(new string('a', 51), "Name", false, Password, cancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.UserNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that refusing an invalid user name and a taken one stores nothing and leaves the first user as it was.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_RejectedUsername_StoresNothing()
    {
        // Arrange: a service with one user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        var before = await StoredAsync(store, "ann");
        var auditsBefore = store.Audits.Count;

        // Act: create users with an invalid name and with a name taken in another case
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("a b", "Other", false, Password, cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ANN", "Other", false, "another long password", cancellationToken));

        // Assert: only the first user is stored, unchanged, and nothing was audited
        var stored = Assert.Single(await store.ListAsync(cancellationToken));
        Assert.Equal("ann", stored.Username);
        Assert.Equal(before.DisplayName, stored.DisplayName);
        Assert.Equal(before.PasswordHash, stored.PasswordHash);
        Assert.Equal(auditsBefore, store.Audits.Count);
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
    ///     Test that a display name with leading and trailing white space is stored trimmed.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_DisplayNameWithPadding_StoresItTrimmed()
    {
        // Arrange: a service
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create a user with a padded display name
        await service.CreateAsync("padded", "  Ann Lee  ", UserRole.Reviewer, Password, cancellationToken);

        // Assert: the stored display name is trimmed
        Assert.Equal("Ann Lee", (await StoredAsync(store, "padded")).DisplayName);
    }

    /// <summary>
    ///     Test that a display name of one character after trimming is accepted and stored trimmed.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_DisplayNameOfOneCharacter_Creates()
    {
        // Arrange: a service
        var (service, store, _) = Create();

        // Act: create a user whose display name is one character between spaces
        await service.CreateAsync("ann", " x ", false, Password, TestContext.Current.CancellationToken);

        // Assert: the stored display name is the one character
        Assert.Equal("x", (await StoredAsync(store, "ann")).DisplayName);
    }

    /// <summary>
    ///     Test that a display name of 100 characters after trimming is accepted and stored trimmed.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_DisplayNameOfHundredCharacters_Creates()
    {
        // Arrange: a service and a 100-character display name between spaces
        var (service, store, _) = Create();
        var name = new string('n', 100);

        // Act: create the user
        await service.CreateAsync("ann", " " + name + " ", false, Password, TestContext.Current.CancellationToken);

        // Assert: all 100 characters are stored
        Assert.Equal(name, (await StoredAsync(store, "ann")).DisplayName);
    }

    /// <summary>
    ///     Test that a missing, empty or blank display name is refused with the display-name-invalid rule.
    /// </summary>
    /// <param name="displayName">The display name that is empty after trimming.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UserService_CreateAsync_DisplayNameBlank_ThrowsDisplayNameInvalid(string? displayName)
    {
        // Arrange: a service
        var (service, _, _) = Create();

        // Act: create the user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", displayName, false, Password, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.DisplayNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a display name of 101 characters after trimming is refused with the display-name-invalid rule.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_DisplayNameLongerThanHundred_ThrowsDisplayNameInvalid()
    {
        // Arrange: a service and a 101-character display name between spaces
        var (service, _, _) = Create();
        var name = " " + new string('n', 101) + " ";

        // Act: create the user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", name, false, Password, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.DisplayNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that refusing a blank display name and a too long one stores and audits nothing.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_RejectedDisplayName_StoresNothing()
    {
        // Arrange: a service
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create users with a blank and with a 101-character display name
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("blank", "   ", false, Password, cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("long", new string('n', 101), false, Password, cancellationToken));

        // Assert: no user is stored and nothing was audited
        Assert.Empty(await store.ListAsync(cancellationToken));
        Assert.Empty(store.Audits);
    }

    /// <summary>
    ///     Test that a password of exactly 12 characters is accepted.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordOfTwelveCharacters_Creates()
    {
        // Arrange: a service and a password of 12 characters
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var password = new string('p', 12);

        // Act: create the user
        var user = await service.CreateAsync("ann", "Ann", UserRole.Reviewer, password, cancellationToken);

        // Assert: the user is created
        Assert.Equal("ann", user.Username);
    }

    /// <summary>
    ///     Test that a password inside the policy range is accepted and signs the new user in.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordInPolicyRange_CreatesAndAuthenticates()
    {
        // Arrange: a service
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create the user with a password of 21 characters, then sign in with it
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        var signedIn = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: the sign-in returns the user
        Assert.Equal("ann", signedIn?.Username);
    }

    /// <summary>
    ///     Test that a password of exactly 128 characters is accepted and only its hash is stored.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordOfHundredTwentyEightCharacters_Creates()
    {
        // Arrange: a service and a password of 128 characters
        var (service, store, _) = Create();
        var password = new string('p', 128);

        // Act: create the user
        await service.CreateAsync("ann", "Ann", false, password, TestContext.Current.CancellationToken);

        // Assert: the user is stored with the hash of the whole password
        Assert.Equal("hash:" + password, (await StoredAsync(store, "ann")).PasswordHash);
    }

    /// <summary>
    ///     Test that a password of 11 characters is refused with the password-invalid rule.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordOfElevenCharacters_ThrowsPasswordInvalid()
    {
        // Arrange: a service
        var (service, _, _) = Create();

        // Act: create a user with a password of 11 characters
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", false, new string('p', 11), TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a password of 129 characters is refused with the password-invalid rule.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordOfHundredTwentyNineCharacters_ThrowsPasswordInvalid()
    {
        // Arrange: a service
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create a user with a password of 129 characters
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", false, new string('p', 129), cancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a password of one repeated letter is accepted, because only its length is checked.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordOfOneRepeatedLetter_Creates()
    {
        // Arrange: a service and a password with no digit, upper-case letter or symbol
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var password = new string('a', 20);

        // Act: create the user and sign in with the password
        await service.CreateAsync("ann", "Ann", false, password, cancellationToken);
        var signedIn = await service.AuthenticateAsync("ann", password, cancellationToken);

        // Assert: the sign-in returns the user
        Assert.Equal("ann", signedIn?.Username);
    }

    /// <summary>
    ///     Test that a missing password is refused with the password-invalid rule.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordMissing_ThrowsPasswordInvalid()
    {
        // Arrange: a service
        var (service, _, _) = Create();

        // Act: create a user without a password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", false, null, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a refused password stores and audits nothing.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_RejectedPassword_StoresNothing()
    {
        // Arrange: a service
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create users with no password, with 11 characters and with 129 characters
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", false, null, cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", false, new string('p', 11), cancellationToken));
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("ann", "Ann", false, new string('p', 129), cancellationToken));

        // Assert: no user is stored and nothing was audited
        Assert.Empty(await store.ListAsync(cancellationToken));
        Assert.Empty(store.Audits);
    }

    /// <summary>
    ///     Test that a password of 12 spaces is kept as it is, not trimmed.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_PasswordOfTwelveSpaces_IsKeptUntrimmed()
    {
        // Arrange: a service
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var spaces = new string(' ', 12);

        // Act: create a user with a 12-character password made of spaces
        await service.CreateAsync("bob", "Bob", UserRole.Reviewer, spaces, cancellationToken);

        // Assert: the spaces are hashed as they are and sign the user in
        Assert.Equal("hash:" + spaces, (await StoredAsync(store, "bob")).PasswordHash);
        Assert.NotNull(await service.AuthenticateAsync("bob", spaces, cancellationToken));
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
    ///     Test that a created user without the administrator request is stored enabled.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_NewUser_IsEnabled()
    {
        // Arrange: a service
        var (service, store, _) = Create();

        // Act: create a user
        await service.CreateAsync("ann", "Ann", false, Password, TestContext.Current.CancellationToken);

        // Assert: the stored user is enabled
        Assert.True((await StoredAsync(store, "ann")).Enabled);
    }

    /// <summary>
    ///     Test that a user created with the administrator request is stored as a system administrator.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_AdministratorRequested_CreatesSystemAdministrator()
    {
        // Arrange: a service
        var (service, store, _) = Create();

        // Act: create a user and ask for the administrator right
        await service.CreateAsync("root", "Root", true, Password, TestContext.Current.CancellationToken);

        // Assert: the stored user is a system administrator
        Assert.True((await StoredAsync(store, "root")).IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a user created without the administrator request is stored as a plain user.
    /// </summary>
    [Fact]
    public async Task UserService_CreateAsync_AdministratorNotRequested_CreatesPlainUser()
    {
        // Arrange: a service
        var (service, store, _) = Create();

        // Act: create a user and do not ask for the administrator right
        await service.CreateAsync("ann", "Ann", false, Password, TestContext.Current.CancellationToken);

        // Assert: the stored user is not a system administrator
        Assert.False((await StoredAsync(store, "ann")).IsSystemAdmin);
    }

    /// <summary>
    ///     Test that the right password signs the user in and returns that user.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_RightPassword_ReturnsTheUser()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: sign in with the right password
        var user = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: the user is returned
        Assert.NotNull(user);
        Assert.Equal(created.Id, user.Id);
        Assert.Equal("ann", user.Username);
    }

    /// <summary>
    ///     Test that the user is found by a user name written in another letter case.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_UserNameInOtherCase_ReturnsTheUser()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: sign in with the user name in upper case
        var user = await service.AuthenticateAsync("ANN", Password, cancellationToken);

        // Assert: the user is returned
        Assert.NotNull(user);
        Assert.Equal(created.Id, user.Id);
    }

    /// <summary>
    ///     Test that a wrong, empty or missing password returns nothing.
    /// </summary>
    /// <param name="password">The password to try.</param>
    [Theory]
    [InlineData("wrong password!")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UserService_AuthenticateAsync_WrongPassword_ReturnsNull(string? password)
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("Ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: try the password
        var user = await service.AuthenticateAsync("ann", password, cancellationToken);

        // Assert: nothing is returned
        Assert.Null(user);
    }

    /// <summary>
    ///     Test that an unknown user name returns nothing.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_UnknownUser_ReturnsNull()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: sign in with an unknown user name and a password that is right for another user
        var user = await service.AuthenticateAsync("nobody", Password, cancellationToken);

        // Assert: nothing is returned
        Assert.Null(user);
    }

    /// <summary>
    ///     Test that a wrong password of an unknown user costs one hash verification, as it does for a known user.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_UnknownUser_VerifiesAHashLikeAKnownUser()
    {
        // Arrange: a service over a hasher that counts verifications, with a user
        var hasher = new CountingHasher();
        var service = new UserService(new FakeUserStore(), hasher, new MovableTimeProvider(_now));
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: count the verifications of a wrong password for the known user and for an unknown one
        hasher.VerifyCalls = 0;
        await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        var known = hasher.VerifyCalls;
        hasher.VerifyCalls = 0;
        await service.AuthenticateAsync("nobody", "wrong password!", cancellationToken);
        var unknown = hasher.VerifyCalls;

        // Assert: one verification each
        Assert.Equal(1, known);
        Assert.Equal(1, unknown);
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
    ///     Test that four failures in a row do not lock the account and that the fifth one does.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_FifthFailureInARow_LocksTheUser()
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: fail four times and sign in, then fail five times and try the right password
        for (var i = 0; i < 4; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var lockAfterFour = (await StoredAsync(store, "ann")).LockoutUntil;
        var beforeLock = await service.AuthenticateAsync("ann", Password, cancellationToken);
        await LockAsync(service, "ann");
        var lockAfterFive = (await StoredAsync(store, "ann")).LockoutUntil;
        var locked = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: four failures do not lock (and the right password signs in), the fifth one does
        Assert.Null(lockAfterFour);
        Assert.NotNull(beforeLock);
        Assert.NotNull(lockAfterFive);
        Assert.Null(locked);
    }

    /// <summary>
    ///     Test that a lockout ends exactly five minutes after it began, not one second earlier.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_Lockout_LastsExactlyFiveMinutes()
    {
        // Arrange: a user locked out at the start instant
        var (service, store, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        await LockAsync(service, "ann");
        var lockoutUntil = (await StoredAsync(store, "ann")).LockoutUntil;

        // Act: try the right password one second before the end of the lockout and at its end
        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        var almost = await service.AuthenticateAsync("ann", Password, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        var after = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: the lockout was set to five minutes, refused before and open at its end
        Assert.Equal(_now + TimeSpan.FromMinutes(5), lockoutUntil);
        Assert.Null(almost);
        Assert.NotNull(after);
    }

    /// <summary>
    ///     Test that the right password is refused while the lockout lasts.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_LockedUser_RightPasswordIsRefused()
    {
        // Arrange: a locked-out user
        var (service, _, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        await LockAsync(service, "ann");
        clock.Advance(TimeSpan.FromMinutes(1));

        // Act: try the right password twice
        var first = await service.AuthenticateAsync("ann", Password, cancellationToken);
        var second = await service.AuthenticateAsync("ann", Password, cancellationToken);

        // Assert: both are refused
        Assert.Null(first);
        Assert.Null(second);
    }

    /// <summary>
    ///     Test that attempts while the lockout lasts leave the failure count and the end of the lockout as they were.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_LockedUser_AttemptsAreNotCounted()
    {
        // Arrange: a user locked out at the start instant; the fifth failure restarts the count
        var (service, store, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        await LockAsync(service, "ann");
        var locked = await StoredAsync(store, "ann");
        clock.Advance(TimeSpan.FromMinutes(1));

        // Act: three attempts while locked: a wrong password, the right one, and the right one in another case
        var wrong = await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        var right = await service.AuthenticateAsync("ann", Password, cancellationToken);
        var otherCase = await service.AuthenticateAsync("ANN", Password, cancellationToken);

        // Assert: all refused, and the stored count and end of the lockout are as right after the lockout
        Assert.Equal(0, locked.FailedLoginCount);
        Assert.Equal(_now + TimeSpan.FromMinutes(5), locked.LockoutUntil);
        Assert.Null(wrong);
        Assert.Null(right);
        Assert.Null(otherCase);
        var after = await StoredAsync(store, "ann");
        Assert.Equal(locked.FailedLoginCount, after.FailedLoginCount);
        Assert.Equal(locked.LockoutUntil, after.LockoutUntil);
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
    ///     Test that disabling the last enabled administrator is refused with the last-administrator rule.
    /// </summary>
    [Fact]
    public async Task UserService_SetEnabledAsync_LastEnabledAdministrator_IsRefused()
    {
        // Arrange: a service with one administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);

        // Act: disable the only administrator
        var disable = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetEnabledAsync("root", false, cancellationToken));

        // Assert: the rule is named and the administrator is still enabled
        Assert.Equal(DomainRule.LastAdmin, disable.Rule);
        Assert.True((await StoredAsync(store, "root")).Enabled);
    }

    /// <summary>
    ///     Test that disabling a user stores the user as disabled.
    /// </summary>
    [Fact]
    public async Task UserService_SetEnabledAsync_False_DisablesTheUser()
    {
        // Arrange: a service with an enabled user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: disable the user
        await service.SetEnabledAsync("ann", false, cancellationToken);

        // Assert: the stored user is disabled
        Assert.False((await StoredAsync(store, "ann")).Enabled);
    }

    /// <summary>
    ///     Test that demoting the last enabled administrator is refused with the last-administrator rule.
    /// </summary>
    [Fact]
    public async Task UserService_SetSystemAdminAsync_LastEnabledAdministrator_IsRefused()
    {
        // Arrange: a service with one administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, cancellationToken);

        // Act: demote the only administrator
        var demote = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSystemAdminAsync("root", false, Actor.CommandLine, cancellationToken));

        // Assert: the rule is named and the user is still an administrator
        Assert.Equal(DomainRule.LastAdmin, demote.Rule);
        Assert.True((await StoredAsync(store, "root")).IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a system administrator can be disabled or demoted while another enabled one remains.
    /// </summary>
    /// <param name="change">The change: <c>disable</c> or <c>demote</c>.</param>
    [Theory]
    [InlineData("disable")]
    [InlineData("demote")]
    public async Task UserService_SetEnabledAndSetSystemAdmin_AnotherEnabledAdministrator_AllowTheChange(string change)
    {
        // Arrange: two enabled administrators
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, cancellationToken);
        await service.CreateAsync("ann", "Ann", true, Password, cancellationToken);

        // Act: disable or demote one of them
        if (change == "disable")
        {
            await service.SetEnabledAsync("ann", false, cancellationToken);
        }
        else
        {
            await service.SetSystemAdminAsync("ann", false, Actor.CommandLine, cancellationToken);
        }

        // Assert: only the one administrator changed, the other one is as it was
        var ann = await StoredAsync(store, "ann");
        var root = await StoredAsync(store, "root");
        Assert.Equal(change != "disable", ann.Enabled);
        Assert.Equal(change == "disable", ann.IsSystemAdmin);
        Assert.True(root.Enabled);
        Assert.True(root.IsSystemAdmin);
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
    ///     Test that a change with the right current password stores the new password, which then signs in while the
    ///     old one does not.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_CorrectCurrentPassword_ChangesThePassword()
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: change the password with the right current one
        await service.ChangeOwnPasswordAsync("ann", Password, "another long password", cancellationToken);

        // Assert: the new hash is stored, the new password signs in and the old one does not
        Assert.Equal("hash:another long password", (await StoredAsync(store, "ann")).PasswordHash);
        Assert.Null(await service.AuthenticateAsync("ann", Password, cancellationToken));
        Assert.NotNull(await service.AuthenticateAsync("ann", "another long password", cancellationToken));
    }

    /// <summary>
    ///     Test that a wrong current password is refused with the current-password-incorrect rule and the stored
    ///     password hash stays as it was.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_WrongCurrentPassword_IsRefusedAndKeepsThePassword()
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        var hashBefore = (await StoredAsync(store, "ann")).PasswordHash;

        // Act: change the password with a wrong current one
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("ann", "wrong password!", "another long password", cancellationToken));

        // Assert: the rule is named and the stored hash is the same
        Assert.Equal(DomainRule.CurrentPasswordIncorrect, exception.Rule);
        Assert.Equal(hashBefore, (await StoredAsync(store, "ann")).PasswordHash);
    }

    /// <summary>
    ///     Test that a new password that breaks the password rules is refused with the password-invalid rule and the
    ///     stored password hash stays as it was.
    /// </summary>
    /// <param name="length">The length of the new password, or <c>null</c> for a missing one.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(11)]
    [InlineData(129)]
    public async Task UserService_ChangeOwnPasswordAsync_NewPasswordBreakingTheRules_IsRefused(int? length)
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        var hashBefore = (await StoredAsync(store, "ann")).PasswordHash;
        var newPassword = length is { } count ? new string('n', count) : null;

        // Act: change the password with the right current one and the bad new one
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("ann", Password, newPassword, cancellationToken));

        // Assert: the rule is named and the stored hash is the same
        Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
        Assert.Equal(hashBefore, (await StoredAsync(store, "ann")).PasswordHash);
    }

    /// <summary>
    ///     Test that one wrong current password counts as one failed sign-in.
    /// </summary>
    [Fact]
    public async Task UserService_ChangeOwnPasswordAsync_WrongCurrentPassword_CountsOneFailure()
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: change the password with a wrong current one
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ChangeOwnPasswordAsync("ann", "wrong password!", "another long password", cancellationToken));

        // Assert: one failure is stored
        Assert.Equal(1, (await StoredAsync(store, "ann")).FailedLoginCount);
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
    ///     Test that setting a new password replaces the password: the new one signs in and the old one does not.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_NewPassword_ReplacesThePassword()
    {
        // Arrange: a service with a user
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);

        // Act: set a new password
        await service.SetPasswordAsync("ann", "another long password", cancellationToken);

        // Assert: the new password signs in at once and the old one does not
        Assert.NotNull(await service.AuthenticateAsync("ann", "another long password", cancellationToken));
        Assert.Null(await service.AuthenticateAsync("ann", Password, cancellationToken));
    }

    /// <summary>
    ///     Test that a new password that breaks the password rules is refused with the password-invalid rule and the
    ///     stored password hash stays as it was.
    /// </summary>
    /// <param name="length">The length of the new password, or <c>null</c> for a missing one.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(11)]
    [InlineData(129)]
    public async Task UserService_SetPasswordAsync_PasswordBreakingTheRules_IsRefused(int? length)
    {
        // Arrange: a service with a user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        var hashBefore = (await StoredAsync(store, "ann")).PasswordHash;
        var newPassword = length is { } count ? new string('n', count) : null;

        // Act: set the bad password
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetPasswordAsync("ann", newPassword, cancellationToken));

        // Assert: the rule is named and the stored hash is the same
        Assert.Equal(DomainRule.PasswordInvalid, exception.Rule);
        Assert.Equal(hashBefore, (await StoredAsync(store, "ann")).PasswordHash);
    }

    /// <summary>
    ///     Test that setting a password ends the lockout of the user and clears the failure count.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_LockedUser_EndsTheLockout()
    {
        // Arrange: a locked-out user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);
        await LockAsync(service, "ann");
        var lockedUntil = (await StoredAsync(store, "ann")).LockoutUntil;

        // Act: set a new password
        await service.SetPasswordAsync("ann", "another long password", cancellationToken);

        // Assert: the lockout was running, is now ended and the count is clear, so the new password signs in
        Assert.Equal(_now + TimeSpan.FromMinutes(5), lockedUntil);
        var stored = await StoredAsync(store, "ann");
        Assert.Null(stored.LockoutUntil);
        Assert.Equal(0, stored.FailedLoginCount);
        Assert.NotNull(await service.AuthenticateAsync("ann", "another long password", cancellationToken));
    }

    /// <summary>
    ///     Test that every change of an unknown user is refused as not found.
    /// </summary>
    /// <param name="change">The kind of change that is tried.</param>
    [Theory]
    [InlineData("disable")]
    [InlineData("enable")]
    [InlineData("role")]
    [InlineData("systemAdmin")]
    [InlineData("ownPassword")]
    [InlineData("password")]
    public async Task UserService_Changes_UnknownUser_ThrowUserNotFound(string change)
    {
        // Arrange: a service without users
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: change an unknown user in the given way
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => change switch
        {
            "disable" => service.SetEnabledAsync("nobody", false, cancellationToken),
            "enable" => service.SetEnabledAsync("nobody", true, cancellationToken),
            "role" => service.SetRoleAsync("nobody", UserRole.Admin, cancellationToken),
            "systemAdmin" => service.SetSystemAdminAsync("nobody", true, Actor.CommandLine, cancellationToken),
            "ownPassword" => service.ChangeOwnPasswordAsync("nobody", Password, Password, cancellationToken),
            _ => service.SetPasswordAsync("nobody", Password, cancellationToken)
        });

        // Assert: the rule names the missing user
        Assert.Equal(DomainRule.UserNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that the system administrator right can be taken away from one of two administrators.
    /// </summary>
    [Fact]
    public async Task UserService_SetSystemAdminAsync_Demote_TakesTheRightAway()
    {
        // Arrange: a service with two system administrators
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        await service.CreateAsync("ann", "Ann", true, Password, Actor.CommandLine, cancellationToken);

        // Act: demote one of them
        await service.SetSystemAdminAsync("root", false, Actor.CommandLine, cancellationToken);

        // Assert: the demoted user lost the right and the other one kept it
        Assert.False((await StoredAsync(store, "root")).IsSystemAdmin);
        Assert.True((await StoredAsync(store, "ann")).IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a promotion by user name stores the system administrator right.
    /// </summary>
    [Fact]
    public async Task UserService_SetSystemAdminAsync_Promote_GivesTheRight()
    {
        // Arrange: a service with a plain user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);

        // Act: promote the user
        await service.SetSystemAdminAsync("ann", true, Actor.CommandLine, cancellationToken);

        // Assert: the stored user is a system administrator
        Assert.True((await StoredAsync(store, "ann")).IsSystemAdmin);
    }

    /// <summary>
    ///     Test that a successful password sign-in stores the time of the sign-in and adds no audit entry.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_RightPassword_StoresTheTimeOfTheSignIn()
    {
        // Arrange: a user who never signed in, and a clock that moves
        var (service, store, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, cancellationToken);
        var created = (await store.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;

        // Act: sign in five minutes later
        clock.Advance(TimeSpan.FromMinutes(5));
        var auditsBefore = store.Audits.Count;
        await service.AuthenticateAsync("ann", Password, cancellationToken);
        var afterSuccess = (await store.GetByUsernameAsync("ann", cancellationToken))!.LastSignInUtc;

        // Assert: the time of the sign-in is stored, and storing it added no audit entry
        Assert.Null(created);
        Assert.Equal(_now + TimeSpan.FromMinutes(5), afterSuccess);
        Assert.Equal(auditsBefore, store.Audits.Count);
    }

    /// <summary>
    ///     Test that a failed sign-in stores no time of a sign-in.
    /// </summary>
    [Fact]
    public async Task UserService_AuthenticateAsync_WrongPassword_StoresNoSignInTime()
    {
        // Arrange: a user who never signed in
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: sign in with a wrong password
        await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);

        // Assert: no time of a sign-in is stored
        Assert.Null((await StoredAsync(store, "ann")).LastSignInUtc);
    }

    /// <summary>
    ///     Test that a re-authentication with the right password stores no time of a sign-in.
    /// </summary>
    [Fact]
    public async Task UserService_ReauthenticatePasswordAsync_RightPassword_StoresNoSignInTime()
    {
        // Arrange: a user who never signed in
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, cancellationToken);

        // Act: re-authenticate with the right password
        var step = await service.ReauthenticatePasswordAsync("ann", Password, cancellationToken);

        // Assert: the user is returned but no time of a sign-in is stored
        Assert.Equal("ann", step.User?.Username);
        Assert.Null((await StoredAsync(store, "ann")).LastSignInUtc);
    }

    /// <summary>
    ///     Test that the first and a later accepted sign-in of an account at a provider store the time of the sign-in.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_AcceptedSignIn_StoresTheTimeOfTheSignIn()
    {
        // Arrange: an account that is created by its first sign-in
        var (service, store, clock) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: the first sign-in and, an hour later, a second one
        var first = (await service.SignInExternalAsync("https://idp", "sub-1", "ann", "Ann", cancellationToken)).User!;
        var afterFirst = (await store.GetByUsernameAsync(first.Username, cancellationToken))!.LastSignInUtc;
        clock.Advance(TimeSpan.FromHours(1));
        await service.SignInExternalAsync("https://idp", "sub-1", "ann", "Ann", cancellationToken);
        var afterLater = (await store.GetByUsernameAsync(first.Username, cancellationToken))!.LastSignInUtc;

        // Assert: the time of each accepted sign-in is stored
        Assert.Equal(_now, afterFirst);
        Assert.Equal(_now + TimeSpan.FromHours(1), afterLater);
    }

    /// <summary>
    ///     Test that a lockout that is still running counts as locked, up to one second before its end.
    /// </summary>
    [Fact]
    public void UserService_IsLocked_RunningLockout_IsLocked()
    {
        // Arrange: users whose lockouts end one minute from now and at the start instant
        var running = new User { LockoutUntil = _now + TimeSpan.FromMinutes(1) };
        var ended = new User { LockoutUntil = _now };

        // Act and assert: the running lockout counts, and so does one that ends one second later
        Assert.True(UserService.IsLocked(running, _now));
        Assert.True(UserService.IsLocked(ended, _now - TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    ///     Test that a lockout is no longer a lockout at its end instant.
    /// </summary>
    [Fact]
    public void UserService_IsLocked_LockoutAtItsEnd_IsNotLocked()
    {
        // Arrange: a user whose lockout ends at the start instant
        var ended = new User { LockoutUntil = _now };

        // Act and assert: at that instant the user is not locked
        Assert.False(UserService.IsLocked(ended, _now));
    }

    /// <summary>
    ///     Test that a user without a lockout is not locked.
    /// </summary>
    [Fact]
    public void UserService_IsLocked_NoLockout_IsNotLocked()
    {
        // Arrange: a user with no lockout
        var none = new User { LockoutUntil = null };

        // Act and assert: the user is not locked
        Assert.False(UserService.IsLocked(none, _now));
    }

    /// <summary>
    ///     Test that an unlock by a system administrator is audited as an unlock of the named user.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_SystemAdministrator_RecordsUserUnlocked()
    {
        // Arrange: a locked-out user and an administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        await LockAsync(service, "ann");

        // Act
        await service.UnlockAsync(new Actor(root.Username, root.DisplayName), ann.Id, cancellationToken);

        // Assert: one entry of the unlock, with the actor and the user
        var audit = Assert.Single(store.Audits, entry => entry.Action == "user.unlocked");
        Assert.Equal("root", audit.ActorUsername);
        Assert.Equal("user 'ann'", audit.Detail);
    }

    /// <summary>
    ///     Test that a system administrator ends the lockout of a user, who then signs in at once.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_SystemAdministrator_EndsTheLockout()
    {
        // Arrange: a locked-out user and an administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        await LockAsync(service, "ann");
        var locked = (await StoredAsync(store, "ann")).LockoutUntil;

        // Act: unlock the user by identifier
        await service.UnlockAsync(new Actor(root.Username, root.DisplayName), ann.Id, cancellationToken);

        // Assert: the lockout was running, is now ended, and the right password works at the same instant
        Assert.Equal(_now + TimeSpan.FromMinutes(5), locked);
        Assert.Null((await StoredAsync(store, "ann")).LockoutUntil);
        Assert.NotNull(await service.AuthenticateAsync("ann", Password, cancellationToken));
    }

    /// <summary>
    ///     Test that an unlock by a system administrator clears the count of failed sign-ins.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_SystemAdministrator_ClearsTheCountOfFailedSignIns()
    {
        // Arrange: a user with four failed sign-ins, and an administrator
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        for (var i = 0; i < 4; i++)
        {
            await service.AuthenticateAsync("ann", "wrong password!", cancellationToken);
        }

        var before = (await StoredAsync(store, "ann")).FailedLoginCount;

        // Act: unlock the user
        await service.UnlockAsync(new Actor(root.Username, root.DisplayName), ann.Id, cancellationToken);

        // Assert: the count was four and is now zero
        Assert.Equal(4, before);
        Assert.Equal(0, (await StoredAsync(store, "ann")).FailedLoginCount);
    }

    /// <summary>
    ///     Test that an actor who is not an enabled system administrator without a temporary password, or who acts
    ///     through an API token, is refused with the actor-not-allowed rule.
    /// </summary>
    /// <param name="actorKind">The kind of actor that tries to unlock.</param>
    [Theory]
    [InlineData("ordinary")]
    [InlineData("unknown")]
    [InlineData("commandLine")]
    [InlineData("apiToken")]
    [InlineData("temporaryPassword")]
    [InlineData("disabledAdministrator")]
    public async Task UserService_UnlockAsync_ActorThatIsNotAnEnabledAdministrator_IsRefused(string actorKind)
    {
        // Arrange: a locked user, an ordinary user, an administrator with a temporary password and a disabled one
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        await service.CreateAsync("tmp", "Tmp", true, Password, Actor.CommandLine, cancellationToken);
        await service.SetPasswordAsync("tmp", Password, Actor.CommandLine, cancellationToken);
        await service.CreateAsync("dis", "Dis", true, Password, Actor.CommandLine, cancellationToken);
        await service.SetEnabledAsync("dis", false, Actor.CommandLine, cancellationToken);
        await LockAsync(service, "ann");
        var actor = actorKind switch
        {
            "ordinary" => new Actor("ann", "Ann"),
            "unknown" => new Actor("nobody", "Nobody"),
            "commandLine" => Actor.CommandLine,
            "apiToken" => new Actor("root", "Root") { ApiTokenId = "t1" },
            "temporaryPassword" => new Actor("tmp", "Tmp"),
            _ => new Actor("dis", "Dis")
        };

        // Act: try to unlock the user
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.UnlockAsync(actor, ann.Id, cancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
    }

    /// <summary>
    ///     Test that a refused actor leaves the lockout and the failure count as they were and adds no audit entry.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_RefusedActor_ChangesAndAuditsNothing()
    {
        // Arrange: a user locked out at the start instant (the fifth failure restarts the count) and an ordinary user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        var ann = await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);
        await LockAsync(service, "ann");
        var locked = await StoredAsync(store, "ann");
        var auditsBefore = store.Audits.Count;

        // Act: the locked user tries to unlock the own account
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.UnlockAsync(new Actor("ann", "Ann"), ann.Id, cancellationToken));

        // Assert: the rule is named, the lockout and the count are as before, and no entry was recorded
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
        Assert.Equal(0, locked.FailedLoginCount);
        Assert.Equal(_now + TimeSpan.FromMinutes(5), locked.LockoutUntil);
        var after = await StoredAsync(store, "ann");
        Assert.Equal(locked.FailedLoginCount, after.FailedLoginCount);
        Assert.Equal(locked.LockoutUntil, after.LockoutUntil);
        Assert.Equal(auditsBefore, store.Audits.Count);
        Assert.DoesNotContain(store.Audits, entry => entry.Action == "user.unlocked");
    }

    /// <summary>
    ///     Test that an identifier that no user has is refused with the user-not-found rule.
    /// </summary>
    [Fact]
    public async Task UserService_UnlockAsync_UnknownUserIdentifier_ThrowsUserNotFound()
    {
        // Arrange: a service with an administrator
        var (service, _, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);

        // Act: unlock an identifier that no user has
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.UnlockAsync(new Actor("root", "Root"), 9999, cancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.UserNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that a password set by another user, an administrator or the command line, is temporary.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_OtherUserByAdministratorOrCommandLine_ForcesAChange()
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

        // Assert
        Assert.False(created);
        Assert.True(byAdmin);
        Assert.True(byCommandLine);
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
    ///     Test that an administrator who sets their own password ends a pending forced change.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_OwnRow_ClearsAPendingChange()
    {
        // Arrange: an administrator whose password was set by the command line, so a change is pending
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("root", "Root", true, Password, Actor.CommandLine, cancellationToken);
        await service.SetPasswordAsync("root", "temporary password", Actor.CommandLine, cancellationToken);
        var pending = (await store.GetByUsernameAsync("root", cancellationToken))!.MustChangePassword;

        // Act: the administrator sets their own password
        await service.SetPasswordAsync("root", "own chosen password", new Actor("root", "Root"), cancellationToken);

        // Assert
        Assert.True(pending);
        Assert.False((await store.GetByUsernameAsync("root", cancellationToken))!.MustChangePassword);
    }

    /// <summary>
    ///     Test that an API-token actor with the name of the user is not "the own row", so the password it sets is
    ///     temporary.
    /// </summary>
    [Fact]
    public async Task UserService_SetPasswordAsync_ApiTokenActorWithTheUserName_ForcesAChange()
    {
        // Arrange: a local user
        var (service, store, _) = Create();
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.CreateAsync("ann", "Ann", false, Password, Actor.CommandLine, cancellationToken);

        // Act: set the password through an API token that carries the name of the user
        await service.SetPasswordAsync("ann", "another long password", new Actor("ann", "Ann") { ApiTokenId = "t1" }, cancellationToken);

        // Assert
        Assert.True((await store.GetByUsernameAsync("ann", cancellationToken))!.MustChangePassword);
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

    /// <summary>
    ///     A password hasher that counts how often a hash is verified.
    /// </summary>
    private sealed class CountingHasher : IPasswordHasher
    {
        /// <summary>
        ///     Gets or sets the number of calls of <see cref="Verify"/> since the count was last reset.
        /// </summary>
        public int VerifyCalls { get; set; }

        /// <inheritdoc />
        public string Hash(string password)
        {
            return "hash:" + password;
        }

        /// <inheritdoc />
        public bool Verify(string hash, string password)
        {
            VerifyCalls++;
            return hash == "hash:" + password;
        }
    }
}
