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
///     What a service wants recorded in the audit trail: everything except the parts the store assigns
///     (sequence, time, hashes).
/// </summary>
/// <remarks>
///     A store method that changes data takes a draft and saves the audit entry in the same transaction as
///     the change, so a change and its entry exist together or not at all. The store resolves the acting
///     user identity from <see cref="ActorUsername"/>, stamps the time, and cleans the text (see
///     <see cref="AuditText"/>). The record is immutable and thread-safe.
/// </remarks>
/// <param name="Action">The action code, for example <c>project.created</c>.</param>
/// <param name="ActorUsername">The user name of the actor, or <c>null</c> when the actor is not a known user.</param>
/// <param name="ActorDisplayName">The display name of the actor, or the whole actor text when there is no user name.</param>
/// <param name="ProjectId">The project the action belongs to, or <c>null</c>.</param>
/// <param name="ReviewId">The review the action belongs to, or <c>null</c>.</param>
/// <param name="Detail">The one-line description; never comment text, passwords or tokens.</param>
public sealed record AuditDraft(
    string Action,
    string? ActorUsername,
    string ActorDisplayName,
    int? ProjectId,
    string? ReviewId,
    string Detail)
{
    /// <summary>
    ///     Gets the actor as the text stored in the entry.
    /// </summary>
    /// <remarks>
    ///     A known user is stored as <c>Display Name (username)</c>; the command line and an unknown actor
    ///     are stored as their plain text.
    /// </remarks>
    public string ActorName =>
        ActorUsername is null || ActorUsername == Actor.CommandLine.Username
            ? ActorDisplayName
            : $"{ActorDisplayName} ({ActorUsername})";

    /// <summary>
    ///     Creates a draft for an action by a signed-in user.
    /// </summary>
    /// <param name="actor">The acting user. Must not be null.</param>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The one-line description.</param>
    /// <param name="projectId">The project the action belongs to, or <c>null</c>.</param>
    /// <param name="reviewId">The review the action belongs to, or <c>null</c>.</param>
    /// <returns>The draft.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    public static AuditDraft By(
        Actor actor,
        string action,
        string detail,
        int? projectId = null,
        string? reviewId = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new AuditDraft(action, actor.Username, actor.DisplayName, projectId, reviewId, detail);
    }
}
