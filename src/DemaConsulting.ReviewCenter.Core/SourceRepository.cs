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
///     A registered git repository as it is shown to an administrator: where it is, who reads it and whether a
///     token is stored, but never the token itself.
/// </summary>
/// <remarks>
///     The type has no member that can hold a token, encrypted or not, so a page that shows it cannot leak one.
///     The record is immutable and thread-safe.
/// </remarks>
/// <param name="Id">The identity assigned by the store.</param>
/// <param name="ProjectId">The identity of the one project the repository belongs to.</param>
/// <param name="Name">The name of the repository, unique within its project (without regard to case).</param>
/// <param name="Location">The location git reads from: an https, http or file URL or an absolute local path.</param>
/// <param name="Username">The user name sent to the server, or <c>null</c> when the repository is read anonymously.</param>
/// <param name="HasToken">Whether a token is stored; the token itself is never exposed.</param>
/// <param name="Enabled">Whether the repository can be selected as a content source.</param>
/// <param name="CreatedAt">The moment the repository was registered (UTC).</param>
/// <param name="UpdatedAt">The moment the registration last changed (UTC).</param>
public sealed record SourceRepository(
    int Id,
    int ProjectId,
    string Name,
    string Location,
    string? Username,
    bool HasToken,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
///     What an ordinary user may know about an enabled repository: its identity and name, nothing about where it
///     is or how it is read.
/// </summary>
/// <param name="Id">The identity of the repository.</param>
/// <param name="Name">The name of the repository.</param>
public sealed record RepositorySummary(int Id, string Name);

/// <summary>
///     The fields of a repository that are stored when it is registered. The token, when there is one, is already
///     encrypted.
/// </summary>
/// <param name="ProjectId">The identity of the project the repository belongs to; it never changes afterwards.</param>
/// <param name="Name">The validated name.</param>
/// <param name="Location">The validated location.</param>
/// <param name="Username">The user name, or <c>null</c>.</param>
/// <param name="EncryptedToken">The encrypted token, or <c>null</c> when there is none.</param>
/// <param name="CreatedAt">The registration time (UTC).</param>
public sealed record NewRepository(
    int ProjectId,
    string Name,
    string Location,
    string? Username,
    string? EncryptedToken,
    DateTimeOffset CreatedAt);

/// <summary>
///     What an administrator wants to do with the stored token of a repository.
/// </summary>
public enum TokenAction
{
    /// <summary>Leave the stored token as it is (the default).</summary>
    Keep,

    /// <summary>Store a new token in place of the old one.</summary>
    Replace,

    /// <summary>Remove the stored token.</summary>
    Clear
}

/// <summary>
///     A change of the stored token: keep it, replace it or clear it.
/// </summary>
/// <remarks>
///     The token is write-only: it can be set or cleared but never read back through the application. In
///     <see cref="RepositoryService"/> the <see cref="Token"/> is the plain token; in
///     <see cref="IRepositoryStore"/> it is the encrypted text. The record is immutable and thread-safe.
/// </remarks>
/// <param name="Action">What to do with the token.</param>
/// <param name="Token">The new token for <see cref="TokenAction.Replace"/>; otherwise <c>null</c>.</param>
public sealed record TokenChange(TokenAction Action, string? Token)
{
    /// <summary>
    ///     Gets the change that leaves the stored token alone.
    /// </summary>
    public static TokenChange Keep { get; } = new(TokenAction.Keep, null);

    /// <summary>
    ///     Gets the change that removes the stored token.
    /// </summary>
    public static TokenChange Clear { get; } = new(TokenAction.Clear, null);

    /// <summary>
    ///     Creates the change that stores a new token.
    /// </summary>
    /// <param name="token">The new token.</param>
    /// <returns>The change.</returns>
    public static TokenChange Replace(string token) => new(TokenAction.Replace, token);

    /// <inheritdoc />
    public override string ToString() => $"TokenChange {{ Action = {Action} }}";
}

/// <summary>
///     The new state of an existing repository registration, as given to the store.
/// </summary>
/// <param name="Name">The validated name.</param>
/// <param name="Location">The validated location.</param>
/// <param name="Username">The user name, or <c>null</c>.</param>
/// <param name="Enabled">Whether the repository can be selected.</param>
/// <param name="Token">What to do with the stored token; its text, if any, is already encrypted.</param>
/// <param name="UpdatedAt">The time of the change (UTC).</param>
public sealed record RepositoryUpdate(
    string Name,
    string Location,
    string? Username,
    bool Enabled,
    TokenChange Token,
    DateTimeOffset UpdatedAt);
