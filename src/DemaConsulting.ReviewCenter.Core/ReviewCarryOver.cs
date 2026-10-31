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
///     What a follow-up review takes over from the review it follows: the rows to store together with the new review and
///     the counts and file names the audit text and the creation rules need.
/// </summary>
/// <remarks>
///     The rows are complete entities of the new review (project and review identifiers set, carried markers set, thread
///     identities not yet assigned); the store inserts them in the same transaction as the review itself, so a carry that
///     fails creates nothing. The record is computed by <see cref="FollowUpCarry.Compute"/> and is an immutable value
///     holder.
/// </remarks>
/// <param name="Answers">The carried checklist answers (Pass and N/A only), sorted by check identifier.</param>
/// <param name="SectionStates">The carried not-applicable sections, sorted by section identifier.</param>
/// <param name="Threads">The carried open threads with copies of their comments, in the order of the source threads.</param>
/// <param name="ResolvedThreadsNotCarried">How many resolved threads of the source were left out on purpose.</param>
/// <param name="DroppedThreadFiles">
///     The sorted paths of files that have open threads in the source but are not in the new review; those threads are not
///     carried and the creator has to acknowledge that.
/// </param>
public sealed record ReviewCarryOver(
    IReadOnlyList<ChecklistAnswer> Answers,
    IReadOnlyList<ChecklistSectionState> SectionStates,
    IReadOnlyList<ReviewThread> Threads,
    int ResolvedThreadsNotCarried,
    IReadOnlyList<string> DroppedThreadFiles);
