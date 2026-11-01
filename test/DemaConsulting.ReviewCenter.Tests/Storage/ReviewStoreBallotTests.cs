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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the ballots of <see cref="ReviewStore"/> against a real SQLite database file: the answers and
///     section states stored for each reviewer slot, the ballot status with its compare-and-set hand-in, the audit
///     entries of a change of the status, and what adding, reassigning and removing a slot does to a ballot.
/// </summary>
/// <remarks>
///     The ballots are dormant in this change: the review-level rows of the record, and so the digest, never see them.
/// </remarks>
public class ReviewStoreBallotTests
{
    /// <summary>
    ///     The time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The test of a complete ballot that the tests give the store: it holds at least one answer.
    /// </summary>
    private static readonly Func<Ballot, bool> _hasAnAnswer = ballot => ballot.Answers.Count > 0;

    /// <summary>
    ///     Builds a review of the file <c>a.txt</c> for the tests.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="follows">The followed review, or <c>null</c>.</param>
    /// <param name="carry">The carried rows, or <c>null</c>.</param>
    /// <returns>The review, ready to add.</returns>
    private static Review NewReview(int projectId, string reviewId, string? follows = null, ReviewCarryOver? carry = null)
    {
        return new Review
        {
            ProjectId = projectId,
            Id = reviewId,
            Title = "Title",
            State = ReviewState.Draft,
            Created = _time,
            ContentFingerprint = TestContent.FingerprintA,
            FollowsReviewId = follows,
            CarriedAnswerCount = carry?.Answers.Count ?? 0,
            CarryOver = carry,
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
        };
    }

