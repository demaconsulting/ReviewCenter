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

using System.Data;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Storage;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the reviewer slots, the record and the signatures of <see cref="ReviewStore"/> against a real
///     SQLite database file.
/// </summary>
public class ReviewStoreSigningTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c> of the file <c>a.txt</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="reviewId">The review id.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db, string reviewId = "REV-1")
    {
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, TestContext.Current.CancellationToken);
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = reviewId,
                Title = "Title",
                State = ReviewState.Draft,
                Created = _time,
                ContentFingerprint = TestContent.FingerprintA,
                Files =
                [
                    new ReviewFile
                    {
                        ProjectId = project.Id,
                        ReviewId = reviewId,
                        Path = "a.txt",
                        Content = TestContent.A,
                        Size = TestContent.A.Length,
                        Sha256 = TestContent.HashA
                    }
                ]
            },
            TestContext.Current.CancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Builds a slot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="primary">The primary user identity.</param>
    /// <param name="alternate">The alternate user identity, if any.</param>
    /// <returns>The slot.</returns>
    private static ReviewerSlot NewSlot(int projectId, int primary = 1, int? alternate = 2)
    {
        return new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = primary, AlternateUserId = alternate };
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
            Role = SlotRole.Alternate,
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
    ///     Adds a thread with one comment to <c>REV-1</c>. The comment is included in the report, so the thread is a
    ///     Finding that is part of the digest.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="text">The comment text.</param>
    /// <returns>The stored thread.</returns>
    private static Task<ReviewThread> AddThreadAsync(TestStore db, int projectId, string text)
    {
        return db.Store.AddThreadAsync(
            new ReviewThread
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                FilePath = "a.txt",
                State = ReviewThreadState.Open,
                Created = _time,
                Comments =
                [
                    new ReviewComment
                    {
                        Author = "Ann",
                        AuthorUsername = "ann",
                        Text = text,
                        Created = _time,
                        IncludeInReport = true
                    }
                ]
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that slots round-trip with database-assigned identities and are listed per review in order.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSlotAsync_TwoSlots_AssignsIdsAndListsInOrderPerReview()
    {
        // Arrange: a project with two reviews
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddReviewAsync(
            new Review
            {
                ProjectId = projectId,
                Id = "REV-2",
                Title = "Other",
                Created = _time,
                ContentFingerprint = TestContent.FingerprintA,
                Files = []
            },
            TestContext.Current.CancellationToken);

        // Act: add two slots (one with a supplied identity) to REV-1 and one to REV-2
        var first = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        var second = await db.Store.AddSlotAsync(
            new ReviewerSlot { Id = 4242, ProjectId = projectId, ReviewId = "REV-1", PrimaryUserId = 3 },
            TestContext.Current.CancellationToken);
        await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "REV-2", PrimaryUserId = 5 },
            TestContext.Current.CancellationToken);
        var list = await db.Store.ListSlotsAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert: identities are assigned by the database, only the own slots are listed, in creation order
        Assert.NotEqual(4242, second.Id);
        Assert.Equal([first.Id, second.Id], list.Select(s => s.Id));
        Assert.Equal((1, 2), (list[0].PrimaryUserId, list[0].AlternateUserId));
        Assert.Equal(3, list[1].PrimaryUserId);
        Assert.Null(list[1].AlternateUserId);
    }

    /// <summary>
    ///     Test that a slot of an unknown review is refused by the database.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSlotAsync_UnknownReview_IsRefusedByDatabase()
    {
        // Arrange: a project without that review
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act, Assert: the foreign key refuses the slot
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = "nope", PrimaryUserId = 1 },
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a slot can be updated and removed, and that unknown slots and other reviews report false.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateAndRemoveSlotAsync_ExistingAndUnknown_ChangesOrReturnsFalse()
    {
        // Arrange: a slot
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);

        // Act: update it, update and remove an unknown slot and a slot through another review, then remove it
        var updated = await db.Store.UpdateSlotAsync(projectId, "REV-1", slot.Id, 7, null, TestContext.Current.CancellationToken);
        var afterUpdate = (await db.Store.ListSlotsAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Single();
        var unknownUpdate = await db.Store.UpdateSlotAsync(projectId, "REV-1", 999, 7, null, TestContext.Current.CancellationToken);
        var otherReviewUpdate = await db.Store.UpdateSlotAsync(projectId, "REV-2", slot.Id, 8, null, TestContext.Current.CancellationToken);
        var unknownRemove = await db.Store.RemoveSlotAsync(projectId, "REV-1", 999, TestContext.Current.CancellationToken);
        var otherReviewRemove = await db.Store.RemoveSlotAsync(projectId, "REV-2", slot.Id, TestContext.Current.CancellationToken);
        var removed = await db.Store.RemoveSlotAsync(projectId, "REV-1", slot.Id, TestContext.Current.CancellationToken);

        // Assert: only the real slot changed and went away
        Assert.True(updated);
        Assert.Equal((7, default(int?)), (afterUpdate.PrimaryUserId, afterUpdate.AlternateUserId));
        Assert.False(unknownUpdate);
        Assert.False(otherReviewUpdate);
        Assert.False(unknownRemove);
        Assert.False(otherReviewRemove);
        Assert.True(removed);
        Assert.Empty(await db.Store.ListSlotsAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the record carries what the digest covers, and changes when a comment is added.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetRecordAsync_StoredReview_CarriesFingerprintAnswersThreadsAndComments()
    {
        // Arrange: a review with an answer, a section state and a thread with a comment
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.SetChecklistAnswerAsync(
            new ChecklistAnswer
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                ItemId = "REQ-01",
                Outcome = ChecklistOutcome.Fail,
                Justification = "Bad",
                AnsweredBy = "Ann",
                AnsweredByUsername = "ann",
                AnsweredAt = _time
            },
            TestContext.Current.CancellationToken);
        await db.Store.SetChecklistSectionStateAsync(
            new ChecklistSectionState
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                SectionId = "DES",
                Applicable = false,
                Reason = "None",
                SetBy = "Ann",
                SetByUsername = "ann",
                SetAt = _time
            },
            TestContext.Current.CancellationToken);
        var thread = await AddThreadAsync(db, projectId, "First");
        var before = await DigestAsync(db, projectId);

        // Act: read the record, add a comment and read the digest again
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", TestContext.Current.CancellationToken);
        await db.Store.AddCommentAsync(
            projectId,
            "REV-1",
            thread.Id,
            new ReviewComment
            {
                Author = "Bob",
                AuthorUsername = "bob",
                Text = "Second",
                Created = _time,
                IncludeInReport = true
            },
            TestContext.Current.CancellationToken);
        var after = await DigestAsync(db, projectId);

        // Assert: the record has every covered part, and the digest follows the comment
        Assert.NotNull(record);
        Assert.Equal(TestContent.FingerprintA, record.ContentFingerprint);
        Assert.Equal("REQ-01", record.Answers.Single().ItemId);
        Assert.False(record.SectionStates.Single().Applicable);
        Assert.Equal(["First"], record.Threads.Single().Comments.Select(c => c.Text));
        Assert.NotEqual(before, after);
    }

    /// <summary>
    ///     Test that the record of an unknown review is <c>null</c>.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetRecordAsync_UnknownReview_ReturnsNull()
    {
        // Arrange: a project without that review
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act, Assert: no record
        Assert.Null(await db.Store.GetRecordAsync(projectId, "nope", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the snapshot carries the record, the slots and the signatures of the review together.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetSnapshotAsync_SignedReview_CarriesRecordSlotsAndSignatures()
    {
        // Arrange: a review with a resolved thread, a slot and a signature of the current digest
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var thread = await AddThreadAsync(db, projectId, "First");
        await db.Store.ResolveThreadAsync(projectId, "REV-1", thread.Id, ThreadKind.Finding, TestContext.Current.CancellationToken);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        var digest = await DigestAsync(db, projectId);
        await db.Store.AddSignatureAsync(NewSignature(projectId, slot.Id, digest), TestContext.Current.CancellationToken);

        // Act
        var snapshot = await db.Store.GetSnapshotAsync(projectId, "REV-1", TestContext.Current.CancellationToken);

        // Assert: the three parts belong together: the signature matches the digest of the record in the snapshot
        Assert.NotNull(snapshot);
        Assert.Equal(digest, RecordDigest.Compute(snapshot.Record));
        Assert.Equal(slot.Id, snapshot.Slots.Single().Id);
        Assert.Equal(digest, snapshot.Signatures.Single().RecordDigest);
    }

    /// <summary>
    ///     Test that the snapshot of an unknown review is <c>null</c>.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetSnapshotAsync_UnknownReview_ReturnsNull()
    {
        // Arrange: a project without that review
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act, Assert: no snapshot
        Assert.Null(await db.Store.GetSnapshotAsync(projectId, "nope", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a signature of the current digest is stored and read back with every value.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSignatureAsync_CurrentDigest_StoresAndRoundTrips()
    {
        // Arrange: a slot and the current digest
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        var digest = await DigestAsync(db, projectId);

        // Act: sign, and list
        var stored = await db.Store.AddSignatureAsync(NewSignature(projectId, slot.Id, digest), TestContext.Current.CancellationToken);
        var read = (await db.Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Single();

        // Assert: assigned identity and every value survived
        Assert.True(stored.Id > 0);
        Assert.Equal(stored.Id, read.Id);
        Assert.Equal(slot.Id, read.SlotId);
        Assert.Equal(1, read.UserId);
        Assert.Equal("Ann", read.DisplayName);
        Assert.Equal(SlotRole.Alternate, read.Role);
        Assert.Equal(_time, read.SignedAt);
        Assert.Equal(digest, read.RecordDigest);
        Assert.Equal("I have reviewed this record and approve it.", read.Meaning);
    }

    /// <summary>
    ///     Test that a digest that is not the one of the stored record is refused inside the store and nothing
    ///     is stored, both for a made-up digest and for one that was current until the record changed.
    /// </summary>
    /// <param name="recordChanges">Whether the record changes after the digest was read.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewStore_AddSignatureAsync_StaleDigest_ThrowsRecordChangedAndStoresNothing(bool recordChanges)
    {
        // Arrange: a slot, and a digest that is either made up or was current and then went stale
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        var digest = new string('a', 64);
        if (recordChanges)
        {
            digest = await DigestAsync(db, projectId);
            await AddThreadAsync(db, projectId, "Late remark");
        }

        // Act: sign with that digest
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.AddSignatureAsync(
            NewSignature(projectId, slot.Id, digest), TestContext.Current.CancellationToken));

        // Assert: refused with the rule, and nothing is stored
        Assert.Equal(DomainRule.RecordChanged, exception.Rule);
        Assert.Empty(await db.Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a second signature of the same slot and digest is refused, which is what makes a double
    ///     submit harmless.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSignatureAsync_SameSlotAndDigestTwice_ThrowsSigningNotAllowed()
    {
        // Arrange: a signed slot
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        var digest = await DigestAsync(db, projectId);
        await db.Store.AddSignatureAsync(NewSignature(projectId, slot.Id, digest), TestContext.Current.CancellationToken);

        // Act: sign the same record for the same slot again
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.AddSignatureAsync(
            NewSignature(projectId, slot.Id, digest), TestContext.Current.CancellationToken));

        // Assert: refused, one signature
        Assert.Equal(DomainRule.SigningNotAllowed, exception.Rule);
        Assert.Single(await db.Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a signature of an unknown slot or review is refused.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSignatureAsync_UnknownSlotOrReview_IsRefused()
    {
        // Arrange: a review without slots
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var digest = await DigestAsync(db, projectId);

        // Act: sign an unknown slot, and sign in an unknown review
        var slot = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.AddSignatureAsync(
            NewSignature(projectId, 999, digest), TestContext.Current.CancellationToken));
        var review = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = projectId,
                ReviewId = "nope",
                SlotId = 1,
                DisplayName = "Ann",
                RecordDigest = digest,
                Meaning = SigningService.MeaningText
            },
            TestContext.Current.CancellationToken));

        // Assert: the rules are named
        Assert.Equal(DomainRule.SlotNotFound, slot.Rule);
        Assert.Equal(DomainRule.ReviewNotFound, review.Rule);
    }

    /// <summary>
    ///     Test that the database itself refuses to remove a slot that has a signature, as a backstop to the
    ///     service rule.
    /// </summary>
    [Fact]
    public async Task ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedByDatabase()
    {
        // Arrange: a signed slot next to an unsigned one, so the review is not approved and so not locked
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        await db.Store.AddSignatureAsync(
            NewSignature(projectId, slot.Id, await DigestAsync(db, projectId)), TestContext.Current.CancellationToken);

        // Act, Assert: removing the slot fails and the slot and its signature remain
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.RemoveSlotAsync(
            projectId, "REV-1", slot.Id, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await db.Store.ListSlotsAsync(projectId, "REV-1", TestContext.Current.CancellationToken)).Count);
        Assert.Single(await db.Store.ListSignaturesAsync(projectId, "REV-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Approves <c>REV-1</c> with one slot signed for the current digest, after one resolved thread was added.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The thread and the slot, and the digest of the approval.</returns>
    private static async Task<(ReviewThread Thread, ReviewerSlot Slot, string Digest)> ApproveAsync(TestStore db, int projectId)
    {
        var thread = await AddThreadAsync(db, projectId, "Looks odd");
        // The thread is a Finding, so it is resolved with a note
        await db.Store.ResolveThreadAsync(
            projectId, "REV-1", thread.Id, ThreadKind.Finding, TestContext.Current.CancellationToken);
        var slot = await db.Store.AddSlotAsync(NewSlot(projectId), TestContext.Current.CancellationToken);
        var digest = await DigestAsync(db, projectId);
        await db.Store.AddSignatureAsync(NewSignature(projectId, slot.Id, digest), TestContext.Current.CancellationToken);
        return (thread, slot, digest);
    }

    /// <summary>
    ///     Test that the store itself refuses every write to the reviewed content of an approved review with
    ///     <see cref="DomainRule.ReviewLocked"/>, inside its transaction, and that the same writes succeed again
    ///     after the review is reopened.
    /// </summary>
    [Fact]
    public async Task ReviewStore_WriteMethods_ApprovedReview_RefuseWithReviewLockedInTransaction()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var (thread, slot, digest) = await ApproveAsync(db, projectId);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;
        var answer = new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            ItemId = "REQ-01",
            Outcome = ChecklistOutcome.Pass,
            AnsweredBy = "Ann",
            AnsweredByUsername = "ann",
            AnsweredAt = _time
        };
        var section = new ChecklistSectionState
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SectionId = "REQ",
            Applicable = false,
            Reason = "Not relevant",
            SetBy = "Ann",
            SetByUsername = "ann",
            SetAt = _time
        };
        var writes = new Dictionary<string, Func<Task>>
        {
            ["thread"] = () => AddThreadAsync(db, projectId, "Another"),
            ["comment"] = () => db.Store.AddCommentAsync(
                projectId, "REV-1", thread.Id, new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = "More", Created = _time }, cancellationToken),
            ["state"] = () => db.Store.ReopenThreadAsync(projectId, "REV-1", thread.Id, cancellationToken),
            ["section"] = () => db.Store.SetChecklistSectionStateAsync(section, cancellationToken),
            ["answer"] = () => db.Store.SetChecklistAnswerAsync(answer, cancellationToken),
            ["add slot"] = () => db.Store.AddSlotAsync(NewSlot(projectId, 3, null), cancellationToken),
            ["update slot"] = () => db.Store.UpdateSlotAsync(projectId, "REV-1", slot.Id, 3, null, cancellationToken),
            ["remove slot"] = () => db.Store.RemoveSlotAsync(projectId, "REV-1", slot.Id, cancellationToken),
            ["due date"] = () => db.Store.UpdateDueDateAsync(
                projectId, "REV-1", null, new DateOnly(2030, 1, 1), AuditTestExtensions.Draft(), cancellationToken)
        };

        // Act, Assert: each write is refused as locked, and nothing changes
        foreach (var (name, write) in writes)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(write);
            Assert.True(ex.Rule == DomainRule.ReviewLocked, $"{name} gave {ex.Rule}");
        }

        // Assert: the record is exactly as approved: same digest, the one slot and the one thread with its comment and
        // the resolution note that approved it, and state, no answer and no section state, and no audit entry was
        // written by the refused writes
        Assert.Equal(digest, await DigestAsync(db, projectId));
        var storedSlot = Assert.Single(await db.Store.ListSlotsAsync(projectId, "REV-1", cancellationToken));
        Assert.Equal((slot.Id, 1, (int?)2), (storedSlot.Id, storedSlot.PrimaryUserId, storedSlot.AlternateUserId));
        var storedThread = Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken));
        Assert.Equal(thread.Id, storedThread.Id);
        Assert.Equal(ReviewThreadState.Resolved, storedThread.State);
        Assert.Equal(2, storedThread.Comments.Count);
        Assert.Equal("Looks odd", storedThread.Comments[0].Text);
        Assert.Empty(await db.Store.ListChecklistAnswersAsync(projectId, "REV-1", cancellationToken));
        Assert.Empty(await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-1", cancellationToken));
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);

        // Act: reopen, then write again
        await db.Store.ReopenReviewAsync(
            projectId,
            "REV-1",
            new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "Again", digest),
            AuditTestExtensions.Draft(),
            cancellationToken);
        await db.Store.SetChecklistAnswerAsync(answer, cancellationToken);

        // Assert: the answer is stored now
        Assert.Single(await db.Store.ListChecklistAnswersAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a reopen stores its history row, raises the reopen count of the record (so the digest changes),
    ///     keeps the old signature and writes exactly one audit entry of the review, and that the chain verifies.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ReopenReviewAsync_Persists_AuditAndHistory()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var (_, slot, digest) = await ApproveAsync(db, projectId);
        var draft = AuditDraft.By(TestActors.Ann, "review.reopened", $"previous digest {digest}; reason: Again", projectId, "REV-1");

        // Act: reopen
        await db.Store.ReopenReviewAsync(
            projectId, "REV-1", new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "Again", digest), draft, cancellationToken);

        // Assert: the history row, the record count and the digest
        var reopen = Assert.Single(await db.Store.ListReopensAsync(projectId, "REV-1", cancellationToken));
        Assert.True(reopen.Id > 0);
        Assert.Equal(("root", "Root", "Again", digest), (reopen.ReopenedBy, reopen.ReopenedByDisplayName, reopen.Reason, reopen.PreviousDigest));
        Assert.Equal(_time, reopen.ReopenedAt);
        Assert.Equal(1, (await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken))!.Reopens);
        Assert.NotEqual(digest, await DigestAsync(db, projectId));

        // Assert: the old signature is kept, no longer current, and the review is no longer approved
        var snapshot = (await db.Store.GetSnapshotAsync(projectId, "REV-1", cancellationToken))!;
        Assert.Single(snapshot.Signatures);
        Assert.Equal(slot.Id, snapshot.Signatures[0].SlotId);
        Assert.False(ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures));

        // Assert: exactly one audit entry of the review was written, with the draft's content, and the chain verifies
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken);
        var entry = Assert.Single(entries, e => e.Action == "review.reopened");
        Assert.Equal((projectId, "REV-1", draft.Detail), (entry.ProjectId, entry.ReviewId, entry.Detail));
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that the reopenings of a review are listed only for that review (not for another review of the same
    ///     project, nor for the same review identifier of another project) and in the order in which they were made.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListReopensAsync_SeveralReviewsAndReopens_AreScopedAndInCreationOrder()
    {
        // Arrange: three approved reviews, the same identifier in two projects and two identifiers in one project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectA = await AddReviewAsync(db);
        var projectB = await AddReviewAsync(db);
        await AddSecondReviewAsync(db, projectA, "REV-2");
        var slotA1 = await db.Store.AddSlotAsync(NewSlot(projectA), cancellationToken);
        var slotA2 = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectA, ReviewId = "REV-2", PrimaryUserId = 1, AlternateUserId = 2 }, cancellationToken);
        var slotB1 = await db.Store.AddSlotAsync(NewSlot(projectB), cancellationToken);
        await ApproveReviewAsync(db, projectA, "REV-1", slotA1.Id);
        await ApproveReviewAsync(db, projectA, "REV-2", slotA2.Id);
        await ApproveReviewAsync(db, projectB, "REV-1", slotB1.Id);

        // Act: approve, reopen and approve again, with the reopenings of the reviews interleaved
        await ReopenReviewAsync(db, projectA, "REV-1", "A1 first");
        await ReopenReviewAsync(db, projectB, "REV-1", "B1 first");
        await ReopenReviewAsync(db, projectA, "REV-2", "A2 first");
        await ApproveReviewAsync(db, projectA, "REV-1", slotA1.Id);
        await ReopenReviewAsync(db, projectA, "REV-1", "A1 second");
        await ApproveReviewAsync(db, projectA, "REV-1", slotA1.Id);
        await ReopenReviewAsync(db, projectB, "REV-1", "B1 second", approveFirst: slotB1.Id);
        await ReopenReviewAsync(db, projectA, "REV-1", "A1 third");

        // Assert: each list holds the reopenings of its review only, oldest first, and the identities grow
        var a1 = await db.Store.ListReopensAsync(projectA, "REV-1", cancellationToken);
        var a2 = await db.Store.ListReopensAsync(projectA, "REV-2", cancellationToken);
        var b1 = await db.Store.ListReopensAsync(projectB, "REV-1", cancellationToken);
        Assert.Equal(["A1 first", "A1 second", "A1 third"], a1.Select(r => r.Reason).ToArray());
        Assert.Equal(["A2 first"], a2.Select(r => r.Reason).ToArray());
        Assert.Equal(["B1 first", "B1 second"], b1.Select(r => r.Reason).ToArray());
        Assert.All(a1, r => Assert.Equal((projectA, "REV-1"), (r.ProjectId, r.ReviewId)));
        Assert.All(a2, r => Assert.Equal((projectA, "REV-2"), (r.ProjectId, r.ReviewId)));
        Assert.All(b1, r => Assert.Equal((projectB, "REV-1"), (r.ProjectId, r.ReviewId)));
        Assert.Equal(a1.OrderBy(r => r.Id).Select(r => r.Id), a1.Select(r => r.Id));
        Assert.Equal(b1.OrderBy(r => r.Id).Select(r => r.Id), b1.Select(r => r.Id));
        Assert.Empty(await db.Store.ListReopensAsync(projectB, "REV-2", cancellationToken));
        Assert.Equal(3, (await db.Store.GetRecordAsync(projectA, "REV-1", cancellationToken))!.Reopens);
    }

    /// <summary>
    ///     Adds a second review to an existing project.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    private static Task AddSecondReviewAsync(TestStore db, int projectId, string reviewId)
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
    ///     Signs the record of a review as it is stored now with the given slot, which approves the review.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>A task that completes when the signature is stored.</returns>
    private static async Task ApproveReviewAsync(TestStore db, int projectId, string reviewId, int slotId)
    {
        var record = await db.Store.GetRecordAsync(projectId, reviewId, TestContext.Current.CancellationToken);
        var signature = new ReviewSignature
        {
            ProjectId = projectId,
            ReviewId = reviewId,
            SlotId = slotId,
            UserId = 1,
            DisplayName = "Ann",
            Role = SlotRole.Alternate,
            SignedAt = _time,
            RecordDigest = RecordDigest.Compute(record!),
            Meaning = SigningService.MeaningText
        };
        await db.Store.AddSignatureAsync(signature, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reopens an approved review with the given reason, optionally approving it first.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="reason">The reason of the reopening.</param>
    /// <param name="approveFirst">The slot that signs the review before it is reopened, if any.</param>
    /// <returns>A task that completes when the review is reopened.</returns>
    private static async Task ReopenReviewAsync(TestStore db, int projectId, string reviewId, string reason, int? approveFirst = null)
    {
        if (approveFirst is { } slotId)
        {
            await ApproveReviewAsync(db, projectId, reviewId, slotId);
        }

        var record = await db.Store.GetRecordAsync(projectId, reviewId, TestContext.Current.CancellationToken);
        await db.Store.ReopenReviewAsync(
            projectId,
            reviewId,
            new ReviewReopen(0, projectId, reviewId, _time, "root", "Root", reason, RecordDigest.Compute(record!)),
            AuditDraft.By(TestActors.Ann, "review.reopened", reason, projectId, reviewId),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that a reopen of a review that is not approved (never approved, already reopened) or does not exist is
    ///     refused with the rule, and stores no row and no audit entry.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ReopenReviewAsync_NotApprovedOrUnknown_IsRefusedAndStoresNothing()
    {
        // Arrange: a review with a slot that has not signed, and an approved review that is reopened once
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var unsignedProject = await AddReviewAsync(db);
        await db.Store.AddSlotAsync(NewSlot(unsignedProject), cancellationToken);
        var approvedProject = await AddReviewAsync(db);
        var (_, _, digest) = await ApproveAsync(db, approvedProject);
        var draft = AuditDraft.By(TestActors.Ann, "review.reopened", "reopened", approvedProject, "REV-1");
        await db.Store.ReopenReviewAsync(
            approvedProject, "REV-1", new ReviewReopen(0, approvedProject, "REV-1", _time, "root", "Root", "Once", digest), draft, cancellationToken);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act: reopen the unsigned review, the review that is already reopened, and an unknown review
        var unsigned = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ReopenReviewAsync(
            unsignedProject, "REV-1", new ReviewReopen(0, unsignedProject, "REV-1", _time, "root", "Root", "Never", digest), draft, cancellationToken));
        var twice = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ReopenReviewAsync(
            approvedProject, "REV-1", new ReviewReopen(0, approvedProject, "REV-1", _time, "root", "Root", "Twice", digest), draft, cancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ReopenReviewAsync(
            approvedProject, "NOPE", new ReviewReopen(0, approvedProject, "NOPE", _time, "root", "Root", "Why", digest), draft, cancellationToken));

        // Assert: the rules are named, and no row and no entry was added
        Assert.Equal(DomainRule.ReviewNotLocked, unsigned.Rule);
        Assert.Equal(DomainRule.ReviewNotLocked, twice.Rule);
        Assert.Equal(DomainRule.ReviewNotFound, unknown.Rule);
        Assert.Empty(await db.Store.ListReopensAsync(unsignedProject, "REV-1", cancellationToken));
        Assert.Equal("Once", Assert.Single(await db.Store.ListReopensAsync(approvedProject, "REV-1", cancellationToken)).Reason);
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a reopen whose audit entry cannot be stored is rolled back as a whole: no history row, no change of
    ///     the reopen count, the review stays approved, and the same reopen works once the audit entry can be stored.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ReopenReviewAsync_AuditInsertFails_RollsBackTheReopen()
    {
        // Arrange: an approved review, and a database that refuses every audit entry
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var (_, _, digest) = await ApproveAsync(db, projectId);
        var draft = AuditDraft.By(TestActors.Ann, "review.reopened", "reopened", projectId, "REV-1");
        var reopen = new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "Again", digest);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;
        Execute(db, "CREATE TRIGGER refuse_audit BEFORE INSERT ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit refused'); END;");

        // Act: reopen
        await Assert.ThrowsAnyAsync<Exception>(() => db.Store.ReopenReviewAsync(projectId, "REV-1", reopen, draft, cancellationToken));

        // Assert: nothing was stored, and the review is still approved with the same digest
        Assert.Empty(await db.Store.ListReopensAsync(projectId, "REV-1", cancellationToken));
        Assert.Equal(0, (await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken))!.Reopens);
        Assert.Equal(digest, await DigestAsync(db, projectId));
        var snapshot = (await db.Store.GetSnapshotAsync(projectId, "REV-1", cancellationToken))!;
        Assert.True(ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures));
        Execute(db, "DROP TRIGGER refuse_audit");
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);

        // Act, Assert: with the audit entry storable again, the same reopen succeeds
        await db.Store.ReopenReviewAsync(projectId, "REV-1", reopen, draft, cancellationToken);
        Assert.Single(await db.Store.ListReopensAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that two reopens of one approved review made at the same time cannot both succeed: one stores its row and
    ///     entry, the other finds the review no longer approved and stores nothing.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ReopenReviewAsync_TwoAtOnce_ExactlyOneSucceeds()
    {
        // Arrange: an approved review
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var (_, _, digest) = await ApproveAsync(db, projectId);
        var draft = AuditDraft.By(TestActors.Ann, "review.reopened", "reopened", projectId, "REV-1");

        // Act: both administrators reopen at the same time
        var results = await Task.WhenAll(
            TryAsync(() => db.Store.ReopenReviewAsync(
                projectId, "REV-1", new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "First", digest), draft, cancellationToken)),
            TryAsync(() => db.Store.ReopenReviewAsync(
                projectId, "REV-1", new ReviewReopen(0, projectId, "REV-1", _time, "adm", "Adm", "Second", digest), draft, cancellationToken)));

        // Assert: exactly one succeeded, the other was told the review is not locked, and only one row and entry exist
        Assert.Single(results, r => r is null);
        Assert.Equal(DomainRule.ReviewNotLocked, Assert.Single(results, r => r is not null)!.Rule);
        Assert.Single(await db.Store.ListReopensAsync(projectId, "REV-1", cancellationToken));
        Assert.Equal(1, (await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken))!.Reopens);
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken);
        Assert.Single(entries, e => e.Action == "review.reopened");
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that the project setting defaults to on for a new project and is changed only if it still holds the
    ///     value the caller read, with the audit entry written in the same step.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateRequireIndependentReviewersAsync_CompareAndSet()
    {
        // Arrange: a new project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var draft = AuditDraft.By(TestActors.Ann, "project.independent-reviewers-changed", "RequireIndependentReviewers on->off", projectId);

        // Act: read the default, switch it off based on "on", try the same again, and try an unknown project
        var initial = (await db.Store.GetProjectAsync(projectId, cancellationToken))!.RequireIndependentReviewers;
        var first = await db.Store.UpdateRequireIndependentReviewersAsync(projectId, true, false, draft, cancellationToken);
        var stale = await db.Store.UpdateRequireIndependentReviewersAsync(projectId, true, false, draft, cancellationToken);
        var unknown = await db.Store.UpdateRequireIndependentReviewersAsync(999, true, false, draft, cancellationToken);

        // Assert: on by default, one change stored and audited once, the stale and unknown ones refused
        Assert.True(initial);
        Assert.True(first);
        Assert.False(stale);
        Assert.False(unknown);
        Assert.False((await db.Store.GetProjectAsync(projectId, cancellationToken))!.RequireIndependentReviewers);
        Assert.Equal(1, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count(e => e.Action == "project.independent-reviewers-changed"));
    }

    /// <summary>
    ///     Reads the value of one review rule from a project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The stored value.</returns>
    private static bool ReadRule(Project project, ReviewRule rule) => rule switch
    {
        ReviewRule.IndependentReviewers => project.RequireIndependentReviewers,
        ReviewRule.NonAuthorChecklistAnswers => project.RequireNonAuthorChecklistAnswers,
        _ => project.RequireNonReopenerSigning
    };

    /// <summary>
    ///     Test that every review rule defaults to on for a new project and is changed only if it still holds the
    ///     value the caller read, with the audit entry written in the same step.
    /// </summary>
    /// <param name="rule">The rule to change.</param>
    [Theory]
    [InlineData(ReviewRule.IndependentReviewers)]
    [InlineData(ReviewRule.NonAuthorChecklistAnswers)]
    [InlineData(ReviewRule.NonReopenerSigning)]
    public async Task ReviewStore_UpdateReviewRuleAsync_CompareAndSet(ReviewRule rule)
    {
        // Arrange: a new project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var draft = AuditDraft.By(TestActors.Ann, "project.review-rule-changed", $"{rule} on->off", projectId);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act: read the default, switch it off based on "on", try the same again, and try an unknown project
        var initial = ReadRule((await db.Store.GetProjectAsync(projectId, cancellationToken))!, rule);
        var first = await db.Store.UpdateReviewRuleAsync(projectId, rule, true, false, draft, cancellationToken);
        var stale = await db.Store.UpdateReviewRuleAsync(projectId, rule, true, false, draft, cancellationToken);
        var unknown = await db.Store.UpdateReviewRuleAsync(999, rule, true, false, draft, cancellationToken);

        // Assert: on by default, one change stored and audited once, the stale and unknown ones refused
        Assert.True(initial);
        Assert.True(first);
        Assert.False(stale);
        Assert.False(unknown);
        Assert.False(ReadRule((await db.Store.GetProjectAsync(projectId, cancellationToken))!, rule));
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken);
        Assert.Equal(auditBefore + 1, entries.Count);
        Assert.Equal(1, entries.Count(e => e.Action == "project.review-rule-changed"));
        Assert.True(AuditChain.Verify(entries).IsIntact);
    }

    /// <summary>
    ///     Test that changing one review rule never changes another.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateReviewRuleAsync_ChangesOnlyTheNamedRule()
    {
        // Arrange: a new project with all rules on
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);

        // Act: switch off one rule after the other
        foreach (var rule in Enum.GetValues<ReviewRule>())
        {
            var draft = AuditDraft.By(TestActors.Ann, "project.review-rule-changed", $"{rule} on->off", projectId);
            Assert.True(await db.Store.UpdateReviewRuleAsync(projectId, rule, true, false, draft, cancellationToken));

            // Assert: exactly the rules switched so far are off
            var project = (await db.Store.GetProjectAsync(projectId, cancellationToken))!;
            foreach (var other in Enum.GetValues<ReviewRule>())
            {
                Assert.Equal(other > rule, ReadRule(project, other));
            }
        }
    }

    /// <summary>
    ///     Test that a rule that is not a defined value is rejected and nothing is recorded.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateReviewRuleAsync_UndefinedRule_ThrowsArgumentOutOfRange()
    {
        // Arrange: a new project
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var draft = AuditDraft.By(TestActors.Ann, "project.review-rule-changed", "x", projectId);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;

        // Act and assert: the undefined rule is rejected and the audit trail is unchanged
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => db.Store.UpdateReviewRuleAsync(projectId, (ReviewRule)99, true, false, draft, cancellationToken));
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that the lock check does not refuse a write to a review that does not exist with
    ///     <see cref="DomainRule.ReviewLocked"/>: an unknown review is not locked, so each write ends the way it does
    ///     without any lock (not found, or refused by the relationships of the database), and stores nothing.
    /// </summary>
    [Fact]
    public async Task ReviewStore_WriteMethods_UnknownReview_AreNotRefusedAsLocked()
    {
        // Arrange: a project without the review "NOPE"
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var auditBefore = (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count;
        var writes = new Dictionary<string, Func<Task>>
        {
            ["thread"] = () => db.Store.AddThreadAsync(
                new ReviewThread
                {
                    ProjectId = projectId,
                    ReviewId = "NOPE",
                    FilePath = "a.txt",
                    State = ReviewThreadState.Open,
                    Created = _time,
                    Comments = [new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = "Hi", Created = _time }]
                },
                cancellationToken),
            ["comment"] = () => db.Store.AddCommentAsync(
                projectId, "NOPE", 1, new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = "More", Created = _time }, cancellationToken),
            ["state"] = () => db.Store.ReopenThreadAsync(projectId, "NOPE", 1, cancellationToken),
            ["section"] = () => db.Store.SetChecklistSectionStateAsync(
                new ChecklistSectionState
                {
                    ProjectId = projectId,
                    ReviewId = "NOPE",
                    SectionId = "REQ",
                    Applicable = false,
                    Reason = "Not relevant",
                    SetBy = "Ann",
                    SetByUsername = "ann",
                    SetAt = _time
                },
                cancellationToken),
            ["answer"] = () => db.Store.SetChecklistAnswerAsync(
                new ChecklistAnswer
                {
                    ProjectId = projectId,
                    ReviewId = "NOPE",
                    ItemId = "REQ-01",
                    Outcome = ChecklistOutcome.Pass,
                    AnsweredBy = "Ann",
                    AnsweredByUsername = "ann",
                    AnsweredAt = _time
                },
                cancellationToken),
            ["add slot"] = () => db.Store.AddSlotAsync(
                new ReviewerSlot { ProjectId = projectId, ReviewId = "NOPE", PrimaryUserId = 1 }, cancellationToken),
            ["update slot"] = () => db.Store.UpdateSlotAsync(projectId, "NOPE", 1, 3, null, cancellationToken),
            ["remove slot"] = () => db.Store.RemoveSlotAsync(projectId, "NOPE", 1, cancellationToken)
        };

        // Act, Assert: no write is refused as locked (a refusal by the database or a not-found result is fine)
        foreach (var (name, write) in writes)
        {
            var exception = await Record.ExceptionAsync(write);
            Assert.False(exception is DomainRuleException { Rule: DomainRule.ReviewLocked }, $"{name} was refused as locked");
        }

        // Assert: nothing was stored and no audit entry was written
        Assert.Empty(await db.Store.ListThreadsAsync(projectId, "NOPE", cancellationToken));
        Assert.Empty(await db.Store.ListSlotsAsync(projectId, "NOPE", cancellationToken));
        Assert.Empty(await db.Store.ListChecklistAnswersAsync(projectId, "NOPE", cancellationToken));
        Assert.Empty(await db.Store.ListChecklistSectionStatesAsync(projectId, "NOPE", cancellationToken));
        Assert.Equal(auditBefore, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a store write racing with the final signature of a review ends in one consistent state, whichever
    ///     comes first: when the signature is stored the write is refused as locked, otherwise the write is stored and
    ///     the signature is refused as a changed record. The signature is started first.
    /// </summary>
    [Fact]
    public Task ReviewStore_AddSignatureAsync_RacingWithWrite_SignatureStartedFirst_EndsConsistent()
    {
        return RaceFinalSignatureWithThreadAsync(signatureFirst: true);
    }

    /// <summary>
    ///     Test that a store write racing with the final signature of a review ends in one consistent state, whichever
    ///     comes first. The write is started first.
    /// </summary>
    [Fact]
    public Task ReviewStore_AddSignatureAsync_RacingWithWrite_WriteStartedFirst_EndsConsistent()
    {
        return RaceFinalSignatureWithThreadAsync(signatureFirst: false);
    }

    /// <summary>
    ///     Runs the final signature of a review and the addition of a thread at the same time, through the store, and
    ///     checks that exactly one of them took effect and that the stored state is consistent with it.
    /// </summary>
    /// <param name="signatureFirst"><c>true</c> to start the signature before the write, <c>false</c> for the reverse.</param>
    /// <returns>A task that completes when the checks are done.</returns>
    private static async Task RaceFinalSignatureWithThreadAsync(bool signatureFirst)
    {
        // Arrange: a review with two slots, the first signed, so that the second signature approves it, and the
        // digest the second signer saw
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await db.Store.AddSlotAsync(NewSlot(projectId, 1, null), cancellationToken);
        var second = await db.Store.AddSlotAsync(NewSlot(projectId, 2, null), cancellationToken);
        var digest = await DigestAsync(db, projectId);
        await db.Store.AddSignatureAsync(NewSignature(projectId, first.Id, digest), cancellationToken);
        Task<DomainRuleException?> Sign() => TryAsync(() => db.Store.AddSignatureAsync(
            NewSignature(projectId, second.Id, digest), cancellationToken));
        Task<DomainRuleException?> Write() => TryAsync(() => AddThreadAsync(db, projectId, "Late remark"));

        // Act: start both, in the stated order, and wait for both. Both queue for the audit gate, so the one started
        // first normally wins, but the checks below hold for either order.
        Task<DomainRuleException?> signing;
        Task<DomainRuleException?> writing;
        if (signatureFirst)
        {
            signing = Sign();
            writing = Write();
        }
        else
        {
            writing = Write();
            signing = Sign();
        }

        var signResult = await signing;
        var writeResult = await writing;

        // Assert: exactly one took effect, and the state agrees with the one that did
        Assert.True((signResult is null) != (writeResult is null), $"sign: {signResult?.Rule}, write: {writeResult?.Rule}");
        var threads = await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken);
        var signatures = await db.Store.ListSignaturesAsync(projectId, "REV-1", cancellationToken);
        var snapshot = (await db.Store.GetSnapshotAsync(projectId, "REV-1", cancellationToken))!;
        if (signResult is null)
        {
            Assert.Equal(DomainRule.ReviewLocked, writeResult!.Rule);
            Assert.Empty(threads);
            Assert.Equal(2, signatures.Count);
            Assert.True(ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures));
        }
        else
        {
            Assert.Equal(DomainRule.RecordChanged, signResult.Rule);
            Assert.Single(threads);
            Assert.Single(signatures);
            Assert.False(ReviewLock.IsConcluded(snapshot.Record, snapshot.Slots, snapshot.Signatures));
        }

        // Assert: the audit chain is intact
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Runs a call and returns the domain rule exception it raises, if any.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <returns>The exception, or <c>null</c> when the call succeeded.</returns>
    private static async Task<DomainRuleException?> TryAsync(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (DomainRuleException ex)
        {
            return ex;
        }
    }

    /// <summary>
    ///     Runs a SQL statement on the database file, for tests that need the database itself to misbehave.
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
    ///     Test that the transaction mode the store asks for takes the database write lock when it begins, so that
    ///     no other writer can change the record between the digest check and the insert. This is the mechanism behind
    ///     the store-level race tests above, shown at the level of the database.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SerializableTransaction_TakesWriteLockAtBegin()
    {
        // Arrange: a database, one connection with a Serializable transaction (as the store opens one) and
        // a second connection that gives up quickly
        using var db = new TestStore();
        await AddReviewAsync(db);
        await using var first = new SqliteConnection(db.ConnectionString);
        await first.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await first.BeginTransactionAsync(IsolationLevel.Serializable, TestContext.Current.CancellationToken);
        var secondString = new SqliteConnectionStringBuilder(db.ConnectionString) { DefaultTimeout = 1 }.ToString();
        await using var second = new SqliteConnection(secondString);
        await second.OpenAsync(TestContext.Current.CancellationToken);
        await using var write = second.CreateCommand();
        write.CommandText = "INSERT INTO Projects (Name, Description, Created) VALUES ('Other', '', '2025-01-01')";

        // Act: the second connection tries to write while the first has only begun its transaction
        var exception = await Assert.ThrowsAsync<SqliteException>(() => write.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        // Assert: the database is busy (SQLITE_BUSY), so the write lock was taken at the begin
        Assert.Equal(5, exception.SqliteErrorCode);
    }
}
