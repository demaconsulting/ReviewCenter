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
///     Unit tests for the accounts of an external identity provider in <see cref="UserService"/>: the match by issuer
///     and subject, the creation of an account, the derived names, and the refusal of every password path.
/// </summary>
public class UserServiceExternalTests
{
    /// <summary>
    ///     The issuer used by the tests.
    /// </summary>
    private const string Issuer = "https://idp.example/";

    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates a service over fresh fakes.
    /// </summary>
    /// <returns>The service and its store.</returns>
    private static (UserService Service, FakeUserStore Store) Create()
    {
        var store = new FakeUserStore();
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        return (new UserService(store, new FakePasswordHasher(), clock), store);
    }

    /// <summary>
    ///     Test that the first sign-in creates an enabled account that is no administrator and has no password, with
    ///     an audit entry that names the host of the provider only, and that the next sign-in finds the same account.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_FirstThenSecond_CreatesOnceAndMatches()
    {
        // Arrange
        var (service, store) = Create();

        // Act
        var first = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive Oidc", TestContext.Current.CancellationToken);
        var second = await service.SignInExternalAsync(Issuer, "sub-1", "other", "Other Name", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ExternalSignInRefusal.None, first.Refusal);
        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.User!.Id, second.User!.Id);
        Assert.Equal("olive", second.User.Username);
        Assert.Equal("Olive Oidc", second.User.DisplayName);
        Assert.True(first.User.Enabled);
        Assert.False(first.User.IsSystemAdmin);
        Assert.True(first.User.IsExternal);
        Assert.Equal(string.Empty, first.User.PasswordHash);
        var audit = Assert.Single(store.Audits);
        Assert.Equal("account.oidc-created", audit.Action);
        Assert.Equal("issuer idp.example", audit.Detail);
    }

    /// <summary>
    ///     Test that the account is found by issuer and subject exactly: another subject or another issuer is another
    ///     person, and an account with the same name, display name or e-mail address is never linked.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_MatchesOnlyByIssuerAndSubject()
    {
        // Arrange: a local account with the name the provider suggests
        var (service, _) = Create();
        var local = await service.CreateAsync("olive", "Olive", UserRole.Admin, Password, TestContext.Current.CancellationToken);

        // Act
        var first = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);
        var otherSubject = await service.SignInExternalAsync(Issuer, "SUB-1", "olive", "Olive", TestContext.Current.CancellationToken);
        var otherIssuer = await service.SignInExternalAsync("https://other.example/", "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert: three new accounts, none of them the local one, and none is an administrator
        var ids = new[] { local.Id, first.User!.Id, otherSubject.User!.Id, otherIssuer.User!.Id };
        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal("olive-2", first.User.Username);
        Assert.Equal("olive-3", otherSubject.User.Username);
        Assert.Equal("olive-4", otherIssuer.User.Username);
        Assert.All(new[] { first, otherSubject, otherIssuer }, result => Assert.False(result.User!.IsSystemAdmin));
    }

    /// <summary>
    ///     Test that a user name that the provider suggests is reduced to the characters of a user name and is
    ///     replaced when nothing usable is left.
    /// </summary>
    /// <param name="candidate">The suggested name.</param>
    /// <param name="expected">The user name of the account.</param>
    [Theory]
    [InlineData("olive", "olive")]
    [InlineData("Olive.O_x-9", "Olive.O_x-9")]
    [InlineData("<b>mal</b>", "bmalb")] // cspell:disable-line
    [InlineData("\u00e9\u00e9\u00e9", "user")]
    [InlineData(null, "user")]
    [InlineData("", "user")]
    [InlineData("a", "user-a")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567", "01234567890123456789012345678901234567890123")]
    public async Task UserService_SignInExternalAsync_Candidate_IsReducedToAValidUsername(string? candidate, string expected)
    {
        // Arrange
        var (service, _) = Create();

        // Act
        var result = await service.SignInExternalAsync(Issuer, "sub-1", candidate, "Name", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, result.User!.Username);
    }

    /// <summary>
    ///     Test that a display name is cleaned (control, invisible formatting and bidirectional characters removed, white
    ///     space collapsed, length bounded) and replaced by the user name when nothing is left.
    /// </summary>
    /// <param name="name">The name that the provider gives.</param>
    /// <param name="expected">The expected display name.</param>
    [Theory]
    [InlineData("  Eve\r\n\u202EMallory   Jr  ", "Eve Mallory Jr")]
    [InlineData("Zero\u200BWidth", "ZeroWidth")]
    [InlineData("   ", "olive")]
    [InlineData(null, "olive")]
    public async Task UserService_SignInExternalAsync_DisplayName_IsSanitized(string? name, string expected)
    {
        // Arrange
        var (service, _) = Create();

        // Act
        var result = await service.SignInExternalAsync(Issuer, "sub-1", "olive", name, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, result.User!.DisplayName);
    }

    /// <summary>
    ///     Test that a very long display name is cut to the limit.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_LongDisplayName_IsBounded()
    {
        // Arrange
        var (service, _) = Create();

        // Act
        var result = await service.SignInExternalAsync(
            Issuer, "sub-1", "olive", new string('x', 1000), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReviewLimits.MaxDisplayNameLength, result.User!.DisplayName.Length);
    }

    /// <summary>
    ///     Test that an identity that is missing or longer than the stored columns is refused without an account.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="subject">The subject.</param>
    [Theory]
    [InlineData(null, "sub-1")]
    [InlineData("", "sub-1")]
    [InlineData(Issuer, null)]
    [InlineData(Issuer, "")]
    public async Task UserService_SignInExternalAsync_MissingIdentity_IsInvalid(string? issuer, string? subject)
    {
        // Arrange
        var (service, store) = Create();

        // Act
        var result = await service.SignInExternalAsync(issuer, subject, "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ExternalSignInRefusal.Invalid, result.Refusal);
        Assert.Null(result.User);
        Assert.Empty(await store.ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an issuer or subject one character over its limit is refused, and one at the limit is accepted.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_IdentityLengthLimits_AreEnforced()
    {
        // Arrange
        var (service, _) = Create();
        var longIssuer = "https://" + new string('a', ReviewLimits.MaxExternalIssuerLength - 8);
        var longSubject = new string('s', ReviewLimits.MaxExternalSubjectLength);

        // Act
        var atLimit = await service.SignInExternalAsync(longIssuer, longSubject, "olive", "Olive", TestContext.Current.CancellationToken);
        var issuerOver = await service.SignInExternalAsync(longIssuer + "a", "sub", "olive", "Olive", TestContext.Current.CancellationToken);
        var subjectOver = await service.SignInExternalAsync(Issuer, longSubject + "s", "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ExternalSignInRefusal.None, atLimit.Refusal);
        Assert.Equal(ExternalSignInRefusal.Invalid, issuerOver.Refusal);
        Assert.Equal(ExternalSignInRefusal.Invalid, subjectOver.Refusal);
    }

    /// <summary>
    ///     Test that a disabled account is refused with its own reason, which the caller must not show, and is not
    ///     changed.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_DisabledAccount_IsRefused()
    {
        // Arrange
        var (service, _) = Create();
        await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);
        await service.SetEnabledAsync("olive", false, new Actor("root", "Root"), TestContext.Current.CancellationToken);

        // Act
        var result = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ExternalSignInRefusal.Disabled, result.Refusal);
        Assert.Null(result.User);
        Assert.False((await service.GetAsync("olive", TestContext.Current.CancellationToken))!.Enabled);
    }

    /// <summary>
    ///     Test that when every numbered name is taken, a name derived from the identity itself is used.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_AllNamesTaken_UsesNameFromIdentity()
    {
        // Arrange: the plain name and the first fifty numbered names are taken
        var (service, _) = Create();
        await service.CreateAsync("olive", "Olive", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        for (var number = 2; number <= 50; number++)
        {
            await service.CreateAsync($"olive-{number}", "Olive", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        }

        // Act
        var result = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("oidc-", result.User!.Username, StringComparison.Ordinal);
        Assert.Equal(17, result.User.Username.Length);
    }

    /// <summary>
    ///     Test that when a local account already holds the name derived from the identity itself, the sign-in still
    ///     creates an account, under a numbered variant of that name, and the local account is not touched.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_IdentityNameTakenByLocalAccount_UsesNumberedVariant()
    {
        // Arrange: every derived name is taken, and so are the name from the identity and its first numbered variant
        var (service, _) = Create();
        var identityName = IdentityName(Issuer, "sub-1");
        await service.CreateAsync("olive", "Olive", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        for (var number = 2; number <= 50; number++)
        {
            await service.CreateAsync($"olive-{number}", "Olive", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        }

        await service.CreateAsync(identityName, "Local", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await service.CreateAsync($"{identityName}-2", "Local Two", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Act
        var result = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert: the account is created under the next free variant, within the length bound, and no local one changed
        Assert.Equal(ExternalSignInRefusal.None, result.Refusal);
        Assert.True(result.Created);
        Assert.Equal($"{identityName}-3", result.User!.Username);
        Assert.True(result.User.IsExternal);
        Assert.InRange(result.User.Username.Length, ReviewLimits.MinUsernameLength, ReviewLimits.MaxUsernameLength);
        Assert.All(result.User.Username, character => Assert.True(char.IsAsciiLetterOrDigit(character) || character == '-'));
        var local = await service.GetAsync(identityName, TestContext.Current.CancellationToken);
        Assert.False(local!.IsExternal);
        Assert.Equal("Local", local.DisplayName);
        var second = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);
        Assert.False(second.Created);
        Assert.Equal(result.User.Id, second.User!.Id);
    }

    /// <summary>
    ///     Test that when a local account holds only the name derived from the identity, the numbered variant
    ///     <c>-2</c> is used.
    /// </summary>
    [Fact]
    public async Task UserService_SignInExternalAsync_OnlyIdentityNameTaken_UsesFirstVariant()
    {
        // Arrange
        var (service, _) = Create();
        var identityName = IdentityName(Issuer, "sub-1");
        await service.CreateAsync("olive", "Olive", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        for (var number = 2; number <= 50; number++)
        {
            await service.CreateAsync($"olive-{number}", "Olive", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        }

        await service.CreateAsync(identityName, "Local", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Act
        var result = await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Created);
        Assert.Equal($"{identityName}-2", result.User!.Username);
    }

    /// <summary>
    ///     Computes the name <see cref="UserService"/> derives from an identity when every name suggested by the
    ///     provider is taken.
    /// </summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="subject">The subject.</param>
    /// <returns>The name: <c>oidc-</c> and the first 12 hexadecimal characters of the hash of the identity.</returns>
    private static string IdentityName(string issuer, string subject)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{issuer}|{subject}"));
        return "oidc-" + Convert.ToHexStringLower(hash)[..12];
    }

    /// <summary>
    ///     Test that no password sign-in, confirmation of a signature, change or set of a password works for an
    ///     account of an identity provider, whatever is typed, and that the account is not locked by the attempts.
    /// </summary>
    [Fact]
    public async Task UserService_ExternalAccount_EveryPasswordPathIsRefused()
    {
        // Arrange
        var (service, _) = Create();
        await service.SignInExternalAsync(Issuer, "sub-1", "olive", "Olive", TestContext.Current.CancellationToken);
        var admin = new Actor("root", "Root");

        // Act
        var signIn = await service.AuthenticateAsync("olive", string.Empty, TestContext.Current.CancellationToken);
        var signInHash = await service.AuthenticateAsync("olive", "hash:", TestContext.Current.CancellationToken);
        var reauth = await service.ReauthenticateAsync("olive", string.Empty, TestContext.Current.CancellationToken);
        var change = await Assert.ThrowsAsync<DomainRuleException>(() => service.ChangeOwnPasswordAsync(
            "olive", string.Empty, "another long password", TestContext.Current.CancellationToken));
        var set = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetPasswordAsync(
            "olive", "another long password", admin, TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(signIn);
        Assert.Null(signInHash);
        Assert.Null(reauth);
        Assert.Equal(DomainRule.PasswordInvalid, set.Rule);
        Assert.NotEqual(DomainRule.UserNotFound, change.Rule);
        Assert.Equal(0, (await service.GetAsync("olive", TestContext.Current.CancellationToken))!.FailedLoginCount);
    }
}
