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
///     Whether a line diff of a file of a review could be produced, and if not, why.
/// </summary>
public enum FileDiffStatus
{
    /// <summary>
    ///     The diff was computed; see <see cref="FileDiff.Result"/>.
    /// </summary>
    Diff,

    /// <summary>
    ///     The head or the baseline is binary, so there are no lines to compare.
    /// </summary>
    Binary,

    /// <summary>
    ///     The file is too large to compare line by line (see <see cref="ReviewLimits.MaxDiffLines"/> and
    ///     <see cref="ReviewLimits.MaxDiffEdits"/>).
    /// </summary>
    TooLarge,

    /// <summary>
    ///     The review has nothing to compare against: it is an upload, or its baseline commit is absent.
    /// </summary>
    NoBaseline
}

/// <summary>
///     The line diff of one file of a review between its frozen baseline and its frozen head.
/// </summary>
/// <remarks>The record is immutable and thread-safe.</remarks>
/// <param name="Status">Whether a diff could be produced.</param>
/// <param name="Result">The diff rows; non-null exactly when <paramref name="Status"/> is <see cref="FileDiffStatus.Diff"/>.</param>
/// <param name="OnlyLineEndingsDiffer">
///     <c>true</c> when the lines are identical but the bytes are not (line endings or the final newline differ).
/// </param>
public sealed record FileDiff(FileDiffStatus Status, LineDiffResult? Result = null, bool OnlyLineEndingsDiffer = false);
