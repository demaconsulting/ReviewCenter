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
///     One time an administrator reopened an approved (locked) review: who, when, why, and which approved record
///     it was.
/// </summary>
/// <remarks>
///     A reopen is history and is never changed or deleted. The number of reopens of a review is part of its
///     record digest (see <see cref="RecordDigest"/>), so after a reopen every earlier signature is out of date and
///     can never be current again, even if the reviewed content is changed back to what it was. The record is an
///     immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="Id">The identity of the reopen, assigned by the store.</param>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ReviewId">The identifier of the reopened review.</param>
/// <param name="ReopenedAt">The time of the reopen (UTC).</param>
/// <param name="ReopenedBy">The user name of the administrator who reopened the review.</param>
/// <param name="ReopenedByDisplayName">The display name of that administrator at the time.</param>
/// <param name="Reason">The reason the administrator gave; at most <see cref="ReviewLimits.MaxReopenReasonLength"/> characters.</param>
/// <param name="PreviousDigest">The digest of the approved record that was reopened.</param>
public sealed record ReviewReopen(
    int Id,
    int ProjectId,
    string ReviewId,
    DateTimeOffset ReopenedAt,
    string ReopenedBy,
    string ReopenedByDisplayName,
    string Reason,
    string PreviousDigest);
