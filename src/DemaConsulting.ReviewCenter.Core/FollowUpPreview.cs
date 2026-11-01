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
///     What a follow-up review would take over from an earlier review, counted without creating anything: the read-only
///     answer of <see cref="ReviewService.PreviewFollowUpAsync"/>.
/// </summary>
/// <remarks>
///     The counts come from the same <see cref="FollowUpCarry.Compute"/> that decides what the creation stores, so the
///     preview and the created review cannot disagree. The record belongs to the <see cref="ReviewService"/> unit and is an
///     immutable value holder.
/// </remarks>
/// <param name="CarriedAnswers">How many Pass and not-applicable answers would be carried.</param>
/// <param name="CarriedSections">How many not-applicable sections would be carried.</param>
/// <param name="CarriedThreads">How many open threads would be carried.</param>
/// <param name="ResolvedThreadsNotCarried">How many resolved threads would be left out on purpose.</param>
/// <param name="DroppedThreadFiles">
///     The sorted paths of files that have open threads in the earlier review but are not in the new one; those threads
///     would not be carried and the creator has to acknowledge that.
/// </param>
/// <param name="CheckThreadsLeftOut">
///     How many threads about a check (open or resolved) the earlier review has; they are never carried and need no
///     acknowledgement, so only their number is shown. Zero by default.
/// </param>
public sealed record FollowUpPreview(
    int CarriedAnswers,
    int CarriedSections,
    int CarriedThreads,
    int ResolvedThreadsNotCarried,
    IReadOnlyList<string> DroppedThreadFiles,
    int CheckThreadsLeftOut = 0);
