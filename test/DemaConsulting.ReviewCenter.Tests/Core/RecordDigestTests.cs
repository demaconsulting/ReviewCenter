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
    /// <param name="included">Whether the comment is part of the report; the tests below cover the report, so yes.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Comment(
        int id,
        string text = "Please fix",
        string author = "Ann",
        string? username = null,
        DateTimeOffset? created = null,
        bool included = true)
    {
        return new ReviewComment
        {
            Id = id,
            ThreadId = 1,
            Author = author,
            AuthorUsername = username ?? author.ToLowerInvariant(),
            Text = text,
            Created = created ?? _time,
            IncludeInReport = included
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
    ///     Builds an answer of the ballot of slot 1.
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
            SlotId = 1,
            ItemId = item,
            Outcome = outcome,
            Justification = justification,
            AnsweredBy = by,
            AnsweredByUsername = by.ToLowerInvariant(),
            AnsweredAt = _time
        };
    }

    /// <summary>
    ///     Builds the frozen checklist most records of these tests carry: the sections REQ (two checks) and DES (one).
    /// </summary>
    /// <returns>The checklist.</returns>
    private static ReviewChecklist TestChecklist()
    {
        return Checklist(
            "default-1",
            ChecklistSectionOf("REQ", "Requirements", ("REQ-01", "Is the code correct?"), ("REQ-02", "Is it traced?")),
            ChecklistSectionOf("DES", "Design", ("DES-01", "Is it sound?")));
    }

    /// <summary>
    ///     Builds a submitted ballot of slot 1.
    /// </summary>
    /// <param name="answers">The answers.</param>
    /// <param name="sections">The section states.</param>
    /// <returns>The ballot.</returns>
    private static Ballot Submitted(
        IReadOnlyList<ChecklistAnswer> answers,
        IReadOnlyList<ChecklistSectionState> sections)
    {
        return new Ballot(1, BallotStatus.Submitted, _time, "ann", answers, sections);
    }

    /// <summary>
    ///     Builds a record from its parts, defaulting to a small baseline: the test checklist and a submitted ballot of
    ///     slot 1.
    /// </summary>
    /// <param name="fingerprint">The content fingerprint.</param>
    /// <param name="answers">The answers of the ballot.</param>
    /// <param name="sections">The section states of the ballot.</param>
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
            TestChecklist(),
            [Submitted(answers ?? [Answer("REQ-01"), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a")], sections ?? [])],
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
            SlotId = 1,
            SectionId = id,
            Applicable = applicable,
            Reason = reason,
            SetBy = setBy,
            SetByUsername = setBy.ToLowerInvariant(),
            SetAt = setAt ?? _time
        };
    }

    /// <summary>
    ///     Test that who marked a section as not applicable, and when, is not covered: only the standing of the section is.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_SectionSetterAndTime_NotCovered()
    {
        // Arrange: the same section that does not apply, set by another person at another time with another reason
        var one = Record(sections: [Section("DES", false, "none")]);
        var other = Record(sections: [Section("DES", false, "else", "Bob", _time.AddDays(2))]);

        // Act, Assert: the digests are equal
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     The canonical lines of <see cref="TestChecklist"/>.
    /// </summary>
    private static readonly string[] _checklistLines =
    [
        "checklist 9:default-1",
        "section 3:DES 6:Design",
        "item 6:DES-01 12:Is it sound?",
        "section 3:REQ 12:Requirements",
        "item 6:REQ-01 20:Is the code correct?",
        "item 6:REQ-02 13:Is it traced?"
    ];

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

        // Assert: version line, fingerprint, checklist, the standing of the checks and sections, then the report
        var expected = string.Join(
            '\n',
            [
                "ReviewCenter record v2",
                "fingerprint 3:abc",
                "source upload",
                .. _checklistLines,
                "checks",
                "check 6:REQ-01 agreed 4:Fail",
                "check 6:REQ-02 pending",
                "sections",
                "applicability 3:DES not-applicable",
                "outcome none",
                "report",
                "thread file 5:a.txt 4:Head 1 2",
                $"claim 3:Ann 3:ann 10:Please fix {ticks}"
            ]);
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
        var record = new ReviewRecord("abc", null, [], [], ReviewSource.Upload);

        // Act: compute the digest
        var digest = RecordDigest.Compute(record);

        // Assert: the SHA-256 of
        // "ReviewCenter record v2\nfingerprint 3:abc\nsource upload\nchecklist -\nchecks\nsections\noutcome none\nreport",
        // worked out with an independent tool (a separate script that builds the text from the documented layout)
        Assert.Equal("4c93e63f9251787ecc5a267ee3d3f28922757b299294add1709bf8a401fad5cd", digest);
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
            SlotId = 1,
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
        return new ReviewRecord("abc", checklist, [Submitted([stored], [])], [], ReviewSource.Upload);
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

        // Assert: version, sections and checks sorted by id, then the standing of the checks
        var expected = string.Join(
            '\n',
            [
                "ReviewCenter record v2",
                "fingerprint 3:abc",
                "source upload",
                .. _checklistLines,
                "checks",
                "check 6:DES-01 pending",
                "check 6:REQ-01 agreed 4:Pass",
                "check 6:REQ-02 pending",
                "sections",
                "outcome none",
                "report"
            ]);
        Assert.Equal(expected, text);
        Assert.Equal("a4ca2841f1831fa27b4c7b241bb2f77ca1977c91b2fe47d72b135c07af29e38c", digest);
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
    ///     Test that who answered a check, the justification and the moment of answering are not covered: a signature
    ///     binds to what the reviewers agree on, not to who gave it or how it was worded.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_AnswererJustificationAndTime_NotCovered()
    {
        // Arrange: the baseline and variants that change the display name, the user name, the time and the wording
        var before = RecordDigest.Compute(ChecklistRecord());

        // Act: compute each variant
        var otherName = RecordDigest.Compute(ChecklistRecord(by: "Bob", username: "ann"));
        var otherUser = RecordDigest.Compute(ChecklistRecord(by: "Ann", username: "ann2"));
        var otherTime = RecordDigest.Compute(ChecklistRecord(answeredAt: _time.AddDays(3)));
        var ballot = Submitted([Answer("REQ-01", justification: "Because.")], []);
        var otherWording = RecordDigest.Compute(ChecklistRecord() with { Ballots = [ballot] });

        // Assert: none of them matters
        Assert.Equal(before, otherName);
        Assert.Equal(before, otherUser);
        Assert.Equal(before, otherTime);
        Assert.Equal(before, otherWording);
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
        return new ReviewRecord("abc", null, [], [thread], ReviewSource.Upload);
    }

    /// <summary>
    ///     Test that the side of a thread is written after the path as a length-prefixed name, with known
    ///     digests for both sides worked out with an independent tool.
    /// </summary>
    [Theory]
    [InlineData(ThreadSide.Head, "4:Head", "15c6f2265ae8e9c29aea106ccaa687a7da34e99ebe18116cbeb8930c3e647bd7")]
    [InlineData(ThreadSide.Baseline, "8:Baseline", "1d50c27f46fcc273951f1f36b8ecba3245dfc743fe7bf1656f4f1424920ee753")]
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
            "ReviewCenter record v2",
            "fingerprint 3:abc",
            "source upload",
            "checklist -",
            "checks",
            "sections",
            "outcome none",
            "report",
            $"thread file 5:a.txt {written} 1 2",
            $"claim 3:Ann 3:ann 10:Please fix {ticks}");
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
            new("abc", null, [], [], new ReviewSource(7, sha1, sha2, "**/*", changes, setId));

        // Act: compute the three digests
        var none = RecordDigest.Compute(Make(null));
        var core = RecordDigest.Compute(Make("core"));
        var empty = RecordDigest.Compute(Make(string.Empty));

        // Assert: known answers worked out with an independent tool, all different, and the line shape
        Assert.Equal("b80fa632a2aa11861ecb6bf8442c85a1485f80b0f0e84a408c9e78705835c747", none);
        Assert.Equal("748c61eff2e4702ee896753c341c9c66c08cd51826c6a8d0e276f510de7d275a", core);
        Assert.Equal("390050cf8169267cb7d48b3e327b1f4e5c182e42b9a06427e02a2edc635adb5a", empty);
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
            "ReviewCenter record v2",
            "fingerprint 3:abc",
            $"source git 7 40:{sha1} 40:{sha2} 4:**/* -",
            "changes",
            "change 5:a.txt 8:Modified 3:bs1 3:hs1",
            "change 5:b.txt 7:Deleted 3:bs2 -",
            "checklist -",
            "checks",
            "sections",
            "outcome none",
            "report");
        Assert.Equal(expected, text);
        Assert.Equal("b80fa632a2aa11861ecb6bf8442c85a1485f80b0f0e84a408c9e78705835c747", RecordDigest.Compute(record));
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
    [InlineData("answer-outcome-not-applicable")]
    [InlineData("answer-removed")]
    [InlineData("section-not-applicable")]
    [InlineData("section-applicable")]
    [InlineData("thread-added")]
    [InlineData("thread-anchor")]
    [InlineData("thread-start-line")]
    [InlineData("thread-end-line")]
    [InlineData("thread-whole-file")]
    [InlineData("thread-start-removed")]
    [InlineData("thread-end-removed")]
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
            "answer-outcome-not-applicable" => Record(
                answers: [Answer("REQ-01", ChecklistOutcome.NotApplicable), Answer("REQ-02", ChecklistOutcome.NotApplicable, "n/a")],
                sections: baseSections),
            "answer-removed" => Record(answers: [Answer("REQ-01")], sections: baseSections),
            "section-not-applicable" => Record(sections: [.. baseSections, Section("REQ", false, "none")]),
            "section-applicable" => Record(sections: []),
            "thread-added" => Record(sections: baseSections, threads: [Thread(1), resolved, Thread(3)]),
            "thread-anchor" => Record(sections: baseSections, threads: [WithLines(Thread(1), 3, 4), resolved]),
            "thread-start-line" => Record(sections: baseSections, threads: [WithLines(Thread(1), 3, 2), resolved]),
            "thread-end-line" => Record(sections: baseSections, threads: [WithLines(Thread(1), 1, 3), resolved]),
            "thread-whole-file" => Record(sections: baseSections, threads: [WithLines(Thread(1), null, null), resolved]),
            "thread-start-removed" => Record(sections: baseSections, threads: [WithLines(Thread(1), null, 2), resolved]),
            "thread-end-removed" => Record(sections: baseSections, threads: [WithLines(Thread(1), 1, null), resolved]),
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
    ///     Test that text cannot imitate the delimiters: each pair of records below would give the very same text
    ///     if strings were written without their length (values joined by a space, lines by a line feed), so
    ///     only the length prefix keeps their digests apart.
    /// </summary>
    /// <param name="scenario">The name of the pair under test.</param>
    [Theory]
    [InlineData("comment-text-versus-two-comments")]
    [InlineData("author-versus-username")]
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

            // An author name that runs into the user name of the author
            "author-versus-username" => (
                Record(threads: [Thread(1, comments: Comment(1, "t", "a b", "u"))]),
                Record(threads: [Thread(1, comments: Comment(1, "t", "a", "b u"))])),

            // A hash that is absent versus one that is the text of the placeholder
            "absent-versus-dash" => (
                Record(source: GitSource(changes: [new ChangedFile("a.txt", ChangeKind.Modified, null, "y")])),
                Record(source: GitSource(changes: [new ChangedFile("a.txt", ChangeKind.Modified, "-", "y")]))),

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
        // Arrange: an empty comment text and an author "a b" versus the text " a" and the author "b",
        // and an author that is empty versus one that ends in a space
        var emptyText = Record(threads: [Thread(1, comments: Comment(1, string.Empty, "a b", "u"))]);
        var leadingSpace = Record(threads: [Thread(1, comments: Comment(1, " a", "b", "u"))]);
        var emptyAuthor = Record(threads: [Thread(1, comments: Comment(1, "a b", string.Empty, "u"))]);
        var trailingSpace = Record(threads: [Thread(1, comments: Comment(1, "a", "b ", "u"))]);

        // Act: compute the digests
        var first = RecordDigest.Compute(emptyText);
        var second = RecordDigest.Compute(leadingSpace);
        var third = RecordDigest.Compute(emptyAuthor);
        var fourth = RecordDigest.Compute(trailingSpace);

        // Assert: each pair differs
        Assert.NotEqual(first, second);
        Assert.NotEqual(third, fourth);
    }

    /// <summary>
    ///     Test that a record that was never reopened has no <c>reopens</c> line, so an explicit zero count and the
    ///     default give the same digest, equal to the known digests of the other records.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ZeroReopens_EqualsDigestWithoutReopensLine()
    {
        // Arrange: the known record, once with the default and once with an explicit zero count, and the other
        // known records (a repository review, a checklist review, a review with a thread) with an explicit zero count
        var legacy = new ReviewRecord("abc", null, [], [], ReviewSource.Upload);
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
        var git = new ReviewRecord("abc", null, [], [], gitSource) with { Reopens = 0 };
        var checklist = ChecklistRecord() with { Reopens = 0 };
        var sided = SidedRecord(ThreadSide.Head) with { Reopens = 0 };

        // Act
        var canonical = RecordDigest.Canonicalize(zero);

        // Assert: the same known digests as before, and no reopens line
        Assert.Equal("4c93e63f9251787ecc5a267ee3d3f28922757b299294add1709bf8a401fad5cd", RecordDigest.Compute(zero));
        Assert.Equal(RecordDigest.Compute(legacy), RecordDigest.Compute(zero));
        Assert.DoesNotContain("reopens", canonical);
        Assert.Equal("b80fa632a2aa11861ecb6bf8442c85a1485f80b0f0e84a408c9e78705835c747", RecordDigest.Compute(git));
        Assert.Equal("a4ca2841f1831fa27b4c7b241bb2f77ca1977c91b2fe47d72b135c07af29e38c", RecordDigest.Compute(checklist));
        Assert.Equal("15c6f2265ae8e9c29aea106ccaa687a7da34e99ebe18116cbeb8930c3e647bd7", RecordDigest.Compute(sided));
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
            Checklist("v1", ChecklistSectionOf("A", "Sec", ("A-1", "q"))),
            [Submitted([OneAnswer("A-1", ChecklistOutcome.NotApplicable)], [])],
            [thread],
            new ReviewSource(
                7,
                new string('a', 40),
                new string('b', 40),
                "**/*",
                [new ChangedFile("c.txt", ChangeKind.Added, null, "hs3")]));
    }

    /// <summary>
    ///     Builds an answer of slot 1 to the given check, with an empty justification.
    /// </summary>
    /// <param name="itemId">The check id.</param>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The answer.</returns>
    private static ChecklistAnswer OneAnswer(string itemId, ChecklistOutcome outcome)
    {
        return new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            SlotId = 1,
            ItemId = itemId,
            Outcome = outcome,
            Justification = string.Empty,
            AnsweredBy = "Bob",
            AnsweredByUsername = "bob",
            AnsweredAt = _time
        };
    }

    /// <summary>
    ///     Test that the enumeration names (<c>NotApplicable</c>, <c>Added</c>), an agreed outcome, a whole-file anchor,
    ///     a user name, and different times of a thread and its comment are written as documented, with known digests
    ///     (also with one reopen) worked out with an independent script.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CoveredFieldsRecord_MatchesDocumentedFormatAndKnownValue()
    {
        // Arrange: the record with the less common values
        var record = CoveredFieldsRecord();
        var commentTicks = _time.AddHours(1).UtcTicks;

        // Act: build the canonical text and the digests
        var text = RecordDigest.Canonicalize(record);
        var digest = RecordDigest.Compute(record);
        var reopenedDigest = RecordDigest.Compute(record with { Reopens = 1 });

        // Assert: the documented text and the known answers
        var expected = string.Join(
            '\n',
            "ReviewCenter record v2",
            "fingerprint 3:abc",
            $"source git 7 40:{new string('a', 40)} 40:{new string('b', 40)} 4:**/* -",
            "changes",
            "change 5:c.txt 5:Added - 3:hs3",
            "checklist 2:v1",
            "section 1:A 3:Sec",
            "item 3:A-1 1:q",
            "checks",
            "check 3:A-1 agreed 13:NotApplicable",
            "sections",
            "outcome approved",
            "report",
            "thread file 5:a.txt 4:Head - -",
            $"claim 3:Bob 3:bob 4:Done {commentTicks}");
        Assert.Equal(expected, text);
        Assert.Equal("245104c8669f8eedbc77a42250fcbd0971c3aa53bc6d052f6717cde03d3984ba", digest);
        Assert.Equal("13914386de652fabc98c7568dc4c8a1130abfaaeeba28b727520bedf309b9c5b", reopenedDigest);
    }

    /// <summary>
    ///     Test that each reopen changes the digest, with the count as the last line, so a signature given before a
    ///     reopen can never be current again whatever the content.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ReopensChangeDigest()
    {
        // Arrange: the same content with no reopen, one and two reopens
        var none = new ReviewRecord("abc", null, [], [], ReviewSource.Upload);
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
