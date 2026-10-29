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
///     What a review is made from when it is created from a registered repository: which repository, which two
///     revisions to compare, and which of the changed files to review.
/// </summary>
/// <remarks>
///     The four values are the "repository source" of a review. They are entered together on the new-review form,
///     sent together in the <c>repository</c> object of the API request, and the review keeps them together (the
///     repository, the two commits they resolve to, and the selection). The record carries the values as entered:
///     <see cref="ReviewService.CreateFromRepositoryAsync"/> trims, validates and resolves them, so a bad value is
///     reported there with its domain rule. It is the request side of what <see cref="ReviewSource"/> records after
///     creation: revision names as entered here, versus the commits they resolved to and the frozen changes there.
///     The record is immutable and thread-safe.
/// </remarks>
/// <param name="RepositoryId">The identity of an enabled registered repository.</param>
/// <param name="HeadRevision">The head revision: a branch, tag or commit name (see <see cref="RevisionValidator"/>).</param>
/// <param name="BaselineRevision">
///     The baseline revision, or <c>null</c> or white space to review every file at the head as added.
/// </param>
/// <param name="Selection">
///     The file selection: patterns, one per line (see <see cref="SelectionMatcher"/>). Only changed files that
///     match are reviewed; <c>null</c> or empty selects every changed file.
/// </param>
public sealed record RepositorySource(
    int RepositoryId,
    string? HeadRevision,
    string? BaselineRevision,
    string? Selection);
