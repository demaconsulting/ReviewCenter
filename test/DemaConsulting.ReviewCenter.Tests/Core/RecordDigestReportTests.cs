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
///     Unit tests of the report block of the record digest (format version 2): which threads and comments are
///     covered, how they are written, and which parts of a thread are deliberately left out.
/// </summary>
/// <remarks>
///     The known digests were worked out with an independent script that builds the text from the documented layout,
///     and then confirmed against this code.
/// </remarks>
public class RecordDigestReportTests
{
    /// <summary>
    ///     The instant of the first comment of every record.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The instant of every later comment, one hour after <see cref="_time"/>.
    /// </summary>
    private static readonly DateTimeOffset _later = _time.AddHours(1);

    /// <summary>
    ///     The lines every canonical text of an upload record with the fingerprint <c>abc</c> starts with.
    /// </summary>
    private static readonly string[] _prefix =
    [
        "ReviewCenter record v2",
        "fingerprint 3:abc",
        "source upload",
        "checklist -",
        "answers",
        "sections"
    ];

    /// <summary>
    ///     Builds a comment.
    /// </summary>
    /// <param name="id">The storage identity, which must not matter.</param>
    /// <param name="text">The comment text.</param>
    /// <param name="author">The display name of the author; the user name is its lowercase form.</param>
    /// <param name="included">Whether the comment is included in the report.</param>
    /// <param name="kind">The kind of the comment.</param>
    /// <param name="created">The time of the comment.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Comment(
        int id,
        string text,
        string author,
        bool included,
        ReviewCommentKind kind = ReviewCommentKind.Comment,
        DateTimeOffset? created = null)
    {
        return new ReviewComment
        {
            Id = id,
            ThreadId = 1,
            Author = author,
            AuthorUsername = author.ToLowerInvariant(),
            Text = text,
            Created = created ?? (author == "Ann" ? _time : _later),
            IncludeInReport = included,
            Kind = kind
        };
    }

    /// <summary>
    ///     Builds a thread on lines 1-2 of a file.
    /// </summary>
    /// <param name="id">The storage identity, which must not matter.</param>
    /// <param name="file">The path of the file.</param>
    /// <param name="comments">The comments in posting order.</param>
    /// <param name="side">The side of the thread.</param>
    /// <param name="state">The state of the thread.</param>
    /// <param name="carriedFrom">The review the thread was carried from, or <c>null</c>.</param>
    /// <param name="created">The creation time of the thread.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(
        int id,
        string file,
        IReadOnlyList<ReviewComment> comments,
        ThreadSide side = ThreadSide.Head,
        ReviewThreadState state = ReviewThreadState.Open,
        string? carriedFrom = null,
        DateTimeOffset? created = null)
    {
        return new ReviewThread
        {
            Id = id,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = file,
            Side = side,
            StartLine = file == "b.txt" ? null : 1,
            EndLine = file == "b.txt" ? null : 2,
            State = state,
            Created = created ?? _time,
            CarriedFromReviewId = carriedFrom,
            CarriedFromThreadId = carriedFrom is null ? null : 9,
            Comments = [.. comments]
        };
    }

    /// <summary>
    ///     Builds an included starting comment ("Please fix" by Ann), the claim of a Finding.
    /// </summary>
    /// <param name="id">The storage identity.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Claim(int id = 10)
    {
        return Comment(id, "Please fix", "Ann", true);
    }

    /// <summary>
    ///     Builds an included resolution note ("Done" by Bob).
    /// </summary>
    /// <param name="id">The storage identity.</param>
    /// <param name="included">Whether the note is included in the report.</param>
    /// <returns>The comment.</returns>
    private static ReviewComment Resolution(int id = 11, bool included = true)
    {
        return Comment(id, "Done", "Bob", included, ReviewCommentKind.Resolution);
    }

    /// <summary>
    ///     Builds a Question with an included reply ("Reply?" by Bob) on a whole file.
    /// </summary>
    /// <param name="id">The storage identity.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread QuestionWithIncludedReply(int id = 2)
    {
        return Thread(id, "b.txt", [Comment(20, "Why?", "Ann", false), Comment(21, "Reply?", "Bob", true)]);
    }

    /// <summary>
    ///     Builds an upload record with the given threads.
    /// </summary>
    /// <param name="threads">The threads.</param>
    /// <param name="reopens">The number of reopens.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(IReadOnlyList<ReviewThread>? threads = null, int reopens = 0)
    {
        return new ReviewRecord("abc", null, [], [], threads ?? [], ReviewSource.Upload, reopens);
    }

    /// <summary>
    ///     Builds the canonical text expected for the lines after the report marker.
    /// </summary>
    /// <param name="reportLines">The lines after <c>report</c>.</param>
    /// <returns>The expected text.</returns>
    private static string Expected(params string[] reportLines)
    {
        return string.Join('\n', [.. _prefix, "report", .. reportLines]);
    }

    /// <summary>
    ///     Builds the expected lines of the Finding thread of <see cref="Claim"/>, with its resolution note when asked.
    /// </summary>
    /// <param name="side">The written side.</param>
    /// <param name="carried">The carried suffix, or the empty text.</param>
    /// <param name="withResolution">Whether the resolution line is expected.</param>
    /// <returns>The lines.</returns>
    private static string[] FindingLines(string side = "4:Head", string carried = "", bool withResolution = false)
    {
        var lines = new List<string>
        {
            $"thread file 5:a.txt {side} 1 2{carried}",
            $"claim 3:Ann 3:ann 10:Please fix {_time.UtcTicks}"
        };
        if (withResolution)
        {
            lines.Add($"resolution 3:Bob 3:bob 4:Done {_later.UtcTicks}");
        }

        return [.. lines];
    }

    /// <summary>
    ///     Test that a record without any included comment has only the report marker, with the documented text and
    ///     known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_EmptyReport_MatchesDocumentedFormatAndKnownValue()
    {
        // Arrange: a record without threads
        var record = Record();

        // Act
        var text = RecordDigest.Canonicalize(record);

        // Assert
        Assert.Equal(Expected(), text);
        Assert.EndsWith("\nsections\nreport", text);
        Assert.Equal("eb725d508e04716f367b32ac442820c89667cc920020b10eacebf181d7365ab3", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that the canonical text starts with the version 2 header.
    /// </summary>
    [Fact]
    public void RecordDigest_Canonicalize_Header_IsV2()
    {
        // Arrange, Act
        var text = RecordDigest.Canonicalize(Record());

        // Assert
        Assert.StartsWith("ReviewCenter record v2\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a resolved Finding is written as the claim and the resolution note, with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ResolvedFinding_MatchesKnownValue()
    {
        // Arrange: a resolved Finding with a note
        var record = Record([Thread(1, "a.txt", [Claim(), Resolution()], state: ReviewThreadState.Resolved)]);

        // Act, Assert
        Assert.Equal(Expected(FindingLines(withResolution: true)), RecordDigest.Canonicalize(record));
        Assert.Equal("d832d0790d0a53f644ccae5deedb323bfc501ed8b7492f70c313af88644d17e1", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that an open Finding is written as the claim only, with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_OpenFinding_MatchesKnownValue()
    {
        // Arrange
        var record = Record([Thread(1, "a.txt", [Claim()])]);

        // Act, Assert
        Assert.Equal(Expected(FindingLines()), RecordDigest.Canonicalize(record));
        Assert.Equal("00aacca477c6cb3b6e37c39f8252caa0852a4d28a9f2364c3865f632cc08651f", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that a Question with an included reply is written with the reply only, on a whole-file anchor, with a
    ///     known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_QuestionWithIncludedReply_MatchesKnownValue()
    {
        // Arrange
        var record = Record([QuestionWithIncludedReply()]);

        // Act, Assert
        Assert.Equal(
            Expected("thread file 5:b.txt 4:Head - -", $"comment 3:Bob 3:bob 6:Reply? {_later.UtcTicks}"),
            RecordDigest.Canonicalize(record));
        Assert.Equal("1ce88b76fb7ee47185876938f5eb20270fd4848a640342206e008f419fee096c", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that the baseline side of a Finding is written after the path, with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_BaselineFinding_MatchesKnownValue()
    {
        // Arrange
        var record = Record([Thread(1, "a.txt", [Claim()], ThreadSide.Baseline)]);

        // Act, Assert
        Assert.Equal(Expected(FindingLines("8:Baseline")), RecordDigest.Canonicalize(record));
        Assert.Equal("2890acd760711bfee36a2319e0cb9c881fb0c21cf2d9b2ecb1c9b23e743b45aa", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that a Finding carried over from another review is written with the carried marker, with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CarriedFinding_MatchesKnownValue()
    {
        // Arrange
        var record = Record([Thread(1, "a.txt", [Claim()], carriedFrom: "rev-0")]);

        // Act, Assert
        Assert.Equal(Expected(FindingLines(carried: " carried 5:rev-0")), RecordDigest.Canonicalize(record));
        Assert.Equal("dfe9775c4f9a8ae507966bbf1bdebe8c375ff4b63176b4a3dfe8464dcc1b4e15", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that the reopen count comes after the report, with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_Reopens_MatchesKnownValueAfterReport()
    {
        // Arrange
        var record = Record(reopens: 1);

        // Act, Assert
        Assert.Equal(Expected() + "\nreopens 1", RecordDigest.Canonicalize(record));
        Assert.Equal("bf3f0cb98cfa4d84725f25f699f771c292755fb7becefa3a65400bba9a0fc398", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that two included threads are written as blocks sorted by their text, whatever the order of the threads,
    ///     with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_TwoIncludedThreads_SortedBlocksMatchKnownValue()
    {
        // Arrange: the same two threads in both orders
        var finding = Thread(1, "a.txt", [Claim(), Resolution()], state: ReviewThreadState.Resolved);
        var question = QuestionWithIncludedReply();
        var forward = Record([finding, question]);
        var backward = Record([question, finding]);

        // Act, Assert: the a.txt block comes first in both
        var expected = Expected(
            [
                .. FindingLines(withResolution: true),
                "thread file 5:b.txt 4:Head - -",
                $"comment 3:Bob 3:bob 6:Reply? {_later.UtcTicks}"
            ]);
        Assert.Equal(expected, RecordDigest.Canonicalize(forward));
        Assert.Equal(expected, RecordDigest.Canonicalize(backward));
        Assert.Equal("4ff79bc0ab85820a0ba674aaccc642d17f818c43ad4ea2474aeb133798c69724", RecordDigest.Compute(forward));
    }

    /// <summary>
    ///     Test that threads made of chat only, in any state and with any replies, leave the digest of an empty report.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ChatOnlyThreads_EqualsEmptyReport()
    {
        // Arrange: an open and a resolved chat thread, one with replies
        var open = Thread(1, "a.txt", [Comment(1, "Why?", "Ann", false), Comment(2, "Because", "Bob", false)]);
        var resolved = Thread(2, "b.txt", [Comment(3, "Hmm", "Ann", false)], state: ReviewThreadState.Resolved);

        // Act, Assert
        Assert.Equal(RecordDigest.Compute(Record()), RecordDigest.Compute(Record([open, resolved])));
    }

    /// <summary>
    ///     Test that chat added to a Finding does not change the digest, and that an included reply does.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ChatAddedToFinding_DigestUnchanged()
    {
        // Arrange
        var plain = Record([Thread(1, "a.txt", [Claim()])]);
        var withChat = Record([Thread(1, "a.txt", [Claim(), Comment(11, "Thanks", "Bob", false)])]);
        var withIncluded = Record([Thread(1, "a.txt", [Claim(), Comment(11, "Thanks", "Bob", true)])]);

        // Act, Assert
        Assert.Equal(RecordDigest.Compute(plain), RecordDigest.Compute(withChat));
        Assert.NotEqual(RecordDigest.Compute(plain), RecordDigest.Compute(withIncluded));
    }

    /// <summary>
    ///     Test that the state of a thread is not covered: opening or resolving it leaves the digest alone.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ThreadState_NotCovered()
    {
        // Arrange
        var open = Record([Thread(1, "a.txt", [Claim()])]);
        var resolved = Record([Thread(1, "a.txt", [Claim()], state: ReviewThreadState.Resolved)]);

        // Act, Assert
        Assert.Equal(RecordDigest.Compute(open), RecordDigest.Compute(resolved));
    }

    /// <summary>
    ///     Test that the creation time of a thread, the storage identities and the identity of the carried thread are
    ///     not covered.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ThreadCreatedAndIds_NotCovered()
    {
        // Arrange: the same carried Finding with other identities and another creation time
        var one = Record([Thread(1, "a.txt", [Claim(10)], carriedFrom: "rev-0")]);
        var other = Record(
            [Thread(77, "a.txt", [Claim(990)], carriedFrom: "rev-0", created: _time.AddDays(3))]);

        // Act, Assert
        Assert.Equal(RecordDigest.Compute(one), RecordDigest.Compute(other));
    }

    /// <summary>
    ///     Test that promoting a Question and demoting it again change the digest and restore the original one.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_PromoteDemote_ChangesDigestAndRoundTrips()
    {
        // Arrange: the same thread as a Question and as a Finding
        var question = Record([Thread(1, "a.txt", [Comment(10, "Please fix", "Ann", false)])]);
        var finding = Record([Thread(1, "a.txt", [Claim()])]);
        var demoted = Record([Thread(1, "a.txt", [Comment(10, "Please fix", "Ann", false)])]);

        // Act, Assert
        Assert.NotEqual(RecordDigest.Compute(question), RecordDigest.Compute(finding));
        Assert.Equal(RecordDigest.Compute(question), RecordDigest.Compute(demoted));
    }

    /// <summary>
    ///     Test that including a reply changes the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ReplyInclusionToggle_ChangesDigest()
    {
        // Arrange
        var excluded = Record([Thread(1, "a.txt", [Claim(), Comment(11, "More", "Bob", false)])]);
        var included = Record([Thread(1, "a.txt", [Claim(), Comment(11, "More", "Bob", true)])]);

        // Act, Assert
        Assert.NotEqual(RecordDigest.Compute(excluded), RecordDigest.Compute(included));
    }

    /// <summary>
    ///     Test that a resolution note is covered when it is included, and that an excluded one changes nothing.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ResolutionNote_CoveredOnlyWhenIncluded()
    {
        // Arrange: a Finding with no note, an included note and an excluded note
        var none = Record([Thread(1, "a.txt", [Claim()])]);
        var included = Record([Thread(1, "a.txt", [Claim(), Resolution(included: true)])]);
        var excluded = Record([Thread(1, "a.txt", [Claim(), Resolution(included: false)])]);

        // Act, Assert
        Assert.NotEqual(RecordDigest.Compute(none), RecordDigest.Compute(included));
        Assert.Equal(RecordDigest.Compute(none), RecordDigest.Compute(excluded));
    }

    /// <summary>
    ///     Test that the order of the threads does not change the digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ThreadOrder_SameDigest()
    {
        // Arrange
        var first = Thread(1, "a.txt", [Claim()]);
        var second = Thread(2, "c.txt", [Comment(30, "Other", "Cy", true)]);

        // Act, Assert
        Assert.Equal(RecordDigest.Compute(Record([first, second])), RecordDigest.Compute(Record([second, first])));
    }

    /// <summary>
    ///     Test that the order of the included comments of a thread matters.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ReportCommentOrder_ChangesDigest()
    {
        // Arrange: two included replies in opposite order
        var one = Comment(11, "one", "Bob", true, created: _later);
        var two = Comment(12, "two", "Bob", true, created: _later);
        var forward = Record([Thread(1, "a.txt", [Claim(), one, two])]);
        var backward = Record([Thread(1, "a.txt", [Claim(), two, one])]);

        // Act, Assert
        Assert.NotEqual(RecordDigest.Compute(forward), RecordDigest.Compute(backward));
    }

    /// <summary>
    ///     Test that text cannot imitate the report lines: a claim whose text holds a following line is not the same
    ///     as the claim and that line.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_DelimitersInReportText_DoNotCollide()
    {
        // Arrange: one claim whose text runs into a comment line versus the claim followed by that comment
        var ticks = _later.UtcTicks;
        var single = Record([Thread(1, "a.txt", [Comment(10, $"a {ticks}\ncomment 3:Bob 3:bob 1:b", "Ann", true)])]);
        var split = Record(
            [
                Thread(
                    1,
                    "a.txt",
                    [Comment(10, "a", "Ann", true, created: _later), Comment(11, "b", "Bob", true, created: _later)])
            ]);

        // Act, Assert: the digests and the texts differ
        Assert.NotEqual(RecordDigest.Compute(single), RecordDigest.Compute(split));
        Assert.NotEqual(RecordDigest.Canonicalize(single), RecordDigest.Canonicalize(split));
    }
}
