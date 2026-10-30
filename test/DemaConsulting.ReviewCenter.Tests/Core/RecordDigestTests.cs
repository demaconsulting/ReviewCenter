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

using System.Security.Cryptography;
using System.Text;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="RecordDigest"/>: the canonical text and the digest over it.
/// </summary>
public class RecordDigestTests
{
    /// <summary>
    ///     The instant used for the times in the baseline record.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a comment.
    /// </summary>
    /// <param name="id">The storage identity, which must not matter.</param>
    /// <param name="text">The comment text.</param>
    /// <param name="author">The display name of the author.</param>
    /// <param name="username">The user name of the author; the lowercase display name when omitted.</param>
    /// <param name="created">The time of the comment; the baseline instant when omitted.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Comment(
        int id,
        string text = "Please fix",
        string author = "Ann",
        string? username = null,
        DateTimeOffset? created = null)
    {
        return new ReviewComment
        {
            Id = id,
            ThreadId = 1,
            Author = author,
            AuthorUsername = username ?? author.ToLowerInvariant(),
            Text = text,
            Created = created ?? _time
        };
    }

    /// <summary>
    ///     Builds a thread.
    /// </summary>
    /// <param name="id">The storage identity, which must not matter.</param>
    /// <param name="path">The file path.</param>
    /// <param name="state">The thread state.</param>
    /// <param name="comments">The comments in posting order.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(
        int id,
        string path = "a.txt",
        ReviewThreadState state = ReviewThreadState.Open,
        params ReviewComment[] comments)
    {
        return new ReviewThread
        {
            Id = id,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = path,
            StartLine = 1,
            EndLine = 2,
            State = state,
            Created = _time,
            Comments = comments.Length == 0 ? [Comment(id * 10)] : [.. comments]
        };
    }

    /// <summary>
    ///     Builds an answer.
    /// </summary>
    /// <param name="item">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="justification">The justification.</param>
    /// <param name="by">Who answered; must not matter.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer Answer(
        string item,
        ChecklistOutcome outcome = ChecklistOutcome.Pass,
        string? justification = null,
        string by = "Ann")
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            ItemId = item,
            Outcome = outcome,
            Justification = justification,
            AnsweredBy = by,
            AnsweredByUsername = by.ToLowerInvariant(),
            AnsweredAt = _time
        };
    }

    /// <summary>
    ///     Builds a record from its parts, defaulting to a small baseline.
    /// </summary>
    /// <param name="fingerprint">The content fingerprint.</param>
    /// <param name="answers">The answers.</param>
    /// <param name="sections">The section states.</param>
    /// <param name="threads">The threads.</param>
    /// <param name="source">The source of the files; an upload when omitted.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(
        string fingerprint = "f1",
        IReadOnlyList<ChecklistAnswer>? answers = null,
        IReadOnlyList<ChecklistSectionState>? sections = null,
        IReadOnlyList<ReviewThread>? threads = null,
        ReviewSource? source = null)
    {
        return new ReviewRecord(
            fingerprint,
            null,
            sections ?? [],
            answers ?? [Answer("REQ-01"), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a")],
            threads ?? [Thread(1), Thread(2, "b.txt", ReviewThreadState.Resolved)],
            source ?? ReviewSource.Upload);
    }

    /// <summary>
    ///     Builds a repository source.
    /// </summary>
    /// <param name="repositoryId">The repository identity.</param>
    /// <param name="baseline">The baseline commit, or <c>null</c> for none.</param>
    /// <param name="head">The head commit.</param>
    /// <param name="selection">The selection text.</param>
    /// <param name="changes">The changes; a modified and a deleted file when omitted.</param>
    /// <param name="reviewSetId">The review-set identifier, or <c>null</c> for none.</param>
    /// <returns>The source.</returns>
    private static ReviewSource GitSource(
        int repositoryId = 7,
        string? baseline = "base1",
        string head = "head1",
        string selection = "**/*",
        IReadOnlyList<ChangedFile>? changes = null,
        string? reviewSetId = null)
    {
        return new ReviewSource(
            repositoryId,
            baseline,
            head,
            selection,
            changes ??
            [
                new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "hs1"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null)
            ],
            reviewSetId);
    }

    /// <summary>
    ///     Builds a section state.
    /// </summary>
    /// <param name="id">The section id.</param>
    /// <param name="applicable">Whether the section applies.</param>
    /// <param name="reason">The reason it does not.</param>
    /// <param name="setBy">The display name of who set it.</param>
    /// <param name="setAt">When it was set; the baseline instant when omitted.</param>
    /// <returns>The section state.</returns>
    private static ChecklistSectionState Section(
        string id,
        bool applicable,
        string? reason,
        string setBy = "Ann",
        DateTimeOffset? setAt = null)
    {
        return new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            SectionId = id,
            Applicable = applicable,
            Reason = reason,
            SetBy = setBy,
            SetByUsername = setBy.ToLowerInvariant(),
            SetAt = setAt ?? _time
        };
    }

    /// <summary>
    ///     Test that who marked a section as not applicable, and when, is not covered (unlike who answered a
    ///     check): only the section and its reason are.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_SectionSetterAndTime_NotCovered()
    {
        // Arrange: the same section that does not apply, set by another person at another time
        var one = Record(sections: [Section("DES", false, "none")]);
        var other = Record(sections: [Section("DES", false, "none", "Bob", _time.AddDays(2))]);

        // Act, Assert: the digests are equal
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Test that the canonical text of a small record has exactly the documented shape.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_SmallRecord_MatchesDocumentedFormat()
    {
        // Arrange: a record with one answer, one section that does not apply and one thread with one comment
        var record = Record(
            "abc",
            [Answer("REQ-01", ChecklistOutcome.Fail, "bad")],
            [Section("DES", false, "none"), Section("DOC", true, null)],
            [Thread(7)]);
        var ticks = _time.UtcTicks;

        // Act: build the canonical text
        var text = RecordDigest.Canonicalize(record);

        // Assert: version line, fingerprint, answers, sections that do not apply, threads
        var expected = string.Join(
            '\n',
            "ReviewCenter record v1",
            "fingerprint 3:abc",
            "source upload",
            "checklist -",
            "answers",
            "answer 6:REQ-01 4:Fail 3:bad 3:Ann 3:ann",
            "sections",
            "not-applicable 3:DES 4:none",
            "threads",
            $"thread 5:a.txt 4:Head 1 2 4:Open {ticks}",
            $"comment 3:Ann 3:ann 10:Please fix {ticks}");
        Assert.Equal(expected, text);
    }

    /// <summary>
    ///     Test that the digest is the SHA-256 of the canonical text as lowercase hexadecimal, and is stable.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_SameRecord_IsStableSha256OfCanonicalText()
    {
        // Arrange: a record
        var record = Record();

        // Act: compute twice
        var first = RecordDigest.Compute(record);
        var second = RecordDigest.Compute(Record());

        // Assert: the same value, 64 lowercase hex characters, equal to the SHA-256 of the text
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RecordDigest.Canonicalize(record)))).ToLowerInvariant();
        Assert.Equal(first, second);
        Assert.Equal(expected, first);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    /// <summary>
    ///     Test that a known record has a known digest, so a change of the format is noticed.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_KnownRecord_MatchesKnownValue()
    {
        // Arrange: a record with only a fingerprint
        var record = new ReviewRecord("abc", null, [], [], [], ReviewSource.Upload);

        // Act: compute the digest
        var digest = RecordDigest.Compute(record);

        // Assert: the SHA-256 of
        // "ReviewCenter record v1\nfingerprint 3:abc\nsource upload\nchecklist -\nanswers\nsections\nthreads",
        // worked out with an independent tool (a separate script that builds the text from the documented layout)
        Assert.Equal("9fee8bc5c8ddc1c908ed605ccb301bfa1be3e0499cc824d787275c7a783543f0", digest);
    }

    /// <summary>
    ///     Builds a frozen checklist from sections of (id, title, items of (id, text)), kept in the order given.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="sections">The sections.</param>
    /// <returns>The frozen checklist as the store would hold it.</returns>
    private static ReviewChecklist Checklist(string version, params ChecklistSection[] sections)
    {
        var definition = new ChecklistDefinition(version, sections);
        return new ReviewChecklist
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            Version = version,
            Definition = definition.ToJson()
        };
    }

    /// <summary>
    ///     Builds a checklist section.
    /// </summary>
    /// <param name="id">The section id.</param>
    /// <param name="title">The section title.</param>
    /// <param name="items">The checks as (id, text) pairs.</param>
    /// <returns>The section.</returns>
    private static ChecklistSection ChecklistSectionOf(string id, string title, params (string Id, string Text)[] items)
    {
        return new ChecklistSection(id, title, "hint", [.. items.Select(i => new ChecklistItem(i.Id, i.Text))]);
    }

    /// <summary>
    ///     Builds an upload record with a checklist and one answer.
    /// </summary>
    /// <param name="requirementText">The wording of check REQ-01.</param>
    /// <param name="by">Who answered.</param>
    /// <param name="username">The user name of the answerer.</param>
    /// <param name="answeredAt">When the check was answered.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord ChecklistRecord(
        string requirementText = "Is the code correct?",
        string by = "Ann",
        string? username = null,
        DateTimeOffset? answeredAt = null)
    {
        var answer = Answer("REQ-01", by: by);
        var stored = new ChecklistAnswer
        {
            ProjectId = answer.ProjectId,
            ReviewId = answer.ReviewId,
            ItemId = answer.ItemId,
            Outcome = answer.Outcome,
            Justification = answer.Justification,
            AnsweredBy = by,
            AnsweredByUsername = username ?? by.ToLowerInvariant(),
            AnsweredAt = answeredAt ?? _time
        };
        var checklist = Checklist(
            "default-1",
            ChecklistSectionOf("REQ", "Requirements", ("REQ-01", requirementText), ("REQ-02", "Is it traced?")),
            ChecklistSectionOf("DES", "Design", ("DES-01", "Is it sound?")));
        return new ReviewRecord("abc", checklist, [], [stored], [], ReviewSource.Upload);
    }

    /// <summary>
    ///     Test that the checklist wording is written sorted by id, and that the digest of a checklist record is the
    ///     known answer worked out with an independent tool.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ChecklistRecord_MatchesDocumentedFormatAndKnownValue()
    {
        // Arrange: a record whose checklist sections and checks are defined out of id order
        var record = ChecklistRecord();

        // Act: build the canonical text and the digest
        var text = RecordDigest.Canonicalize(record);
        var digest = RecordDigest.Compute(record);

        // Assert: version, sections and checks sorted by id, then the answer with who answered
        var expected = string.Join(
            '\n',
            "ReviewCenter record v1",
            "fingerprint 3:abc",
            "source upload",
            "checklist 9:default-1",
            "section 3:DES 6:Design",
            "item 6:DES-01 12:Is it sound?",
            "section 3:REQ 12:Requirements",
            "item 6:REQ-01 20:Is the code correct?",
            "item 6:REQ-02 13:Is it traced?",
            "answers",
            "answer 6:REQ-01 4:Pass - 3:Ann 3:ann",
            "sections",
            "threads");
        Assert.Equal(expected, text);
        Assert.Equal("5f6e64aa7091e4156ec43493c56d9f0cc2407a24a92efec3012ae02e1f954e43", digest);
    }

    /// <summary>
    ///     Test that changing the wording of a question, the title of a section or the checklist version changes the
    ///     digest, so a signature binds to the questions that were asked.
    /// </summary>
    /// <param name="change">The name of the change under test.</param>
    [Theory]
    [InlineData("item-wording")]
    [InlineData("section-title")]
    [InlineData("version")]
    [InlineData("item-removed")]
    [InlineData("checklist-removed")]
    public void RecordDigest_Compute_ChecklistWordingChange_ChangesDigest(string change)
    {
        // Arrange: the baseline record and its digest
        var before = RecordDigest.Compute(ChecklistRecord());

        // Act: change one thing in the checklist
        var baseline = ChecklistRecord();
        var changed = change switch
        {
            "item-wording" => ChecklistRecord(requirementText: "Is the code right?"),
            "section-title" => baseline with
            {
                Checklist = Checklist(
                    "default-1",
                    ChecklistSectionOf("REQ", "Requirement", ("REQ-01", "Is the code correct?"), ("REQ-02", "Is it traced?")),
                    ChecklistSectionOf("DES", "Design", ("DES-01", "Is it sound?")))
            },
            "version" => baseline with
            {
                Checklist = Checklist(
                    "default-2",
                    ChecklistSectionOf("REQ", "Requirements", ("REQ-01", "Is the code correct?"), ("REQ-02", "Is it traced?")),
                    ChecklistSectionOf("DES", "Design", ("DES-01", "Is it sound?")))
            },
            "item-removed" => baseline with
            {
                Checklist = Checklist(
                    "default-1",
                    ChecklistSectionOf("REQ", "Requirements", ("REQ-01", "Is the code correct?")),
                    ChecklistSectionOf("DES", "Design", ("DES-01", "Is it sound?")))
            },
            _ => baseline with { Checklist = null }
        };

        // Assert: the digest differs
        Assert.NotEqual(before, RecordDigest.Compute(changed));
    }

    /// <summary>
    ///     Test that the order the checklist is defined in does not change the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ChecklistDefinitionOrder_SameDigest()
    {
        // Arrange: the same checklist with sections and checks in the opposite order
        var one = ChecklistRecord();
        var other = one with
        {
            Checklist = Checklist(
                "default-1",
                ChecklistSectionOf("DES", "Design", ("DES-01", "Is it sound?")),
                ChecklistSectionOf("REQ", "Requirements", ("REQ-02", "Is it traced?"), ("REQ-01", "Is the code correct?")))
        };

        // Act, Assert: the digests are equal
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Test that who answered a check is covered (display name and user name), so a signature breaks when
    ///     somebody else answers, while the moment of answering is not covered.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_AnsweredByChanged_ChangesDigestButAnsweredAtDoesNot()
    {
        // Arrange: the baseline and variants that change the display name, the user name and the time
        var before = RecordDigest.Compute(ChecklistRecord());

        // Act: compute each variant
        var otherName = RecordDigest.Compute(ChecklistRecord(by: "Bob", username: "ann"));
        var otherUser = RecordDigest.Compute(ChecklistRecord(by: "Ann", username: "ann2"));
        var otherTime = RecordDigest.Compute(ChecklistRecord(answeredAt: _time.AddDays(3)));

        // Assert: the person matters, the time does not
        Assert.NotEqual(before, otherName);
        Assert.NotEqual(before, otherUser);
        Assert.Equal(before, otherTime);
    }

    /// <summary>
    ///     Test that a stored definition that is not valid JSON, or not a complete definition, is not hidden: the
    ///     digest refuses it with a JSON error (never a null reference error).
    /// </summary>
    /// <param name="definition">The corrupted definition text.</param>
    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Version\":\"1\"}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":null}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[null]}")]
    [InlineData("{\"Sections\":[]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Title\":\"T\",\"Items\":[]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Items\":[]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\"}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":null}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[null]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[{\"Id\":\"A-1\"}]}]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[{\"Text\":\"q\"}]}]}")]
    public void RecordDigest_Canonicalize_MalformedChecklistDefinition_Throws(string definition)
    {
        // Arrange: a record whose frozen checklist holds corrupted text
        var record = ChecklistRecord() with
        {
            Checklist = new ReviewChecklist { ProjectId = 1, ReviewId = "rev-1", Version = "x", Definition = definition }
        };

        // Act, Assert: reading the definition fails with a JSON error
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => RecordDigest.Canonicalize(record));
    }

    /// <summary>
    ///     Test that the check for a complete definition is not too strict: a definition without sections, and a
    ///     section without an applicability hint, still give a canonical text.
    /// </summary>
    /// <param name="definition">The minimal definition text.</param>
    [Theory]
    [InlineData("{\"Version\":\"1\",\"Sections\":[]}")]
    [InlineData("{\"Version\":\"1\",\"Sections\":[{\"Id\":\"A\",\"Title\":\"T\",\"Items\":[{\"Id\":\"A-1\",\"Text\":\"q\"}]}]}")]
    public void RecordDigest_Canonicalize_MinimalChecklistDefinition_Computes(string definition)
    {
        // Arrange: a record whose frozen checklist holds a minimal but complete definition
        var record = ChecklistRecord() with
        {
            Checklist = new ReviewChecklist { ProjectId = 1, ReviewId = "rev-1", Version = "x", Definition = definition }
        };

        // Act: build the canonical text
        var text = RecordDigest.Canonicalize(record);

        // Assert: the checklist block is written
        Assert.Contains("checklist 1", text);
    }

    /// <summary>
    ///     Builds a record of an upload with a single thread anchored to lines 1-2 of <c>a.txt</c> on the given side.
    /// </summary>
    /// <param name="side">The side of the thread.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord SidedRecord(ThreadSide side)
    {
        var thread = new ReviewThread
        {
            Id = 7,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = "a.txt",
            Side = side,
            StartLine = 1,
            EndLine = 2,
            State = ReviewThreadState.Open,
            Created = _time,
            Comments = [Comment(70)]
        };
        return new ReviewRecord("abc", null, [], [], [thread], ReviewSource.Upload);
    }

    /// <summary>
    ///     Test that the side of a thread is written after the path as a length-prefixed name, with known
    ///     digests for both sides worked out with an independent tool.
    /// </summary>
    [Theory]
    [InlineData(ThreadSide.Head, "4:Head", "fe1a3eea5aab9f90922ebdea3caa92b637333d4f082eb7a80b44eb1335acc896")]
    [InlineData(ThreadSide.Baseline, "8:Baseline", "68acc67bebb0c5decad4669037cc3f7618844cf0e59919cb911aef79e525cf0d")]
    public void RecordDigest_Compute_ThreadSide_IsWrittenAndMatchesKnownValue(ThreadSide side, string written, string expectedDigest)
    {
        // Arrange: an upload record with one thread on the given side
        var record = SidedRecord(side);
        var ticks = _time.UtcTicks;

        // Act: build the canonical text and the digest
        var text = RecordDigest.Canonicalize(record);
        var digest = RecordDigest.Compute(record);

        // Assert: the thread line carries the side after the path, and the digest is the known answer
        var expected = string.Join(
            '\n',
            "ReviewCenter record v1",
            "fingerprint 3:abc",
            "source upload",
            "checklist -",
            "answers",
            "sections",
            "threads",
            $"thread 5:a.txt {written} 1 2 4:Open {ticks}",
            $"comment 3:Ann 3:ann 10:Please fix {ticks}");
        Assert.Equal(expected, text);
        Assert.Equal(expectedDigest, digest);
    }

    /// <summary>
    ///     Test that moving a thread from the head to the baseline changes the digest, so a signature binds to the side.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ThreadSideChange_ChangesDigest()
    {
        // Arrange, Act: the digests of the same thread on each side
        var head = RecordDigest.Compute(SidedRecord(ThreadSide.Head));
        var baseline = RecordDigest.Compute(SidedRecord(ThreadSide.Baseline));

        // Assert: they differ
        Assert.NotEqual(head, baseline);
    }

    /// <summary>
    ///     Test that the review-set identifier is part of the canonical text and the digest, and that "no set" and
    ///     an empty set identifier are different, so a signature binds to the set.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ReviewSetId_IsCoveredAndDistinguishesNullFromEmpty()
    {
        // Arrange: the same repository record without a set, with a set and with an empty set identifier
        var sha1 = new string('a', 40);
        var sha2 = new string('b', 40);
        ChangedFile[] changes =
        [
            new("a.txt", ChangeKind.Modified, "bs1", "hs1"),
            new("b.txt", ChangeKind.Deleted, "bs2", null)
        ];
        ReviewRecord Make(string? setId) =>
            new("abc", null, [], [], [], new ReviewSource(7, sha1, sha2, "**/*", changes, setId));

        // Act: compute the three digests
        var none = RecordDigest.Compute(Make(null));
        var core = RecordDigest.Compute(Make("core"));
        var empty = RecordDigest.Compute(Make(string.Empty));

        // Assert: known answers worked out with an independent tool, all different, and the line shape
        Assert.Equal("b0eae44ae9062359375b7a361857794617e5c28b5a6a2b7ac732ec36549857d8", none);
        Assert.Equal("27b7ef9d9b7303fc1c55808f00331469c99e8518e66af16a3317d1811e678059", core);
        Assert.Equal("d44de252d6f0c7d7d878db59fedc2f21d20592d8669050216b5d77487cbf0760", empty);
        Assert.Contains($"source git 7 40:{sha1} 40:{sha2} 4:**/* 4:core\n", RecordDigest.Canonicalize(Make("core")));
        Assert.Contains($"source git 7 40:{sha1} 40:{sha2} 4:**/* -\n", RecordDigest.Canonicalize(Make(null)));
    }

    /// <summary>
    ///     Test that the canonical text of a repository review has exactly the documented shape.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_GitSource_MatchesDocumentedFormat()
    {
        // Arrange: a repository record whose changes are given out of path order
        var sha1 = new string('a', 40);
        var sha2 = new string('b', 40);
        var record = new ReviewRecord(
            "abc",
            null,
            [],
            [],
            [],
            new ReviewSource(
                7,
                sha1,
                sha2,
                "**/*",
                [
                    new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null),
                    new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "hs1")
                ]));

        // Act: build the canonical text
        var text = RecordDigest.Canonicalize(record);

        // Assert: the source block comes after the fingerprint, with the changes sorted by path
        var expected = string.Join(
            '\n',
            "ReviewCenter record v1",
            "fingerprint 3:abc",
            $"source git 7 40:{sha1} 40:{sha2} 4:**/* -",
            "changes",
            "change 5:a.txt 8:Modified 3:bs1 3:hs1",
            "change 5:b.txt 7:Deleted 3:bs2 -",
            "checklist -",
            "answers",
            "sections",
            "threads");
        Assert.Equal(expected, text);
        Assert.Equal("b0eae44ae9062359375b7a361857794617e5c28b5a6a2b7ac732ec36549857d8", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that each part of the source changes the digest, so a signature binds to it.
    /// </summary>
    /// <param name="change">The name of the change under test.</param>
    [Theory]
    [InlineData("upload-to-git")]
    [InlineData("repository")]
    [InlineData("baseline-sha")]
    [InlineData("baseline-removed")]
    [InlineData("head-sha")]
    [InlineData("selection")]
    [InlineData("review-set")]
    [InlineData("kind")]
    [InlineData("path")]
    [InlineData("baseline-hash")]
    [InlineData("head-hash")]
    [InlineData("change-added")]
    [InlineData("change-removed")]
    public void RecordDigest_Compute_SourceChange_ChangesDigest(string change)
    {
        // Arrange: a repository record and its digest
        var before = RecordDigest.Compute(Record(source: GitSource()));

        // Act: change one thing
        var changed = change switch
        {
            "upload-to-git" => Record(),
            "repository" => Record(source: GitSource(repositoryId: 8)),
            "baseline-sha" => Record(source: GitSource(baseline: "base2")),
            "baseline-removed" => Record(source: GitSource(baseline: null)),
            "head-sha" => Record(source: GitSource(head: "head2")),
            "selection" => Record(source: GitSource(selection: "src/**")),
            "review-set" => Record(source: GitSource(reviewSetId: "core")),
            "kind" => Record(source: GitSource(changes:
            [
                new ChangedFile("a.txt", ChangeKind.Added, "bs1", "hs1"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null)
            ])),
            "path" => Record(source: GitSource(changes:
            [
                new ChangedFile("c.txt", ChangeKind.Modified, "bs1", "hs1"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null)
            ])),
            "baseline-hash" => Record(source: GitSource(changes:
            [
                new ChangedFile("a.txt", ChangeKind.Modified, "other", "hs1"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null)
            ])),
            "head-hash" => Record(source: GitSource(changes:
            [
                new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "other"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null)
            ])),
            "change-added" => Record(source: GitSource(changes:
            [
                new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "hs1"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null),
                new ChangedFile("c.txt", ChangeKind.Added, null, "hs3")
            ])),
            _ => Record(source: GitSource(changes: [new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "hs1")]))
        };

        // Assert: the digest differs
        Assert.NotEqual(before, RecordDigest.Compute(changed));
    }

    /// <summary>
    ///     Test that the order the changes are given in does not change the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ChangeOrder_SameDigest()
    {
        // Arrange: the same changes in opposite order
        var one = Record(source: GitSource());
        var other = Record(source: GitSource(changes:
        [
            new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null),
            new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "hs1")
        ]));

        // Act, Assert: the digests are equal
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Test that every thing the digest covers changes it.
    /// </summary>
    /// <param name="change">The name of the change under test.</param>
    [Theory]
    [InlineData("fingerprint")]
    [InlineData("answer-outcome")]
    [InlineData("answer-justification")]
    [InlineData("answer-justification-null-vs-empty")]
    [InlineData("answer-outcome-not-applicable")]
    [InlineData("answer-added")]
    [InlineData("section-not-applicable")]
    [InlineData("section-reason")]
    [InlineData("thread-added")]
    [InlineData("thread-state")]
    [InlineData("thread-anchor")]
    [InlineData("thread-start-line")]
    [InlineData("thread-end-line")]
    [InlineData("thread-whole-file")]
    [InlineData("thread-start-removed")]
    [InlineData("thread-end-removed")]
    [InlineData("thread-created")]
    [InlineData("thread-path")]
    [InlineData("comment-text")]
    [InlineData("comment-author")]
    [InlineData("comment-username")]
    [InlineData("comment-created")]
    [InlineData("comment-added")]
    public void RecordDigest_Compute_CoveredChange_ChangesDigest(string change)
    {
        // Arrange: a baseline record with a section that does not apply, and the digest of it
        var baseSections = new[] { Section("DES", false, "none") };
        var baseline = Record(sections: baseSections);
        var before = RecordDigest.Compute(baseline);
        var resolved = Thread(2, "b.txt", ReviewThreadState.Resolved);

        // Act: apply one change
        var changed = change switch
        {
            "fingerprint" => Record("f2", sections: baseSections),
            "answer-outcome" => Record(
                answers: [Answer("REQ-01", ChecklistOutcome.Fail, "x"), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a")],
                sections: baseSections),
            "answer-justification" => Record(
                answers: [Answer("REQ-01"), Answer("REQ-02", ChecklistOutcome.NotApplicable, "other")],
                sections: baseSections),
            "answer-justification-null-vs-empty" => Record(
                answers: [Answer("REQ-01", justification: string.Empty), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a")],
                sections: baseSections),
            "answer-outcome-not-applicable" => Record(
                answers: [Answer("REQ-01", ChecklistOutcome.NotApplicable), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a")],
                sections: baseSections),
            "answer-added" => Record(
                answers: [Answer("REQ-01"), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a"), Answer("REQ-03")],
                sections: baseSections),
            "section-not-applicable" => Record(sections: [.. baseSections, Section("DOC", false, "none")]),
            "section-reason" => Record(sections: [Section("DES", false, "different")]),
            "thread-added" => Record(sections: baseSections, threads: [Thread(1), resolved, Thread(3)]),
            "thread-state" => Record(sections: baseSections, threads: [Thread(1, state: ReviewThreadState.Resolved), resolved]),
            "thread-anchor" => Record(sections: baseSections, threads: [WithLines(Thread(1), 3, 4), resolved]),
            "thread-start-line" => Record(sections: baseSections, threads: [WithLines(Thread(1), 3, 2), resolved]),
            "thread-end-line" => Record(sections: baseSections, threads: [WithLines(Thread(1), 1, 3), resolved]),
            "thread-whole-file" => Record(sections: baseSections, threads: [WithLines(Thread(1), null, null), resolved]),
            "thread-start-removed" => Record(sections: baseSections, threads: [WithLines(Thread(1), null, 2), resolved]),
            "thread-end-removed" => Record(sections: baseSections, threads: [WithLines(Thread(1), 1, null), resolved]),
            "thread-created" => Record(sections: baseSections, threads: [WithCreated(Thread(1), _time.AddMinutes(1)), resolved]),
            "thread-path" => Record(sections: baseSections, threads: [Thread(1, "c.txt"), resolved]),
            "comment-text" => Record(sections: baseSections, threads: [Thread(1, comments: Comment(10, "Changed")), resolved]),
            "comment-author" => Record(sections: baseSections, threads: [Thread(1, comments: Comment(10, author: "Bob")), resolved]),
            "comment-username" => Record(sections: baseSections, threads: [Thread(1, comments: Comment(10, username: "ann2")), resolved]),
            "comment-created" => Record(sections: baseSections, threads: [Thread(1, comments: Comment(10, created: _time.AddMinutes(1))), resolved]),
            _ => Record(sections: baseSections, threads: [Thread(1, "a.txt", ReviewThreadState.Open, Comment(10), Comment(11, "More")), resolved])
        };

        // Assert: the digest differs
        Assert.NotEqual(before, RecordDigest.Compute(changed));
    }

    /// <summary>
    ///     Test that the order of threads and answers, and the storage identities, do not change the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ReorderedAndRenumbered_SameDigest()
    {
        // Arrange: a record and the same content with another row order and other storage identities
        var one = Record(
            answers: [Answer("REQ-01"), Answer("REQ-02")],
            threads: [Thread(1), Thread(2, "b.txt", ReviewThreadState.Resolved)]);
        var other = Record(
            answers: [Answer("REQ-02"), Answer("REQ-01")],
            threads: [Thread(90, "b.txt", ReviewThreadState.Resolved, Comment(900)), Thread(80, comments: Comment(800))]);

        // Act, Assert: the digests are equal
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Test that the order of the comments of a thread matters.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CommentOrder_ChangesDigest()
    {
        // Arrange: two threads with the same two comments in opposite order
        var first = Record(threads: [Thread(1, "a.txt", ReviewThreadState.Open, Comment(1, "one"), Comment(2, "two"))]);
        var second = Record(threads: [Thread(1, "a.txt", ReviewThreadState.Open, Comment(1, "two"), Comment(2, "one"))]);

        // Act, Assert: the digests differ
        Assert.NotEqual(RecordDigest.Compute(first), RecordDigest.Compute(second));
    }

    /// <summary>
    ///     Test that a section that applies adds nothing to the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ApplicableSection_NotCovered()
    {
        // Arrange: the same answers with and without an extra section that applies
        var one = Record(answers: [Answer("REQ-01", by: "Ann")]);
        var other = Record(answers: [Answer("REQ-01", by: "Ann")], sections: [Section("DES", true, null)]);

        // Act, Assert: the digests are equal
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Builds a checklist answer with explicit names, so that two names can be chosen to run together.
    /// </summary>
    /// <param name="justification">The justification, possibly <c>null</c>.</param>
    /// <param name="by">The display name of the answerer.</param>
    /// <param name="username">The user name of the answerer.</param>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The answer to check <c>REQ-01</c>.</returns>
    private static ChecklistAnswer AnswerWithNames(
        string? justification,
        string by,
        string username,
        ChecklistOutcome outcome = ChecklistOutcome.Pass)
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            ItemId = "REQ-01",
            Outcome = outcome,
            Justification = justification,
            AnsweredBy = by,
            AnsweredByUsername = username,
            AnsweredAt = _time
        };
    }

    /// <summary>
    ///     Test that text cannot imitate the delimiters: each pair of records below would give the very same text
    ///     if strings were written without their length (values joined by a space, lines by a line feed), so
    ///     only the length prefix keeps their digests apart.
    /// </summary>
    /// <param name="scenario">The name of the pair under test.</param>
    [Theory]
    [InlineData("comment-text-versus-two-comments")]
    [InlineData("justification-versus-answerer-name")]
    [InlineData("absent-versus-dash")]
    [InlineData("change-path-versus-kind-and-hash")]
    [InlineData("section-title-versus-item-line")]
    public void RecordDigest_Compute_TextWithDelimiters_DoesNotCollide(string scenario)
    {
        // Arrange: two different records whose unprefixed texts are identical
        var ticks = _time.UtcTicks;
        var (first, second) = scenario switch
        {
            // One comment whose text holds the end of the first comment and the start of the next comment line,
            // versus those two comments
            "comment-text-versus-two-comments" => (
                Record(threads: [Thread(1, comments: Comment(1, $"a {ticks}\ncomment Ann ann b"))]),
                Record(threads: [Thread(1, "a.txt", ReviewThreadState.Open, Comment(1, "a"), Comment(2, "b"))])),

            // A justification that runs into the display name of the answerer
            "justification-versus-answerer-name" => (
                Record(answers: [AnswerWithNames("x y", "z", "u")]),
                Record(answers: [AnswerWithNames("x", "y z", "u")])),

            // A justification that is absent versus one that is the text of the placeholder
            "absent-versus-dash" => (
                Record(answers: [AnswerWithNames(null, "Ann", "ann")]),
                Record(answers: [AnswerWithNames("-", "Ann", "ann")])),

            // A path that runs into the change kind, versus the shorter path with the kind and the baseline hash
            "change-path-versus-kind-and-hash" => (
                Record(source: GitSource(changes: [new ChangedFile("p Added", ChangeKind.Modified, "x", "y")])),
                Record(source: GitSource(changes: [new ChangedFile("p", ChangeKind.Added, "Modified", "x y")]))),

            // A section title that holds the next line of the checklist, versus a title and a real check
            _ => (
                Record() with { Checklist = Checklist("v", ChecklistSectionOf("A", "T\nitem A-1 q")) },
                Record() with { Checklist = Checklist("v", ChecklistSectionOf("A", "T", ("A-1", "q"))) })
        };

        // Act: compute the digests
        var firstDigest = RecordDigest.Compute(first);
        var secondDigest = RecordDigest.Compute(second);

        // Assert: the digests and the canonical texts differ
        Assert.NotEqual(firstDigest, secondDigest);
        Assert.NotEqual(RecordDigest.Canonicalize(first), RecordDigest.Canonicalize(second));
    }

    /// <summary>
    ///     Test that values with whitespace and empty values cannot run together: each pair below would give the
    ///     same text if strings were written without their length (values joined by a space), so only the length
    ///     prefix keeps their digests apart.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_EmptyAndSplitValues_DoNotCollide()
    {
        // Arrange: an empty justification and an answerer "a b" versus the justification " a" and the answerer "b",
        // and an answerer that is empty versus one that ends in a space
        var emptyJustification = Record(answers: [AnswerWithNames(string.Empty, "a b", "u")]);
        var leadingSpace = Record(answers: [AnswerWithNames(" a", "b", "u")]);
        var emptyAnswerer = Record(answers: [AnswerWithNames("a b", string.Empty, "u")]);
        var trailingSpace = Record(answers: [AnswerWithNames("a", "b ", "u")]);

        // Act: compute the digests
        var first = RecordDigest.Compute(emptyJustification);
        var second = RecordDigest.Compute(leadingSpace);
        var third = RecordDigest.Compute(emptyAnswerer);
        var fourth = RecordDigest.Compute(trailingSpace);

        // Assert: each pair differs
        Assert.NotEqual(first, second);
        Assert.NotEqual(third, fourth);
    }

    /// <summary>
    ///     Test that a record that was never reopened has the digest it had before reopening existed: no
    ///     <c>reopens</c> line is written, so the known digest holds and old signatures stay valid.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ZeroReopens_EqualsLegacyDigest()
    {
        // Arrange: the known record, once with the default and once with an explicit zero count, and the other
        // known records (a repository review, a checklist review, a review with a thread) with an explicit zero count
        var legacy = new ReviewRecord("abc", null, [], [], [], ReviewSource.Upload);
        var zero = legacy with { Reopens = 0 };
        var gitSource = new ReviewSource(
            7,
            new string('a', 40),
            new string('b', 40),
            "**/*",
            [
                new ChangedFile("a.txt", ChangeKind.Modified, "bs1", "hs1"),
                new ChangedFile("b.txt", ChangeKind.Deleted, "bs2", null)
            ]);
        var git = new ReviewRecord("abc", null, [], [], [], gitSource) with { Reopens = 0 };
        var checklist = ChecklistRecord() with { Reopens = 0 };
        var sided = SidedRecord(ThreadSide.Head) with { Reopens = 0 };

        // Act
        var canonical = RecordDigest.Canonicalize(zero);

        // Assert: the same known digests as before, and no reopens line
        Assert.Equal("9fee8bc5c8ddc1c908ed605ccb301bfa1be3e0499cc824d787275c7a783543f0", RecordDigest.Compute(zero));
        Assert.Equal(RecordDigest.Compute(legacy), RecordDigest.Compute(zero));
        Assert.DoesNotContain("reopens", canonical);
        Assert.Equal("b0eae44ae9062359375b7a361857794617e5c28b5a6a2b7ac732ec36549857d8", RecordDigest.Compute(git));
        Assert.Equal("5f6e64aa7091e4156ec43493c56d9f0cc2407a24a92efec3012ae02e1f954e43", RecordDigest.Compute(checklist));
        Assert.Equal("fe1a3eea5aab9f90922ebdea3caa92b637333d4f082eb7a80b44eb1335acc896", RecordDigest.Compute(sided));
    }

    /// <summary>
    ///     Builds the record of the second known answer: a repository review with an added file, a not-applicable
    ///     answer with an empty justification, and a whole-file thread that is resolved, created at another time
    ///     than its comment.
    /// </summary>
    /// <returns>The record.</returns>
    private static ReviewRecord CoveredFieldsRecord()
    {
        var thread = new ReviewThread
        {
            Id = 3,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = "a.txt",
            Side = ThreadSide.Head,
            StartLine = null,
            EndLine = null,
            State = ReviewThreadState.Resolved,
            Created = _time,
            Comments = [Comment(30, "Done", "Bob", created: _time.AddHours(1))]
        };
        return new ReviewRecord(
            "abc",
            null,
            [],
            [AnswerWithNames(string.Empty, "Bob", "bob", ChecklistOutcome.NotApplicable)],
            [thread],
            new ReviewSource(
                7,
                new string('a', 40),
                new string('b', 40),
                "**/*",
                [new ChangedFile("c.txt", ChangeKind.Added, null, "hs3")]));
    }

    /// <summary>
    ///     Test that the enumeration names (<c>NotApplicable</c>, <c>Added</c>, <c>Resolved</c>), an empty
    ///     justification, a whole-file anchor, a user name, and different times of a thread and its comment are
    ///     written as documented, with known digests (also with one reopen) worked out with an independent script.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CoveredFieldsRecord_MatchesDocumentedFormatAndKnownValue()
    {
        // Arrange: the record with the less common values
        var record = CoveredFieldsRecord();
        var threadTicks = _time.UtcTicks;
        var commentTicks = _time.AddHours(1).UtcTicks;

        // Act: build the canonical text and the digests
        var text = RecordDigest.Canonicalize(record);
        var digest = RecordDigest.Compute(record);
        var reopenedDigest = RecordDigest.Compute(record with { Reopens = 1 });

        // Assert: the documented text and the known answers
        var expected = string.Join(
            '\n',
            "ReviewCenter record v1",
            "fingerprint 3:abc",
            $"source git 7 40:{new string('a', 40)} 40:{new string('b', 40)} 4:**/* -",
            "changes",
            "change 5:c.txt 5:Added - 3:hs3",
            "checklist -",
            "answers",
            "answer 6:REQ-01 13:NotApplicable 0: 3:Bob 3:bob",
            "sections",
            "threads",
            $"thread 5:a.txt 4:Head - - 8:Resolved {threadTicks}",
            $"comment 3:Bob 3:bob 4:Done {commentTicks}");
        Assert.Equal(expected, text);
        Assert.Equal("cd2da19bd94a03706d356d3dbeb34261c8dc769fe4124eb2bd98c7b9c955f03e", digest);
        Assert.Equal("b934cbd343bcd2766bd778b1549b2c282f25d23f303d54530b4c3337db07f10e", reopenedDigest);
    }

    /// <summary>
    ///     Test that each reopen changes the digest, with the count as the last line, so a signature given before a
    ///     reopen can never be current again whatever the content.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ReopensChangeDigest()
    {
        // Arrange: the same content with no reopen, one and two reopens
        var none = new ReviewRecord("abc", null, [], [], [], ReviewSource.Upload);
        var one = none with { Reopens = 1 };
        var two = none with { Reopens = 2 };

        // Act
        var digests = new[] { RecordDigest.Compute(none), RecordDigest.Compute(one), RecordDigest.Compute(two) };

        // Assert: three different digests; the count is the final line of the canonical text
        Assert.Equal(3, digests.Distinct().Count());
        Assert.EndsWith("\nreopens 1", RecordDigest.Canonicalize(one));
        Assert.EndsWith("\nreopens 2", RecordDigest.Canonicalize(two));
    }

    /// <summary>
    ///     Test that a null record is refused.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_NullRecord_Throws()
    {
        // Arrange, Act, Assert: null is refused
        Assert.Throws<ArgumentNullException>(() => RecordDigest.Compute(null!));
        Assert.Throws<ArgumentNullException>(() => RecordDigest.Canonicalize(null!));
    }

    /// <summary>
    ///     Copies a thread with another line range.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <param name="start">The first line, or <c>null</c> for none.</param>
    /// <param name="end">The last line, or <c>null</c> for none.</param>
    /// <returns>The copy.</returns>
    private static ReviewThread WithLines(ReviewThread thread, int? start, int? end)
    {
        return WithAnchor(thread, start, end, thread.Created);
    }

    /// <summary>
    ///     Copies a thread with another creation time.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <param name="created">The creation time.</param>
    /// <returns>The copy.</returns>
    private static ReviewThread WithCreated(ReviewThread thread, DateTimeOffset created)
    {
        return WithAnchor(thread, thread.StartLine, thread.EndLine, created);
    }

    /// <summary>
    ///     Copies a thread with another line range and creation time.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <param name="start">The first line, or <c>null</c> for none.</param>
    /// <param name="end">The last line, or <c>null</c> for none.</param>
    /// <param name="created">The creation time.</param>
    /// <returns>The copy.</returns>
    private static ReviewThread WithAnchor(ReviewThread thread, int? start, int? end, DateTimeOffset created)
    {
        return new ReviewThread
        {
            Id = thread.Id,
            ProjectId = thread.ProjectId,
            ReviewId = thread.ReviewId,
            FilePath = thread.FilePath,
            Side = thread.Side,
            StartLine = start,
            EndLine = end,
            State = thread.State,
            Created = created,
            Comments = thread.Comments
        };
    }
}
