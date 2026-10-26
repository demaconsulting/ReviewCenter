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
///     One review-set of a repository's <c>.reviewmark.yaml</c>: a named group of files that is reviewed together.
/// </summary>
/// <remarks>
///     Only the fields ReviewCenter plans from are kept; everything else in the file is ignored. The record is
///     immutable and thread-safe.
/// </remarks>
/// <param name="Id">The identifier of the set, unique within the file.</param>
/// <param name="Title">The human-readable title of the set.</param>
/// <param name="Paths">
///     The ordered include and exclude patterns of the set: an exclusion starts with <c>!</c>, exactly as in a
///     review selection (see <see cref="SelectionMatcher"/>).
/// </param>
public sealed record ReviewSetDefinition(string Id, string Title, IReadOnlyList<string> Paths);
