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
///     Unit tests for <see cref="RepositoryService"/> using in-memory fakes: the registration rules, the
///     administrator-only access, the write-only token and the audit trail.
/// </summary>
public class RepositoryServiceTests
{
    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     A token used to check that it never leaks.
    /// </summary>
    private const string Secret = "s3cr3t-T0ken-value";

    /// <summary>
    ///     The administrator actor.
    /// </summary>
    private static readonly Actor _admin = new("adm", "Adm");

    /// <summary>
    ///     The fixture: a service over fakes with an administrator, a reviewer and a disabled administrator.
    /// </summary>
    /// <param name="Service">The service under test.</param>
    /// <param name="Store">The fake store.</param>
    /// <param name="Protector">The fake protector.</param>
    /// <param name="Users">The user service.</param>
    private sealed record Fixture(
        RepositoryService Service,
        FakeRepositoryStore Store,
        FakeCredentialProtector Protector,
        UserService Users);

    /// <summary>
    ///     Creates the fixture.
    /// </summary>
    /// <returns>The fixture.</returns>
    private static async Task<Fixture> CreateAsync()
    {
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var cancellationToken = TestContext.Current.CancellationToken;
        await users.CreateAsync("adm", "Adm", UserRole.Admin, Password, cancellationToken);
        await users.CreateAsync("rev", "Rev", UserRole.Reviewer, Password, cancellationToken);
        var store = new FakeRepositoryStore();
        var protector = new FakeCredentialProtector();
        return new Fixture(new RepositoryService(store, protector, users, clock), store, protector, users);
    }

    /// <summary>
    ///     Test that a repository with a user name and token is registered enabled, with the token stored only
    ///     encrypted and never returned.
    /// </summary>
    [Fact]
    public async Task RepositoryService_CreateAsync_WithToken_StoresItEncryptedAndWriteOnly()
    {
        // Arrange: a fixture
        var f = await CreateAsync();

        // Act: register a private repository
        var created = await f.Service.CreateAsync(
            "main-repo", "https://example.com/org/repo.git", "svc", Secret, _admin, TestContext.Current.CancellationToken);

        // Assert: enabled, token flagged but not exposed, and the store holds only the encrypted text
        Assert.True(created.Enabled);
        Assert.True(created.HasToken);
        Assert.Equal("svc", created.Username);
        var stored = f.Store.StoredToken(created.Id);
        Assert.NotNull(stored);
        Assert.DoesNotContain(Secret, stored, StringComparison.Ordinal);
        Assert.Equal(Secret, f.Protector.Unprotect(stored));
        Assert.DoesNotContain(Secret, created.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an anonymous repository (no user name, no token) can be registered with every allowed
    ///     location form.
    /// </summary>
    /// <param name="location">The location.</param>
    [Theory]
    [InlineData("https://example.com/org/repo.git")]
    [InlineData("http://localhost:8080/repo.git")]
    [InlineData("file:///srv/git/repo.git")]
    public async Task RepositoryService_CreateAsync_AllowedLocation_Registers(string location)
    {
        // Arrange: a fixture
        var f = await CreateAsync();

        // Act: register it
        var created = await f.Service.CreateAsync("anon", location, null, null, _admin, TestContext.Current.CancellationToken);

        // Assert: stored without credentials
        Assert.Equal(location, created.Location);
        Assert.Null(created.Username);
        Assert.False(created.HasToken);
    }

    /// <summary>
    ///     Test that an absolute local path is a valid location.
    /// </summary>
    [Fact]
    public async Task RepositoryService_CreateAsync_AbsoluteLocalPath_Registers()
    {
        // Arrange: a fixture and an absolute path of this platform
        var f = await CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), "git", "repo.git");

        // Act: register it
        var created = await f.Service.CreateAsync("local", path, null, null, _admin, TestContext.Current.CancellationToken);

        // Assert: stored as entered
        Assert.Equal(path, created.Location);
    }

