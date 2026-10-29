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
///     Where a comment thread is anchored: a file of a review, optionally narrowed to a range of lines of one side.
/// </summary>
/// <remarks>
///     The four values always travel together: the review page collects them from one selection, the
///     <see cref="ThreadService"/> validates them against one another (a range needs both ends, a side needs lines,
///     lines need a file that has them on that side), and a <see cref="ReviewThread"/> stores them side by side.
///     Grouping them keeps a thread request to what differs between requests (who, what text, which review) and
///     gives the anchor rules one name. The record only carries the values; it checks nothing itself, because every
///     rule needs the review and its files, which only the service can read. The record is immutable and thread-safe.
/// </remarks>
/// <param name="FilePath">The path of the file within the review (ordinal comparison).</param>
/// <param name="StartLine">The first line of the anchor (1-based), or <c>null</c> to anchor to the whole file.</param>
/// <param name="EndLine">The last line of the anchor (inclusive); <c>null</c> exactly when <paramref name="StartLine"/> is <c>null</c>.</param>
/// <param name="Side">
///     The version of the file the line numbers refer to; the head by default. It is ignored for a whole-file anchor
///     (which is always stored as <see cref="ThreadSide.Head"/> and rejects <see cref="ThreadSide.Baseline"/>).
/// </param>
public sealed record ThreadAnchor(
    string FilePath,
    int? StartLine = null,
    int? EndLine = null,
    ThreadSide Side = ThreadSide.Head);
