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

using System.Data.Common;
using System.Reflection;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.Data.Sqlite;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for <see cref="AuditStore"/> and the audit entries written by the other stores, against a real
///     SQLite database file.
/// </summary>
public class AuditStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Runs a SQL statement directly against the database file, as someone with file access would.
    /// </summary>
    /// <param name="db">The test database.</param>
    /// <param name="sql">The statement.</param>
    private static void Execute(TestStore db, string sql)
    {
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    ///     Reads the whole trail in sequence order.
    /// </summary>
    /// <param name="db">The test database.</param>
    /// <returns>The entries, oldest first.</returns>
    private static async Task<IReadOnlyList<AuditEntry>> ReadAll(TestStore db)
    {
        return await db.Audit.ReadAfterAsync(0, 10_000, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Builds a draft by a known test actor.
    /// </summary>
    /// <param name="detail">The detail text.</param>
    /// <param name="projectId">The project, or <c>null</c>.</param>
    /// <param name="reviewId">The review, or <c>null</c>.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(string detail, int? projectId = null, string? reviewId = null)
    {
        return AuditDraft.By(TestActors.Ann, "test.event", detail, projectId, reviewId);
    }

    /// <summary>
    ///     Builds a review with one file.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="id">The review id.</param>
    /// <returns>The review.</returns>
    private static Review NewReview(int projectId, string id)
    {
        byte[] content = [1, 2, 3];
        return new Review
        {
            ProjectId = projectId,
            Id = id,
            Title = "Title " + id,
            State = ReviewState.Draft,
            Created = _created,
            ContentFingerprint = Fingerprint.Compute([content]),
            Files =
            [
                new ReviewFile
                {
                    ProjectId = projectId,
                    ReviewId = id,
                    Path = "a.txt",
                    Content = content,
                    Size = content.Length,
                    Sha256 = Fingerprint.ComputeFileHash(content)
                }
            ]
        };
    }

    /// <summary>
    ///     Test that appended entries get gap-free sequence numbers from 1, each naming the hash of the one
    ///     before, and the whole chain verifies.
    /// </summary>
    [Fact]
    public async Task AuditStore_AppendAsync_SequentialAppends_GapFreeLinkedAndVerifiable()
    {
        // Arrange
        using var db = new TestStore();

        // Act
        for (var i = 1; i <= 20; i++)
        {
            await db.Audit.AppendAsync(Draft("event " + i), TestContext.Current.CancellationToken);
        }

        var entries = await ReadAll(db);

        // Assert
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), entries.Select(e => e.Sequence));
        Assert.Equal(AuditChain.GenesisHash, entries[0].PreviousHash);
        for (var i = 1; i < entries.Count; i++)
        {
            Assert.Equal(entries[i - 1].Hash, entries[i].PreviousHash);
        }

        var result = AuditChain.Verify(entries);
        Assert.True(result.IsIntact);
        Assert.Equal(20, result.Count);
        Assert.Equal(entries[^1].Hash, result.HeadHash);
    }

    /// <summary>
    ///     Test that the stored fields survive the round trip exactly, so the recomputed hash matches: the
    ///     user is resolved from the user name, an unknown actor has no user, text is cleaned, and the time is
    ///     the instant of the append.
    /// </summary>
    [Fact]
    public async Task AuditStore_AppendAsync_Draft_StoresResolvedActorCleanedTextAndTime()
    {
        // Arrange: one known user
        using var db = new TestStore();
        var user = await db.Users.AddAsync(
            new User { Username = "ann", DisplayName = "Ann", Enabled = true, PasswordHash = "h", Created = _created },
            TestContext.Current.CancellationToken);
        var before = DateTimeOffset.UtcNow;

        // Act
        var known = await db.Audit.AppendAsync(
            new AuditDraft("review.created", "ANN", "Ann", 4, "rev-1", "line one\r\nline two"),
            TestContext.Current.CancellationToken);
        var unknown = await db.Audit.AppendAsync(
            new AuditDraft("auth.sign-in-failed", null, "unknown user", null, null, "wrong password"),
            TestContext.Current.CancellationToken);
        var after = DateTimeOffset.UtcNow;
        var stored = await ReadAll(db);

        // Assert
        Assert.Equal(user.Id, known.ActorUserId);
        Assert.Equal("Ann (ANN)", known.ActorName);
        Assert.Equal("line one  line two", known.Detail);
        Assert.Null(unknown.ActorUserId);
        Assert.Equal("unknown user", unknown.ActorName);
        Assert.InRange(known.TimestampUtc, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.Equal(known.TimestampUtc.UtcTicks, stored[1].TimestampUtc.UtcTicks);
        Assert.Equal((4, "rev-1"), (stored[1].ProjectId, stored[1].ReviewId));
        Assert.True(AuditChain.Verify(stored).IsIntact);
    }

    /// <summary>
    ///     Test that many concurrent appends produce one linear chain: every sequence number exactly once, no
    ///     gaps, and a chain that verifies.
    /// </summary>
    [Fact]
    public async Task AuditStore_AppendAsync_ConcurrentAppends_ProduceLinearChain()
    {
        // Arrange
        using var db = new TestStore();

        // Act
        await Task.WhenAll(Enumerable.Range(1, 40)
            .Select(i => Task.Run(
                () => db.Audit.AppendAsync(Draft("event " + i), TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken)));
        var entries = await ReadAll(db);

        // Assert
        Assert.Equal(Enumerable.Range(1, 40).Select(i => (long)i), entries.Select(e => e.Sequence));
        Assert.Equal(40, entries.Select(e => e.Detail).Distinct().Count());
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that concurrent changes from different stores, each with its audit entry, still produce one
    ///     linear chain and that every change has its entry.
    /// </summary>
    [Fact]
    public async Task AuditStore_ConcurrentChangesFromDifferentStores_ProduceLinearChain()
    {
        // Arrange
        using var db = new TestStore();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var projects = Enumerable.Range(1, 10)
            .Select(i => Task.Run(
                () => db.Store.AddProjectAsync(
                    new Project { Name = "P" + i, Created = _created },
                    p => AuditDraft.By(TestActors.Ann, "project.created", "project " + p.Id, p.Id),
                    cancellationToken),
                cancellationToken));
        var users = Enumerable.Range(1, 10)
            .Select(i => Task.Run(
                () => db.Users.AddAsync(
                    new User { Username = "user" + i, DisplayName = "U", Enabled = true, PasswordHash = "h", Created = _created },
                    u => AuditDraft.By(TestActors.Ann, "user.created", "user " + u.Username),
                    cancellationToken),
                cancellationToken));
        var events = Enumerable.Range(1, 10)
            .Select(i => Task.Run(() => db.Audit.AppendAsync(Draft("event " + i), cancellationToken), cancellationToken));
        await Task.WhenAll(projects.Cast<Task>().Concat(users).Concat(events));
        var entries = await ReadAll(db);

        // Assert
        Assert.Equal(30, entries.Count);
        Assert.Equal(Enumerable.Range(1, 30).Select(i => (long)i), entries.Select(e => e.Sequence));
        Assert.Equal(10, entries.Count(e => e.Action == "project.created"));
        Assert.Equal(10, entries.Count(e => e.Action == "user.created"));
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that the database itself refuses a second entry with an existing sequence number, whatever the
    ///     code does.
    /// </summary>
    [Fact]
    public async Task AuditStore_Database_DuplicateSequence_IsRefusedByUniqueKey()
    {
        // Arrange
        using var db = new TestStore();
        await db.Audit.AppendAsync(Draft("first"), TestContext.Current.CancellationToken);

        // Act / Assert
        Assert.ThrowsAny<DbException>(() => Execute(
            db,
            "INSERT INTO AuditEntries (Sequence, TimestampUtc, ActorName, Action, Detail, PreviousHash, Hash) "
            + "VALUES (1, '2025-03-04 05:06:07', 'x', 'y', 'z', 'p', 'h')"));
        Assert.Single(await ReadAll(db));
    }

    /// <summary>
    ///     Test that a change and its audit entry exist together: a project is stored with exactly one entry
    ///     describing it.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_WithDraft_StoresProjectAndEntryTogether()
    {
        // Arrange
        using var db = new TestStore();

        // Act
        var project = await db.Store.AddProjectAsync(
            new Project { Name = "Apollo", Created = _created },
            p => AuditDraft.By(TestActors.Ann, "project.created", $"project 'Apollo' ({p.Id})", p.Id),
            TestContext.Current.CancellationToken);
        var entries = await ReadAll(db);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal("project.created", entry.Action);
        Assert.Equal(project.Id, entry.ProjectId);
        Assert.Equal($"project 'Apollo' ({project.Id})", entry.Detail);
    }

    /// <summary>
    ///     Test that a change that fails leaves no audit entry: a duplicate review is refused and the trail
    ///     holds only the entry of the first.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddReviewAsync_FailedChange_LeavesNoAuditEntry()
    {
        // Arrange
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "A", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "rev-1"), Draft("first", project.Id, "rev-1"), TestContext.Current.CancellationToken);
        var before = (await ReadAll(db)).Count;

        // Act
        await Assert.ThrowsAsync<DomainRuleException>(
            () => db.Store.AddReviewAsync(NewReview(project.Id, "rev-1"), Draft("second", project.Id, "rev-1"), TestContext.Current.CancellationToken));
        var entries = await ReadAll(db);

        // Assert
        Assert.Equal(before, entries.Count);
        Assert.DoesNotContain(entries, e => e.Detail == "second");
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that a draft that cannot be built rolls the change back: nothing is stored, no entry either.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_DraftFactoryThrows_StoresNothing()
    {
        // Arrange
        using var db = new TestStore();

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Store.AddProjectAsync(
                new Project { Name = "Apollo", Created = _created },
                _ => throw new InvalidOperationException("no draft"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(await db.Store.ListProjectsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await ReadAll(db));
    }

    /// <summary>
    ///     Test the other direction of atomicity: when the audit entry cannot be stored, the change is rolled
    ///     back as well. A trigger that refuses every audit insert stands in for a failing disk.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_AuditInsertFails_RollsBackTheChange()
    {
        // Arrange
        using var db = new TestStore();
        Execute(db, "CREATE TRIGGER refuse_audit BEFORE INSERT ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit refused'); END;");

        // Act
        await Assert.ThrowsAnyAsync<Exception>(
            () => db.Store.AddProjectAsync(
                new Project { Name = "Apollo", Created = _created },
                p => AuditDraft.By(TestActors.Ann, "project.created", "project", p.Id),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(await db.Store.ListProjectsAsync(TestContext.Current.CancellationToken));
        Execute(db, "DROP TRIGGER refuse_audit");
        Assert.Empty(await ReadAll(db));
    }

    /// <summary>
    ///     Test that a failing audit insert also rolls back a user change, and a later append still works.
    /// </summary>
    [Fact]
    public async Task UserStore_AddAsync_AuditInsertFails_RollsBackTheUserAndTheGateIsReleased()
    {
        // Arrange
        using var db = new TestStore();
        Execute(db, "CREATE TRIGGER refuse_audit BEFORE INSERT ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit refused'); END;");

        // Act
        await Assert.ThrowsAnyAsync<Exception>(
            () => db.Users.AddAsync(
                new User { Username = "ann", DisplayName = "Ann", Enabled = true, PasswordHash = "h", Created = _created },
                u => AuditDraft.By(TestActors.Ann, "user.created", "user 'ann'"),
                TestContext.Current.CancellationToken));
        Execute(db, "DROP TRIGGER refuse_audit");
        var user = await db.Users.GetByUsernameAsync("ann", TestContext.Current.CancellationToken);
        var next = await db.Audit.AppendAsync(Draft("after the failure"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(user);
        Assert.Equal(1, next.Sequence);
    }

    /// <summary>
    ///     Test that a change that takes no effect (a comment on a missing thread, a state change of a missing
    ///     thread) records nothing.
    /// </summary>
    [Fact]
    public async Task ReviewStore_NoOpChanges_RecordNothing()
    {
        // Arrange
        using var db = new TestStore();
        var project = await db.Store.AddProjectAsync(new Project { Name = "A", Created = _created }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "rev-1"), Draft("review", project.Id, "rev-1"), TestContext.Current.CancellationToken);
        var before = (await ReadAll(db)).Count;

        // Act
        var comment = await db.Store.AddCommentAsync(
            project.Id,
            "rev-1",
            999,
            new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = "x", Created = _created },
            _ => Draft("comment"),
            TestContext.Current.CancellationToken);
        var changed = await db.Store.SetThreadStateAsync(project.Id, "rev-1", 999, ReviewThreadState.Resolved, Draft("resolve"), TestContext.Current.CancellationToken);
        var removed = await db.Store.RemoveSlotAsync(project.Id, "rev-1", 999, Draft("remove"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(comment);
        Assert.False(changed);
        Assert.False(removed);
        Assert.Equal(before, (await ReadAll(db)).Count);
    }

    /// <summary>
    ///     Test that a change of a user that carries no draft (a failure counter) records nothing, and one
    ///     that carries a draft records it.
    /// </summary>
    [Fact]
    public async Task UserStore_UpdateAsync_WithAndWithoutDraft_RecordsOnlyWithDraft()
    {
        // Arrange
        using var db = new TestStore();
        var user = await db.Users.AddAsync(
            new User { Username = "ann", DisplayName = "Ann", Enabled = true, PasswordHash = "h", Created = _created },
            u => AuditDraft.By(TestActors.Ann, "user.created", "user 'ann'"),
            TestContext.Current.CancellationToken);

        // Act
        user.FailedLoginCount = 3;
        await db.Users.UpdateAsync(user, null, TestContext.Current.CancellationToken);
        var afterCounter = (await ReadAll(db)).Count;
        user.Enabled = false;
        await db.Users.UpdateAsync(user, AuditDraft.By(TestActors.Ann, "user.disabled", "user 'ann'"), TestContext.Current.CancellationToken);
        var entries = await ReadAll(db);

        // Assert
        Assert.Equal(1, afterCounter);
        Assert.Equal(["user.created", "user.disabled"], entries.Select(e => e.Action));
        Assert.Equal(3, (await db.Users.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))?.FailedLoginCount);
    }

    /// <summary>
    ///     Test that editing a stored entry with SQL is detected at that entry.
    /// </summary>
    [Fact]
    public async Task AuditStore_Verification_EditedRow_ReportsThatSequence()
    {
        // Arrange
        using var db = new TestStore();
        for (var i = 1; i <= 5; i++)
        {
            await db.Audit.AppendAsync(Draft("event " + i), TestContext.Current.CancellationToken);
        }

        // Act
        Execute(db, "UPDATE AuditEntries SET Detail = 'forged' WHERE Sequence = 3");
        var result = AuditChain.Verify(await ReadAll(db));

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(3, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that deleting a stored entry with SQL is detected at the deleted sequence.
    /// </summary>
    [Fact]
    public async Task AuditStore_Verification_DeletedRow_ReportsThatSequence()
    {
        // Arrange
        using var db = new TestStore();
        for (var i = 1; i <= 5; i++)
        {
            await db.Audit.AppendAsync(Draft("event " + i), TestContext.Current.CancellationToken);
        }

        // Act
        Execute(db, "DELETE FROM AuditEntries WHERE Sequence = 2");
        var result = AuditChain.Verify(await ReadAll(db));

        // Assert
        Assert.False(result.IsIntact);
        Assert.Equal(2, result.FirstBadSequence);
    }

    /// <summary>
    ///     Test that the review filter returns only the entries of one review, newest first, and that the
    ///     before and after cursors page through them.
    /// </summary>
    [Fact]
    public async Task AuditStore_ListAsync_FilterAndCursors_ReturnExpectedEntriesNewestFirst()
    {
        // Arrange: ten entries alternating between two reviews of project 1, plus one of another project
        using var db = new TestStore();
        for (var i = 1; i <= 10; i++)
        {
            await db.Audit.AppendAsync(Draft("event " + i, 1, i % 2 == 0 ? "even" : "odd"), TestContext.Current.CancellationToken);
        }

        await db.Audit.AppendAsync(Draft("other", 2, "even"), TestContext.Current.CancellationToken);
        var token = TestContext.Current.CancellationToken;

        // Act
        var all = await db.Audit.ListAsync(null, null, null, null, 100, token);
        var even = await db.Audit.ListAsync(1, "even", null, null, 100, token);
        var olderThan6 = await db.Audit.ListAsync(1, "even", 6, null, 2, token);
        var newerThan6 = await db.Audit.ListAsync(1, "even", null, 6, 2, token);
        var wholeProject = await db.Audit.ListAsync(2, "even", null, null, 100, token);

        // Assert
        Assert.Equal(Enumerable.Range(1, 11).Reverse().Select(i => (long)i), all.Select(e => e.Sequence));
        Assert.Equal([10L, 8L, 6L, 4L, 2L], even.Select(e => e.Sequence));
        Assert.Equal([4L, 2L], olderThan6.Select(e => e.Sequence));
        Assert.Equal([10L, 8L], newerThan6.Select(e => e.Sequence));
        Assert.Equal("other", Assert.Single(wholeProject).Detail);
    }

    /// <summary>
    ///     Test that batches are read in ascending order from the given sequence and are bounded.
    /// </summary>
    [Fact]
    public async Task AuditStore_ReadAfterAsync_Batches_AreAscendingAndBounded()
    {
        // Arrange
        using var db = new TestStore();
        for (var i = 1; i <= 7; i++)
        {
            await db.Audit.AppendAsync(Draft("event " + i), TestContext.Current.CancellationToken);
        }

        // Act
        var first = await db.Audit.ReadAfterAsync(0, 3, TestContext.Current.CancellationToken);
        var second = await db.Audit.ReadAfterAsync(3, 3, TestContext.Current.CancellationToken);
        var last = await db.Audit.ReadAfterAsync(6, 3, TestContext.Current.CancellationToken);
        var none = await db.Audit.ReadAfterAsync(7, 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([1L, 2L, 3L], first.Select(e => e.Sequence));
        Assert.Equal([4L, 5L, 6L], second.Select(e => e.Sequence));
        Assert.Equal([7L], last.Select(e => e.Sequence));
        Assert.Empty(none);
    }

    /// <summary>
    ///     Test that neither the store nor its interface offers any way to change or remove an entry.
    /// </summary>
    [Fact]
    public void AuditStore_PublicSurface_OffersNoUpdateOrDelete()
    {
        // Arrange
        var forbidden = new[] { "Update", "Delete", "Remove", "Set", "Clear", "Truncate", "Replace", "Save" };
        var storeMethods = typeof(AuditStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name);
        var interfaceMethods = typeof(IAuditStore).GetMethods().Select(m => m.Name);

        // Act
        var names = storeMethods.Concat(interfaceMethods).Distinct().ToList();

        // Assert
        Assert.Equal(["AppendAsync", "ListAsync", "ReadAfterAsync"], names.Order());
        Assert.DoesNotContain(names, name => forbidden.Any(word => name.StartsWith(word, StringComparison.Ordinal)));
    }
}
