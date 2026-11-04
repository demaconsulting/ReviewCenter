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
///     One mail the server sent (or is about to send) and counted against the sending limits.
/// </summary>
/// <remarks>
///     The rows exist only to enforce the limits (per user, per target address and for the whole server); they hold
///     no mail text and are not shown anywhere. The address is kept so that the limit per address can be applied
///     across all users, which is what stops one user from using the server to mail a third party again and
///     again. The type is not thread-safe.
/// </remarks>
public sealed class NotificationSendLogEntry
{
    /// <summary>
    ///     The kind of a mail that carries a confirmation code.
    /// </summary>
    public const string KindConfirmation = "confirmation";

    /// <summary>
    ///     The kind of a mail that tells a user in the mode <see cref="NotificationMode.Every"/> about news.
    /// </summary>
    public const string KindUpdate = "update";

    /// <summary>
    ///     The kind of a daily summary mail.
    /// </summary>
    public const string KindDigest = "digest";

    /// <summary>
    ///     Gets the identity of the row, assigned by the database.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the user the mail was sent for.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    ///     Gets the address the mail was sent to (normalized, lower case).
    /// </summary>
    public string ToAddress { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the kind of the mail, for example <see cref="KindConfirmation"/>.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the mail was sent (UTC).
    /// </summary>
    public DateTimeOffset SentUtc { get; init; }
}