    /// <summary>
    ///     Adds a project with the review <c>REV-1</c> of the file <c>a.txt</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddReviewAsync(TestStore db)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var project = await db.Store.AddProjectAsync(new Project { Name = "P", Created = _time }, cancellationToken);
        await db.Store.AddReviewAsync(NewReview(project.Id, "REV-1"), cancellationToken);
        return project.Id;
    }

    /// <summary>
    ///     Adds the follow-up review <c>REV-2</c> of <c>REV-1</c> that carries one answer and one section state of the
    ///     user <c>ann</c>.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>A task that completes when the review is stored.</returns>
    private static async Task AddFollowUpAsync(TestStore db, int projectId)
    {
        var carry = new ReviewCarryOver(
            [
                new ChecklistAnswer
                {
                    ProjectId = projectId,
                    ReviewId = "REV-2",
                    ItemId = "REQ-01",
                    Outcome = ChecklistOutcome.Pass,
                    AnsweredBy = "Ann",
                    AnsweredByUsername = "ann",
                    AnsweredAt = _time,
                    CarriedFromReviewId = "REV-1"
                }
            ],
            [
                new ChecklistSectionState
                {
                    ProjectId = projectId,
                    ReviewId = "REV-2",
                    SectionId = "docs",
                    Applicable = false,
                    Reason = "No documents",
                    SetBy = "Ann",
                    SetByUsername = "ann",
                    SetAt = _time,
                    CarriedFromReviewId = "REV-1"
                }
            ],
            [],
            0,
            []);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-2", "REV-1", carry), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a slot to a review.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="primary">The primary user identity.</param>
    /// <param name="alternate">The alternate user identity, if any.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The identity of the slot.</returns>
    private static async Task<int> AddSlotAsync(
        TestStore db,
        int projectId,
        int primary = 1,
        int? alternate = null,
        string reviewId = "REV-1")
    {
        var slot = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = reviewId, PrimaryUserId = primary, AlternateUserId = alternate },
            TestContext.Current.CancellationToken);
        return slot.Id;
    }

    /// <summary>
    ///     Adds a user.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> AddUserAsync(TestStore db, string username)
    {
        var user = await db.Users.AddAsync(
            new User { Username = username, DisplayName = username, Enabled = true, PasswordHash = "hash", Created = _time },
            TestContext.Current.CancellationToken);
        return user.Id;
    }

    /// <summary>
    ///     Builds an answer in the ballot of a slot of <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="itemId">The check identifier.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="username">The user name of the answerer.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer NewAnswer(
        int projectId,
        int slotId,
        string itemId = "REQ-01",
        ChecklistOutcome outcome = ChecklistOutcome.Pass,
        string username = "ann")
    {
        return new ChecklistAnswer
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SlotId = slotId,
            ItemId = itemId,
            Outcome = outcome,
            AnsweredBy = username,
            AnsweredByUsername = username,
            AnsweredAt = _time
        };
    }

    /// <summary>
    ///     Builds a section state in the ballot of a slot of <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="sectionId">The section identifier.</param>
    /// <param name="applicable">Whether the section applies.</param>
    /// <param name="username">The user name of the person who set the state.</param>
    /// <returns>The section state.</returns>
    private static ChecklistSectionState NewSection(
        int projectId,
        int slotId,
        string sectionId = "docs",
        bool applicable = false,
        string username = "ann")
    {
        return new ChecklistSectionState
        {
            ProjectId = projectId,
            ReviewId = "REV-1",
            SlotId = slotId,
            SectionId = sectionId,
            Applicable = applicable,
            Reason = applicable ? null : "No documents",
            SetBy = username,
            SetByUsername = username,
            SetAt = _time
        };
    }

    /// <summary>
    ///     Builds an audit draft of a change in <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="action">The action of the entry.</param>
    /// <returns>The draft.</returns>
    private static AuditDraft Draft(int projectId, string action = "test.change")
    {
        return AuditDraft.By(TestActors.Ann, action, "ballot test", projectId, "REV-1");
    }

    /// <summary>
    ///     Reads the actions of all audit entries in the order they were recorded.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The actions.</returns>
    private static async Task<List<string>> ActionsAsync(TestStore db)
    {
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, TestContext.Current.CancellationToken);
        return entries.Select(entry => entry.Action).ToList();
    }

    /// <summary>
    ///     Reads the last audit entry.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <returns>The entry.</returns>
    private static async Task<AuditEntry> LastEntryAsync(TestStore db)
    {
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, TestContext.Current.CancellationToken);
        return entries[^1];
    }

    /// <summary>
    ///     Stores an answer in the ballot of a slot without an automatic hand-in.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="answer">The answer.</param>
    /// <returns>The ballot as stored.</returns>
    private static Task<Ballot> SetAsync(TestStore db, ChecklistAnswer answer)
    {
        return db.Store.SetBallotAnswerAsync(answer, Draft(answer.ProjectId), null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Hands in the ballot of a slot of <c>REV-1</c> with a complete ballot.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <returns>The ballot as stored.</returns>
    private static Task<Ballot> SubmitAsync(TestStore db, int projectId, int slotId)
    {
        return db.Store.SubmitBallotAsync(
            projectId,
            "REV-1",
            slotId,
            _time,
            "ann",
            Draft(projectId, "test.submit"),
            _ => true,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Test that the first answer of a slot creates a draft ballot with that answer and records the audit entry the
    ///     caller supplied.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_FirstAnswer_CreatesDraftBallot()
    {
        // Arrange: a review with two slots, so the ballot is never handed in by the edit
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        var before = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var ballot = await SetAsync(db, NewAnswer(projectId, slotId));
        var stored = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);

        // Assert
        Assert.Null(before);
        Assert.Equal(BallotStatus.Draft, ballot.Status);
        Assert.Null(ballot.SubmittedAt);
        Assert.Null(ballot.SubmittedByUsername);
        var answer = Assert.Single(ballot.Answers);
        Assert.Equal("REQ-01", answer.ItemId);
        Assert.Equal(slotId, answer.SlotId);
        Assert.Equal(BallotStatus.Draft, stored!.Status);
        Assert.Equal(slotId, stored.SlotId);
        Assert.Equal(auditBefore + 1, (await ActionsAsync(db)).Count);
        Assert.Equal("test.change", (await LastEntryAsync(db)).Action);
    }

    /// <summary>
    ///     Test that the same check answered in two slots is stored as two rows, one in each ballot, so the slots never
    ///     overwrite each other.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_SameItemTwoSlots_StoresOneRowPerSlot()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await AddSlotAsync(db, projectId);
        var second = await AddSlotAsync(db, projectId, 2);

        // Act
        await SetAsync(db, NewAnswer(projectId, first, "REQ-01", ChecklistOutcome.Pass));
        await SetAsync(db, NewAnswer(projectId, second, "REQ-01", ChecklistOutcome.Fail, "bob"));

        // Assert: one row for each slot, each with its own outcome and author
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        var rows = await context.ChecklistAnswers.AsNoTracking().OrderBy(a => a.SlotId).ToListAsync(cancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Equal((first, ChecklistOutcome.Pass, "ann"), (rows[0].SlotId, rows[0].Outcome, rows[0].AnsweredByUsername));
        Assert.Equal((second, ChecklistOutcome.Fail, "bob"), (rows[1].SlotId, rows[1].Outcome, rows[1].AnsweredByUsername));
    }

    /// <summary>
    ///     Test that a second answer to the same check in the same slot replaces the first.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_SameSlotAgain_ReplacesTheAnswer()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, slotId, "REQ-01", ChecklistOutcome.Pass));

        // Act
        await SetAsync(db, NewAnswer(projectId, slotId, "REQ-01", ChecklistOutcome.Fail));

        // Assert
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        var answer = Assert.Single(ballot!.Answers);
        Assert.Equal(ChecklistOutcome.Fail, answer.Outcome);
    }

    /// <summary>
    ///     Test that a slot that does not exist, or that belongs to another review, is refused as not found and nothing
    ///     is stored for it.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_UnknownOrForeignSlot_ThrowsSlotNotFound()
    {
        // Arrange: a slot of the second review, which the first review must not accept
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.AddReviewAsync(NewReview(projectId, "REV-3"), cancellationToken);
        var foreign = await AddSlotAsync(db, projectId, reviewId: "REV-3");
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(() => SetAsync(db, NewAnswer(projectId, 999)));
        var other = await Assert.ThrowsAsync<DomainRuleException>(() => SetAsync(db, NewAnswer(projectId, foreign)));

        // Assert
        Assert.Equal(DomainRule.SlotNotFound, unknown.Rule);
        Assert.Equal(DomainRule.SlotNotFound, other.Rule);
        Assert.Empty(await db.Store.ListBallotsAsync(projectId, "REV-1", cancellationToken));
        Assert.Empty(await db.Store.ListBallotsAsync(projectId, "REV-3", cancellationToken));
        Assert.Equal(auditBefore, (await ActionsAsync(db)).Count);
    }

    /// <summary>
    ///     Test that the review-level identity 0 is not a slot: the ballot methods refuse it, and the review-level
    ///     methods refuse the identity of a slot.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_BaselineSlotId_IsRefused()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => SetAsync(db, NewAnswer(projectId, ChecklistAnswer.BaselineSlotId)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.Store.SetBallotSectionStateAsync(
            NewSection(projectId, ChecklistAnswer.BaselineSlotId), Draft(projectId), null, cancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => SubmitAsync(db, projectId, ChecklistAnswer.BaselineSlotId));
        await Assert.ThrowsAsync<ArgumentException>(
            () => db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, slotId), cancellationToken));
        Assert.Empty(await db.Store.ListBallotsAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a concluded review refuses a ballot edit as locked inside the store and keeps the ballot and the
    ///     audit trail as they were.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_ConcludedReview_ThrowsReviewLocked()
    {
        // Arrange: the only slot signs the record
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);
        await db.Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                SlotId = slotId,
                UserId = 1,
                DisplayName = "Ann",
                Role = SlotRole.Primary,
                SignedAt = _time,
                RecordDigest = RecordDigest.Compute(record!),
                Meaning = SigningService.MeaningText
            },
            cancellationToken);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var edit = await Assert.ThrowsAsync<DomainRuleException>(() => SetAsync(db, NewAnswer(projectId, slotId)));
        var section = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.SetBallotSectionStateAsync(
            NewSection(projectId, slotId), Draft(projectId), null, cancellationToken));
        var submit = await Assert.ThrowsAsync<DomainRuleException>(() => SubmitAsync(db, projectId, slotId));

        // Assert
        Assert.Equal(DomainRule.ReviewLocked, edit.Rule);
        Assert.Equal(DomainRule.ReviewLocked, section.Rule);
        Assert.Equal(DomainRule.ReviewLocked, submit.Rule);
        Assert.Null(await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken));
        Assert.Equal(auditBefore, (await ActionsAsync(db)).Count);
    }

    /// <summary>
    ///     Test that the section states of two slots are kept apart and round-trip with every column through the ballot.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotSectionStateAsync_PerSlotRows_RoundTrip()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await AddSlotAsync(db, projectId);
        var second = await AddSlotAsync(db, projectId, 2);

        // Act: the same section, not applicable in the first slot and applicable in the second; then a replacement
        await db.Store.SetBallotSectionStateAsync(NewSection(projectId, first), Draft(projectId), null, cancellationToken);
        await db.Store.SetBallotSectionStateAsync(
            NewSection(projectId, second, applicable: true, username: "bob"), Draft(projectId), null, cancellationToken);
        await db.Store.SetBallotSectionStateAsync(
            NewSection(projectId, first, applicable: true), Draft(projectId), null, cancellationToken);
        var ballots = await db.Store.ListBallotsAsync(projectId, "REV-1", cancellationToken);

        // Assert
        Assert.Equal(2, ballots.Count);
        var one = Assert.Single(ballots[0].SectionStates);
        Assert.Equal((first, "docs", true, "ann"), (one.SlotId, one.SectionId, one.Applicable, one.SetByUsername));
        Assert.Null(one.Reason);
        var two = Assert.Single(ballots[1].SectionStates);
        Assert.Equal((second, "docs", true, "bob"), (two.SlotId, two.SectionId, two.Applicable, two.SetByUsername));
        Assert.Equal(_time, two.SetAt);
        Assert.Empty(await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that an edit of a handed-in ballot makes it a draft again and records a reopen entry with the slot
    ///     number only, after the entry of the edit itself.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_SubmittedBallot_RevertsToDraftWithAudit()
    {
        // Arrange: a handed-in ballot in the second slot of two
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await AddSlotAsync(db, projectId);
        var slotId = await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, slotId));
        await SubmitAsync(db, projectId, slotId);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var ballot = await SetAsync(db, NewAnswer(projectId, slotId, outcome: ChecklistOutcome.Fail));

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot.Status);
        Assert.Null(ballot.SubmittedAt);
        Assert.Null(ballot.SubmittedByUsername);
        var actions = await ActionsAsync(db);
        Assert.Equal(auditBefore + 2, actions.Count);
        Assert.Equal(["test.change", "ballot.reopened"], actions.Skip(auditBefore));
        Assert.Equal("slot 2: ballot reverted to draft by an edit", (await LastEntryAsync(db)).Detail);
    }

    /// <summary>
    ///     Test that in a review with exactly one slot a complete ballot is handed in by the edit itself, stamped with
    ///     the time and the user of the edit, and that the entry of the edit is followed by a submit entry.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_SingleSlotCompleteBallot_AutoSubmitsWithAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var ballot = await db.Store.SetBallotAnswerAsync(
            NewAnswer(projectId, slotId), Draft(projectId), _hasAnAnswer, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Submitted, ballot.Status);
        Assert.Equal(_time, ballot.SubmittedAt);
        Assert.Equal("ann", ballot.SubmittedByUsername);
        var stored = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        Assert.Equal(BallotStatus.Submitted, stored!.Status);
        Assert.Equal(["test.change", "ballot.submitted"], (await ActionsAsync(db)).Skip(auditBefore));
        Assert.Equal("slot 1: ballot submitted (1 answers, 0 section states)", (await LastEntryAsync(db)).Detail);
    }

    /// <summary>
    ///     Test that editing an already handed-in single-slot ballot with a complete result keeps it handed in and adds
    ///     no status entry, because the status did not change.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_SingleSlotSubmittedBallotEdited_StaysSubmittedWithoutTransitionAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await db.Store.SetBallotAnswerAsync(NewAnswer(projectId, slotId), Draft(projectId), _hasAnAnswer, cancellationToken);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var ballot = await db.Store.SetBallotAnswerAsync(
            NewAnswer(projectId, slotId, outcome: ChecklistOutcome.Fail), Draft(projectId), _hasAnAnswer, cancellationToken);

        // Assert: only the entry of the edit
        Assert.Equal(BallotStatus.Submitted, ballot.Status);
        Assert.Equal(ChecklistOutcome.Fail, Assert.Single(ballot.Answers).Outcome);
        Assert.Equal(["test.change"], (await ActionsAsync(db)).Skip(auditBefore));
    }

    /// <summary>
    ///     Test that with two slots a complete ballot is never handed in by an edit, however the caller judges it.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SetBallotAnswerAsync_TwoSlots_NeverAutoSubmits()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var ballot = await db.Store.SetBallotAnswerAsync(
            NewAnswer(projectId, slotId), Draft(projectId), _hasAnAnswer, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot.Status);
        Assert.Equal(["test.change"], (await ActionsAsync(db)).Skip(auditBefore));
    }

    /// <summary>
    ///     Test that handing in a complete draft stores the status with the time and the user and records the entry the
    ///     caller supplied.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SubmitBallotAsync_CompleteDraft_SubmitsWithAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await AddSlotAsync(db, projectId);
        var slotId = await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, slotId));
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var ballot = await SubmitAsync(db, projectId, slotId);

        // Assert
        Assert.Equal(BallotStatus.Submitted, ballot.Status);
        Assert.Equal(_time, ballot.SubmittedAt);
        Assert.Equal("ann", ballot.SubmittedByUsername);
        Assert.Single(ballot.Answers);
        var stored = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        Assert.Equal((BallotStatus.Submitted, _time, "ann"), (stored!.Status, stored.SubmittedAt, stored.SubmittedByUsername));
        Assert.Equal(["test.submit"], (await ActionsAsync(db)).Skip(auditBefore));
    }

    /// <summary>
    ///     Test that an incomplete ballot is refused as incomplete, stays a draft and leaves the audit trail alone.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SubmitBallotAsync_IncompleteBallot_ThrowsBallotIncompleteAndStoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, slotId));
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => db.Store.SubmitBallotAsync(
            projectId, "REV-1", slotId, _time, "ann", Draft(projectId, "test.submit"), _ => false, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.BallotIncomplete, exception.Rule);
        var stored = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        Assert.Equal(BallotStatus.Draft, stored!.Status);
        Assert.Null(stored.SubmittedAt);
        Assert.Equal(auditBefore, (await ActionsAsync(db)).Count);
    }

    /// <summary>
    ///     Test that handing in a ballot twice is a compare-and-set that does nothing the second time: the first time and
    ///     user stay, no entry is recorded and the completeness test is not even asked.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SubmitBallotAsync_AlreadySubmitted_ReturnsBallotWithoutAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await AddSlotAsync(db, projectId);
        var slotId = await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, slotId));
        await SubmitAsync(db, projectId, slotId);
        var auditBefore = (await ActionsAsync(db)).Count;
        var asked = false;

        // Act
        var ballot = await db.Store.SubmitBallotAsync(
            projectId,
            "REV-1",
            slotId,
            _time.AddHours(1),
            "bob",
            Draft(projectId, "test.submit"),
            _ =>
            {
                asked = true;
                return false;
            },
            cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Submitted, ballot.Status);
        Assert.Equal(_time, ballot.SubmittedAt);
        Assert.Equal("ann", ballot.SubmittedByUsername);
        Assert.False(asked);
        Assert.Equal(auditBefore, (await ActionsAsync(db)).Count);
    }

    /// <summary>
    ///     Test that a slot without any row can hand in an empty ballot when nothing needs an answer, and that the empty
    ///     ballot is stored as handed in.
    /// </summary>
    [Fact]
    public async Task ReviewStore_SubmitBallotAsync_NoBallotAndNothingToAnswer_StoresSubmittedBallot()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);

        // Act
        var ballot = await db.Store.SubmitBallotAsync(
            projectId, "REV-1", slotId, _time, "ann", Draft(projectId, "test.submit"), _ => true, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Submitted, ballot.Status);
        Assert.Empty(ballot.Answers);
        Assert.Empty(ballot.SectionStates);
        var stored = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        Assert.Equal((BallotStatus.Submitted, _time, "ann"), (stored!.Status, stored.SubmittedAt, stored.SubmittedByUsername));
    }

    /// <summary>
    ///     Test that the listing returns each slot's ballot with its own rows, in the order of the slots, and no
    ///     ballot for a slot that has none.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListBallotsAsync_TwoSlots_ReturnsOwnRowsInSlotOrder()
    {
        // Arrange: the second slot is edited first, a third slot is left alone
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var first = await AddSlotAsync(db, projectId);
        var second = await AddSlotAsync(db, projectId, 2);
        await AddSlotAsync(db, projectId, 3);
        await SetAsync(db, NewAnswer(projectId, second, "REQ-02", username: "bob"));
        await SetAsync(db, NewAnswer(projectId, first, "REQ-01"));
        await SetAsync(db, NewAnswer(projectId, first, "REQ-03"));
        await db.Store.SetBallotSectionStateAsync(NewSection(projectId, second), Draft(projectId), null, cancellationToken);

        // Act
        var ballots = await db.Store.ListBallotsAsync(projectId, "REV-1", cancellationToken);

        // Assert
        Assert.Equal([first, second], ballots.Select(b => b.SlotId));
        Assert.Equal(["REQ-01", "REQ-03"], ballots[0].Answers.Select(a => a.ItemId));
        Assert.Empty(ballots[0].SectionStates);
        Assert.Equal(["REQ-02"], ballots[1].Answers.Select(a => a.ItemId));
        Assert.Single(ballots[1].SectionStates);
        Assert.Empty(await db.Store.ListBallotsAsync(projectId, "REV-2", cancellationToken));
    }

    /// <summary>
    ///     Test that a slot that has no ballot yet reads as no ballot at all, and so does a slot that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetBallotAsync_NotStartedSlot_ReturnsNull()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);

        // Act
        var started = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);
        var unknown = await db.Store.GetBallotAsync(projectId, "REV-1", 999, cancellationToken);

        // Assert
        Assert.Null(started);
        Assert.Null(unknown);
    }

    /// <summary>
    ///     Test that the review-level listings return the review-level rows only: the rows of a ballot are not listed,
    ///     and the review-level rows are not part of a ballot.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListChecklistAnswersAsync_BallotRows_AreNotListedAsReviewRows()
    {
        // Arrange: one review-level row and one row of a ballot for the same check
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        await db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, ChecklistAnswer.BaselineSlotId), cancellationToken);
        await db.Store.SetChecklistSectionStateAsync(
            NewSection(projectId, ChecklistAnswer.BaselineSlotId, "other"), cancellationToken);
        await SetAsync(db, NewAnswer(projectId, slotId, outcome: ChecklistOutcome.Fail));
        await db.Store.SetBallotSectionStateAsync(NewSection(projectId, slotId), Draft(projectId), null, cancellationToken);

        // Act
        var answers = await db.Store.ListChecklistAnswersAsync(projectId, "REV-1", cancellationToken);
        var sections = await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-1", cancellationToken);
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);

        // Assert
        var answer = Assert.Single(answers);
        Assert.Equal((ChecklistAnswer.BaselineSlotId, ChecklistOutcome.Pass), (answer.SlotId, answer.Outcome));
        Assert.Equal("other", Assert.Single(sections).SectionId);
        Assert.Equal(ChecklistOutcome.Fail, Assert.Single(ballot!.Answers).Outcome);
        Assert.Equal("docs", Assert.Single(ballot.SectionStates).SectionId);
    }

    /// <summary>
    ///     Test that ballot rows and ballot statuses change neither the record nor its digest, which still hold the
    ///     review-level rows only.
    /// </summary>
    [Fact]
    public async Task ReviewStore_GetRecordAsync_BallotRows_DoNotChangeTheRecordOrItsDigest()
    {
        // Arrange: a review-level row, then the digest
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var slotId = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        await db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, ChecklistAnswer.BaselineSlotId), cancellationToken);
        var before = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);
        var digest = RecordDigest.Compute(before!);

        // Act: edits and a hand-in of a ballot
        await SetAsync(db, NewAnswer(projectId, slotId, "REQ-02", ChecklistOutcome.Fail));
        await db.Store.SetBallotSectionStateAsync(NewSection(projectId, slotId), Draft(projectId), null, cancellationToken);
        await SubmitAsync(db, projectId, slotId);
        var after = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);
        var snapshot = await db.Store.GetSnapshotAsync(projectId, "REV-1", cancellationToken);

        // Assert
        Assert.Equal(digest, RecordDigest.Compute(after!));
        Assert.Equal("REQ-01", Assert.Single(after!.Answers).ItemId);
        Assert.Empty(after.SectionStates);
        Assert.Equal(digest, RecordDigest.Compute(snapshot!.Record));
    }

    /// <summary>
    ///     Test that a slot added to a follow-up review starts from a draft ballot with copies of the carried rows,
    ///     which keep the answerer, the time and the carried marker, and that the review-level rows stay as they were.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSlotAsync_FollowUpBaseline_SeedsDraftBallotWithCarriedRows()
    {
        // Arrange: a follow-up review with one carried answer and one carried section state
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await AddFollowUpAsync(db, projectId);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        var slotId = await AddSlotAsync(db, projectId, reviewId: "REV-2");
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-2", slotId, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot!.Status);
        var answer = Assert.Single(ballot.Answers);
        Assert.Equal((slotId, "REQ-01", "REV-1", "ann", _time), (answer.SlotId, answer.ItemId, answer.CarriedFromReviewId, answer.AnsweredByUsername, answer.AnsweredAt));
        var section = Assert.Single(ballot.SectionStates);
        Assert.Equal((slotId, "docs", "REV-1", false), (section.SlotId, section.SectionId, section.CarriedFromReviewId, section.Applicable));
        Assert.Single(await db.Store.ListChecklistAnswersAsync(projectId, "REV-2", cancellationToken));
        Assert.Single(await db.Store.ListChecklistSectionStatesAsync(projectId, "REV-2", cancellationToken));
        Assert.Equal(["test.change"], (await ActionsAsync(db)).Skip(auditBefore));
    }

    /// <summary>
    ///     Test that a slot added to a review without carried rows creates no ballot.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddSlotAsync_NoBaseline_CreatesNoBallot()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await db.Store.SetChecklistAnswerAsync(NewAnswer(projectId, ChecklistAnswer.BaselineSlotId), cancellationToken);

        // Act: the review-level row is not carried, so it seeds nothing
        var slotId = await AddSlotAsync(db, projectId);

        // Assert
        Assert.Null(await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken));
        Assert.Empty(await db.Store.ListBallotsAsync(projectId, "REV-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a reviewer who leaves a slot takes the rows of the reviewer along, that the others stay, that a
    ///     handed-in ballot is a draft again, and that the entry names the slot number and the counts only.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateSlotAsync_DepartingUser_RemovesOwnRowsAndRevertsToDraft()
    {
        // Arrange: ann (primary) and bob (alternate) both contributed to a handed-in ballot
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var carol = await AddUserAsync(db, "carol");
        var slotId = await AddSlotAsync(db, projectId, ann, bob);
        await AddSlotAsync(db, projectId, carol);
        await SetAsync(db, NewAnswer(projectId, slotId, "REQ-01", username: "ann"));
        await SetAsync(db, NewAnswer(projectId, slotId, "REQ-02", username: "bob"));
        await db.Store.SetBallotSectionStateAsync(
            NewSection(projectId, slotId, username: "Ann"), Draft(projectId), null, cancellationToken);
        await SubmitAsync(db, projectId, slotId);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act: ann is replaced by carol
        var updated = await db.Store.UpdateSlotAsync(projectId, "REV-1", slotId, bob, carol, Draft(projectId), cancellationToken);
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);

        // Assert
        Assert.True(updated);
        Assert.Equal(BallotStatus.Draft, ballot!.Status);
        Assert.Null(ballot.SubmittedAt);
        Assert.Equal("REQ-02", Assert.Single(ballot.Answers).ItemId);
        Assert.Empty(ballot.SectionStates);
        Assert.Equal(["test.change", "ballot.reassigned"], (await ActionsAsync(db)).Skip(auditBefore));
        Assert.Equal(
            "slot 1: ballot reverted to draft after a reassignment (1 answers, 1 section states of departed reviewers removed)",
            (await LastEntryAsync(db)).Detail);
    }

    /// <summary>
    ///     Test that the rows carried from an earlier review stay when the reviewer named on them leaves the slot,
    ///     because the departing reviewer did not give them, and that nothing then needs a reassignment entry.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateSlotAsync_CarriedRowsOfDepartingUser_AreKept()
    {
        // Arrange: a follow-up review whose carried rows name ann; ann is the primary reviewer of the seeded slot
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        await AddFollowUpAsync(db, projectId);
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var slotId = await AddSlotAsync(db, projectId, ann, reviewId: "REV-2");
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        await db.Store.UpdateSlotAsync(
            projectId, "REV-2", slotId, bob, null, AuditDraft.By(TestActors.Ann, "test.change", "x", projectId, "REV-2"), cancellationToken);
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-2", slotId, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Draft, ballot!.Status);
        Assert.Single(ballot.Answers);
        Assert.Single(ballot.SectionStates);
        Assert.Equal(["test.change"], (await ActionsAsync(db)).Skip(auditBefore));
    }

    /// <summary>
    ///     Test that swapping the roles of the two reviewers of a slot keeps a handed-in ballot as it is and records no
    ///     reassignment.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateSlotAsync_RoleSwapOnly_KeepsBallot()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var slotId = await AddSlotAsync(db, projectId, ann, bob);
        await AddSlotAsync(db, projectId, bob);
        await SetAsync(db, NewAnswer(projectId, slotId));
        await SubmitAsync(db, projectId, slotId);
        var auditBefore = (await ActionsAsync(db)).Count;

        // Act
        await db.Store.UpdateSlotAsync(projectId, "REV-1", slotId, bob, ann, Draft(projectId), cancellationToken);
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-1", slotId, cancellationToken);

        // Assert
        Assert.Equal(BallotStatus.Submitted, ballot!.Status);
        Assert.Single(ballot.Answers);
        Assert.Equal(["test.change"], (await ActionsAsync(db)).Skip(auditBefore));
    }

    /// <summary>
    ///     Test that removing a slot deletes its answers, its section states and its ballot status, and leaves the
    ///     ballot of another slot alone.
    /// </summary>
    [Fact]
    public async Task ReviewStore_RemoveSlotAsync_WithBallot_DeletesTheBallotRows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var removed = await AddSlotAsync(db, projectId);
        var kept = await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, removed));
        await db.Store.SetBallotSectionStateAsync(NewSection(projectId, removed), Draft(projectId), null, cancellationToken);
        await SetAsync(db, NewAnswer(projectId, kept));

        // Act
        var result = await db.Store.RemoveSlotAsync(projectId, "REV-1", removed, cancellationToken);

        // Assert: nothing of the slot is left in any table
        Assert.True(result);
        Assert.Null(await db.Store.GetBallotAsync(projectId, "REV-1", removed, cancellationToken));
        Assert.Equal([kept], (await db.Store.ListBallotsAsync(projectId, "REV-1", cancellationToken)).Select(b => b.SlotId));
        await using var context = await db.Factory.CreateDbContextAsync(cancellationToken);
        Assert.False(await context.ChecklistAnswers.AnyAsync(a => a.SlotId == removed, cancellationToken));
        Assert.False(await context.ChecklistSectionStates.AnyAsync(s => s.SlotId == removed, cancellationToken));
        Assert.False(await context.ChecklistBallots.AnyAsync(b => b.SlotId == removed, cancellationToken));
    }

    /// <summary>
    ///     Test that the database refuses to remove a signed slot and that the refusal rolls back the deletion of the
    ///     ballot rows, so the ballot is still complete.
    /// </summary>
    [Fact]
    public async Task ReviewStore_RemoveSlotAsync_SignedSlot_IsRefusedAndKeepsBallotRows()
    {
        // Arrange: a signed slot with a ballot next to an unsigned one, so the review is not locked
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var signed = await AddSlotAsync(db, projectId);
        await AddSlotAsync(db, projectId, 2);
        await SetAsync(db, NewAnswer(projectId, signed));
        await db.Store.SetBallotSectionStateAsync(NewSection(projectId, signed), Draft(projectId), null, cancellationToken);
        var record = await db.Store.GetRecordAsync(projectId, "REV-1", cancellationToken);
        await db.Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = projectId,
                ReviewId = "REV-1",
                SlotId = signed,
                UserId = 1,
                DisplayName = "Ann",
                Role = SlotRole.Primary,
                SignedAt = _time,
                RecordDigest = RecordDigest.Compute(record!),
                Meaning = SigningService.MeaningText
            },
            cancellationToken);

        // Act
        await Assert.ThrowsAsync<SqliteException>(
            () => db.Store.RemoveSlotAsync(projectId, "REV-1", signed, cancellationToken));

        // Assert
        var ballot = await db.Store.GetBallotAsync(projectId, "REV-1", signed, cancellationToken);
        Assert.Single(ballot!.Answers);
        Assert.Single(ballot.SectionStates);
        Assert.Equal(2, (await db.Store.ListSlotsAsync(projectId, "REV-1", cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a run of ballot edits, hand-ins, reopens, reassignments and removals keeps the audit chain intact,
    ///     with no entry that holds free text.
    /// </summary>
    [Fact]
    public async Task ReviewStore_BallotTransitions_KeepTheAuditChainIntact()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var projectId = await AddReviewAsync(db);
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var slotId = await AddSlotAsync(db, projectId, ann);

        // Act: auto-submit, reopen by an edit, reassignment, removal
        await db.Store.SetBallotAnswerAsync(NewAnswer(projectId, slotId), Draft(projectId), _hasAnAnswer, cancellationToken);
        var second = await AddSlotAsync(db, projectId, bob);
        await SetAsync(db, NewAnswer(projectId, slotId, "REQ-02"));
        await SubmitAsync(db, projectId, slotId);
        await db.Store.UpdateSlotAsync(projectId, "REV-1", slotId, bob, null, Draft(projectId), cancellationToken);
        await db.Store.RemoveSlotAsync(projectId, "REV-1", second, cancellationToken);

        // Assert
        var entries = await db.Audit.ReadAfterAsync(0, 10_000, cancellationToken);
        Assert.True(AuditChain.Verify(entries).IsIntact);
        var actions = entries.Select(e => e.Action).ToList();
        Assert.Contains("ballot.submitted", actions);
        Assert.Contains("ballot.reopened", actions);
        Assert.Contains("ballot.reassigned", actions);
        Assert.All(
            entries.Where(e => e.Action.StartsWith("ballot.", StringComparison.Ordinal)),
            e => Assert.StartsWith("slot ", e.Detail, StringComparison.Ordinal));
    }
}
