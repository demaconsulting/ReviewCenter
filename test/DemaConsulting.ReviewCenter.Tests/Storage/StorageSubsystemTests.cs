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
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Subsystem tests for the Storage subsystem: the real Core services running over the database setup,
///     the database context and the review store with a real SQLite database file.
/// </summary>
public class StorageSubsystemTests
{
    /// <summary>
    ///     The instant reported by the services' clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Opens the database described by the options, creates it if needed, and builds the domain services
    ///     over it.
    /// </summary>
    /// <param name="options">The database options.</param>
    /// <returns>The provider that owns the database factory, with the two services and the store under them.</returns>
    private static (ServiceProvider Provider, ProjectService Projects, ReviewService Reviews, ReviewStore Store) Open(DatabaseOptions options)
    {
        var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder => DatabaseSetup.Configure(builder, options))
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        DatabaseSetup.EnsureCreated(factory, options);
        var store = new ReviewStore(factory, new AuditAppender(TimeProvider.System));
        var time = new FixedTimeProvider(_now);
        return (provider, TestServices.Projects(store, time), TestServices.Reviews(store, time), store);
    }

    /// <summary>
    ///     Test the complete workflow against a real database file: data written through the services is still
    ///     there, byte for byte and with the same fingerprint, after the database is closed and opened again.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsProjectAndReview()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        int projectId;

        // Act: create a project and a review of three files, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            var project = await first.Projects.CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);
            projectId = project.Id;
            await first.Reviews.CreateDraftAsync(
                projectId,
                "rev-1",
                "First review",
                [new("a.txt", TestContent.A), new("b.txt", TestContent.B), new("c.bin", TestContent.C)],
                TestContext.Current.CancellationToken);
        }

        // Act: open the same database again and read everything back
        var second = Open(options);
        using (second.Provider)
        {
            var projects = await second.Projects.ListAsync(TestContext.Current.CancellationToken);
            var review = await second.Reviews.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
            var content = await second.Reviews.GetFileContentAsync(projectId, "rev-1", "c.bin", TestContext.Current.CancellationToken);

            // Assert: the project, the review with its known fingerprint, and the exact bytes survived
            Assert.Equal("Apollo", Assert.Single(projects).Name);
            Assert.NotNull(review);
            Assert.Equal(TestContent.FingerprintABC, review.ContentFingerprint);
            Assert.Equal(3, review.Files.Count);
            Assert.Equal(TestContent.C, content);
        }
    }

    /// <summary>
    ///     Test that threads, comments and the thread state written through the services are still there, in
    ///     order, after the database is closed and opened again.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_ServicesOverDatabase_ReopenedDatabase_KeepsThreadsAndComments()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        int projectId;

        // Act: create a review, start a thread on a line, reply and resolve it, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            var project = await first.Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
            projectId = project.Id;
            await first.Reviews.CreateDraftAsync(
                projectId, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
            var threads = TestServices.Threads(first.Store, new FixedTimeProvider(_now));
            var thread = await threads.StartThreadAsync(
                projectId, "rev-1", new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, "First", TestContext.Current.CancellationToken);
            await threads.ReplyAsync(projectId, "rev-1", thread.Id, TestActors.Bob, "Second", TestContext.Current.CancellationToken);
            await threads.ResolveAsync(projectId, "rev-1", thread.Id, TestContext.Current.CancellationToken);
        }

        // Act: open the same database again and read the threads back
        var second = Open(options);
        using (second.Provider)
        {
            var listed = Assert.Single(
                await TestServices.Threads(second.Store, new FixedTimeProvider(_now)).ListAsync(projectId, "rev-1", TestContext.Current.CancellationToken));

            // Assert: anchor, state and both comments survived, in posting order
            Assert.Equal((1, 1), (listed.StartLine, listed.EndLine));
            Assert.Equal(ReviewThreadState.Resolved, listed.State);
            Assert.Equal(["Ann", "Bob"], listed.Comments.Select(c => c.Author));
            Assert.Equal(["ann", "bob"], listed.Comments.Select(c => c.AuthorUsername));
            Assert.Equal(["First", "Second"], listed.Comments.Select(c => c.Text));
        }
    }

    /// <summary>
    ///     Test that the checklist snapshot, the answers and the section applicability written through the
    ///     services are still there after the database is closed and opened again.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_Checklist_PersistsSnapshotAnswersAndSectionsAcrossReopen()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        int projectId;

        // Act: create a review, answer one check, mark one section as not applicable, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            var project = await first.Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
            projectId = project.Id;
            await first.Reviews.CreateDraftAsync(
                projectId, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
            var checklists = TestServices.Checklists(first.Store, new FixedTimeProvider(_now));
            await checklists.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail, "Broken", TestActors.Ann, TestContext.Current.CancellationToken);
            await checklists.SetSectionApplicabilityAsync(
                projectId, "rev-1", "DES", false, "No design", TestActors.Ann, TestContext.Current.CancellationToken);
        }

        // Act: open the same database again and read the checklist
        var second = Open(options);
        using (second.Provider)
        {
            var state = await TestServices.Checklists(second.Store, new FixedTimeProvider(_now))
                .GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

            // Assert: the snapshot, the answer and the applicability survived
            Assert.NotNull(state);
            Assert.Equal(DefaultChecklist.Version, state.Definition.Version);
            Assert.Equal("Broken", state.AnswerFor("REQ-01")!.Justification);
            Assert.Equal("No design", state.ReasonFor("DES"));
            Assert.Equal(new ChecklistProgress(1, 53), state.Overall);
        }
    }

    /// <summary>
    ///     Test that a user created through the service, and the change of its password, are still there after
    ///     the database is closed and opened again, and that the user signs in with the changed password.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_Users_PersistAcrossReopen()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };

        // Act: create an administrator and change a password, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            var users = NewUserService(first.Provider);
            await users.CreateAsync("Root", "Root", UserRole.Admin, "correct horse battery", TestContext.Current.CancellationToken);
            await users.ChangeOwnPasswordAsync(
                "root", "correct horse battery", "another long password", TestContext.Current.CancellationToken);
        }

        // Act: open the same database again and sign in
        var second = Open(options);
        using (second.Provider)
        {
            var users = NewUserService(second.Provider);
            var signedIn = await users.AuthenticateAsync("ROOT", "another long password", TestContext.Current.CancellationToken);
            var oldPassword = await users.AuthenticateAsync("root", "correct horse battery", TestContext.Current.CancellationToken);

            // Assert: the user, with its role, survived and only the changed password works
            Assert.Equal(("Root", true), (signedIn?.Username, signedIn?.IsSystemAdmin));
            Assert.Null(oldPassword);
        }
    }

    /// <summary>
    ///     Test that a token created through the service, and the revocation of a second one, are still there after
    ///     the database is closed and opened again, that the value still authenticates its owner, and that the
    ///     value itself was never stored.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_AccessTokens_PersistAcrossReopen()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var cancellationToken = TestContext.Current.CancellationToken;
        var ann = new Actor("ann", "Ann");
        CreatedPersonalAccessToken kept;
        CreatedPersonalAccessToken revoked;

        // Act: create a user and two tokens, revoke one, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            await NewUserService(first.Provider).CreateAsync("ann", "Ann", UserRole.Reviewer, "correct horse battery", cancellationToken);
            var tokens = NewTokenService(first.Provider);
            kept = await tokens.CreateAsync(ann, "build server", 90, cancellationToken);
            revoked = await tokens.CreateAsync(ann, "laptop", 30, cancellationToken);
            await tokens.RevokeAsync(ann, revoked.Token.Id, cancellationToken);
        }

        // Act: open the same database again and use both tokens
        var second = Open(options);
        using (second.Provider)
        {
            var tokens = NewTokenService(second.Provider);
            var accepted = await tokens.AuthenticateAsync(kept.Value, cancellationToken);
            var refused = await tokens.AuthenticateAsync(revoked.Value, cancellationToken);
            var listed = await tokens.ListAsync(ann, cancellationToken);

            // Assert: the kept token works, the revoked one does not, and no row holds a value
            Assert.True(accepted.Succeeded);
            Assert.False(refused.Succeeded);
            Assert.Equal(2, listed.Count);
            Assert.DoesNotContain(listed, t => t.TokenHash == kept.Value || t.Name == kept.Value);
            Assert.NotNull(Assert.Single(listed, t => t.Name == "laptop").Revoked);
        }
    }

    /// <summary>
    ///     Test that a repository registered through the service, with its encrypted token and its disabled state,
    ///     is still there after the database is closed and opened again.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_Repositories_PersistAcrossReopen()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var admin = new Actor("root", "Root");
        int id;
        int projectId;

        // Act: register a private repository, disable it, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            var users = NewUserService(first.Provider);
            await users.CreateAsync("root", "Root", true, "correct horse battery", TestContext.Current.CancellationToken);
            var project = await first.Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
            projectId = project.Id;
            var service = NewRepositoryService(first.Provider, first.Store);
            var created = await service.CreateAsync(
                projectId, "private", "https://example.com/r.git", "svc", "tkn-Persist", admin, TestContext.Current.CancellationToken);
            id = created.Id;
            await service.SetEnabledAsync(id, false, admin, TestContext.Current.CancellationToken);
        }

        // Act: open the same database again and read the registry
        var second = Open(options);
        using (second.Provider)
        {
            var service = NewRepositoryService(second.Provider, second.Store);
            var listed = Assert.Single(await service.ListAsync(admin, projectId, TestContext.Current.CancellationToken));
            var offered = await service.ListEnabledAsync(admin, projectId, TestContext.Current.CancellationToken);

            // Assert: the registration, the token flag, the project and the disabled state survived; nothing is offered
            Assert.Equal(("private", "svc", true, false, projectId), (listed.Name, listed.Username, listed.HasToken, listed.Enabled, listed.ProjectId));
            Assert.Empty(offered);
        }
    }

    /// <summary>
    ///     Creates the repository service over the real database, with a fake token protector.
    /// </summary>
    /// <param name="provider">The provider that owns the database factory.</param>
    /// <param name="store">The review store that knows the projects.</param>
    /// <returns>The service.</returns>
    private static RepositoryService NewRepositoryService(ServiceProvider provider, ReviewStore store)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        return TestServices.Repositories(
            store,
            new FixedTimeProvider(_now),
            new RepositoryStore(factory, new AuditAppender(TimeProvider.System)));
    }

    /// <summary>
    ///     Test that a reviewer slot and its signature written through the services are still there, and still
    ///     current, after the database is closed and opened again, and that the review is then approved.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_Signatures_PersistAcrossReopenAndStayCurrent()
    {
        // Arrange: a new database file
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        int projectId;

        // Act: create a review with every check answered and one slot, sign it, then close the database
        var first = Open(options);
        using (first.Provider)
        {
            var users = NewUserService(first.Provider);
            var ann = await users.CreateAsync("ann", "Ann", false, "correct horse battery", TestContext.Current.CancellationToken);
            var bob = await users.CreateAsync("bob", "Bob", false, "correct horse battery", TestContext.Current.CancellationToken);
            var project = await first.Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
            projectId = project.Id;
            await first.Store.AddMembershipAsync(ann.Id, projectId, TestContext.Current.CancellationToken);
            await first.Store.AddMembershipAsync(bob.Id, projectId, TestContext.Current.CancellationToken);
            await first.Reviews.CreateDraftAsync(
                projectId, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
            var checklists = TestServices.Checklists(first.Store, new FixedTimeProvider(_now));
            foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
            {
                await checklists.SetAnswerAsync(
                    projectId, "rev-1", item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, TestContext.Current.CancellationToken);
            }

            var signing = TestServices.Signing(first.Store, users, new FixedTimeProvider(_now));
            var slot = await signing.AddSlotAsync(
                TestActors.Ann, projectId, "rev-1", ann.Id, bob.Id, TestContext.Current.CancellationToken);
            var approval = await signing.GetApprovalAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
            await signing.SignAsync(
                TestActors.Bob, projectId, "rev-1", slot.Id, approval!.Digest, "correct horse battery", TestContext.Current.CancellationToken);
        }

        // Act: open the same database again and read the approval
        var second = Open(options);
        using (second.Provider)
        {
            var signing = TestServices.Signing(second.Store, NewUserService(second.Provider), new FixedTimeProvider(_now));
            var approval = await signing.GetApprovalAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

            // Assert: the signature survived, belongs to the alternate, and is still current for the same record
            Assert.NotNull(approval);
            Assert.True(approval.IsApproved);
            var status = Assert.Single(approval.Slots);
            Assert.Equal(SlotSignatureState.Signed, status.State);
            Assert.Equal("Bob", status.SignerName);
            Assert.Equal(_now, status.SignedAt);
        }
    }

    /// <summary>
    ///     Builds a user service over the database of a provider.
    /// </summary>
    /// <param name="provider">The provider that owns the database factory.</param>
    /// <returns>The service, over the real user store and a fast test hasher.</returns>
    private static UserService NewUserService(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        return new UserService(new UserStore(factory, new AuditAppender(TimeProvider.System)), new FakePasswordHasher(), new FixedTimeProvider(_now));
    }

    /// <summary>
    ///     Builds the token service over the real stores of an opened database.
    /// </summary>
    /// <param name="provider">The provider that owns the database factory.</param>
    /// <returns>The token service.</returns>
    private static PersonalAccessTokenService NewTokenService(ServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>();
        var appender = new AuditAppender(TimeProvider.System);
        return new PersonalAccessTokenService(
            new PersonalAccessTokenStore(factory, appender), new UserStore(factory, appender), new FixedTimeProvider(_now));
    }

    /// <summary>
    ///     Test that opening a database for a provider that is not supported fails with an error that names the
    ///     provider, and that no database file is created.
    /// </summary>
    [Fact]
    public void StorageSubsystem_UnsupportedProvider_Opening_FailsWithClearErrorAndCreatesNothing()
    {
        // Arrange: options that select a provider that is not implemented, with a new file path
        var connectionString = TestDatabase.NewConnectionString();
        var options = new DatabaseOptions { Provider = "Oracle", ConnectionString = connectionString };

        // Act: open the database
        var error = Assert.Throws<InvalidOperationException>(() => Open(options));

        // Assert: the error names the provider and the file was not created
        Assert.Contains("Oracle", error.Message, StringComparison.Ordinal);
        var file = connectionString.Split(';')[0].Replace("Data Source=", string.Empty, StringComparison.Ordinal);
        Assert.False(File.Exists(file));
    }

    /// <summary>
    ///     Test that a second review with an id already used in the same project is refused by the services
    ///     over a real database, and that the original review is left unchanged.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_ServicesOverDatabase_DuplicateReviewId_IsRefusedAndKeepsOriginal()
    {
        // Arrange: a database with one review
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var (provider, projects, reviews, _) = Open(options);
        using var _ = provider;
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "Original", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: try to create another review with the same id
        var error = await Assert.ThrowsAsync<DomainRuleException>(
            () => reviews.CreateDraftAsync(project.Id, "rev-1", "Other", [new("b.txt", TestContent.B)], TestContext.Current.CancellationToken));

        // Assert: the rule is named and the original review is unchanged
        Assert.Equal(DomainRule.ReviewIdDuplicate, error.Rule);
        var review = await reviews.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken);
        Assert.NotNull(review);
        Assert.Equal("Original", review.Title);
        Assert.Equal(TestContent.FingerprintA, review.ContentFingerprint);
        Assert.Single(await reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a review for a project that does not exist is refused over a real database, both by the
    ///     services and by the database's own foreign key, and that the data already stored is unchanged.
    /// </summary>
    [Fact]
    public async Task StorageSubsystem_ServicesOverDatabase_UnknownProject_IsRefusedAndKeepsData()
    {
        // Arrange: a database with one project that has one review
        var options = new DatabaseOptions { ConnectionString = TestDatabase.NewConnectionString() };
        var (provider, projects, reviews, store) = Open(options);
        using var _ = provider;
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "Original", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        var unknownId = project.Id + 1;
        var orphan = new Review
        {
            ProjectId = unknownId,
            Id = "rev-2",
            Title = "Orphan",
            State = ReviewState.Draft,
            Created = _now,
            ContentFingerprint = TestContent.FingerprintA
        };

        // Act: create a review through the services, and add one directly to the store, for the unknown project
        var error = await Assert.ThrowsAsync<DomainRuleException>(
            () => reviews.CreateDraftAsync(unknownId, "rev-2", "Other", [new("b.txt", TestContent.B)], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<DbUpdateException>(
            () => store.AddReviewAsync(orphan, TestContext.Current.CancellationToken));

        // Assert: the rule is named, nothing exists for the unknown project, and the existing data is unchanged
        Assert.Equal(DomainRule.ProjectNotFound, error.Rule);
        Assert.False(await store.ReviewExistsAsync(unknownId, "rev-2", TestContext.Current.CancellationToken));
        Assert.Null(await projects.GetAsync(unknownId, TestContext.Current.CancellationToken));
        Assert.Equal("Apollo", Assert.Single(await projects.ListAsync(TestContext.Current.CancellationToken)).Name);
        var review = Assert.Single(await reviews.ListAsync(project.Id, TestContext.Current.CancellationToken));
        Assert.Equal("Original", review.Title);
        Assert.Equal(TestContent.FingerprintA, review.ContentFingerprint);
    }
}
