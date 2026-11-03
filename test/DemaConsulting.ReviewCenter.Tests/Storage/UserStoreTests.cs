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
using DemaConsulting.ReviewCenter.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for <see cref="UserStore"/> against a real SQLite database file.
/// </summary>
public class UserStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Opens a new database file and returns a store over it.
    /// </summary>
    /// <param name="provider">The provider that owns the database factory; dispose it when done.</param>
    /// <returns>The store.</returns>
    private static UserStore Open(out ServiceProvider provider)
    {
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        return new UserStore(factory, new AuditAppender(TimeProvider.System));
    }

    /// <summary>
    ///     Builds a user for the tests.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="issuer">The issuer of the external identity, or <c>null</c> for a local user.</param>
    /// <param name="subject">The subject of the external identity, or <c>null</c> for a local user.</param>
    /// <returns>The user.</returns>
    private static User NewUser(string username, string? issuer = null, string? subject = null)
    {
        return new User
        {
            ExternalIssuer = issuer,
            ExternalSubject = subject,
            Id = 9001, // supplied identities must be ignored; the store assigns its own
            Username = username,
            DisplayName = "Display " + username,
            IsSystemAdmin = true,
            Enabled = true,
            PasswordHash = "hash",
            FailedLoginCount = 2,
            LockoutUntil = _created.AddMinutes(5),
            LastSignInUtc = _created.AddHours(1),
            Created = _created
        };
    }

    /// <summary>
    ///     Test that a user round-trips with every field and a database-assigned identity.
    /// </summary>
    [Fact]
    public async Task UserStore_AddAsync_User_RoundTripsEveryField()
    {
        // Arrange: a store
        var store = Open(out var provider);
        using var _ = provider;

        // Act: add a user and read it back
        var added = await store.AddAsync(NewUser("Ann"), TestContext.Current.CancellationToken);
        var read = await store.GetByUsernameAsync("Ann", TestContext.Current.CancellationToken);

        // Assert: the identity is the database's, and every value survived
        Assert.NotNull(read);
        Assert.NotEqual(9001, added.Id);
        Assert.Equal(added.Id, read.Id);
        Assert.Equal(("Ann", "Display Ann", true, true), (read.Username, read.DisplayName, read.IsSystemAdmin, read.Enabled));
        Assert.Equal(("hash", 2, _created.AddMinutes(5), _created), (read.PasswordHash, read.FailedLoginCount, read.LockoutUntil, read.Created));
        Assert.Equal(_created.AddHours(1), read.LastSignInUtc);
    }

    /// <summary>
    ///     Test that a user who never signed in has no last sign-in, and that an update stores and clears it.
    /// </summary>
    [Fact]
    public async Task UserStore_UpdateAsync_LastSignIn_IsStoredAndClearedAndNullByDefault()
    {
        // Arrange: a store with a user who never signed in
        var store = Open(out var provider);
        using var _ = provider;
        var fresh = NewUser("Ann");
        fresh.LastSignInUtc = null;
        await store.AddAsync(fresh, TestContext.Current.CancellationToken);
        var user = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!;
        var initial = user.LastSignInUtc;

        // Act: store a sign-in time, read it, then clear it
        user.LastSignInUtc = _created.AddDays(2);
        await store.UpdateAsync(user, TestContext.Current.CancellationToken);
        var stored = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!.LastSignInUtc;
        user.LastSignInUtc = null;
        await store.UpdateAsync(user, TestContext.Current.CancellationToken);
        var cleared = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!.LastSignInUtc;

        // Assert
        Assert.Null(initial);
        Assert.Equal(_created.AddDays(2), stored);
        Assert.Null(cleared);
    }

    /// <summary>
    ///     Test that the flag that a user must change the password is stored when the user is added and cleared by an
    ///     update.
    /// </summary>
    [Fact]
    public async Task UserStore_UpdateAsync_MustChangePassword_IsStoredAndCleared()
    {
        // Arrange: a store with a user who must change the password
        var store = Open(out var provider);
        using var _ = provider;
        var confined = NewUser("Ann");
        confined.MustChangePassword = true;
        await store.AddAsync(confined, TestContext.Current.CancellationToken);
        var user = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!;
        var initial = user.MustChangePassword;

        // Act: clear the flag and read the user again
        user.MustChangePassword = false;
        await store.UpdateAsync(user, TestContext.Current.CancellationToken);
        var cleared = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!.MustChangePassword;

        // Assert: the flag was stored and then cleared
        Assert.True(initial);
        Assert.False(cleared);
    }

    /// <summary>
    ///     Test that a user name that differs only in case is refused as taken, and that lookups ignore case.
    /// </summary>
    [Fact]
    public async Task UserStore_AddAsync_UsernameOtherCase_ThrowsUserNameTakenAndLookupIgnoresCase()
    {
        // Arrange: a store with a user
        var store = Open(out var provider);
        using var _ = provider;
        await store.AddAsync(NewUser("Ann"), TestContext.Current.CancellationToken);

        // Act: add a user whose name differs in case, and look the first one up in another case
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => store.AddAsync(NewUser("ANN"), TestContext.Current.CancellationToken));
        var found = await store.GetByUsernameAsync("aNN", TestContext.Current.CancellationToken);
        var missing = await store.GetByUsernameAsync("bob", TestContext.Current.CancellationToken);

        // Assert: the rule is named, the lookup finds the original, and an unknown user is null
        Assert.Equal(DomainRule.UserNameTaken, exception.Rule);
        Assert.Equal("Ann", found?.Username);
        Assert.Null(missing);
        Assert.Single(await store.ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an update stores the changeable fields.
    /// </summary>
    [Fact]
    public async Task UserStore_UpdateAsync_ChangedFields_AreStored()
    {
        // Arrange: a store with a user
        var store = Open(out var provider);
        using var _ = provider;
        await store.AddAsync(NewUser("Ann"), TestContext.Current.CancellationToken);
        var user = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!;

        // Act: change the fields and read the user again
        user.IsSystemAdmin = false;
        user.Enabled = false;
        user.PasswordHash = "other";
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await store.UpdateAsync(user, TestContext.Current.CancellationToken);
        var read = (await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!;

        // Assert: all changes are stored
        Assert.Equal((false, false, "other", 0), (read.IsSystemAdmin, read.Enabled, read.PasswordHash, read.FailedLoginCount));
        Assert.Null(read.LockoutUntil);
    }

    /// <summary>
    ///     Test that users are listed ordered by user name.
    /// </summary>
    [Fact]
    public async Task UserStore_ListAsync_Users_AreOrderedByUsername()
    {
        // Arrange: a store with users added out of order
        var store = Open(out var provider);
        using var _ = provider;
        await store.AddAsync(NewUser("cy"), TestContext.Current.CancellationToken);
        await store.AddAsync(NewUser("ann"), TestContext.Current.CancellationToken);
        await store.AddAsync(NewUser("bob"), TestContext.Current.CancellationToken);

        // Act: list the users
        var listed = await store.ListAsync(TestContext.Current.CancellationToken);

        // Assert: ordered by user name
        Assert.Equal(["ann", "bob", "cy"], listed.Select(user => user.Username));
    }

    /// <summary>
    ///     Test that an account of an identity provider round-trips its issuer and subject, is found by the exact
    ///     pair only, and that a local user has none.
    /// </summary>
    [Fact]
    public async Task UserStore_GetByExternalAsync_FindsOnlyTheExactPair()
    {
        // Arrange: a store with a local user and a user of a provider
        var store = Open(out var provider);
        using var _ = provider;
        await store.AddAsync(NewUser("ann"), TestContext.Current.CancellationToken);
        var external = NewUser("olive", "https://idp.example/", "Sub-1");
        var added = await store.AddAsync(external, TestContext.Current.CancellationToken);

        // Act
        var found = await store.GetByExternalAsync("https://idp.example/", "Sub-1", TestContext.Current.CancellationToken);
        var otherCase = await store.GetByExternalAsync("https://idp.example/", "sub-1", TestContext.Current.CancellationToken);
        var otherIssuer = await store.GetByExternalAsync("https://idp.example", "Sub-1", TestContext.Current.CancellationToken);
        var local = await store.GetByUsernameAsync("ann", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(added.Id, found?.Id);
        Assert.True(found?.IsExternal);
        Assert.Equal(("https://idp.example/", "Sub-1"), (found?.ExternalIssuer, found?.ExternalSubject));
        Assert.Null(otherCase);
        Assert.Null(otherIssuer);
        Assert.False(local?.IsExternal);
    }

    /// <summary>
    ///     Test that two accounts of the same issuer and subject are refused by the database, so a race between two
    ///     first sign-ins cannot create two accounts, while many local users (without an identity) are allowed.
    /// </summary>
    [Fact]
    public async Task UserStore_AddAsync_SameExternalIdentityTwice_IsRefused()
    {
        // Arrange: a store with a user of a provider and two local users
        var store = Open(out var provider);
        using var _ = provider;
        await store.AddAsync(NewUser("ann"), TestContext.Current.CancellationToken);
        await store.AddAsync(NewUser("bob"), TestContext.Current.CancellationToken);
        await store.AddAsync(NewUser("olive", "https://idp.example/", "sub-1"), TestContext.Current.CancellationToken);
        var second = NewUser("olive-2", "https://idp.example/", "sub-1");

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => store.AddAsync(second, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.UserNameTaken, exception.Rule);
        Assert.Equal(3, (await store.ListAsync(TestContext.Current.CancellationToken)).Count);
    }

    /// <summary>
    ///     The statement that makes the database refuse every audit entry.
    /// </summary>
    private const string RefuseAuditSql =
        "CREATE TRIGGER refuse_audit BEFORE INSERT ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit refused'); END;";

    /// <summary>
    ///     Builds the audit draft of a user change.
    /// </summary>
    /// <param name="action">The audit action.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string action) => AuditDraft.By(new Actor("adm", "Adm"), action, "detail");

    /// <summary>
    ///     Runs one SQL statement against the database of the test store.
    /// </summary>
    /// <param name="db">The test database.</param>
    /// <param name="sql">The statement.</param>
    /// <returns>A task that completes when the statement ran.</returns>
    private static async Task ExecuteSqlAsync(TestStore db, string sql)
    {
        await using var context = await db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Counts the audit entries of a database.
    /// </summary>
    /// <param name="db">The test database.</param>
    /// <returns>The number of entries.</returns>
    private static async Task<int> CountAuditAsync(TestStore db)
    {
        await using var context = await db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await context.AuditEntries.CountAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that a new user that is refused because its name is taken leaves no audit entry, besides the entry of
    ///     the user that holds the name.
    /// </summary>
    [Fact]
    public async Task UserStore_AddAsync_NameTakenWithDraft_StoresNoAuditEntry()
    {
        // Arrange: a user that was added with its audit entry
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        await db.Users.AddAsync(NewUser("Ann"), u => Draft("user.created"), cancellationToken);

        // Act: add a user whose name differs only in case, with an audit draft
        await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Users.AddAsync(NewUser("ANN"), u => Draft("user.created"), cancellationToken));

        // Assert: only the entry of the first user exists
        Assert.Equal(1, await CountAuditAsync(db));
    }

    /// <summary>
    ///     Test that a user stays as it was when the audit entry of its update cannot be stored.
    /// </summary>
    [Fact]
    public async Task UserStore_UpdateAsync_AuditInsertFails_LeavesTheUserUnchanged()
    {
        // Arrange: a user, then a database that refuses every audit entry
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await db.Users.AddAsync(NewUser("ann"), u => Draft("user.created"), cancellationToken);
        await ExecuteSqlAsync(db, RefuseAuditSql);

        // Act: disable the user with an audit draft
        user.Enabled = false;
        await Assert.ThrowsAnyAsync<Exception>(() => db.Users.UpdateAsync(user, Draft("user.disabled"), cancellationToken));
        await ExecuteSqlAsync(db, "DROP TRIGGER refuse_audit");

        // Assert: the user is still enabled and only the creation entry exists
        Assert.True((await db.Users.GetByUsernameAsync("ann", cancellationToken))!.Enabled);
        Assert.Equal(1, await CountAuditAsync(db));
    }

    /// <summary>
    ///     Test that adding a user to a project stores no account and no membership when the audit entry cannot be
    ///     stored.
    /// </summary>
    [Fact]
    public async Task UserStore_AddToProjectAsync_AuditInsertFails_StoresNothing()
    {
        // Arrange: a project, then a database that refuses every audit entry
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var auditCountBefore = await CountAuditAsync(db);
        await ExecuteSqlAsync(db, RefuseAuditSql);

        // Act: add a new user to the project
        await Assert.ThrowsAnyAsync<Exception>(
            () => db.Users.AddToProjectAsync(
                NewUser("newbie"),
                project.Id,
                ProjectRole.User,
                u => Draft("project.account-created"),
                cancellationToken));
        await ExecuteSqlAsync(db, "DROP TRIGGER refuse_audit");

        // Assert: no account, no membership, and no entry
        Assert.Null(await db.Users.GetByUsernameAsync("newbie", cancellationToken));
        Assert.Empty(await db.Store.ListMembershipsAsync(project.Id, cancellationToken));
        Assert.Equal(auditCountBefore, await CountAuditAsync(db));
    }
}
