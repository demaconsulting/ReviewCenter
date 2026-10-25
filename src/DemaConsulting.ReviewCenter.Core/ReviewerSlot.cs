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
///     One reviewer slot of a review: a primary reviewer and an optional alternate, either of whom may sign.
/// </summary>
/// <remarks>
///     The slot stores user identities only; names are resolved when the slot is shown, so a renamed user
///     appears under the new name. A slot with any signature is never changed or removed (see
///     <see cref="SigningService"/>), so a signature always points to a slot that still describes who signed.
///     The class is a plain data holder with no behavior; instances are not thread-safe.
/// </remarks>
public sealed class ReviewerSlot
{
    /// <summary>
    ///     Gets the identity of the slot, assigned by the store; it grows with creation order.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the identity of the owning project.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the review the slot belongs to.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the identity of the primary reviewer.
    /// </summary>
    public int PrimaryUserId { get; init; }

    /// <summary>
    ///     Gets the identity of the alternate reviewer, or <c>null</c> when the slot has none.
    /// </summary>
    public int? AlternateUserId { get; init; }
}
