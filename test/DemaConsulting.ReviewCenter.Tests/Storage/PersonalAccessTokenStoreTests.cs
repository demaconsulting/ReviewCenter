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
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for <see cref="PersonalAccessTokenStore"/> and the token tables of <see cref="ReviewCenterDbContext"/>
///     against a real SQLite database file.
/// </summary>
public class PersonalAccessTokenStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a token for the tests.
    /// </summary>
    /// <param name="userId">The owner.</param>
    /// <param name="hash">The token hash.</param>
    /// <param name="created">The creation time.</param>
    /// <returns>The token, ready to add.</returns>
    private static PersonalAccessToken NewToken(int userId, string hash, DateTimeOffset? created = null)
    {
        return new PersonalAccessToken
        {
            Id = 9001, // supplied identities must be ignored; the store assigns its own
            UserId = userId,
            Name = "build server",
            DisplayId = hash[..8].ToUpperInvariant(),
            TokenHash = hash,
            Created = created ?? _created,
            Expires = (created ?? _created).AddDays(90)
        };
    }

    /// <summary>
    ///     Builds an audit draft for a token change.
    /// </summary>
    /// <param name="token">The token the change is about.</param>
    /// <param name="action">The audit action.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(PersonalAccessToken token, string action)
    {
        return AuditDraft.By(new Actor("ann", "Ann"), action, $"token '{token.Name}' ({token.DisplayId})");
    }

    /// <summary>
    ///     Creates the user <c>ann</c> in the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> AddUserAsync(TestStore db, string username = "ann")
    {
        var user = await db.Users.AddAsync(
            new User { Username = username, DisplayName = username, PasswordHash = "hash", Created = _created },
            TestContext.Current.CancellationToken);
        return user.Id;
    }

    /// <summary>
    ///     A hash value of the shape the service stores (64 lowercase hex characters) with a distinguishing start.
    /// </summary>
    /// <param name="start">The first character.</param>
    /// <returns>The hash.</returns>
    private static string Hash(char start) => start + new string('0', 63);

    /// <summary>
    ///     Test that a token round-trips with every field, a database-assigned identity and its audit entry saved
    ///     in the same transaction, and that nothing but the hash of the value is in the row.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenStore_AddAsync_Token_RoundTripsAndAudits()
    {
        // Arrange: a database with a user
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: add a token and read it back through the list
        var added = await db.Tokens.AddAsync(NewToken(userId, Hash('a')), t => Draft(t, "token.created"), cancellationToken);
        var read = Assert.Single(await db.Tokens.ListForUserAsync(userId, cancellationToken));

        // Assert: the identity is the database's and every value survived
        Assert.NotEqual(9001, added.Id);
        Assert.Equal(added.Id, read.Id);
        Assert.Equal(("build server", "A0000000", Hash('a')), (read.Name, read.DisplayId, read.TokenHash));
        Assert.Equal((_created, _created.AddDays(90), null, null), (read.Created, read.Expires, read.LastUsed, read.Revoked));

        // Assert: the audit entry exists and carries the identity-assigned token
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var entry = Assert.Single(await context.AuditEntries.Where(e => e.Action == "token.created").ToListAsync(cancellationToken));
        Assert.Contains("A0000000", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that two tokens with the same hash are refused by the database and the audit entry is not kept.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenStore_AddAsync_DuplicateHash_IsRefusedAndNothingIsAudited()
    {
        // Arrange: a database with a user and one token
        using var db = new TestStore();
        var userId = await AddUserAsync(db);
        var cancellationToken = TestContext.Current.CancellationToken;
        await db.Tokens.AddAsync(NewToken(userId, Hash('a')), t => Draft(t, "token.created"), cancellationToken);

        // Act: add another token with the same hash
        await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Tokens.AddAsync(NewToken(userId, Hash('a')), t => Draft(t, "token.created"), cancellationToken));

        // Assert: one token and one audit entry remain
        Assert.Single(await db.Tokens.ListForUserAsync(userId, cancellationToken));
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        Assert.Equal(1, await context.AuditEntries.CountAsync(e => e.Action == "token.created", cancellationToken));
    }

    /// <summary>
    ///     Test that the lookup by hash finds exactly the token with that hash, with its owner, and nothing else.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenStore_FindByHashAsync_Hash_FindsTokenWithOwner()
    {
        // Arrange: two users with a token each
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var cancellationToken = TestContext.Current.CancellationToken;
        await db.Tokens.AddAsync(NewToken(ann, Hash('a')), t => Draft(t, "token.created"), cancellationToken);
        await db.Tokens.AddAsync(NewToken(bob, Hash('b')), t => Draft(t, "token.created"), cancellationToken);

        // Act: look up by each hash, and by an unknown hash
        var foundA = await db.Tokens.FindByHashAsync(Hash('a'), cancellationToken);
        var foundB = await db.Tokens.FindByHashAsync(Hash('b'), cancellationToken);
        var unknown = await db.Tokens.FindByHashAsync(Hash('c'), cancellationToken);

        // Assert: each lookup returns its own token with the owner loaded; the unknown hash finds nothing
        Assert.Equal("ann", foundA?.User?.Username);
        Assert.Equal("bob", foundB?.User?.Username);
        Assert.Null(unknown);
    }

    /// <summary>
    ///     Test that the list holds only the tokens of the user, newest first, including revoked ones.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenStore_ListForUserAsync_Tokens_AreOwnedAndNewestFirst()
    {
        // Arrange: tokens created at three times for ann, and one for bob
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var cancellationToken = TestContext.Current.CancellationToken;
        await db.Tokens.AddAsync(NewToken(ann, Hash('a'), _created), t => Draft(t, "token.created"), cancellationToken);
        var middle = await db.Tokens.AddAsync(NewToken(ann, Hash('b'), _created.AddDays(1)), t => Draft(t, "token.created"), cancellationToken);
        await db.Tokens.AddAsync(NewToken(ann, Hash('c'), _created.AddDays(2)), t => Draft(t, "token.created"), cancellationToken);
        await db.Tokens.AddAsync(NewToken(bob, Hash('d'), _created.AddDays(3)), t => Draft(t, "token.created"), cancellationToken);
        await db.Tokens.RevokeAsync(middle.Id, ann, _created.AddDays(4), t => Draft(t, "token.revoked"), cancellationToken);

        // Act: list ann's tokens
        var list = await db.Tokens.ListForUserAsync(ann, cancellationToken);

        // Assert: three tokens of ann, newest first, the revoked one included
        Assert.Equal([Hash('c'), Hash('b'), Hash('a')], list.Select(t => t.TokenHash));
        Assert.NotNull(list[1].Revoked);
    }

    /// <summary>
    ///     Test that revoking sets the time and writes the audit entry, and that a second revocation, another
    ///     user's token and a missing token change nothing and write no entry.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenStore_RevokeAsync_OwnToken_RevokesOnceAndAudits()
    {
        // Arrange: a token of ann, and bob
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await db.Tokens.AddAsync(NewToken(ann, Hash('a')), t => Draft(t, "token.created"), cancellationToken);
        var when = _created.AddHours(1);

        // Act: bob tries first, then a missing token, then ann revokes twice
        var byOther = await db.Tokens.RevokeAsync(token.Id, bob, when, t => Draft(t, "token.revoked"), cancellationToken);
        var missing = await db.Tokens.RevokeAsync(999, ann, when, t => Draft(t, "token.revoked"), cancellationToken);
        var first = await db.Tokens.RevokeAsync(token.Id, ann, when, t => Draft(t, "token.revoked"), cancellationToken);
        var second = await db.Tokens.RevokeAsync(token.Id, ann, when.AddHours(1), t => Draft(t, "token.revoked"), cancellationToken);

        // Assert: only the first owner revocation worked, it kept its time, and exactly one revoke entry exists
        Assert.Null(byOther);
        Assert.Null(missing);
        Assert.Equal(when, first?.Revoked);
        Assert.Null(second);
        Assert.Equal(when, (await db.Tokens.FindByHashAsync(Hash('a'), cancellationToken))?.Revoked);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        Assert.Equal(1, await context.AuditEntries.CountAsync(e => e.Action == "token.revoked", cancellationToken));
    }

    /// <summary>
    ///     Test that recording a use changes only the last-used time: no audit entry is written and a revocation
    ///     is not undone.
    /// </summary>
    [Fact]
    public async Task PersonalAccessTokenStore_TouchAsync_Token_ChangesOnlyLastUsed()
    {
        // Arrange: a revoked token
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await db.Tokens.AddAsync(NewToken(ann, Hash('a')), t => Draft(t, "token.created"), cancellationToken);
        await db.Tokens.RevokeAsync(token.Id, ann, _created.AddHours(1), t => Draft(t, "token.revoked"), cancellationToken);
        var auditCountBefore = await CountAuditAsync(db);

        // Act: record a use
        var written = await db.Tokens.TouchAsync(token.Id, null, _created.AddHours(2), cancellationToken);
        var read = (await db.Tokens.FindByHashAsync(Hash('a'), cancellationToken))!;

        // Assert: the use is recorded, the revocation and every other value is untouched, no audit entry was added
        Assert.True(written);
        Assert.Equal(_created.AddHours(2), read.LastUsed);
        Assert.Equal(_created.AddHours(1), read.Revoked);
        Assert.Equal(_created.AddDays(90), read.Expires);
        Assert.Equal(auditCountBefore, await CountAuditAsync(db));
    }

    /// <summary>
    ///     Test that several parallel uses that all saw the same last-used value (never used, or a stale time) write
    ///     exactly once, and that a use that saw an older value than the stored one writes nothing.
    /// </summary>
    /// <param name="stale">Whether the token was used before; <c>false</c> for a token never used.</param>
    [Theory(Timeout = 60000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersonalAccessTokenStore_TouchAsync_ParallelSameExpected_WritesOnce(bool stale)
    {
        // Arrange: a token that was never used, or used long ago
        using var db = new TestStore();
        var ann = await AddUserAsync(db);
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await db.Tokens.AddAsync(NewToken(ann, Hash('a')), t => Draft(t, "token.created"), cancellationToken);
        DateTimeOffset? seen = stale ? _created.AddHours(1) : null;
        if (stale)
        {
            Assert.True(await db.Tokens.TouchAsync(token.Id, null, seen!.Value, cancellationToken));
        }

        // Act: eight parallel uses that all read the same last-used value
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(
            i => Task.Run(() => db.Tokens.TouchAsync(token.Id, seen, _created.AddHours(2 + i), cancellationToken), cancellationToken)));
        var read = (await db.Tokens.FindByHashAsync(Hash('a'), cancellationToken))!;

        // Assert: exactly one wrote, and its moment is the stored one
        Assert.Equal(1, results.Count(r => r));
        Assert.NotNull(read.LastUsed);
        Assert.NotEqual(seen, read.LastUsed);

        // A late caller that still holds the old value changes nothing
        Assert.False(await db.Tokens.TouchAsync(token.Id, seen, _created.AddHours(20), cancellationToken));
        Assert.Equal(read.LastUsed, (await db.Tokens.FindByHashAsync(Hash('a'), cancellationToken))!.LastUsed);
    }

    /// <summary>
    ///     Test that the database tables carry the token columns, the unique index on the hash and the creator
    ///     columns of a review, and that a review remembers who created it and how.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Model_HasTokenTableAndReviewCreatorColumns()
    {
        // Arrange: a database, and a review created through the API
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-1",
                Title = "Title",
                State = ReviewState.Draft,
                Created = _created,
                CreatedBy = "ann",
                CreatedViaApi = true,
                ContentFingerprint = Fingerprint.Compute([TestContent.A]),
                Files =
                [
                    new ReviewFile
                    {
                        ProjectId = project.Id,
                        ReviewId = "REV-1",
                        Path = "a.txt",
                        Content = TestContent.A,
                        Size = TestContent.A.Length,
                        Sha256 = Fingerprint.ComputeFileHash(TestContent.A)
                    }
                ]
            },
            cancellationToken);

        // Act: read the review and the model of the token table
        var review = await db.Store.GetReviewAsync(project.Id, "REV-1", cancellationToken);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var entity = context.Model.FindEntityType(typeof(PersonalAccessToken))!;
        var hashIndex = entity.GetIndexes().Single(i => i.Properties.Single().Name == nameof(PersonalAccessToken.TokenHash));

        // Assert: the review remembers its creator; the hash index is unique; the table has no column for the value
        Assert.Equal(("ann", true), (review?.CreatedBy, review?.CreatedViaApi));
        Assert.True(hashIndex.IsUnique);
        Assert.Equal(
            ["Created", "DisplayId", "Expires", "Id", "LastUsed", "Name", "Revoked", "TokenHash", "UserId"],
            entity.GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     Counts the audit entries of a database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The number of entries.</returns>
    private static async Task<int> CountAuditAsync(TestStore db)
    {
        await using var context = await db.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await context.AuditEntries.CountAsync(TestContext.Current.CancellationToken);
    }
}
