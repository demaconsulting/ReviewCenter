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
///     What one user has already seen of one review: the one read-state row of the inbox and of the unread markers.
/// </summary>
/// <remarks>
///     There is at most one row per user and review, and there is no separate notification record: the inbox is
///     computed from the review data and this row says what is new to the user. The comment watermark works because
///     comment identities are assigned by the store in posting order and are never reused, so one integer replaces a
///     time per thread. The row is a personal convenience, not part of the review record: it is not audited and not
///     part of the record digest (see <see cref="RecordDigest"/>). The record is an immutable value holder and is
///     safe to share between threads.
/// </remarks>
/// <param name="UserId">The identity of the user who saw the review.</param>
/// <param name="ProjectId">The identity of the owning project.</param>
/// <param name="ReviewId">The identifier of the review.</param>
/// <param name="LastSeenCommentId">
///     The highest comment identity the user has seen; a comment by someone else with a higher identity is unread.
///     Zero means no comment was seen.
/// </param>
/// <param name="LastSeenAt">
///     The time (UTC) read before the review was loaded when the user last saw it; approvals, reopens and failed
///     checks after this time are new to the user.
/// </param>
public sealed record ReviewUserState(
    int UserId,
    int ProjectId,
    string ReviewId,
    int LastSeenCommentId,
    DateTimeOffset LastSeenAt);
