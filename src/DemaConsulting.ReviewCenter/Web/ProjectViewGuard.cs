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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components.Authorization;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     The one re-check of project view access that every project-scoped page runs at the start of each action
///     it handles on an open circuit.
/// </summary>
/// <remarks>
///     A page decides whether the signed-in user may see a project when it loads. The circuit then stays open, and
///     the membership may be removed in the meantime, so each handler asks again before it reads or changes
///     anything. The user is read from the authentication state at that moment and the decision comes from
///     <see cref="IProjectAccess"/>, so the result matches what a non-member gets. The guard covers the moment
///     before a handler starts; a removal that lands inside a single handler is not detected until the next one.
/// </remarks>
internal static class ProjectViewGuard
{
    /// <summary>
    ///     Reports whether the signed-in user may still view a project; when not, tells the page to drop its data.
    /// </summary>
    /// <param name="access">The project access rules. Must not be null.</param>
    /// <param name="authentication">The authentication state of the page. Must not be null.</param>
    /// <param name="projectId">The identity of the project the page shows.</param>
    /// <param name="onLost">
    ///     Called once when the access is gone: the page clears everything it loaded and shows the same "does not
    ///     exist" state as for a non-member. Must not be null.
    /// </param>
    /// <returns>
    ///     <c>true</c> when the user may still view the project; <c>false</c> after <paramref name="onLost"/> was
    ///     called (a missing user, a non-member, an unknown project and a disabled user all count as lost), in
    ///     which case the handler must stop.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static async Task<bool> GuardViewAsync(
        this IProjectAccess access,
        AuthenticationStateProvider authentication,
        int projectId,
        Action onLost)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(onLost);

        var actor = await authentication.GetActorOrNullAsync();
        if (await access.CanViewAsync(actor, projectId))
        {
            return true;
        }

        onLost();
        return false;
    }
}
