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
///     One entry of the tamper-evident audit trail: who did what, when, and the hash that links the entry to
///     the one before it.
/// </summary>
/// <remarks>
///     Entries are append-only. <see cref="Hash"/> covers every other field including
///     <see cref="PreviousHash"/>, so changing, removing, reordering or inserting an entry breaks the chain
///     at a position <see cref="AuditChain"/> can name. The actor is stored as a snapshot of text, not as a
///     reference, so a later change to the user cannot rewrite history. Detail text never carries comment
///     text, passwords or tokens.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>
    ///     Gets the position of the entry in the chain; the first entry is 1 and there are no gaps.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    ///     Gets the time the entry was recorded, in UTC.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    ///     Gets the identity of the acting user when the actor is a known user; otherwise <c>null</c>.
    /// </summary>
    public int? ActorUserId { get; init; }

    /// <summary>
    ///     Gets the acting person as text at the time of the action (display name and user name).
    /// </summary>
    public string ActorName { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the action code, for example <c>review.created</c>.
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the project the action belongs to, or <c>null</c>.
    /// </summary>
    public int? ProjectId { get; init; }

    /// <summary>
    ///     Gets the review the action belongs to, or <c>null</c>.
    /// </summary>
    public string? ReviewId { get; init; }

    /// <summary>
    ///     Gets the one-line description of what happened.
    /// </summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the hash of the previous entry, or <see cref="AuditChain.GenesisHash"/> for the first entry.
    /// </summary>
    public string PreviousHash { get; init; } = AuditChain.GenesisHash;

    /// <summary>
    ///     Gets the hash of this entry as lowercase hexadecimal text.
    /// </summary>
    public string Hash { get; init; } = string.Empty;
}
