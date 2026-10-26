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

using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     Subsystem tests for the ContentSources subsystem: the real registry service over the real store, the real
///     credential protector and the real git source reading a real temporary repository.
/// </summary>
public class ContentSourcesSubsystemTests
{
    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     A token used to check that it never leaks.
    /// </summary>
    private const string Secret = "tkn-Subsystem.Secret";

    /// <summary>
    ///     Test the complete path: an administrator registers a repository with a token, the token is stored only
    ///     encrypted, access is built from the registry and git reads the repository through it.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_RegisteredRepository_IsReadThroughTheRealGitSource()
    {
        // Arrange: a local origin, a database, a persisted key ring and the real services
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = Path.Combine(root.Path, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, "readme.txt", "hello\n");
        var first = GitTestSupport.Commit(origin, "first");
        GitTestSupport.Write(origin, "readme.txt", "hello again\n");
        var second = GitTestSupport.Commit(origin, "second");

        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .AddDataProtection().SetApplicationName("SubsystemTests")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root.Path, "keys"))).Services
            .BuildServiceProvider();
        using var _ = provider;
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var cancellationToken = TestContext.Current.CancellationToken;
        await users.CreateAsync("adm", "Adm", UserRole.Admin, Password, cancellationToken);
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var service = new RepositoryService(
            new RepositoryStore(factory, new AuditAppender(TimeProvider.System)),
            new CredentialProtector(provider.GetRequiredService<IDataProtectionProvider>()),
            users,
            clock);
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);

        // Act: register a private https repository and a local one, read the local one through git
        var admin = new Actor("adm", "Adm");
        var priv = await service.CreateAsync("private", "https://example.com/org/r.git", "svc", Secret, admin, cancellationToken);
        var registered = await service.CreateAsync("local", origin, null, null, admin, cancellationToken);
        var privateAccess = await service.GetAccessAsync(priv.Id, cancellationToken);
        var access = await service.GetAccessAsync(registered.Id, cancellationToken);
        await using var session = await source.OpenAsync(access, cancellationToken);
        var head = await session.ResolveRevisionAsync("main", cancellationToken);
        var changes = await session.ListChangesAsync(first, head, cancellationToken);
        var content = await session.ReadBlobAsync(Assert.Single(changes.Changes).HeadBlobSha!, 1024, cancellationToken);

        // Assert: the registry kept the token only as ciphertext, access carries it, and git read the change
        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            var row = await context.Repositories.SingleAsync(r => r.Id == priv.Id, cancellationToken);
            Assert.NotNull(row.EncryptedToken);
            Assert.DoesNotContain(Secret, row.EncryptedToken, StringComparison.Ordinal);
        }

        Assert.Equal(Secret, privateAccess.Token);
        Assert.DoesNotContain(Secret, privateAccess.ToString(), StringComparison.Ordinal);
        Assert.Equal(second, head);
        Assert.Equal("hello again\n", System.Text.Encoding.UTF8.GetString(content));
    }

    /// <summary>
    ///     Test that the review-sets of a repository are read from the configuration file of a real repository
    ///     through the real git source and parser, and that the ancestry of its commits is answered.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_ReviewSets_AreReadFromTheRepositoryConfiguration()
    {
        // Arrange: an origin with a configuration file in its first commit and a change in its second
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = Path.Combine(root.Path, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, ".reviewmark.yaml", "reviews:\n  - id: docs\n    title: Documentation\n    paths:\n      - \"docs/**\"\n");
        GitTestSupport.Write(origin, "docs/a.md", "a\n");
        var first = GitTestSupport.Commit(origin, "first");
        GitTestSupport.Write(origin, "docs/a.md", "b\n");
        var second = GitTestSupport.Commit(origin, "second");
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: read the file at the head through git, parse it, and ask about the ancestry
        await using var session = await source.OpenAsync(new RepositoryAccess(1, "local", origin, null, null), cancellationToken);
        var listing = await session.ListChangesAsync(null, second, cancellationToken);
        var file = Assert.Single(listing.Changes, change => change.Path == ".reviewmark.yaml");
        var sets = new ReviewMarkConfigParser().Parse(await session.ReadBlobAsync(file.HeadBlobSha!, ReviewLimits.MaxReviewSetFileBytes, cancellationToken));
        var forward = await session.IsAncestorAsync(first, second, cancellationToken);
        var backward = await session.IsAncestorAsync(second, first, cancellationToken);

        // Assert: the set as written, and the history in one direction only
        var set = Assert.Single(sets);
        Assert.Equal("docs", set.Id);
        Assert.Equal(["docs/**"], set.Paths);
        Assert.True(forward);
        Assert.False(backward);
    }

    /// <summary>
    ///     Test that a repository that is disabled in the registry cannot be opened for reading.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_DisabledRepository_IsNotAvailableForReading()
    {
        // Arrange: a registered repository that is then disabled
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .AddDataProtection().UseEphemeralDataProtectionProvider().Services
            .BuildServiceProvider();
        using var _ = provider;
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var cancellationToken = TestContext.Current.CancellationToken;
        await users.CreateAsync("adm", "Adm", UserRole.Admin, Password, cancellationToken);
        var service = new RepositoryService(
            new RepositoryStore(factory, new AuditAppender(TimeProvider.System)),
            new CredentialProtector(provider.GetRequiredService<IDataProtectionProvider>()),
            users,
            clock);
        var admin = new Actor("adm", "Adm");
        var registered = await service.CreateAsync("off", "https://example.com/r.git", null, null, admin, cancellationToken);
        await service.SetEnabledAsync(registered.Id, false, admin, cancellationToken);

        // Act: ask for the summaries and for access
        var summaries = await service.ListEnabledAsync(cancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.GetAccessAsync(registered.Id, cancellationToken));

        // Assert: it is not offered and access is refused
        Assert.Empty(summaries);
        Assert.Equal(DomainRule.RepositoryDisabled, exception.Rule);
    }
}