    /// <summary>
    ///     Test that invalid names are refused with the name rule.
    /// </summary>
    /// <param name="name">The invalid name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("-x")]
    [InlineData(".x")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("\u00e9")]
    [InlineData("1234567890123456789012345678901234567890x")]
    public async Task RepositoryService_CreateAsync_InvalidName_ThrowsRepositoryNameInvalid(string name)
    {
        // Arrange: a fixture
        var f = await CreateAsync();

        // Act: register it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.CreateAsync(name, "https://example.com/r.git", null, null, _admin, TestContext.Current.CancellationToken));

        // Assert: the rule is named and nothing is stored
        Assert.Equal(DomainRule.RepositoryNameInvalid, exception.Rule);
        Assert.Empty(await f.Store.ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the name length limit is inclusive and a duplicate name in another case is refused.
    /// </summary>
    [Fact]
    public async Task RepositoryService_CreateAsync_NameLimitsAndDuplicate_AreEnforced()
    {
        // Arrange: a fixture with a repository whose name has the longest allowed length
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var longest = new string('n', RepositoryService.MaxNameLength);
        await f.Service.CreateAsync(longest, "https://example.com/r.git", null, null, _admin, cancellationToken);

        // Act: register the same name in another case
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.CreateAsync(longest.ToUpperInvariant(), "https://example.com/r2.git", null, null, _admin, cancellationToken));

        // Assert: the name is taken
        Assert.Equal(DomainRule.RepositoryNameTaken, exception.Rule);
    }

    /// <summary>
    ///     Test that unsafe or unsupported locations are refused with the location rule.
    /// </summary>
    /// <param name="location">The invalid location.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-c core.sshCommand=x")]
    [InlineData("--upload-pack=x")]
    [InlineData("ssh://git@example.com/r.git")]
    [InlineData("git@example.com:org/r.git")]
    [InlineData("ext::sh -c touch% /tmp/x")]
    [InlineData("git://example.com/r.git")]
    [InlineData("relative/path")]
    [InlineData("https://user:pw@example.com/r.git")]
    [InlineData("https://user@example.com/r.git")]
    [InlineData("https://")]
    [InlineData("https://example.com/my repo.git")]
    [InlineData("https://example.com/r.git\nx")]
    [InlineData("https://example.com/r.git?token=abc")]
    [InlineData("https://example.com/r.git#frag")]
    [InlineData("https://example.com/r.git?")]
    public async Task RepositoryService_CreateAsync_InvalidLocation_ThrowsRepositoryLocationInvalid(string location)
    {
        // Arrange: a fixture
        var f = await CreateAsync();

        // Act: register it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.CreateAsync("repo", location, null, null, _admin, TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.RepositoryLocationInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that credentials that do not fit the location or each other are refused.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <param name="username">The user name, or null.</param>
    /// <param name="token">The token, or null.</param>
    [Theory]
    [InlineData("https://example.com/r.git", null, "tok")]
    [InlineData("https://example.com/r.git", "a:b", "tok")]
    [InlineData("https://example.com/r.git", "svc", "has space")]
    [InlineData("http://example.com/r.git", "svc", "tok")]
    [InlineData("file:///srv/r.git", "svc", null)]
    public async Task RepositoryService_CreateAsync_InvalidCredentials_ThrowsRepositoryCredentialInvalid(
        string location,
        string? username,
        string? token)
    {
        // Arrange: a fixture
        var f = await CreateAsync();

        // Act: register it
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.CreateAsync("repo", location, username, token, _admin, TestContext.Current.CancellationToken));

        // Assert: the rule is named and the token is not in the message
        Assert.Equal(DomainRule.RepositoryCredentialInvalid, exception.Rule);
        Assert.DoesNotContain("tok", exception.Message.Replace("token", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that only enabled administrators can use the registry: a reviewer, an unknown user and a disabled
    ///     administrator are refused for every operation.
    /// </summary>
    [Fact]
    public async Task RepositoryService_AdminOnly_RefusesOtherActors()
    {
        // Arrange: a fixture with a repository and a disabled administrator
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await f.Service.CreateAsync("repo", "https://example.com/r.git", null, null, _admin, cancellationToken);
        await f.Users.CreateAsync("gone", "Gone", UserRole.Admin, Password, cancellationToken);
        await f.Users.SetEnabledAsync("gone", false, _admin, cancellationToken);
        var actors = new[] { new Actor("rev", "Rev"), new Actor("nobody", "Nobody"), new Actor("gone", "Gone") };

        foreach (var actor in actors)
        {
            // Act: try every operation
            var attempts = new Func<Task>[]
            {
                () => f.Service.CreateAsync("other", "https://example.com/o.git", null, null, actor, cancellationToken),
                () => f.Service.UpdateAsync(created.Id, "repo", "https://example.com/r.git", null, TokenChange.Keep, actor, cancellationToken),
                () => f.Service.SetEnabledAsync(created.Id, false, actor, cancellationToken),
                () => f.Service.ListAsync(actor, cancellationToken)
            };

            // Assert: each is refused with the actor rule
            foreach (var attempt in attempts)
            {
                var exception = await Assert.ThrowsAsync<DomainRuleException>(attempt);
                Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
            }
        }

        // Assert: nothing changed and only the creation was audited
        Assert.Single(f.Store.Audits);
        Assert.True((await f.Store.GetAsync(created.Id, cancellationToken))!.Enabled);
    }

    /// <summary>
    ///     Test that every change is audited with the changed field names and that no audit text or message
    ///     contains the token.
    /// </summary>
    [Fact]
    public async Task RepositoryService_Changes_AreAuditedWithoutSecrets()
    {
        // Arrange: a fixture
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: create, update several fields, replace and clear the token, disable and enable
        var created = await f.Service.CreateAsync("repo", "https://example.com/r.git", "svc", Secret, _admin, cancellationToken);
        await f.Service.UpdateAsync(created.Id, "renamed", "https://example.com/r2.git", "svc2", TokenChange.Replace("n3w-T0ken"), _admin, cancellationToken);
        await f.Service.UpdateAsync(created.Id, "renamed", "https://example.com/r2.git", "svc2", TokenChange.Clear, _admin, cancellationToken);
        await f.Service.SetEnabledAsync(created.Id, false, _admin, cancellationToken);
        await f.Service.SetEnabledAsync(created.Id, true, _admin, cancellationToken);

        // Assert: the actions and details are as designed
        Assert.Equal(
            ["repository.created", "repository.updated", "repository.updated", "repository.disabled", "repository.enabled"],
            f.Store.Audits.Select(a => a.Action).ToArray());
        Assert.Equal(
            "repository 'repo', location 'https://example.com/r.git', user name set, token set",
            f.Store.Audits[0].Detail);
        Assert.Equal("repository 'renamed': name, location, user name, token replaced", f.Store.Audits[1].Detail);
        Assert.Equal("repository 'renamed': token cleared", f.Store.Audits[2].Detail);
        Assert.Equal("repository 'renamed'", f.Store.Audits[3].Detail);
        Assert.All(f.Store.Audits, audit =>
        {
            Assert.Equal("adm", audit.ActorUsername);
            Assert.DoesNotContain(Secret, audit.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("n3w-T0ken", audit.Detail, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Test that keeping the token leaves the stored token alone, and that an update that changes nothing, or
    ///     a repeated enable, makes no audit entry.
    /// </summary>
    [Fact]
    public async Task RepositoryService_NoOpChanges_AreNotAudited()
    {
        // Arrange: a fixture with a private repository
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await f.Service.CreateAsync("repo", "https://example.com/r.git", "svc", Secret, _admin, cancellationToken);
        var before = f.Store.StoredToken(created.Id);

        // Act: update with the same values and keep the token, enable an enabled repository, clear nothing
        var same = await f.Service.UpdateAsync(created.Id, "repo", "https://example.com/r.git", "svc", TokenChange.Keep, _admin, cancellationToken);
        var enabled = await f.Service.SetEnabledAsync(created.Id, true, _admin, cancellationToken);

        // Assert: nothing changed and only the creation was audited
        Assert.Equal(created, same);
        Assert.Equal(created, enabled);
        Assert.Equal(before, f.Store.StoredToken(created.Id));
        Assert.Single(f.Store.Audits);
    }

    /// <summary>
    ///     Test that a Replace without a token, an update of an unknown repository and clearing the token while a
    ///     user name remains on a token-free repository are handled by the rules.
    /// </summary>
    [Fact]
    public async Task RepositoryService_UpdateAsync_InvalidRequests_ThrowRules()
    {
        // Arrange: a fixture with a private repository
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await f.Service.CreateAsync("repo", "https://example.com/r.git", "svc", Secret, _admin, cancellationToken);

        // Act: replace with no token, update an unknown repository, and drop the user name while keeping the token
        var empty = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(created.Id, "repo", "https://example.com/r.git", "svc", TokenChange.Replace(string.Empty), _admin, cancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(99, "repo", "https://example.com/r.git", null, TokenChange.Keep, _admin, cancellationToken));
        var noUser = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(created.Id, "repo", "https://example.com/r.git", null, TokenChange.Keep, _admin, cancellationToken));

        // Assert: the rules are named
        Assert.Equal(DomainRule.RepositoryCredentialInvalid, empty.Rule);
        Assert.Equal(DomainRule.RepositoryNotFound, unknown.Rule);
        Assert.Equal(DomainRule.RepositoryCredentialInvalid, noUser.Rule);
    }

    /// <summary>
    ///     Test that ordinary users see only the id and name of enabled repositories, and a disabled repository is
    ///     neither listed nor available to the git layer.
    /// </summary>
    [Fact]
    public async Task RepositoryService_DisabledRepository_IsNotSelectable()
    {
        // Arrange: a fixture with two repositories, one of them disabled
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var open = await f.Service.CreateAsync("open", "https://example.com/o.git", "svc", Secret, _admin, cancellationToken);
        var closed = await f.Service.CreateAsync("closed", "https://example.com/c.git", null, null, _admin, cancellationToken);
        await f.Service.SetEnabledAsync(closed.Id, false, _admin, cancellationToken);

        // Act: list what an ordinary user may choose and try to get access to both
        var summaries = await f.Service.ListEnabledAsync(cancellationToken);
        var access = await f.Service.GetAccessAsync(open.Id, cancellationToken);
        var refused = await Assert.ThrowsAsync<DomainRuleException>(() => f.Service.GetAccessAsync(closed.Id, cancellationToken));
        var all = await f.Service.ListAsync(_admin, cancellationToken);

        // Assert: only the enabled one is selectable, the access carries the decrypted token, the administrator sees both
        Assert.Equal([new RepositorySummary(open.Id, "open")], summaries);
        Assert.Equal(Secret, access.Token);
        Assert.Equal("svc", access.Username);
        Assert.DoesNotContain(Secret, access.ToString(), StringComparison.Ordinal);
        Assert.Equal(DomainRule.RepositoryDisabled, refused.Rule);
        Assert.Equal(2, all.Count);
    }

    /// <summary>
    ///     Test that a token that can no longer be decrypted (the key ring was lost) is reported by name, without
    ///     the stored text, and an unknown repository is reported as missing.
    /// </summary>
    [Fact]
    public async Task RepositoryService_GetAccessAsync_UnreadableToken_ThrowsCredentialsUnreadable()
    {
        // Arrange: a fixture with a private repository and a protector that lost its key
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await f.Service.CreateAsync("repo", "https://example.com/r.git", "svc", Secret, _admin, cancellationToken);
        f.Protector.Unreadable = true;

        // Act: get access to it and to an unknown repository
        var unreadable = await Assert.ThrowsAsync<DomainRuleException>(() => f.Service.GetAccessAsync(created.Id, cancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => f.Service.GetAccessAsync(42, cancellationToken));

        // Assert: the rules are named and the message names the repository
        Assert.Equal(DomainRule.CredentialsUnreadable, unreadable.Rule);
        Assert.Contains("'repo'", unreadable.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, unreadable.Message, StringComparison.Ordinal);
        Assert.Equal(DomainRule.RepositoryNotFound, unknown.Rule);
    }

    /// <summary>
    ///     Test that an update that lands between the two reads of <c>GetAccessAsync</c> cannot pair the location of
    ///     one version with the token of another: the update waits for the read, and the next read sees it whole.
    /// </summary>
    [Fact]
    public async Task RepositoryService_GetAccessAsync_ConcurrentUpdate_ReturnsConsistentPair()
    {
        // Arrange: a private repository and a store that starts an update between the metadata read and the token read
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await f.Service.CreateAsync("repo", "https://example.com/old.git", "svc", Secret, _admin, cancellationToken);
        Task<SourceRepository>? update = null;
        f.Store.BeforeGetEncryptedToken = () =>
            update = f.Service.UpdateAsync(created.Id, "repo", "https://example.com/new.git", "svc", TokenChange.Replace("n3w-T0ken-value"), _admin, cancellationToken);

        // Act: read the access while the update is started, then read again after the update finished
        var during = await f.Service.GetAccessAsync(created.Id, cancellationToken);
        Assert.NotNull(update);
        await update;
        var after = await f.Service.GetAccessAsync(created.Id, cancellationToken);

        // Assert: each read has the location and token of the same version
        Assert.Equal("https://example.com/old.git", during.Location);
        Assert.Equal(Secret, during.Token);
        Assert.Equal("https://example.com/new.git", after.Location);
        Assert.Equal("n3w-T0ken-value", after.Token);
    }

    /// <summary>
    ///     Test that a token that is the same text as the repository name or the user name is refused when a
    ///     repository is registered, because both are shown in messages and the audit trail; a different case is allowed.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <param name="username">The user name.</param>
    /// <param name="token">The token.</param>
    /// <param name="refused">Whether the registration must be refused.</param>
    [Theory]
    [InlineData("tok-1234", "svc", "tok-1234", true)]
    [InlineData("repo", "tok-1234", "tok-1234", true)]
    [InlineData("repo", "svc", "svc", true)]
    [InlineData("Tok-1234", "svc", "tok-1234", false)]
    [InlineData("repo", "SVC", "svc", false)]
    public async Task RepositoryService_CreateAsync_TokenEqualsNameOrUsername_ThrowsRepositoryCredentialInvalid(
        string name,
        string username,
        string token,
        bool refused)
    {
        // Arrange: a fixture
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: register the repository
        var attempt = () => f.Service.CreateAsync(name, "https://example.com/r.git", username, token, _admin, cancellationToken);

        // Assert: refused with the credential rule and nothing stored, or registered (the match is case-sensitive)
        if (refused)
        {
            var exception = await Assert.ThrowsAsync<DomainRuleException>(attempt);
            Assert.Equal(DomainRule.RepositoryCredentialInvalid, exception.Rule);
            Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
            Assert.Empty(f.Store.Audits);
        }
        else
        {
            Assert.True((await attempt()).HasToken);
        }
    }

    /// <summary>
    ///     Test that an update that replaces the token, or keeps it while renaming the repository or changing the user
    ///     name, is refused when the token is the same text as the new name or user name; nothing is changed.
    /// </summary>
    [Fact]
    public async Task RepositoryService_UpdateAsync_TokenEqualsNameOrUsername_ThrowsRepositoryCredentialInvalid()
    {
        // Arrange: a private repository whose stored token is Secret
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        const string location = "https://example.com/r.git";
        var created = await f.Service.CreateAsync("repo", location, "svc", Secret, _admin, cancellationToken);
        var before = f.Store.StoredToken(created.Id);

        // Act: replace the token with the repository name, with the user name, and keep the token while renaming
        // the repository or the user to the token
        var replaceName = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(created.Id, "repo", location, "svc", TokenChange.Replace("repo"), _admin, cancellationToken));
        var replaceUser = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(created.Id, "repo", location, "svc", TokenChange.Replace("svc"), _admin, cancellationToken));
        var keepRenamed = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(created.Id, Secret, location, "svc", TokenChange.Keep, _admin, cancellationToken));
        var keepUser = await Assert.ThrowsAsync<DomainRuleException>(
            () => f.Service.UpdateAsync(created.Id, "repo", location, Secret, TokenChange.Keep, _admin, cancellationToken));
        var unchanged = await f.Service.GetAccessAsync(created.Id, cancellationToken);

        // Assert: all four are refused with the credential rule, without the token in the message, and nothing changed
        foreach (var exception in new[] { replaceName, replaceUser, keepRenamed, keepUser })
        {
            Assert.Equal(DomainRule.RepositoryCredentialInvalid, exception.Rule);
            Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, f.Store.StoredToken(created.Id));
        Assert.Equal("repo", unchanged.Name);
        Assert.Equal("svc", unchanged.Username);
        Assert.Single(f.Store.Audits);
    }

    /// <summary>
    ///     Test that renaming a repository whose stored token can no longer be decrypted is not blocked by the
    ///     comparison (the token cannot be compared and is useless until replaced), and that clearing the token
    ///     while renaming to the old token text is allowed.
    /// </summary>
    [Fact]
    public async Task RepositoryService_UpdateAsync_KeepUnreadableOrClearedToken_DoesNotCompare()
    {
        // Arrange: a private repository whose protector lost its key
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var created = await f.Service.CreateAsync("repo", "https://example.com/r.git", "svc", Secret, _admin, cancellationToken);
        f.Protector.Unreadable = true;

        // Act: rename it while keeping the unreadable token, then clear the token while renaming to the old text
        var renamed = await f.Service.UpdateAsync(created.Id, "renamed", "https://example.com/r.git", "svc", TokenChange.Keep, _admin, cancellationToken);
        var cleared = await f.Service.UpdateAsync(created.Id, Secret, "https://example.com/r.git", null, TokenChange.Clear, _admin, cancellationToken);

        // Assert: both succeed
        Assert.Equal("renamed", renamed.Name);
        Assert.Equal(Secret, cleared.Name);
        Assert.False(cleared.HasToken);
    }

    /// <summary>
    ///     Test that the token change and access types never show a token in their text form.
    /// </summary>
    [Fact]
    public void RepositoryService_TokenTypes_ToStringHidesToken()
    {
        // Arrange: a change and an access holding the secret
        var change = TokenChange.Replace(Secret);
        var access = new RepositoryAccess(1, "repo", "https://example.com/r.git", "svc", Secret);

        // Act: format them
        var texts = new[] { change.ToString(), access.ToString() };

        // Assert: the secret is not in either
        Assert.All(texts, text => Assert.DoesNotContain(Secret, text, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that null arguments are refused.
    /// </summary>
    [Fact]
    public async Task RepositoryService_NullArguments_ThrowArgumentNull()
    {
        // Arrange: a fixture
        var f = await CreateAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act and Assert: a null service dependency or argument is refused
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(null!, f.Protector, f.Users, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(f.Store, null!, f.Users, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(f.Store, f.Protector, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new RepositoryService(f.Store, f.Protector, f.Users, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Service.CreateAsync(null!, "x", null, null, _admin, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Service.CreateAsync("x", null!, null, null, _admin, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Service.CreateAsync("x", "y", null, null, null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Service.ListAsync(null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Service.SetEnabledAsync(1, true, null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Service.UpdateAsync(1, "x", "y", null, null!, _admin, cancellationToken));
    }
}
