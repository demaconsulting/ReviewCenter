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
///     The signed-in person on whose behalf a service records a comment or a checklist answer.
/// </summary>
/// <remarks>
///     The record carries only what is stored with the work: the stable user name and the display name at
///     the time. The services accept an actor, never free text, so the author of a record always comes from
///     a signed-in user and cannot be supplied by a form field. The record is immutable and thread-safe.
/// </remarks>
/// <param name="Username">The user name of the signed-in user.</param>
/// <param name="DisplayName">The display name of the signed-in user at the time of the action.</param>
public sealed record Actor(string Username, string DisplayName)
{
    /// <summary>
    ///     Gets the non-secret display id of the personal access token the request was authenticated with, or
    ///     <c>null</c> when the user is signed in with a password (the web pages and the command line).
    /// </summary>
    /// <remarks>
    ///     Set only by the API, never by the web pages. It marks work done by automation: the audit trail names the
    ///     token, and <see cref="SigningService"/> refuses every signing action of such an actor, because a signature
    ///     must be a person's fresh act and a token is a stored credential.
    /// </remarks>
    public string? ApiTokenId { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the actor was authenticated with a personal access token.
    /// </summary>
    public bool ViaApiToken => ApiTokenId is not null;

    /// <summary>
    ///     Gets the actor used for changes made from the command line (for example <c>user add</c>), where no
    ///     person is signed in.
    /// </summary>
    /// <remarks>
    ///     The user name contains characters a real user name cannot have, so it never matches a stored user.
    /// </remarks>
    public static Actor CommandLine { get; } = new("(command line)", "command line");
}
