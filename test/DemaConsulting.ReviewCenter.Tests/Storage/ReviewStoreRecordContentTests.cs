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
///     Tests for the record content of <see cref="ReviewStore"/> against a real SQLite database file: the report flag
///     and the kind of the comments, the resolution note of a Finding stored in the same transaction as the state,
///     promote, demote and the toggle of a reply, the refusal of every thread write on a concluded review, and the
///     open-thread rule of the final signature inside the write transaction.
/// </summary>
public class ReviewStoreRecordContentTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c> of the file <c>a.txt</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "REV-1",
                Title = "Title",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = TestContent.FingerprintA,
                Files =
                [
                    new ReviewFile
                    {
                        ProjectId = project.Id,
                        ReviewId = "REV-1",
                        Path = "a.txt",
                        Content = TestContent.A,
                        Size = TestContent.A.Length,
                        Sha256 = TestContent.HashA
                    }
                ]
            },
            cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Builds a comment.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="included">Whether the comment is included in the report.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Comment(string text, bool included = false)
    {
        return new ReviewComment
        {
            Author = "Ann",
            AuthorUsername = "ann",
            Text = text,
            Created = _time,
            IncludeInReport = included
        };
    }

    /// <summary>
    ///     Adds an open thread on <c>a.txt</c> whose starting comment has the given flag.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="included">Whether the starting comment is included, which makes the thread a Finding.</param>
    /// <returns>The stored thread.</returns>
    private static Task<ReviewThread> AddThreadAsync(TestStore db, int projectId, bool included)
    {
        return db.Store.AddThreadAsync(
            new ReviewThread
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                FilePath = "a.txt",
                State = ReviewThreadState.Open,
                Created = _time,
                Comments = [Comment("Starting comment", included)]
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a reply to a thread of <c>REV-1</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="threadId">The thread identity.</param>
    /// <param name="included">Whether the reply is included in the report.</param>
    /// <returns>The stored reply.</returns>
    private static async Task<ReviewComment> AddReplyAsync(TestStore db, int projectId, int threadId, bool included)
    {
        var reply = await db.Store.AddCommentAsync(
            projectId, "REV-1", threadId, Comment("A reply", included), TestContext.Current.CancellationToken);
        return reply!;
    }

    /// <summary>
    ///     Builds an audit draft for a thread action.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="action">The action.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(int projectId, string action)
    {
        return AuditDraft.By(TestActors.Ann, action, "detail of " + action, projectId, "REV-1");
    }

    /// <summary>
    ///     Reads the thread of <c>REV-1</c> again.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="threadId">The thread identity.</param>
    /// <returns>The stored thread.</returns>
    private static async Task<ReviewThread> ReadThreadAsync(TestStore db, int projectId, int threadId)
    {
        var threads = await db.Store.ListThreadsAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        return threads.Single(t => t.Id == threadId);
    }

    /// <summary>
    ///     Builds a slot of <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="user">The primary user identity.</param>
    /// <returns>The slot.</returns>
    private static ReviewerSlot NewSlot(int projectId, int user)
    {
        return new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = user };
    }

    /// <summary>
    ///     Builds a signature of the given digest.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="digest">The digest the signature binds itself to.</param>
    /// <returns>The signature.</returns>
    private static ReviewSignature NewSignature(int projectId, int slotId, string digest)
    {
        return new ReviewSignature
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SlotId = slotId,
            UserId = 1,
            DisplayName = "Ann",
            Role = SlotRole.Primary,
            SignedAt = _time,
            RecordDigest = digest,
            Meaning = SigningService.MeaningText
        };
    }

    /// <summary>
    ///     Computes the digest of <c>REV-1</c> as it is stored now.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The digest.</returns>
    private static async Task<string> DigestAsync(TestStore db, int projectId)
    {
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        return RecordDigest.Compute(record!);
    }

    /// <summary>
    ///     Reads the snapshot of <c>REV-1</c> and applies the conclusion rule to it.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns><c>true</c> when the review is concluded.</returns>
    private static async Task<bool> IsConcludedAsync(TestStore db, int projectId)
    {
        var snapshot = await db.Store.GetSnapshotAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        return ReviewLock.IsConcluded(snapshot!.Record, snapshot.Slots, snapshot.Signatures);
    }

    /// <summary>
    ///     Test that a thread added with an included starting comment stores the flag, and that the thread reads back
    ///     as a Finding, while a thread without the flag reads back as a Question.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddThreadAsync_IncludedComment_PersistsFlagAndKind()
    {
        // Arrange
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act
        var finding = await AddThreadAsync(db, projectId, true);
        var question = await AddThreadAsync(db, projectId, false);
        var storedFinding = await ReadThreadAsync(db, projectId, finding.Id);
        var storedQuestion = await ReadThreadAsync(db, projectId, question.Id);

        // Assert
        Assert.Equal(ThreadKind.Finding, storedFinding.Kind);
        Assert.True(Assert.Single(storedFinding.Comments).IncludeInReport);
        Assert.Equal(ReviewCommentKind.Comment, storedFinding.Comments[0].Kind);
        Assert.Equal(ThreadKind.Question, storedQuestion.Kind);
        Assert.False(Assert.Single(storedQuestion.Comments).IncludeInReport);
    }

    /// <summary>
    ///     Test that a reply added with the flag stores the flag.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddCommentAsync_IncludedReply_PersistsFlag()
    {
        // Arrange
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, false);

        // Act
        await AddReplyAsync(db, projectId, thread.Id, true);
        await AddReplyAsync(db, projectId, thread.Id, false);
        var stored = await ReadThreadAsync(db, projectId, thread.Id);

        // Assert: the thread stays a Question, and only the first reply is included
        Assert.Equal(ThreadKind.Question, stored.Kind);
        Assert.Equal([false, true, false], stored.Comments.Select(c => c.IncludeInReport));
    }

    /// <summary>
    ///     Test that the store refuses to resolve a Finding without a note, inside the transaction, and that the
    ///     thread stays open and no audit entry is written.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ResolveThreadAsync_FindingWithoutResolution_ThrowsResolutionNoteRequired()
    {
        // Arrange: a Finding
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, true);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, null, Draft(projectId, "thread.resolved"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ResolutionNoteRequired, exception.Rule);
        var stored = await ReadThreadAsync(db, projectId, thread.Id);
        Assert.Equal(ReviewThreadState.Open, stored.State);
        Assert.Single(stored.Comments);
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that the kind is read again inside the transaction and compared with the kind the caller expected: a
    ///     promote or a demote that lands between the caller's read and the write is refused with
    ///     <see cref="DomainRule.RecordChanged"/>, and nothing is stored and no audit entry is written; the thread
    ///     stays open and can be resolved by a caller who expects the kind it has now.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ResolveThreadAsync_KindChangedSinceRead_ThrowsRecordChangedAndStoresNothing()
    {
        // Arrange: a Finding that another actor demotes and a Question that another actor promotes after the
        // caller looked at them
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var demoted = await AddThreadAsync(db, projectId, true);
        var promoted = await AddThreadAsync(db, projectId, false);
        await db.Store.SetThreadKindAsync(
            projectId, "REV-1", demoted.Id, ThreadKind.Question, Draft(projectId, "thread.kind-changed"), cancellationToken);
        await db.Store.SetThreadKindAsync(
            projectId, "REV-1", promoted.Id, ThreadKind.Finding, Draft(projectId, "thread.kind-changed"), cancellationToken);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act: resolve both as a caller would that still expects the kind it read, one with and one without a note
        var demotedFailure = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ResolveThreadAsync(
            projectId, "REV-1", demoted.Id, ThreadKind.Finding, Comment("Done", true), Draft(projectId, "thread.resolved"), cancellationToken));
        var promotedFailure = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ResolveThreadAsync(
            projectId, "REV-1", promoted.Id, ThreadKind.Question, null, Draft(projectId, "thread.resolved"), cancellationToken));

        // Assert: refused as a changed record, with nothing stored and nothing audited
        Assert.Equal(DomainRule.RecordChanged, demotedFailure.Rule);
        Assert.Equal(DomainRule.RecordChanged, promotedFailure.Rule);
        foreach (var id in new[] { demoted.Id, promoted.Id })
        {
            var stored = await ReadThreadAsync(db, projectId, id);
            Assert.Equal(ReviewThreadState.Open, stored.State);
            Assert.Single(stored.Comments);
        }

        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);

        // And a caller who expects the kind the threads have now resolves them; the note of the Question is not included
        var plain = await db.Store.ResolveThreadAsync(
            projectId, "REV-1", demoted.Id, ThreadKind.Question, Comment("Done", true), Draft(projectId, "thread.resolved"), cancellationToken);
        var noted = await db.Store.ResolveThreadAsync(
            projectId, "REV-1", promoted.Id, ThreadKind.Finding, Comment("Fixed"), Draft(projectId, "thread.resolved"), cancellationToken);
        Assert.True(plain);
        Assert.True(noted);
        Assert.False((await ReadThreadAsync(db, projectId, demoted.Id)).Comments[1].IncludeInReport);
        Assert.True((await ReadThreadAsync(db, projectId, promoted.Id)).Comments[1].IncludeInReport);
    }

    /// <summary>
    ///     Test that no store method can resolve a thread without the resolution-note rule: a Finding cannot be
    ///     resolved without a note, the reopen method does not resolve, the store interface has no method that takes a
    ///     thread state (so <c>ResolveThreadAsync</c> is the only way to resolve), and reopening still works.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ThreadStateMethods_CannotResolveWithoutNote_ReopenStillWorks()
    {
        // Arrange: an open Finding
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var finding = await AddThreadAsync(db, projectId, true);

        // Act: the reopen method on the open Finding, which must not resolve it, and the resolve method without a note
        var reopened = await db.Store.ReopenThreadAsync(
            projectId, "REV-1", finding.Id, Draft(projectId, "thread.reopened"), cancellationToken);
        var afterReopen = await ReadThreadAsync(db, projectId, finding.Id);
        var refusal = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ResolveThreadAsync(
            projectId, "REV-1", finding.Id, ThreadKind.Finding, null, Draft(projectId, "thread.resolved"), cancellationToken));
        var afterRefusal = await ReadThreadAsync(db, projectId, finding.Id);

        // Assert: the thread was never resolved without a note
        Assert.True(reopened);
        Assert.Equal(ReviewThreadState.Open, afterReopen.State);
        Assert.Equal(DomainRule.ResolutionNoteRequired, refusal.Rule);
        Assert.Equal(ReviewThreadState.Open, afterRefusal.State);

        // Assert: the interface has no method that takes a thread state, so only the resolve method can resolve
        var stateMethods = typeof(IReviewStore).GetMethods()
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(ReviewThreadState)))
            .ToList();
        Assert.Empty(stateMethods);

        // Act: resolve with a note, reopen, and the thread is open again with the history kept
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", finding.Id, ThreadKind.Finding, Comment("Fixed it"), Draft(projectId, "thread.resolved"), cancellationToken);
        var reopenedAgain = await db.Store.ReopenThreadAsync(
            projectId, "REV-1", finding.Id, Draft(projectId, "thread.reopened"), cancellationToken);
        var final = await ReadThreadAsync(db, projectId, finding.Id);

        // Assert
        Assert.True(reopenedAgain);
        Assert.Equal(ReviewThreadState.Open, final.State);
        Assert.Equal(2, final.Comments.Count);
    }

    /// <summary>
    ///     Test that a Finding resolved with a note stores the state and an included Resolution comment in one
    ///     transaction, with one audit entry.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ResolveThreadAsync_FindingWithNote_StoresIncludedResolutionAndAudits()
    {
        // Arrange: a Finding
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, true);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act
        var resolved = await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, Comment("Fixed it"), Draft(projectId, "thread.resolved"), cancellationToken);

        // Assert: resolved, the note is an included Resolution comment, and one audit entry was added
        Assert.True(resolved);
        var stored = await ReadThreadAsync(db, projectId, thread.Id);
        Assert.Equal(ReviewThreadState.Resolved, stored.State);
        Assert.Equal(ThreadKind.Finding, stored.Kind);
        Assert.Equal(2, stored.Comments.Count);
        Assert.Equal("Fixed it", stored.Comments[1].Text);
        Assert.Equal(ReviewCommentKind.Resolution, stored.Comments[1].Kind);
        Assert.True(stored.Comments[1].IncludeInReport);
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken);
        Assert.Equal(auditBefore + 1, entries.Count);
        Assert.Equal("thread.resolved", entries[^1].Action);
    }

    /// <summary>
    ///     Test that a thread that is already resolved gives <c>false</c> and adds neither a comment nor an audit
    ///     entry, and so does an unknown thread.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ResolveThreadAsync_AlreadyResolved_ReturnsFalseAndAddsNothing()
    {
        // Arrange: a Finding that is resolved
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, true);
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, Comment("Fixed it"), Draft(projectId, "thread.resolved"), cancellationToken);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act
        var again = await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, Comment("Again"), Draft(projectId, "thread.resolved"), cancellationToken);
        var unknown = await db.Store.ResolveThreadAsync(
            projectId, "REV-1", 9999, ThreadKind.Finding, Comment("Nothing"), Draft(projectId, "thread.resolved"), cancellationToken);

        // Assert
        Assert.False(again);
        Assert.False(unknown);
        Assert.Equal(2, (await ReadThreadAsync(db, projectId, thread.Id)).Comments.Count);
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that promote and demote set the flag of the starting comment and of the resolution notes, and leave
    ///     the replies alone, that the same kind again changes nothing, and that an unknown thread gives <c>false</c>.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetThreadKindAsync_PromoteDemote_UpdatesStartingAndResolutionComments()
    {
        // Arrange: a resolved Question with an included reply and a resolution note
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, false);
        await AddReplyAsync(db, projectId, thread.Id, true);
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Question, Comment("Answered"), Draft(projectId, "thread.resolved"), cancellationToken);

        // Act: promote, promote again, demote, demote again, and an unknown thread
        var promoted = await db.Store.SetThreadKindAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, Draft(projectId, "thread.kind-changed"), cancellationToken);
        var afterPromote = await ReadThreadAsync(db, projectId, thread.Id);
        var promotedAgain = await db.Store.SetThreadKindAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, Draft(projectId, "thread.kind-changed"), cancellationToken);
        var demoted = await db.Store.SetThreadKindAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Question, Draft(projectId, "thread.kind-changed"), cancellationToken);
        var afterDemote = await ReadThreadAsync(db, projectId, thread.Id);
        var demotedAgain = await db.Store.SetThreadKindAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Question, Draft(projectId, "thread.kind-changed"), cancellationToken);
        var unknown = await db.Store.SetThreadKindAsync(
            projectId, "REV-1", 9999, ThreadKind.Finding, Draft(projectId, "thread.kind-changed"), cancellationToken);

        // Assert: the starting comment and the note follow the kind; the reply keeps its own flag
        Assert.True(promoted);
        Assert.Equal(ThreadKind.Finding, afterPromote.Kind);
        Assert.Equal([true, true, true], afterPromote.Comments.Select(c => c.IncludeInReport));
        Assert.False(promotedAgain);
        Assert.True(demoted);
        Assert.Equal(ThreadKind.Question, afterDemote.Kind);
        Assert.Equal([false, true, false], afterDemote.Comments.Select(c => c.IncludeInReport));
        Assert.False(demotedAgain);
        Assert.False(unknown);
    }

    /// <summary>
    ///     Test that a reply can be toggled on its own, that the same value again and an unknown comment give
    ///     <c>false</c>, and that the starting comment and a resolution note are refused with
    ///     <see cref="DomainRule.CommentNotTogglable"/>.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetCommentReportAsync_ReplyToggles_StartingAndResolutionRefused()
    {
        // Arrange: a resolved Question with a reply
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, false);
        var reply = await AddReplyAsync(db, projectId, thread.Id, false);
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Question, Comment("Answered"), Draft(projectId, "thread.resolved"), cancellationToken);
        var stored = await ReadThreadAsync(db, projectId, thread.Id);
        var startingId = stored.Comments[0].Id;
        var noteId = stored.Comments[2].Id;

        // Act
        var on = await db.Store.SetCommentReportAsync(
            projectId, "REV-1", thread.Id, reply.Id, true, Draft(projectId, "thread.comment-report-changed"), cancellationToken);
        var onAgain = await db.Store.SetCommentReportAsync(
            projectId, "REV-1", thread.Id, reply.Id, true, Draft(projectId, "thread.comment-report-changed"), cancellationToken);
        var unknown = await db.Store.SetCommentReportAsync(
            projectId, "REV-1", thread.Id, 9999, true, Draft(projectId, "thread.comment-report-changed"), cancellationToken);
        var starting = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.SetCommentReportAsync(
            projectId, "REV-1", thread.Id, startingId, true, Draft(projectId, "thread.comment-report-changed"), cancellationToken));
        var note = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.SetCommentReportAsync(
            projectId, "REV-1", thread.Id, noteId, true, Draft(projectId, "thread.comment-report-changed"), cancellationToken));
        var off = await db.Store.SetCommentReportAsync(
            projectId, "REV-1", thread.Id, reply.Id, false, Draft(projectId, "thread.comment-report-changed"), cancellationToken);

        // Assert
        Assert.True(on);
        Assert.False(onAgain);
        Assert.False(unknown);
        Assert.Equal(DomainRule.CommentNotTogglable, starting.Rule);
        Assert.Equal(DomainRule.CommentNotTogglable, note.Rule);
        Assert.True(off);
        var final = await ReadThreadAsync(db, projectId, thread.Id);
        Assert.Equal([false, false, false], final.Comments.Select(c => c.IncludeInReport));
    }

    /// <summary>
    ///     Test that every thread write of the store is refused with <see cref="DomainRule.ReviewLocked"/> on a
    ///     concluded review, and that nothing is stored.
    /// </summary>
    /// <param name="write">The write: <c>state</c>, <c>resolve</c>, <c>kind</c> or <c>report</c>.</param>
    [Theory]
    [InlineData("state")]
    [InlineData("resolve")]
    [InlineData("kind")]
    [InlineData("report")]
    public async Task ReviewStore_ThreadWrites_ConcludedReview_ThrowReviewLocked(string write)
    {
        // Arrange: a concluded review: a resolved thread with a reply, and its only slot signed
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, false);
        var reply = await AddReplyAsync(db, projectId, thread.Id, false);
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Question, cancellationToken);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId, 1), cancellationToken);
        var digest = await DigestAsync(db, projectId);
        await db.Store.AddSignatureAsync(NewSignature(projectId, slot.Id, digest), cancellationToken);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;
        Func<Task> action = write switch
        {
            "state" => () => db.Store.ReopenThreadAsync(
                projectId, "REV-1", thread.Id, cancellationToken),
            "resolve" => () => db.Store.ResolveThreadAsync(
                projectId, "REV-1", thread.Id, ThreadKind.Question, Comment("Note"), Draft(projectId, "thread.resolved"), cancellationToken),
            "kind" => () => db.Store.SetThreadKindAsync(
                projectId, "REV-1", thread.Id, ThreadKind.Finding, Draft(projectId, "thread.kind-changed"), cancellationToken),
            _ => () => db.Store.SetCommentReportAsync(
                projectId, "REV-1", thread.Id, reply.Id, true, Draft(projectId, "thread.comment-report-changed"), cancellationToken)
        };

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(action);

        // Assert: refused as locked, and the record is exactly as signed
        Assert.Equal(DomainRule.ReviewLocked, exception.Rule);
        Assert.Equal(digest, await DigestAsync(db, projectId));
        var stored = await ReadThreadAsync(db, projectId, thread.Id);
        Assert.Equal(ReviewThreadState.Resolved, stored.State);
        Assert.Equal(2, stored.Comments.Count);
        Assert.Equal(ThreadKind.Question, stored.Kind);
        Assert.False(stored.Comments[1].IncludeInReport);
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that the store refuses a signature while a thread is open, even with the current digest, and stores
    ///     nothing. A chat thread is not part of the digest, so only the open-thread rule stops it.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSignatureAsync_OpenThread_ThrowsSigningNotAllowedAndStoresNothing()
    {
        // Arrange: a slot and an open chat thread
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId, 1), cancellationToken);
        await AddThreadAsync(db, projectId, false);
        var digest = await DigestAsync(db, projectId);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.AddSignatureAsync(
            NewSignature(projectId, slot.Id, digest), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        Assert.Empty(await db.Store.ListSignaturesAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a chat thread opened after the first signature, which leaves the digest unchanged, still stops
    ///     the final signature inside the write transaction, so the review does not conclude over an open thread.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSignatureAsync_ThreadOpenedBeforeFinalSignature_IsRefused()
    {
        // Arrange: two slots, the first signed, then a chat thread that does not change the digest
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await db.Store.AddSlotAsync(NewSlot(projectId, 1), cancellationToken);
        var second = await db.Store.AddSlotAsync(NewSlot(projectId, 2), cancellationToken);
        var digest = await DigestAsync(db, projectId);
        await db.Store.AddSignatureAsync(NewSignature(projectId, first.Id, digest), cancellationToken);
        await AddThreadAsync(db, projectId, false);
        Assert.Equal(digest, await DigestAsync(db, projectId));

        // Act: the second signer signs the digest they saw
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.AddSignatureAsync(
            NewSignature(projectId, second.Id, digest), cancellationToken));

        // Assert: refused by the open-thread rule, not as a changed record; one signature, not concluded
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        Assert.Single(await db.Store.ListSignaturesAsync(projectId, "REV-1", cancellationToken));
        Assert.False(await IsConcludedAsync(db, projectId));
    }

    /// <summary>
    ///     Test that the lock the store applies to its writes is the conclusion rule of the record: an all-signed
    ///     review with an open thread is not locked, and it locks when the thread is resolved.
    /// </summary>
    [Fact]
    public async Task ReviewStore_IsConcluded_Records_FollowReviewLock()
    {
        // Arrange: slot one signed, a chat thread opened, and the unsigned second slot removed
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await db.Store.AddSlotAsync(NewSlot(projectId, 1), cancellationToken);
        var second = await db.Store.AddSlotAsync(NewSlot(projectId, 2), cancellationToken);
        await db.Store.AddSignatureAsync(
            NewSignature(projectId, first.Id, await DigestAsync(db, projectId)), cancellationToken);
        var thread = await AddThreadAsync(db, projectId, false);
        await db.Store.RemoveSlotAsync(projectId, "REV-1", second.Id, cancellationToken);

        // Act: with the thread open, a thread write is allowed; resolving it concludes the review
        var concludedOpen = await IsConcludedAsync(db, projectId);
        var reply = await AddReplyAsync(db, projectId, thread.Id, false);
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Question, cancellationToken);
        var concludedResolved = await IsConcludedAsync(db, projectId);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ReopenThreadAsync(
            projectId, "REV-1", thread.Id, cancellationToken));

        // Assert: the rule says open: not concluded, and resolved: concluded, and the store agrees
        Assert.False(concludedOpen);
        Assert.True(reply.Id > 0);
        Assert.True(concludedResolved);
        Assert.Equal(DomainRule.ReviewLocked, exception.Rule);
    }

    /// <summary>
    ///     Test that the comment columns default to a plain chat comment: a row inserted without the flag and the
    ///     kind reads back as not included and of kind Comment, and the kind is stored by name.
    /// </summary>
    [Fact]
    public async Task ReviewCenterDbContext_Model_CommentColumns_DefaultToChatAndComment()
    {
        // Arrange: a thread, and a comment row inserted without the new columns
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, true);
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var threadId = thread.Id;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO Comments (ThreadId, Author, AuthorUsername, Text, Created) VALUES ({threadId}, 'Bob', 'bob', 'Raw', '2025-03-04 05:06:07+00:00')",
            cancellationToken);

        // Act
        var stored = await ReadThreadAsync(db, projectId, thread.Id);
        var kinds = await context.Database
            .SqlQueryRaw<string>("SELECT Kind AS Value FROM Comments ORDER BY Id")
            .ToListAsync(cancellationToken);

        // Assert: the raw row has the defaults, and the kind is text
        var raw = stored.Comments[1];
        Assert.Equal("Raw", raw.Text);
        Assert.False(raw.IncludeInReport);
        Assert.Equal(ReviewCommentKind.Comment, raw.Kind);
        Assert.Equal(["Comment", "Comment"], kinds);
    }

    /// <summary>
    ///     Test that the kind of a thread read from the store is derived from the flag of its starting comment, and
    ///     that the flags of the replies do not matter.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetRecordAsync_ThreadKind_DerivedFromStartingComment()
    {
        // Arrange: a Question with an included reply and a Finding with a plain reply
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var question = await AddThreadAsync(db, projectId, false);
        await AddReplyAsync(db, projectId, question.Id, true);
        var finding = await AddThreadAsync(db, projectId, true);
        await AddReplyAsync(db, projectId, finding.Id, false);

        // Act
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ThreadKind.Question, record!.Threads.Single(t => t.Id == question.Id).Kind);
        Assert.Equal(ThreadKind.Finding, record.Threads.Single(t => t.Id == finding.Id).Kind);
    }
}
