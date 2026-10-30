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
    ///     Adds a thread with one comment to <c>REV-1</c>.
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
                Comments = [new ReviewComment { Author = "Ann", AuthorUsername = "ann", Text = text, Created = _time }]
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
            new ReviewComment { Author = "Bob", AuthorUsername = "bob", Text = "Second", Created = _time },
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
        // Arrange: a review with a thread, a slot and a signature of the current digest
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await AddThreadAsync(db, projectId, "First");
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
        await db.Store.SetThreadStateAsync(
            projectId, "REV-1", thread.Id, ReviewThreadState.Resolved, TestContext.Current.CancellationToken);
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
            ["state"] = () => db.Store.SetThreadStateAsync(projectId, "REV-1", thread.Id, ReviewThreadState.Open, cancellationToken),
            ["section"] = () => db.Store.SetChecklistSectionStateAsync(section, cancellationToken),
            ["answer"] = () => db.Store.SetChecklistAnswerAsync(answer, cancellationToken),
            ["add slot"] = () => db.Store.AddSlotAsync(NewSlot(projectId, 3, null), cancellationToken),
            ["update slot"] = () => db.Store.UpdateSlotAsync(projectId, "REV-1", slot.Id, 3, null, cancellationToken),
            ["remove slot"] = () => db.Store.RemoveSlotAsync(projectId, "REV-1", slot.Id, cancellationToken)
        };

        // Act, Assert: each write is refused as locked, and nothing changes
        foreach (var (name, write) in writes)
        {
            var ex = await Assert.ThrowsAsync<DomainRuleException>(write);
            Assert.True(ex.Rule == DomainRule.ReviewLocked, $"{name} gave {ex.Rule}");
        }

        Assert.Equal(digest, await DigestAsync(db, projectId));
        Assert.Single(await db.Store.ListSlotsAsync(projectId, "REV-1", cancellationToken));
        Assert.Single(await db.Store.ListThreadsAsync(projectId, "REV-1", cancellationToken));

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
    ///     keeps the old signature and writes its audit entry; and that an unapproved or unknown review cannot be
    ///     reopened.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ReopenReviewAsync_Persists_AuditAndHistory()
    {
        // Arrange: an approved review and a second review that is not approved
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var (_, slot, digest) = await ApproveAsync(db, projectId);
        var draft = AuditDraft.By(TestActors.Ann, "review.reopened", $"previous digest {digest}; reason: Again", projectId, "REV-1");

        // Act: reopen
        await db.Store.ReopenReviewAsync(
            projectId, "REV-1", new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "Again", digest), draft, cancellationToken);
        var notApproved = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ReopenReviewAsync(
            projectId, "REV-1", new ReviewReopen(0, projectId, "REV-1", _time, "root", "Root", "Twice", digest), draft, cancellationToken));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.ReopenReviewAsync(
            projectId, "NOPE", new ReviewReopen(0, projectId, "NOPE", _time, "root", "Root", "Why", digest), draft, cancellationToken));

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
        Assert.False(ReviewLock.IsApproved(snapshot.Record, snapshot.Slots, snapshot.Signatures));

        // Assert: exactly one audit entry was written, in the chain, and a second reopen is refused
        Assert.Equal(1, (await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken)).Count(e => e.Action == "review.reopened"));
        Assert.Equal(DomainRule.ReviewNotLocked, notApproved.Rule);
        Assert.Equal(DomainRule.ReviewNotFound, unknown.Rule);
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
    ///     Test that the transaction mode the store asks for takes the database write lock when it begins, so that
    ///     no other writer can change the record between the digest check and the insert.
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
