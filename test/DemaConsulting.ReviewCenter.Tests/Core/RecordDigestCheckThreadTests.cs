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
///     Unit tests of the way the record digest covers a thread about a check: the <c>thread check</c> anchor line, the
///     chat that stays out, the file threads whose digests do not change and the ordinal order of the blocks.
/// </summary>
/// <remarks>
///     The known digest was worked out with an independent script that builds the text from the documented layout,
///     and then confirmed against this code.
/// </remarks>
public class RecordDigestCheckThreadTests
{
    /// <summary>
    ///     The instant of every comment.
    /// </summary>
    private static readonly DateTimeOffset _time = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The lines every canonical text of an upload record with the fingerprint <c>abc</c> starts with.
    /// </summary>
    private static readonly string[] _prefix =
    [
        "ReviewCenter record v2",
        "fingerprint 3:abc",
        "source upload",
        "checklist -",
        "checks",
        "sections",
        "outcome none"
    ];

    /// <summary>
    ///     Builds a thread by Ann with one comment.
    /// </summary>
    /// <param name="file">The path of the file, or the empty text for a thread about a check.</param>
    /// <param name="checkId">The check, or <c>null</c> for a file thread.</param>
    /// <param name="included">Whether the comment is included in the report.</param>
    /// <param name="state">The state of the thread.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread Thread(
        string file,
        string? checkId,
        bool included,
        ReviewThreadState state = ReviewThreadState.Open)
    {
        return new ReviewThread
        {
            Id = 1,
            ProjectId = 1,
            ReviewId = "rev-1",
            FilePath = file,
            CheckItemId = checkId,
            StartLine = file.Length > 0 ? 1 : null,
            EndLine = file.Length > 0 ? 2 : null,
            State = state,
            Created = _time,
            Comments =
            [
                new ReviewComment
                {
                    Id = 10,
                    ThreadId = 1,
                    Author = "Ann",
                    AuthorUsername = "ann",
                    Text = "Please fix",
                    Created = _time,
                    IncludeInReport = included
                }
            ]
        };
    }

    /// <summary>
    ///     Builds an upload record with the given threads.
    /// </summary>
    /// <param name="threads">The threads.</param>
    /// <returns>The record.</returns>
    private static ReviewRecord Record(params ReviewThread[] threads)
    {
        return new ReviewRecord("abc", null, [], threads, ReviewSource.Upload, 0);
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
    ///     Test that an included comment of a thread about a check is written under a <c>thread check</c> line with
    ///     the check identifier only, with a known digest.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_IncludedCheckThreadComment_UsesCheckAnchorLine()
    {
        // Arrange
        var record = Record(Thread(string.Empty, "C-1", true));

        // Act, Assert
        Assert.Equal(
            Expected("thread check 3:C-1", $"claim 3:Ann 3:ann 10:Please fix {_time.UtcTicks}"),
            RecordDigest.Canonicalize(record));
        Assert.Equal("c775d645ef3c173b15a7d3260b891d3df5800834c59d7ee782e8f785856b1119", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that a thread about a check made of chat only, in any state, leaves the digest of an empty report.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_ChatCheckThread_DoesNotChangeDigest()
    {
        // Arrange
        var empty = RecordDigest.Compute(Record());

        // Act, Assert
        Assert.Equal(empty, RecordDigest.Compute(Record(Thread(string.Empty, "C-1", false))));
        Assert.Equal(empty, RecordDigest.Compute(Record(Thread(string.Empty, "C-1", false, ReviewThreadState.Resolved))));
    }

    /// <summary>
    ///     Test that the digests of records with file threads only are the ones they were before threads about a check
    ///     existed, so no signature on a file thread changes.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_FileThreadsOnly_DigestUnchangedByCheckThreadSupport()
    {
        // Arrange: the open Finding of the report tests, whose digest is a known value there as well
        var record = Record(Thread("a.txt", null, true));

        // Act, Assert
        Assert.Equal(
            Expected("thread file 5:a.txt 4:Head 1 2", $"claim 3:Ann 3:ann 10:Please fix {_time.UtcTicks}"),
            RecordDigest.Canonicalize(record));
        Assert.Equal("15c6f2265ae8e9c29aea106ccaa687a7da34e99ebe18116cbeb8930c3e647bd7", RecordDigest.Compute(record));
    }

    /// <summary>
    ///     Test that blocks of file threads and of threads about a check are sorted by their text, whatever the order
    ///     of the threads, so a check block comes before a file block.
    /// </summary>
    [Fact]
    public void RecordDigest_Compute_CheckAndFileBlocks_AreSortedOrdinally()
    {
        // Arrange: the same two threads in both orders
        var file = Thread("a.txt", null, true);
        var check = Thread(string.Empty, "C-1", true);

        // Act, Assert
        var expected = Expected(
            "thread check 3:C-1",
            $"claim 3:Ann 3:ann 10:Please fix {_time.UtcTicks}",
            "thread file 5:a.txt 4:Head 1 2",
            $"claim 3:Ann 3:ann 10:Please fix {_time.UtcTicks}");
        Assert.Equal(expected, RecordDigest.Canonicalize(Record(file, check)));
        Assert.Equal(expected, RecordDigest.Canonicalize(Record(check, file)));
    }
}
