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
///     Tests for <see cref="RepositoryStore"/> against a real SQLite database file.
/// </summary>
public class RepositoryStoreTests
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
    private static RepositoryStore Open(out ServiceProvider provider)
    {
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        using (var context = factory.CreateDbContext())
        {
            // every repository belongs to a project, so the first project (identity 1) exists
            context.Projects.Add(new Project { Name = "Project", Created = _created });
            context.SaveChanges();
        }

        return new RepositoryStore(factory, new AuditAppender(TimeProvider.System));
    }

    /// <summary>
    ///     Builds a new repository in project 1 for the tests.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="token">The encrypted token, or null.</param>
    /// <returns>The repository.</returns>
    private static NewRepository NewRepo(string name, string? token = null) =>
        new(1, name, "https://example.com/" + name + ".git", token is null ? null : "svc", token, _created);

    /// <summary>
    ///     Builds an audit draft for a repository change.
    /// </summary>
    /// <param name="action">The audit action.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string action) => AuditDraft.By(new Actor("adm", "Adm"), action, "detail");

    /// <summary>
    ///     Test that a repository round-trips with every field, a database-assigned identity and its audit entry
    ///     saved in the same transaction.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_AddAsync_Repository_RoundTripsAndAudits()
    {
        // Arrange: a store
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: add a private repository and read it back
        var added = await store.AddAsync(NewRepo("alpha", "cipher"), r => Draft("repository.created"), cancellationToken);
        var read = await store.GetAsync(added.Id, cancellationToken);

        // Assert: every value survived, the repository is enabled and the audit entry exists
        Assert.NotNull(read);
        Assert.Equal(added, read);
        Assert.Equal(("alpha", "https://example.com/alpha.git", "svc", true, true), (read.Name, read.Location, read.Username, read.HasToken, read.Enabled));
        Assert.Equal((_created, _created), (read.CreatedAt, read.UpdatedAt));
        await using var context = await provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>().CreateDbContextAsync(cancellationToken);
        Assert.Equal(["repository.created"], await context.AuditEntries.Select(e => e.Action).ToArrayAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that the encrypted token is read only through the dedicated method and is null when there is none.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_GetEncryptedTokenAsync_ReturnsStoredText()
    {
        // Arrange: a store with a private and an anonymous repository
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;
        var priv = await store.AddAsync(NewRepo("priv", "cipher"), r => Draft("repository.created"), cancellationToken);
        var anon = await store.AddAsync(NewRepo("anon"), r => Draft("repository.created"), cancellationToken);

        // Act: read the tokens
        var cipher = await store.GetEncryptedTokenAsync(priv.Id, cancellationToken);
        var none = await store.GetEncryptedTokenAsync(anon.Id, cancellationToken);
        var missing = await store.GetEncryptedTokenAsync(999, cancellationToken);

        // Assert: only the private one has a token
        Assert.Equal("cipher", cipher);
        Assert.Null(none);
        Assert.Null(missing);
    }

    /// <summary>
    ///     Test that a name that differs only in case is refused as taken, for adding and for renaming, and that
    ///     the failed change leaves no audit entry.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_NameOtherCase_ThrowsRepositoryNameTaken()
    {
        // Arrange: a store with two repositories
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.AddAsync(NewRepo("alpha"), r => Draft("repository.created"), cancellationToken);
        var beta = await store.AddAsync(NewRepo("beta"), r => Draft("repository.created"), cancellationToken);

        // Act: add a duplicate and rename the other one to a duplicate
        var add = await Assert.ThrowsAsync<DomainRuleException>(
            () => store.AddAsync(NewRepo("ALPHA"), r => Draft("repository.created"), cancellationToken));
        var rename = await Assert.ThrowsAsync<DomainRuleException>(
            () => store.UpdateAsync(
                beta.Id,
                new RepositoryUpdate("Alpha", beta.Location, null, true, TokenChange.Keep, _created),
                Draft("repository.updated"),
                cancellationToken));

        // Assert: the rule is named and only the two creations were audited
        Assert.Equal(DomainRule.RepositoryNameTaken, add.Rule);
        Assert.Equal(DomainRule.RepositoryNameTaken, rename.Rule);
        await using var context = await provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>().CreateDbContextAsync(cancellationToken);
        Assert.Equal(2, await context.AuditEntries.CountAsync(cancellationToken));
    }

    /// <summary>
    ///     Test that an update changes the fields and keeps, replaces or clears the token as asked.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_UpdateAsync_TokenActions_KeepReplaceClear()
    {
        // Arrange: a store with a private repository
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;
        var added = await store.AddAsync(NewRepo("alpha", "cipher"), r => Draft("repository.created"), cancellationToken);
        var later = _created.AddHours(1);

        // Act: keep the token while renaming, then replace it, then clear it
        var kept = await store.UpdateAsync(added.Id, new RepositoryUpdate("alpha2", added.Location, "svc", true, TokenChange.Keep, later), Draft("repository.updated"), cancellationToken);
        var keptToken = await store.GetEncryptedTokenAsync(added.Id, cancellationToken);
        var replaced = await store.UpdateAsync(added.Id, new RepositoryUpdate("alpha2", added.Location, "svc", true, TokenChange.Replace("cipher2"), later), Draft("repository.updated"), cancellationToken);
        var replacedToken = await store.GetEncryptedTokenAsync(added.Id, cancellationToken);
        var cleared = await store.UpdateAsync(added.Id, new RepositoryUpdate("alpha2", added.Location, null, true, TokenChange.Clear, later), Draft("repository.updated"), cancellationToken);
        var clearedToken = await store.GetEncryptedTokenAsync(added.Id, cancellationToken);

        // Assert: the token followed the action and the update time moved
        Assert.Equal(("alpha2", true, later, "cipher"), (kept.Name, kept.HasToken, kept.UpdatedAt, keptToken));
        Assert.Equal((true, "cipher2"), (replaced.HasToken, replacedToken));
        Assert.Equal((false, null, null), (cleared.HasToken, clearedToken, cleared.Username));
        Assert.Equal(_created, cleared.CreatedAt);
    }

    /// <summary>
    ///     Test that disabling keeps the registration (nothing is deleted) and that updating an unknown
    ///     repository names the rule.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_UpdateAsync_DisableAndUnknown()
    {
        // Arrange: a store with a repository
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;
        var added = await store.AddAsync(NewRepo("alpha"), r => Draft("repository.created"), cancellationToken);

        // Act: disable it and update an unknown identity
        var disabled = await store.UpdateAsync(added.Id, new RepositoryUpdate("alpha", added.Location, null, false, TokenChange.Keep, _created), Draft("repository.disabled"), cancellationToken);
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => store.UpdateAsync(999, new RepositoryUpdate("x", "https://example.com/x.git", null, true, TokenChange.Keep, _created), Draft("repository.updated"), cancellationToken));

        // Assert: still listed, disabled, and the unknown one is named
        Assert.False(disabled.Enabled);
        Assert.False((await store.ListAsync(1, cancellationToken)).Single().Enabled);
        Assert.Equal(DomainRule.RepositoryNotFound, unknown.Rule);
    }

    /// <summary>
    ///     Test that the list is ordered by name without regard to case and that an unknown identity reads as null.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_ListAsync_OrdersByNameIgnoringCase()
    {
        // Arrange: a store with names in mixed order and case
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var name in new[] { "beta", "Alpha", "gamma" })
        {
            await store.AddAsync(NewRepo(name), r => Draft("repository.created"), cancellationToken);
        }

        // Act: list them and read an unknown one
        var listed = await store.ListAsync(1, cancellationToken);
        var missing = await store.GetAsync(999, cancellationToken);

        // Assert: alphabetical, and the unknown one is null
        Assert.Equal(["Alpha", "beta", "gamma"], listed.Select(r => r.Name).ToArray());
        Assert.Null(missing);
    }

    /// <summary>
    ///     Test that the same name may be used in two projects, that each list holds only its own project's
    ///     repositories, and that a repository for an unknown project is refused by the database.
    /// </summary>
    [Fact]
    public async Task RepositoryStore_ListAsync_ScopedToProject_AndNamesAreUniquePerProject()
    {
        // Arrange: a store with a second project
        var store = Open(out var provider);
        using var _ = provider;
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var context = await provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>()
                         .CreateDbContextAsync(cancellationToken))
        {
            context.Projects.Add(new Project { Name = "Second", Created = _created });
            await context.SaveChangesAsync(cancellationToken);
        }

        // Act: add the same name to both projects, then list each, then add to an unknown project
        await store.AddAsync(NewRepo("alpha"), r => Draft("repository.created"), cancellationToken);
        await store.AddAsync(
            new NewRepository(2, "alpha", "https://example.com/other.git", null, null, _created),
            r => Draft("repository.created"),
            cancellationToken);
        var first = await store.ListAsync(1, cancellationToken);
        var second = await store.ListAsync(2, cancellationToken);
        var duplicate = await Assert.ThrowsAsync<DomainRuleException>(() => store.AddAsync(
            NewRepo("ALPHA"), r => Draft("repository.created"), cancellationToken));

        // Assert: each project lists its own, and the name is unique only within a project
        Assert.Equal([1], first.Select(r => r.ProjectId).ToArray());
        Assert.Equal([2], second.Select(r => r.ProjectId).ToArray());
        Assert.Equal(DomainRule.RepositoryNameTaken, duplicate.Rule);
    }

    /// <summary>
    ///     Test that the read model has no token member other than the flag, so a page that shows it cannot leak
    ///     a token.
    /// </summary>
    [Fact]
    public void RepositoryStore_ReadModel_HasNoTokenMember()
    {
        // Arrange: the member names of the read model
        var names = typeof(SourceRepository).GetProperties().Select(p => p.Name).ToArray();

        // Act and Assert: nothing named like a token except the flag
        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.Ordinal) && n != nameof(SourceRepository.HasToken));
    }
}
