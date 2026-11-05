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
        var cancellationToken = TestContext.Current.CancellationToken;
        var reviewStore = new ReviewStore(factory, new AuditAppender(TimeProvider.System));
        var project = await TestServices.Projects(reviewStore, clock).CreateAsync("Apollo", null, cancellationToken);
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var service = TestServices.Repositories(
            reviewStore,
            clock,
            new RepositoryStore(factory, new AuditAppender(TimeProvider.System)),
            new CredentialProtector(provider.GetRequiredService<IDataProtectionProvider>()));
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);

        // Act: register a private https repository and a local one, read the local one through git
        var admin = new Actor("adm", "Adm");
        var priv = await service.CreateAsync(project.Id, "private", "https://example.com/org/r.git", "svc", Secret, admin, cancellationToken);
        var registered = await service.CreateAsync(project.Id, "local", origin, null, null, admin, cancellationToken);
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
    ///     through the real git source and parser.
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
        GitTestSupport.Commit(origin, "first");
        GitTestSupport.Write(origin, "docs/a.md", "b\n");
        var second = GitTestSupport.Commit(origin, "second");
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: read the file at the head through git and parse it
        await using var session = await source.OpenAsync(new RepositoryAccess(1, "local", origin, null, null), cancellationToken);
        var listing = await session.ListChangesAsync(null, second, cancellationToken);
        var file = Assert.Single(listing.Changes, change => change.Path == ".reviewmark.yaml");
        var sets = new ReviewMarkConfigParser().Parse(await session.ReadBlobAsync(file.HeadBlobSha!, ReviewLimits.MaxReviewSetFileBytes, cancellationToken));

        // Assert: the set as written
        var set = Assert.Single(sets);
        Assert.Equal("docs", set.Id);
        Assert.Equal(["docs/**"], set.Paths);
    }

    /// <summary>
    ///     Test that the real git source tells that an earlier commit is in the history of a later commit.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_Ancestry_EarlierCommitIsInTheHistoryOfALaterCommit()
    {
        // Arrange: an origin with two commits and a git source over a private cache folder
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var (origin, first, second) = CreateTwoCommitOrigin(root.Path);
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await source.OpenAsync(new RepositoryAccess(1, "local", origin, null, null), cancellationToken);

        // Act: ask whether the first commit is an ancestor of the second
        var forward = await session.IsAncestorAsync(first, second, cancellationToken);

        // Assert: it is
        Assert.True(forward);
    }

    /// <summary>
    ///     Test that the real git source tells that a later commit is not in the history of an earlier commit.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_Ancestry_LaterCommitIsNotInTheHistoryOfAnEarlierCommit()
    {
        // Arrange: an origin with two commits and a git source over a private cache folder
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var (origin, first, second) = CreateTwoCommitOrigin(root.Path);
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await source.OpenAsync(new RepositoryAccess(1, "local", origin, null, null), cancellationToken);

        // Act: ask whether the second commit is an ancestor of the first
        var backward = await session.IsAncestorAsync(second, first, cancellationToken);

        // Assert: it is not
        Assert.False(backward);
    }

    /// <summary>
    ///     Test that a repository that is disabled in the registry cannot be opened for reading.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_DisabledRepository_IsNotAvailableForReading()
    {
        // Arrange: a registered repository that is then disabled
        using var provider = NewProvider();
        var (service, project, registered) = await RegisterDisabledRepositoryAsync(provider);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: ask for access
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => service.GetAccessAsync(project.Id, registered.Id, cancellationToken));

        // Assert: access is refused
        Assert.Equal(DomainRule.RepositoryDisabled, exception.Rule);
    }

    /// <summary>
    ///     Test that a repository that is disabled in the registry is not offered for selection.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_DisabledRepository_IsNotOfferedForSelection()
    {
        // Arrange: a registered repository that is then disabled
        using var provider = NewProvider();
        var (service, project, _) = await RegisterDisabledRepositoryAsync(provider);

        // Act: ask for the summaries of the repositories that may be selected
        var summaries = await service.ListEnabledAsync(new Actor("adm", "Adm"), project.Id, TestContext.Current.CancellationToken);

        // Assert: the disabled repository is not among them
        Assert.Empty(summaries);
    }

    /// <summary>
    ///     Test that a revision that does not exist in a real repository is refused with the revision-not-found rule.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_UnknownRevision_IsRefusedWithRevisionNotFound()
    {
        // Arrange: an origin with one commit and a git source over a private cache folder
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = Path.Combine(root.Path, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, "readme.txt", "hello\n");
        GitTestSupport.Commit(origin, "first");
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await source.OpenAsync(new RepositoryAccess(1, "local", origin, null, null), cancellationToken);

        // Act: resolve a name that matches nothing
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => session.ResolveRevisionAsync("no-such-branch", cancellationToken));

        // Assert: the refusal carries the revision-not-found rule
        Assert.Equal(DomainRule.RevisionNotFound, unknown.Rule);
    }

    /// <summary>
    ///     Test that a revision that could be read as a git option is refused with the revision-invalid rule.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_OptionShapedRevision_IsRefusedWithRevisionInvalid()
    {
        // Arrange: an origin with one commit and a git source over a private cache folder
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var origin = Path.Combine(root.Path, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, "readme.txt", "hello\n");
        GitTestSupport.Commit(origin, "first");
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = await source.OpenAsync(new RepositoryAccess(1, "local", origin, null, null), cancellationToken);

        // Act: resolve a name that looks like a git option
        var option = await Assert.ThrowsAsync<DomainRuleException>(
            () => session.ResolveRevisionAsync("--upload-pack=x", cancellationToken));

        // Assert: the refusal carries the revision-invalid rule
        Assert.Equal(DomainRule.RevisionInvalid, option.Rule);
    }

    /// <summary>
    ///     Test that a repository whose location cannot be cloned is reported as an access failure that names the
    ///     repository and not its location.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_UnreachableRepository_IsAnAccessFailureNamingTheRepository()
    {
        // Arrange: a registered location that does not exist and a git source over a private cache folder
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var missing = Path.Combine(root.Path, "nothing-here");
        var gitOptions = new GitOptions { CachePath = Path.Combine(root.Path, "cache"), TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);

        // Act: open the repository for the first time
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => source.OpenAsync(new RepositoryAccess(1, "origin", missing, null, null), TestContext.Current.CancellationToken));

        // Assert: the rule and the repository name are in the error and the location is not
        Assert.Equal(DomainRule.GitAccessFailed, exception.Rule);
        Assert.Contains("'origin'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(missing, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that no mirror folder is left behind when the first clone of a repository fails.
    /// </summary>
    [Fact]
    public async Task ContentSourcesSubsystem_UnreachableRepository_LeavesNoMirrorFolder()
    {
        // Arrange: a registered location that does not exist and a git source over a private cache folder
        Assert.SkipWhen(!GitTestSupport.Available, GitTestSupport.MissingMessage);
        using var root = new TempFolder();
        var missing = Path.Combine(root.Path, "nothing-here");
        var cache = Path.Combine(root.Path, "cache");
        var gitOptions = new GitOptions { CachePath = cache, TimeoutSeconds = 60 };
        var source = new GitSource(new GitRunner(gitOptions), gitOptions);

        // Act: open the repository for the first time, which fails
        await Assert.ThrowsAsync<DomainRuleException>(
            () => source.OpenAsync(new RepositoryAccess(1, "origin", missing, null, null), TestContext.Current.CancellationToken));

        // Assert: no mirror folder remains in the cache
        Assert.False(Directory.Exists(Path.Combine(cache, "repo-1")));
    }

    /// <summary>
    ///     Build a service provider with a new database and an ephemeral key ring.
    /// </summary>
    /// <returns>The provider; the caller disposes it.</returns>
    private static ServiceProvider NewProvider()
    {
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .AddDataProtection().UseEphemeralDataProtectionProvider().Services
            .BuildServiceProvider();
        DatabaseSetup.EnsureCreated(provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(), options);
        return provider;
    }

    /// <summary>
    ///     Register a repository in a new project and disable it.
    /// </summary>
    /// <param name="provider">The provider made by <see cref="NewProvider"/>.</param>
    /// <returns>The repository service, the project, and the disabled repository.</returns>
    private static async Task<(RepositoryService Service, Project Project, SourceRepository Repository)> RegisterDisabledRepositoryAsync(
        ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var cancellationToken = TestContext.Current.CancellationToken;
        var reviewStore = new ReviewStore(factory, new AuditAppender(TimeProvider.System));
        var project = await TestServices.Projects(reviewStore, clock).CreateAsync("Apollo", null, cancellationToken);
        var service = TestServices.Repositories(
            reviewStore,
            clock,
            new RepositoryStore(factory, new AuditAppender(TimeProvider.System)),
            new CredentialProtector(provider.GetRequiredService<IDataProtectionProvider>()));
        var admin = new Actor("adm", "Adm");
        var registered = await service.CreateAsync(project.Id, "off", "https://example.com/r.git", null, null, admin, cancellationToken);
        await service.SetEnabledAsync(registered.Id, false, admin, cancellationToken);
        return (service, project, registered);
    }

    /// <summary>
    ///     Make an origin with two commits.
    /// </summary>
    /// <param name="rootPath">The folder in which the origin is made.</param>
    /// <returns>The origin folder and the identifiers of the first and the second commit.</returns>
    private static (string Origin, string First, string Second) CreateTwoCommitOrigin(string rootPath)
    {
        var origin = Path.Combine(rootPath, "origin");
        Directory.CreateDirectory(origin);
        GitTestSupport.Init(origin);
        GitTestSupport.Write(origin, "readme.txt", "hello\n");
        var first = GitTestSupport.Commit(origin, "first");
        GitTestSupport.Write(origin, "readme.txt", "hello again\n");
        var second = GitTestSupport.Commit(origin, "second");
        return (origin, first, second);
    }
}
