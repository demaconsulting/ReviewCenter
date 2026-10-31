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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of <see cref="FollowUpCarry"/>, the pure rules of a follow-up review: which earlier review fits and
///     exactly which answers, section states and threads are carried over. No store is involved.
/// </summary>
public class FollowUpCarryTests
{
    /// <summary>
    ///     The instant at which the earlier review's rows were written.
    /// </summary>
    private static readonly DateTimeOffset _then = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The instant at which the new review is created.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 4, 5, 6, 7, 8, TimeSpan.Zero);

    /// <summary>
    ///     Builds a checklist definition with two sections and three checks.
    /// </summary>
    /// <param name="thirdText">The wording of the third check.</param>
    /// <param name="betaTitle">The title of the second section.</param>
    /// <param name="withThird">Whether the third check exists.</param>
    /// <returns>The definition.</returns>
    private static ChecklistDefinition Definition(string thirdText = "Third", string betaTitle = "Beta", bool withThird = true)
    {
        return new ChecklistDefinition(
            "v",
            [
                new ChecklistSection("a", "Alpha", string.Empty, [new ChecklistItem("a-01", "First"), new ChecklistItem("a-02", "Second")]),
                new ChecklistSection("b", betaTitle, string.Empty, withThird ? [new ChecklistItem("b-01", thirdText)] : [])
            ]);
    }

    /// <summary>
    ///     Builds an answer of the earlier review.
    /// </summary>
    /// <param name="item">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The answer, given by Bob.</returns>
    private static ChecklistAnswer Answer(string item, ChecklistOutcome outcome)
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "old",
            ItemId = item,
            Outcome = outcome,
            Justification = outcome == ChecklistOutcome.Pass ? null : "Because",
            AnsweredBy = "Bob",
            AnsweredByUsername = "bob",
            AnsweredAt = _then
        };
    }

    /// <summary>
    ///     Builds a section state of the earlier review.
    /// </summary>
    /// <param name="section">The section id.</param>
    /// <param name="applicable">Whether the section applies.</param>
    /// <returns>The state, set by Bob.</returns>
    private static ChecklistSectionState Section(string section, bool applicable)
    {
        return new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "old",
            SectionId = section,
            Applicable = applicable,
            Reason = applicable ? null : "Not relevant",
            SetBy = "Bob",
            SetByUsername = "bob",
            SetAt = _then
        };
    }

    /// <summary>
    ///     Builds a thread of the earlier review with two comments.
    /// </summary>
    /// <param name="id">The thread identity.</param>
    /// <param name="path">The file.</param>
    /// <param name="state">The state.</param>
    /// <param name="start">The first line, or <c>null</c> for the whole file.</param>
    /// <param name="end">The last line, or <c>null</c> for the whole file.</param>
    /// <param name="side">The side.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(
        int id,
        string path = "a.txt",
        ReviewThreadState state = ReviewThreadState.Open,
        int? start = 3,
        int? end = 5,
        ThreadSide side = ThreadSide.Head)
    {
        return new ReviewThread
        {
            Id = id,
            ProjectId = 1,
            ReviewId = "old",
            FilePath = path,
            Side = side,
            StartLine = start,
            EndLine = end,
            State = state,
            Created = _then,
            Comments =
            [
                new ReviewComment { Id = id * 10, ThreadId = id, Author = "Bob", AuthorUsername = "bob", Text = "First remark", Created = _then },
                new ReviewComment { Id = id * 10 + 1, ThreadId = id, Author = "Cyd", AuthorUsername = "cyd", Text = "Reply", Created = _then.AddHours(1) }
            ]
        };
    }

    /// <summary>
    ///     Builds a file of a review.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="head">The head hash.</param>
    /// <param name="baseline">The baseline hash, or <c>null</c> for none.</param>
    /// <returns>The file.</returns>
    private static ReviewFile File(string path, string head = "h1", string? baseline = "b1")
    {
        return new ReviewFile { ProjectId = 1, ReviewId = "x", Path = path, Sha256 = head, BaselineSha256 = baseline };
    }

    /// <summary>
    ///     Builds the record of the earlier review.
    /// </summary>
    /// <param name="answers">The answers.</param>
    /// <param name="sections">The section states.</param>
    /// <param name="threads">The threads.</param>
    /// <param name="withChecklist">Whether the review has a checklist.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(
        ChecklistAnswer[]? answers = null,
        ChecklistSectionState[]? sections = null,
        ReviewThread[]? threads = null,
        bool withChecklist = true)
    {
        var checklist = withChecklist
            ? new ReviewChecklist { ProjectId = 1, ReviewId = "old", Version = "v", Definition = Definition().ToJson() }
            : null;
        return new ReviewRecord("fp", checklist, sections ?? [], answers ?? [], threads ?? [], ReviewSource.Upload);
    }

    /// <summary>
    ///     Computes what is carried from a record into a review whose files are given.
    /// </summary>
    /// <param name="record">The earlier record.</param>
    /// <param name="definition">The checklist of the new review.</param>
    /// <param name="oldFiles">The files of the earlier review; <c>a.txt</c> when omitted.</param>
    /// <param name="newFiles">The files of the new review; <c>a.txt</c> when omitted.</param>
    /// <returns>The carry-over.</returns>
    private static ReviewCarryOver Compute(
        ReviewRecord record,
        ChecklistDefinition? definition = null,
        ReviewFile[]? oldFiles = null,
        ReviewFile[]? newFiles = null)
    {
        return FollowUpCarry.Compute(
            "old", record, oldFiles ?? [File("a.txt")], 1, "new", newFiles ?? [File("a.txt")], definition ?? Definition(), _now);
    }

    /// <summary>
    ///     Test that Pass and not-applicable answers whose check has the same wording are carried with outcome,
    ///     justification, answerer and time unchanged, and are marked as carried from the earlier review.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_PassAndNotApplicableAnswers_AreCarriedWithOriginalAnswerer()
    {
        // Arrange
        var record = Record([Answer("a-01", ChecklistOutcome.Pass), Answer("a-02", ChecklistOutcome.NotApplicable)]);

        // Act
        var carry = Compute(record);

        // Assert
        Assert.Equal(["a-01", "a-02"], carry.Answers.Select(a => a.ItemId));
        Assert.All(carry.Answers, answer =>
        {
            Assert.Equal("old", answer.CarriedFromReviewId);
            Assert.Equal("new", answer.ReviewId);
            Assert.Equal(1, answer.ProjectId);
            Assert.Equal("Bob", answer.AnsweredBy);
            Assert.Equal("bob", answer.AnsweredByUsername);
            Assert.Equal(_then, answer.AnsweredAt);
        });
        Assert.Equal(ChecklistOutcome.NotApplicable, carry.Answers[1].Outcome);
        Assert.Equal("Because", carry.Answers[1].Justification);
    }

    /// <summary>
    ///     Test that a Fail answer, an answer to a check whose wording changed and an answer to a check that is gone
    ///     are not carried, while the unchanged Pass next to them is.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_FailChangedAndMissingChecks_AreNotCarried()
    {
        // Arrange: a Fail, a Pass on a check whose text changes, and a Pass on a check the new checklist lacks
        var record = Record(
            [Answer("a-01", ChecklistOutcome.Pass), Answer("a-02", ChecklistOutcome.Fail), Answer("b-01", ChecklistOutcome.Pass)]);

        // Act
        var changed = Compute(record, Definition("Third, reworded"));
        var missing = Compute(record, Definition(withThird: false));

        // Assert
        Assert.Equal(["a-01"], changed.Answers.Select(a => a.ItemId));
        Assert.Equal(["a-01"], missing.Answers.Select(a => a.ItemId));
    }

    /// <summary>
    ///     Test that nothing is carried from a review without a checklist.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_SourceWithoutChecklist_CarriesNoChecklistRows()
    {
        // Arrange
        var record = Record([Answer("a-01", ChecklistOutcome.Pass)], [Section("b", false)], withChecklist: false);

        // Act
        var carry = Compute(record);

        // Assert
        Assert.Empty(carry.Answers);
        Assert.Empty(carry.SectionStates);
    }

    /// <summary>
    ///     Test that a section that does not apply is carried with its reason when the new checklist has the section
    ///     with the same title, and is not carried when the section is gone, renamed or applies.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_NotApplicableSections_AreCarriedOnlyWhenTheSectionIsTheSame()
    {
        // Arrange
        var record = Record(sections: [Section("a", true), Section("b", false)]);

        // Act
        var same = Compute(record);
        var renamed = Compute(record, Definition(betaTitle: "Gamma"));
        var gone = Compute(
            record,
            new ChecklistDefinition("v", [new ChecklistSection("a", "Alpha", string.Empty, [new ChecklistItem("a-01", "First")])]));

        // Assert
        var state = Assert.Single(same.SectionStates);
        Assert.Equal("b", state.SectionId);
        Assert.False(state.Applicable);
        Assert.Equal("Not relevant", state.Reason);
        Assert.Equal("Bob", state.SetBy);
        Assert.Equal(_then, state.SetAt);
        Assert.Equal("old", state.CarriedFromReviewId);
        Assert.Empty(renamed.SectionStates);
        Assert.Empty(gone.SectionStates);
    }

    /// <summary>
    ///     Test that an open thread is carried with all comments verbatim and in order, in the state Open, and that
    ///     the thread and comment identities are left for the store to assign.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_OpenThread_IsCarriedWithAllCommentsVerbatim()
    {
        // Arrange
        var record = Record(threads: [Thread(7)]);

        // Act
        var carry = Compute(record);

        // Assert
        var thread = Assert.Single(carry.Threads);
        Assert.Equal(ReviewThreadState.Open, thread.State);
        Assert.Equal("old", thread.CarriedFromReviewId);
        Assert.Equal(7, thread.CarriedFromThreadId);
        Assert.Equal(0, thread.Id);
        Assert.Equal(_then, thread.Created);
        Assert.Equal(["First remark", "Reply"], thread.Comments.Select(c => c.Text));
        Assert.Equal(["bob", "cyd"], thread.Comments.Select(c => c.AuthorUsername));
        Assert.Equal(["Bob", "Cyd"], thread.Comments.Select(c => c.Author));
        Assert.Equal([_then, _then.AddHours(1)], thread.Comments.Select(c => c.Created));
        Assert.All(thread.Comments, comment => Assert.Equal(0, comment.Id));
        Assert.Equal(0, carry.ResolvedThreadsNotCarried);
    }

    /// <summary>
    ///     Test that a carried Finding keeps the flags and the kind of its comments, including a resolution note of an
    ///     earlier round, so it is still a Finding in the new review.
    /// </summary>
    [Fact]
    public void FollowUpCarry_CarryThreads_CarriedFinding_KeepsKindFlagsAndResolutionComments()
    {
        // Arrange: an open (reopened) Finding with an included starting comment, a chat reply and an included note
        var finding = new ReviewThread
        {
            Id = 1,
            ProjectId = 1,
            ReviewId = "old",
            FilePath = "a.txt",
            StartLine = 3,
            EndLine = 5,
            Created = _then,
            Comments =
            [
                new ReviewComment { Id = 10, ThreadId = 1, Author = "Bob", AuthorUsername = "bob", Text = "Fix", Created = _then, IncludeInReport = true },
                new ReviewComment { Id = 11, ThreadId = 1, Author = "Cyd", AuthorUsername = "cyd", Text = "Chat", Created = _then },
                new ReviewComment
                {
                    Id = 12,
                    ThreadId = 1,
                    Author = "Bob",
                    AuthorUsername = "bob",
                    Text = "Done",
                    Created = _then,
                    IncludeInReport = true,
                    Kind = ReviewCommentKind.Resolution
                }
            ]
        };

        // Act
        var thread = Assert.Single(Compute(Record(threads: [finding])).Threads);

        // Assert
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        Assert.Equal([true, false, true], thread.Comments.Select(c => c.IncludeInReport));
        Assert.Equal(
            [ReviewCommentKind.Comment, ReviewCommentKind.Comment, ReviewCommentKind.Resolution],
            thread.Comments.Select(c => c.Kind));
    }

    /// <summary>
    ///     Test that a carried chat thread stays chat: no comment of it is in the report.
    /// </summary>
    [Fact]
    public void FollowUpCarry_CarryThreads_CarriedChat_StaysChat()
    {
        // Arrange: the helper thread has two chat comments
        var record = Record(threads: [Thread(1)]);

        // Act
        var thread = Assert.Single(Compute(record).Threads);

        // Assert
        Assert.Equal(ThreadKind.Question, thread.Kind);
        Assert.All(thread.Comments, comment => Assert.False(comment.IncludeInReport));
        Assert.All(thread.Comments, comment => Assert.Equal(ReviewCommentKind.Comment, comment.Kind));
    }

    /// <summary>
    ///     Test that the system comment of a moved anchor is never part of the report, even in a carried Finding.
    /// </summary>
    [Fact]
    public void FollowUpCarry_CarryThreads_MovedAnchorNote_IsExcludedComment()
    {
        // Arrange: a Finding on lines of a file whose content changed
        var finding = new ReviewThread
        {
            Id = 1,
            ProjectId = 1,
            ReviewId = "old",
            FilePath = "a.txt",
            StartLine = 3,
            EndLine = 5,
            Created = _then,
            Comments =
            [
                new ReviewComment { Id = 10, ThreadId = 1, Author = "Bob", AuthorUsername = "bob", Text = "Fix", Created = _then, IncludeInReport = true }
            ]
        };

        // Act
        var thread = Assert.Single(Compute(Record(threads: [finding]), newFiles: [File("a.txt", "other", "b1")]).Threads);

        // Assert: the thread stays a Finding and the added note is chat
        Assert.Equal(ThreadKind.Finding, thread.Kind);
        var note = thread.Comments[^1];
        Assert.Equal(FollowUpCarry.SystemUsername, note.AuthorUsername);
        Assert.False(note.IncludeInReport);
        Assert.Equal(ReviewCommentKind.Comment, note.Kind);
    }

    /// <summary>
    ///     Test that a resolved thread is not carried but is counted, and that an open thread on a file that is not in
    ///     the new review is not carried but its file is listed, each file once and in order.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_ResolvedAndDroppedThreads_AreCountedAndListed()
    {
        // Arrange
        var record = Record(threads:
        [
            Thread(1, state: ReviewThreadState.Resolved),
            Thread(2, "z.txt"),
            Thread(3, "m.txt"),
            Thread(4, "z.txt"),
            Thread(5)
        ]);

        // Act
        var carry = Compute(record, oldFiles: [File("a.txt"), File("m.txt"), File("z.txt")]);

        // Assert
        Assert.Equal(1, carry.ResolvedThreadsNotCarried);
        Assert.Equal(["m.txt", "z.txt"], carry.DroppedThreadFiles);
        Assert.Equal([5], carry.Threads.Select(t => t.CarriedFromThreadId));
    }

    /// <summary>
    ///     Test that a line anchor is kept only when the content of the side it refers to is identical in both
    ///     reviews, and otherwise moves to the whole file with a system comment at the end that names the original
    ///     anchor.
    /// </summary>
    /// <param name="side">The side of the original anchor.</param>
    /// <param name="newHead">The head hash in the new review.</param>
    /// <param name="newBaseline">The baseline hash in the new review.</param>
    /// <param name="kept">Whether the anchor stays.</param>
    [Theory]
    [InlineData(ThreadSide.Head, "h1", "other", true)]
    [InlineData(ThreadSide.Head, "other", "b1", false)]
    [InlineData(ThreadSide.Baseline, "other", "b1", true)]
    [InlineData(ThreadSide.Baseline, "h1", "other", false)]
    public void FollowUpCarry_Compute_LineAnchor_IsKeptOnlyForIdenticalContent(ThreadSide side, string newHead, string newBaseline, bool kept)
    {
        // Arrange
        var record = Record(threads: [Thread(1, side: side)]);

        // Act
        var carry = Compute(record, newFiles: [File("a.txt", newHead, newBaseline)]);

        // Assert
        var thread = Assert.Single(carry.Threads);
        if (kept)
        {
            Assert.Equal(side, thread.Side);
            Assert.Equal(3, thread.StartLine);
            Assert.Equal(5, thread.EndLine);
            Assert.Equal(2, thread.Comments.Count);
            return;
        }

        Assert.Equal(ThreadSide.Head, thread.Side);
        Assert.Null(thread.StartLine);
        Assert.Null(thread.EndLine);
        Assert.Equal(3, thread.Comments.Count);
        var note = thread.Comments[^1];
        Assert.Equal(FollowUpCarry.SystemAuthor, note.Author);
        Assert.Equal(FollowUpCarry.SystemUsername, note.AuthorUsername);
        Assert.Equal(_now, note.Created);
        Assert.Contains("old", note.Text, StringComparison.Ordinal);
        Assert.Contains(side == ThreadSide.Head ? "head lines 3 to 5" : "baseline lines 3 to 5", note.Text, StringComparison.Ordinal);
        Assert.Equal(["First remark", "Reply"], thread.Comments.Take(2).Select(c => c.Text));
    }

    /// <summary>
    ///     Test that the system comment names a single line without a range, that a whole-file anchor is kept even
    ///     when the file changed, and that an anchor is moved when the file of the earlier review is unknown or has
    ///     no hash for the side.
    /// </summary>
    [Fact]
    public void FollowUpCarry_Compute_SingleLineWholeFileAndUnknownHash_AreHandled()
    {
        // Arrange: a one-line anchor, a whole-file anchor, and a baseline anchor in a file that had no baseline
        var record = Record(threads:
        [
            Thread(1, start: 4, end: 4),
            Thread(2, start: null, end: null),
            Thread(3, side: ThreadSide.Baseline)
        ]);

        // Act: the file changed at the head and has no baseline hash in either review
        var carry = Compute(
            record,
            oldFiles: [File("a.txt", "h1", null)],
            newFiles: [File("a.txt", "h2", null)]);

        // Assert
        Assert.Contains("head line 4", carry.Threads[0].Comments[^1].Text, StringComparison.Ordinal);
        Assert.Equal(2, carry.Threads[1].Comments.Count);
        Assert.Null(carry.Threads[1].StartLine);
        Assert.Equal(3, carry.Threads[2].Comments.Count);
        Assert.Null(carry.Threads[2].StartLine);
    }

    /// <summary>
    ///     Test the fit of an earlier review: the same review-set, the same repository with a common file, and
    ///     two uploads with a common file fit; another set, another repository, an upload against a repository and no
    ///     common file do not.
    /// </summary>
    /// <param name="sourceRepository">The repository of the earlier review, or <c>null</c> for an upload.</param>
    /// <param name="sourceSet">The review-set of the earlier review.</param>
    /// <param name="repository">The repository of the new review, or <c>null</c> for an upload.</param>
    /// <param name="set">The review-set of the new review.</param>
    /// <param name="path">The one file of the new review; the earlier review has <c>a.txt</c>.</param>
    /// <param name="fits">Whether the earlier review fits.</param>
    [Theory]
    [InlineData(null, null, null, null, "a.txt", true)]
    [InlineData(null, null, null, null, "b.txt", false)]
    [InlineData(5, null, 5, null, "a.txt", true)]
    [InlineData(5, null, 5, null, "b.txt", false)]
    [InlineData(5, null, 6, null, "a.txt", false)]
    [InlineData(5, null, null, null, "a.txt", false)]
    [InlineData(null, null, 5, null, "a.txt", false)]
    [InlineData(5, "core", 5, "core", "b.txt", true)]
    [InlineData(5, "core", 5, "docs", "a.txt", false)]
    [InlineData(5, "core", 5, null, "a.txt", true)]
    [InlineData(5, "core", 5, null, "b.txt", false)]
    [InlineData(5, null, 5, "core", "a.txt", true)]
    public void FollowUpCarry_CheckFit_Matrix(int? sourceRepository, string? sourceSet, int? repository, string? set, string path, bool fits)
    {
        // Arrange
        var source = new Review
        {
            ProjectId = 1,
            Id = "old",
            RepositoryId = sourceRepository,
            ReviewSetId = sourceSet,
            Files = [File("a.txt")]
        };

        // Act
        var message = FollowUpCarry.CheckFit(source, repository, set, [path]);

        // Assert
        Assert.Equal(fits, message is null);
        if (!fits)
        {
            Assert.Contains("'old'", message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that the message about dropped threads names up to ten files, says how many more there are, and uses
    ///     the singular for one file.
    /// </summary>
    [Fact]
    public void FollowUpCarry_DroppedMessage_ListsFilesAndTheRest()
    {
        // Arrange
        var files = Enumerable.Range(1, 12).Select(i => $"f{i:00}.txt").ToList();

        // Act
        var many = FollowUpCarry.DroppedMessage(files);
        var one = FollowUpCarry.DroppedMessage(["only.txt"]);

        // Assert
        Assert.Contains("12 files", many, StringComparison.Ordinal);
        Assert.Contains("'f10.txt' and 2 more", many, StringComparison.Ordinal);
        Assert.DoesNotContain("f11.txt", many, StringComparison.Ordinal);
        Assert.Contains("1 file that is not in the new review", one, StringComparison.Ordinal);
        Assert.Contains("'only.txt'", one, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the helper refuses null arguments.
    /// </summary>
    [Fact]
    public void FollowUpCarry_NullArguments_Throw()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => FollowUpCarry.CheckFit(null!, null, null, []));
        Assert.Throws<ArgumentNullException>(() => FollowUpCarry.CheckFit(new Review(), null, null, null!));
        Assert.Throws<ArgumentNullException>(() => FollowUpCarry.Compute("old", null!, [], 1, "new", [], Definition(), _now));
        Assert.Throws<ArgumentNullException>(() => FollowUpCarry.DroppedMessage(null!));
    }
}
