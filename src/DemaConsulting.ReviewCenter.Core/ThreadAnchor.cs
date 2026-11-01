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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Where a comment thread is anchored: a file of a review, optionally narrowed to a range of lines of one side, or
///     a check of the frozen checklist of the review.
/// </summary>
/// <remarks>
///     The values always travel together: the review page collects them from one selection, the
///     <see cref="ThreadService"/> validates them against one another (a range needs both ends, a side needs lines,
///     lines need a file that has them on that side), and a <see cref="ReviewThread"/> stores them side by side.
///     Grouping them keeps a thread request to what differs between requests (who, what text, which review) and
///     gives the anchor rules one name. A thread has exactly one kind of anchor: a file anchor (the default, with
///     <see cref="CheckItemId"/> <c>null</c>) or a check anchor (<see cref="ForCheck"/>, with an empty
///     <see cref="FilePath"/>, no lines and the head side). The record only carries the values; it checks nothing
///     itself, because every rule needs the review and its files or checklist, which only the service can read. The
///     record is immutable and thread-safe.
/// </remarks>
/// <param name="FilePath">
///     The path of the file within the review (ordinal comparison); empty for a check anchor.
/// </param>
/// <param name="StartLine">The first line of the anchor (1-based), or <c>null</c> to anchor to the whole file.</param>
/// <param name="EndLine">The last line of the anchor (inclusive); <c>null</c> exactly when <paramref name="StartLine"/> is <c>null</c>.</param>
/// <param name="Side">
///     The version of the file the line numbers refer to; the head by default. It is ignored for a whole-file anchor
///     (which is always stored as <see cref="ThreadSide.Head"/> and rejects <see cref="ThreadSide.Baseline"/>).
/// </param>
/// <param name="CheckItemId">
///     The identifier of the check of the frozen checklist the thread is about, or <c>null</c> for a file anchor.
/// </param>
public sealed record ThreadAnchor(
    string FilePath,
    int? StartLine = null,
    int? EndLine = null,
    ThreadSide Side = ThreadSide.Head,
    string? CheckItemId = null)
{
    /// <summary>
    ///     Gets a value indicating whether the anchor names a check of the checklist rather than a file.
    /// </summary>
    public bool IsCheck => CheckItemId is not null;

    /// <summary>
    ///     Creates the anchor of a discussion about one check of the frozen checklist.
    /// </summary>
    /// <remarks>
    ///     A check has no file and no lines, so the anchor carries an empty path, no range and the head side; this is
    ///     the one shape <see cref="ThreadService"/> accepts for a check.
    /// </remarks>
    /// <param name="itemId">The identifier of the check. Must not be null.</param>
    /// <returns>The check anchor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemId"/> is null.</exception>
    public static ThreadAnchor ForCheck(string itemId)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return new ThreadAnchor(string.Empty, CheckItemId: itemId);
    }
}
