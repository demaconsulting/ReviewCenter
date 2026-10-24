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
///     One comment of a review thread: who wrote it, what, and when.
/// </summary>
/// <remarks>
///     Comments are immutable records: once stored they are never edited or deleted, because a
///     review record must show exactly what was said. Instances are created by
///     <see cref="ThreadService"/>, which enforces the rules, and are populated by the storage
///     layer. The type is not thread-safe.
/// </remarks>
public sealed class ReviewComment
{
    /// <summary>
    ///     Gets the identity assigned by the database; it grows with posting order.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the identity of the thread the comment belongs to.
    /// </summary>
    public int ThreadId { get; init; }

    /// <summary>
    ///     Gets the author name as typed (trimmed, 1 to <see cref="ReviewLimits.MaxAuthorLength"/> characters).
    ///     There is no sign-in yet, so the name is an unverified label.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the comment text (trimmed, 1 to <see cref="ReviewLimits.MaxCommentLength"/> characters;
    ///     line breaks inside the text are kept). It is plain text, never markup.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the comment was posted (UTC).
    /// </summary>
    public DateTimeOffset Created { get; init; }
}
