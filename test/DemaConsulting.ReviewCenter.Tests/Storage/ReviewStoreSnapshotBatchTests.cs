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
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the set-based batch reads of <see cref="ReviewStore"/> (<see cref="IReviewStore.ListSnapshotsAsync"/>
///     and the batch overload of <see cref="IReviewStore.ListReopensAsync(int, IReadOnlyCollection{string}, CancellationToken)"/>)
///     against a real SQLite database file.
/// </summary>
public class ReviewStoreSnapshotBatchTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Counts the commands that are sent to the database.
    /// </summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        /// <summary>
        ///     Gets the number of commands run so far.
        /// </summary>
        public int Count { get; set; }

        /// <inheritdoc />
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Count++;
            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }

        /// <inheritdoc />
        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            Count++;
            return result;
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    ///     Adds a project.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddProjectAsync(TestStore db)
    {
        var project = await db.Store.AddProjectAsync(
            new Project { Name = "P", Created = _time }, TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Adds an upload review of the file <c>a.txt</c> to a project.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="withChecklist">Whether the review carries a checklist snapshot.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    private static Task AddReviewAsync(TestStore db, int projectId, string reviewId, bool withChecklist = false)
    {
        return db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = projectId,
                Id = reviewId,
                Title = "Title",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = TestContent.FingerprintA,
                Checklist = withChecklist
                    ? new ReviewChecklist
                    {
                        ProjectId = projectId,
                        ReviewId = reviewId,
                        Version = DefaultChecklist.Version,
                        Definition = DefaultChecklist.Current.ToJson()
                    }
                    : null,
                Files =
                [
                    new ReviewFile
                    {
                        ProjectId = projectId,
                        ReviewId = reviewId,
                        Path = "a.txt",
                        Content = TestContent.A,
                        Size = TestContent.A.Length,
                        Sha256 = TestContent.HashA
                    }
                ]
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a review that comes from a repository, with a modified, an added and a deleted file, and that follows
    ///     another review.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    private static Task AddRepositoryReviewAsync(TestStore db, int projectId, string reviewId)
    {
        ReviewFile File(string path, ChangeKind kind, byte[]? baseline, byte[]? head) => new()
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            Path = path,
            ChangeKind = kind,
            Content = head ?? [],
            Size = head?.Length ?? 0,
            Sha256 = head is null ? string.Empty : Fingerprint.ComputeFileHash(head),
            BaselineContent = baseline,
            BaselineSize = baseline?.Length,
            BaselineSha256 = baseline is null ? null : Fingerprint.ComputeFileHash(baseline)
        };

        return db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = projectId,
                Id = reviewId,
                Title = "Repository",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = Fingerprint.Compute(new[] { TestContent.A, TestContent.B }),
                RepositoryId = 7,
                RepositoryName = "main-repo",
                BaselineSha = new string('a', 40),
                HeadSha = new string('b', 40),
                Selection = "src/**",
                ReviewSetId = "core",
                Files =
                [
                    File("b.txt", ChangeKind.Added, null, TestContent.B),
                    File("a.txt", ChangeKind.Modified, TestContent.B, TestContent.A),
                    File("c.txt", ChangeKind.Deleted, TestContent.C, null)
                ]
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a slot to a review.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The identity of the slot.</returns>
    private static async Task<int> AddSlotAsync(TestStore db, int projectId, string reviewId)
    {
        var slot = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = reviewId, PrimaryUserId = 1, AlternateUserId = 2 },
            TestContext.Current.CancellationToken);
        return slot.Id;
    }

    /// <summary>
    ///     Signs the record of a review as it is stored now with the given slot.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>A task that completes when the signature is stored.</returns>
    private static async Task SignAsync(TestStore db, int projectId, string reviewId, int slotId)
    {
        var record = await db.Store.GetRecordAsync(projectId, reviewId, TestContext.Current.CancellationToken);
        await db.Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = projectId,
                ReviewId = reviewId,
                SlotId = slotId,
                UserId = 1,
                DisplayName = "Ann",
                Role = SlotRole.Primary,
                SignedAt = _time,
                RecordDigest = RecordDigest.Compute(record!),
                Meaning = SigningService.MeaningText
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reopens an approved review.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="reason">The reason of the reopening.</param>
    /// <returns>A task that completes when the review is reopened.</returns>
    private static async Task ReopenAsync(TestStore db, int projectId, string reviewId, string reason)
    {
        var record = await db.Store.GetRecordAsync(projectId, reviewId, TestContext.Current.CancellationToken);
        await db.Store.ReopenReviewAsync(
            projectId,
            reviewId,
            new ReviewReopen(0, projectId, reviewId, _time, "root", "Root", reason, RecordDigest.Compute(record!)),
            AuditDraft.By(TestActors.Ann, "review.reopened", reason, projectId, reviewId),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a thread with a comment to a review.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="text">The text of the comment.</param>
    /// <returns>The stored thread.</returns>
    private static Task<ReviewThread> AddThreadAsync(TestStore db, int projectId, string reviewId, string text)
    {
        return db.Store.AddThreadAsync(
            new ReviewThread
            {
                ProjectId = projectId,
                ReviewId = reviewId,
                FilePath = "a.txt",
                State = ReviewThreadState.Open,
                Created = _time,
                Comments = [new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = text, Created = _time }]
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Describes everything a snapshot carries as text, so two snapshots can be compared part by part.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The description.</returns>
    private static string Describe(ReviewSnapshot snapshot)
    {
        var record = snapshot.Record;
        var parts = new List<string>
        {
            "digest=" + RecordDigest.Compute(record),
            "fingerprint=" + record.ContentFingerprint,
            "reopens=" + record.Reopens,
            "follows=" + record.FollowsReviewId,
            "checklist=" + (record.Checklist is null ? "-" : record.Checklist.Version + "/" + record.Checklist.Definition.Length),
            "source=" + string.Join('|', record.Source.RepositoryId, record.Source.BaselineSha, record.Source.HeadSha, record.Source.Selection, record.Source.ReviewSetId),
            "changes=" + string.Join(';', record.Source.Changes.Select(c => $"{c.Path}:{c.Kind}:{c.BaselineSha256}:{c.HeadSha256}")),
            "ballots=" + string.Join(
                ';',
                record.Ballots.OrderBy(b => b.SlotId).Select(b =>
                    $"{b.SlotId}:{b.Status}:"
                    + string.Join(',', b.Answers.OrderBy(a => a.ItemId).Select(a => $"{a.ItemId}:{a.Outcome}:{a.Justification}"))
                    + ":"
                    + string.Join(',', b.SectionStates.OrderBy(s => s.SectionId).Select(s => $"{s.SectionId}:{s.Applicable}:{s.Reason}")))),
            "threads=" + string.Join(
                ';',
                record.Threads.Select(t => $"{t.Id}:{t.State}:{t.FilePath}:[{string.Join(',', t.Comments.Select(c => c.Id + "=" + c.Text))}]")),
            "slots=" + string.Join(';', snapshot.Slots.Select(s => $"{s.Id}:{s.PrimaryUserId}:{s.AlternateUserId}")),
            "signatures=" + string.Join(';', snapshot.Signatures.Select(s => $"{s.Id}:{s.SlotId}:{s.RecordDigest}"))
        };
        return string.Join('\n', parts);
    }

    /// <summary>
    ///     Test that the batch read returns, for every review, exactly what the single snapshot read returns, and the
    ///     reopen history exactly as the single history read lists it: for an approved review, a reopened one, one
    ///     whose signature is out of date, one with threads, comments, answers and section states, one without slots, a
    ///     review from a repository with files, and the same review identifier in another project.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSnapshotsAsync_SeveralReviews_EqualsSingleReadsAndReopenHistory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddProjectAsync(db);
        var otherProjectId = await AddProjectAsync(db);

        await AddReviewAsync(db, projectId, "REV-APPROVED");
        await SignAsync(db, projectId, "REV-APPROVED", await AddSlotAsync(db, projectId, "REV-APPROVED"));

        await AddReviewAsync(db, projectId, "REV-REOPENED");
        var reopenedSlot = await AddSlotAsync(db, projectId, "REV-REOPENED");
        await SignAsync(db, projectId, "REV-REOPENED", reopenedSlot);
        await ReopenAsync(db, projectId, "REV-REOPENED", "First");
        await SignAsync(db, projectId, "REV-REOPENED", reopenedSlot);
        await ReopenAsync(db, projectId, "REV-REOPENED", "Second");

        await AddReviewAsync(db, projectId, "REV-STALE");
        await SignAsync(db, projectId, "REV-STALE", await AddSlotAsync(db, projectId, "REV-STALE"));
        await ReopenAsync(db, projectId, "REV-STALE", "Stale");
        await AddThreadAsync(db, projectId, "REV-STALE", "After the reopening");

        await AddReviewAsync(db, projectId, "REV-BUSY", withChecklist: true);
        var thread = await AddThreadAsync(db, projectId, "REV-BUSY", "First");
        await AddThreadAsync(db, projectId, "REV-BUSY", "Other thread");
        await db.Store.AddCommentAsync(
            projectId,
            "REV-BUSY",
            thread.Id,
            new ReviewComment { Author = "Bob", AuthorUsername = "bob", Text = "Second", Created = _time },
            cancellationToken);
        var busySlot = await AddSlotAsync(db, projectId, "REV-BUSY");
        var otherBusySlot = await AddSlotAsync(db, projectId, "REV-BUSY");
        foreach (var itemId in new[] { "REQ-02", "REQ-01" })
        {
            await db.Store.SetBallotAnswerAsync(
                new ChecklistAnswer
                {
                    ProjectId = projectId,
                    ReviewId = "REV-BUSY",
                    SlotId = busySlot,
                    ItemId = itemId,
                    Outcome = ChecklistOutcome.Fail,
                    Justification = "Bad " + itemId,
                    AnsweredBy = "Ann",
                    AnsweredByUsername = "ann",
                    AnsweredAt = _time
                },
                cancellationToken);
        }

        await db.Store.SetBallotSectionStateAsync(
            new ChecklistSectionState
            {
                ProjectId = projectId,
                ReviewId = "REV-BUSY",
                SlotId = busySlot,
                SectionId = "DES",
                Applicable = false,
                Reason = "None",
                SetBy = "Ann",
                SetByUsername = "ann",
                SetAt = _time
            },
            cancellationToken);
        await db.Store.SetBallotAnswerAsync(
            new ChecklistAnswer
            {
                ProjectId = projectId,
                ReviewId = "REV-BUSY",
                SlotId = otherBusySlot,
                ItemId = "REQ-01",
                Outcome = ChecklistOutcome.Pass,
                AnsweredBy = "Bob",
                AnsweredByUsername = "bob",
                AnsweredAt = _time
            },
            cancellationToken);

        await AddReviewAsync(db, projectId, "REV-EMPTY");
        await AddRepositoryReviewAsync(db, projectId, "REV-REPO");
        await AddReviewAsync(db, otherProjectId, "REV-APPROVED");
        await AddThreadAsync(db, otherProjectId, "REV-APPROVED", "Other project");
        var ids = new[] { "REV-APPROVED", "REV-REOPENED", "REV-STALE", "REV-BUSY", "REV-EMPTY", "REV-REPO" };

        // Act
        var batch = await db.Store.ListSnapshotsAsync(projectId, ids, cancellationToken);
        var history = await db.Store.ListReopensAsync(projectId, ids, cancellationToken);

        // Assert: every review is there, equal to its single read
        Assert.Equal(ids.Order(StringComparer.Ordinal), batch.Keys.Order(StringComparer.Ordinal));
        foreach (var id in ids)
        {
            var single = await db.Store.GetSnapshotAsync(projectId, id, cancellationToken);
            Assert.Equal(Describe(single!), Describe(batch[id]));
        }

        // Assert: the interesting parts are really there (the comparison above would also pass on empty reads)
        Assert.Equal(2, batch["REV-REOPENED"].Record.Reopens);
        Assert.True(ReviewLock.IsConcluded(
            batch["REV-APPROVED"].Record, batch["REV-APPROVED"].Slots, batch["REV-APPROVED"].Signatures));
        Assert.False(ReviewLock.IsConcluded(
            batch["REV-STALE"].Record, batch["REV-STALE"].Slots, batch["REV-STALE"].Signatures));
        Assert.Single(batch["REV-STALE"].Signatures);
        Assert.Equal(2, batch["REV-BUSY"].Record.Threads.Count);
        Assert.Equal(["First", "Second"], batch["REV-BUSY"].Record.Threads[0].Comments.Select(c => c.Text));
        Assert.Equal([2, 1], batch["REV-BUSY"].Record.Ballots.OrderBy(b => b.SlotId).Select(b => b.Answers.Count));
        Assert.Single(batch["REV-BUSY"].Record.Ballots.Single(b => b.SlotId == busySlot).SectionStates);
        Assert.NotNull(batch["REV-BUSY"].Record.Checklist);
        Assert.Equal(2, batch["REV-BUSY"].Slots.Count);
        Assert.Empty(batch["REV-EMPTY"].Slots);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], batch["REV-REPO"].Record.Source.Changes.Select(c => c.Path));
        Assert.Empty(batch["REV-APPROVED"].Record.Threads);

        // Assert: the history of every reopened review equals the single read, and the others are absent
        Assert.Equal(["REV-REOPENED", "REV-STALE"], history.Keys.Order(StringComparer.Ordinal));
        var single2 = await db.Store.ListReopensAsync(projectId, "REV-REOPENED", cancellationToken);
        Assert.Equal(["First", "Second"], history["REV-REOPENED"].Select(r => r.Reason));
        Assert.Equal(single2.Select(r => r.Id), history["REV-REOPENED"].Select(r => r.Id));
        Assert.Equal(
            (await db.Store.ListReopensAsync(projectId, "REV-STALE", cancellationToken)).Select(r => r.Id),
            history["REV-STALE"].Select(r => r.Id));
    }

    /// <summary>
    ///     Test that no identifiers give an empty result, that an unknown identifier is absent, that a duplicated
    ///     identifier is read once, and that a review of another project is not found.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSnapshotsAsync_EmptyUnknownDuplicateAndForeignIds_AreAbsentOrReadOnce()
    {
        // Arrange: a review in the first project, none in the second
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddProjectAsync(db);
        var otherProjectId = await AddProjectAsync(db);
        await AddReviewAsync(db, projectId, "REV-1");

        // Act
        var none = await db.Store.ListSnapshotsAsync(projectId, [], cancellationToken);
        var noneReopens = await db.Store.ListReopensAsync(projectId, [], cancellationToken);
        var mixed = await db.Store.ListSnapshotsAsync(projectId, ["REV-1", "nope", "REV-1"], cancellationToken);
        var foreign = await db.Store.ListSnapshotsAsync(otherProjectId, ["REV-1"], cancellationToken);

        // Assert
        Assert.Empty(none);
        Assert.Empty(noneReopens);
        Assert.Equal(["REV-1"], mixed.Keys);
        Assert.Empty(foreign);
        await Assert.ThrowsAsync<ArgumentNullException>(() => db.Store.ListSnapshotsAsync(projectId, null!, cancellationToken));
    }

    /// <summary>
    ///     Test that more identifiers than one chunk are all read, wherever the existing reviews sit in the list.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSnapshotsAsync_MoreIdsThanOneChunk_ReadsAll()
    {
        // Arrange: two reviews, one first and one last in a list of 700 identifiers
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore(fastCommits: true);
        var projectId = await AddProjectAsync(db);
        await AddReviewAsync(db, projectId, "FIRST");
        await AddReviewAsync(db, projectId, "LAST");
        var ids = new[] { "FIRST" }.Concat(Enumerable.Range(0, 698).Select(i => "NOPE-" + i)).Append("LAST").ToList();

        // Act
        var batch = await db.Store.ListSnapshotsAsync(projectId, ids, cancellationToken);

        // Assert
        Assert.Equal(["FIRST", "LAST"], batch.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     Test that the number of queries of the batch read does not grow with the number of reviews, and that it is
    ///     far below what the single reads of the same reviews send.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSnapshotsAsync_MoreReviews_SendsTheSameNumberOfQueries()
    {
        // Arrange: a store over the same database that counts the commands it sends
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore(fastCommits: true);
        var counter = new CommandCounter();
        var options = new DatabaseOptions { ConnectionString = db.ConnectionString };
        using var provider = new ServiceCollection()
            .AddDbContextFactory<ReviewCenterDbContext>(builder =>
            {
                DatabaseSetup.Configure(builder, options);
                builder.AddInterceptors(counter);
            })
            .BuildServiceProvider();
        var store = new ReviewStore(
            provider.GetRequiredService<IDbContextFactory<ReviewCenterDbContext>>(),
            new AuditAppender(TimeProvider.System));
        var projectId = await AddProjectAsync(db);
        var ids = new List<string>();
        async Task AddMoreAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var id = $"REV-{ids.Count}";
                ids.Add(id);
                await AddReviewAsync(db, projectId, id);
                var thread = await AddThreadAsync(db, projectId, id, "Comment");

                // An open thread would refuse the signature
                await db.Store.ResolveThreadAsync(
                    projectId, id, thread.Id, ThreadKind.Question, TestContext.Current.CancellationToken);
                await SignAsync(db, projectId, id, await AddSlotAsync(db, projectId, id));
            }
        }

        await AddMoreAsync(2);

        // Act: read two reviews, add many more, read them all, and read them one by one
        counter.Count = 0;
        await store.ListSnapshotsAsync(projectId, ids.ToList(), cancellationToken);
        var few = counter.Count;
        await AddMoreAsync(18);
        counter.Count = 0;
        var all = await store.ListSnapshotsAsync(projectId, ids.ToList(), cancellationToken);
        var many = counter.Count;
        counter.Count = 0;
        foreach (var id in ids)
        {
            await store.GetSnapshotAsync(projectId, id, cancellationToken);
        }

        var single = counter.Count;

        // Assert
        Assert.Equal(20, all.Count);
        Assert.True(few > 0);
        Assert.Equal(few, many);
        Assert.True(single > many * 5, $"The single reads sent {single} commands, the batch read {many}.");
    }
}
