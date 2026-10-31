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
///     The one re-check of access that every project-scoped or administration page runs at the start of each
///     action it handles on an open circuit; it covers five levels: view, contribute, administer, audit and system
///     administrator.
/// </summary>
/// <remarks>
///     A page decides what the signed-in user may see when it loads. The circuit then stays open, and the
///     membership or the rights may be removed in the meantime, so each handler asks again before it reads or changes
///     anything. The user is read from the authentication state at that moment and the decision comes from
///     <see cref="IProjectAccess"/>, so the result matches what a user without the right gets. All levels
///     share one private core, so they differ only in the question they ask. The page's callback drops everything
///     the page loaded for that level (an administrator page also drops what an ordinary member must not read), so
///     that no stale data stays rendered next to an error. The contribute level is the one exception: a member who
///     may only read gets no callback, so the page can keep its data and say the access is read-only. The guard
///     covers the moment before a handler starts; a
///     removal that lands inside a single handler is not detected until the next one.
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
    public static Task<bool> GuardViewAsync(
        this IProjectAccess access,
        AuthenticationStateProvider authentication,
        int projectId,
        Action onLost)
    {
        ArgumentNullException.ThrowIfNull(access);
        return GuardAsync(authentication, onLost, actor => access.CanViewAsync(actor, projectId));
    }

    /// <summary>
    ///     Reports whether the signed-in user may still work in a project (create reviews, comment, answer, sign);
    ///     when the project is no longer visible, tells the page to drop its data.
    /// </summary>
    /// <remarks>
    ///     Different from the other levels on purpose: a user who sees the project but may only read it (an
    ///     auditor) gets <c>false</c> <b>without</b> the callback, so the page can show that the access is
    ///     read-only and keep what it loaded.
    /// </remarks>
    /// <param name="access">The project access rules. Must not be null.</param>
    /// <param name="authentication">The authentication state of the page. Must not be null.</param>
    /// <param name="projectId">The identity of the project the page shows.</param>
    /// <param name="onLost">
    ///     Called once when the project is no longer visible: the page clears everything it loaded and shows the
    ///     same "does not exist" state as for a non-member. Not called for a read-only member. Must not be null.
    /// </param>
    /// <returns>
    ///     <c>true</c> when the user may still work in the project; <c>false</c> without the callback for a
    ///     read-only member; <c>false</c> after <paramref name="onLost"/> was called when the project is not
    ///     visible (a missing user, a non-member, an unknown project and a disabled user all count as lost). In
    ///     both cases the handler must stop.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static async Task<bool> GuardContributeAsync(
        this IProjectAccess access,
        AuthenticationStateProvider authentication,
        int projectId,
        Action onLost)
    {
        ArgumentNullException.ThrowIfNull(access);

        // One question for the callback decision (visible) and one for the answer (may work), both from the
        // same actor read
        var contributes = false;
        var visible = await GuardAsync(
            authentication,
            onLost,
            async actor =>
            {
                if (!await access.CanViewAsync(actor, projectId))
                {
                    return false;
                }

                contributes = await access.CanContributeAsync(actor, projectId);
                return true;
            });
        return visible && contributes;
    }

    /// <summary>
    ///     Reports whether the signed-in user may still administer a project (a system administrator or a project
    ///     admin of it); when not, tells the page to drop its data.
    /// </summary>
    /// <param name="access">The project access rules. Must not be null.</param>
    /// <param name="authentication">The authentication state of the page. Must not be null.</param>
    /// <param name="projectId">The identity of the project the page administers.</param>
    /// <param name="onLost">
    ///     Called once when the right is gone: the page clears everything it loaded and typed and shows the same
    ///     "does not exist" state as for a non-member. Must not be null.
    /// </param>
    /// <returns>
    ///     <c>true</c> when the user may still administer the project; <c>false</c> after
    ///     <paramref name="onLost"/> was called (a missing user, a plain member, a non-member, an unknown project
    ///     and a disabled user all count as lost), in which case the handler must stop.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static Task<bool> GuardAdministerAsync(
        this IProjectAccess access,
        AuthenticationStateProvider authentication,
        int projectId,
        Action onLost)
    {
        ArgumentNullException.ThrowIfNull(access);
        return GuardAsync(authentication, onLost, actor => access.CanAdministerAsync(actor, projectId));
    }

    /// <summary>
    ///     Reports whether the signed-in user may still read the audit trail of a project (a system administrator,
    ///     a project admin or an auditor of it); when not, tells the page to drop its data.
    /// </summary>
    /// <param name="access">The project access rules. Must not be null.</param>
    /// <param name="authentication">The authentication state of the page. Must not be null.</param>
    /// <param name="projectId">The identity of the project whose audit trail the page shows.</param>
    /// <param name="onLost">
    ///     Called once when the right is gone: the page clears everything it loaded and shows the same "does not
    ///     exist" state as for a non-member. Must not be null.
    /// </param>
    /// <returns>
    ///     <c>true</c> when the user may still read the audit trail; <c>false</c> after
    ///     <paramref name="onLost"/> was called (a missing user, a plain member, a non-member, an unknown project
    ///     and a disabled user all count as lost), in which case the handler must stop.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static Task<bool> GuardAuditAsync(
        this IProjectAccess access,
        AuthenticationStateProvider authentication,
        int projectId,
        Action onLost)
    {
        ArgumentNullException.ThrowIfNull(access);
        return GuardAsync(authentication, onLost, actor => access.CanAuditAsync(actor, projectId));
    }

    /// <summary>
    ///     Reports whether the signed-in user is still an enabled system administrator; when not, tells the page to
    ///     drop what only a system administrator may see.
    /// </summary>
    /// <param name="access">The project access rules. Must not be null.</param>
    /// <param name="authentication">The authentication state of the page. Must not be null.</param>
    /// <param name="onLost">
    ///     Called once when the right is gone: the page clears what it loaded for system administrators and shows
    ///     the reason. Must not be null.
    /// </param>
    /// <returns>
    ///     <c>true</c> when the user is still a system administrator; <c>false</c> after
    ///     <paramref name="onLost"/> was called (a missing user, a demoted user and a disabled user all count as
    ///     lost), in which case the handler must stop.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static Task<bool> GuardSystemAdminAsync(
        this IProjectAccess access,
        AuthenticationStateProvider authentication,
        Action onLost)
    {
        ArgumentNullException.ThrowIfNull(access);
        return GuardAsync(authentication, onLost, actor => access.IsSystemAdminAsync(actor));
    }

    /// <summary>
    ///     Reads the signed-in user and runs the access question of one level; calls back once when it is refused.
    /// </summary>
    /// <param name="authentication">The authentication state of the page. Must not be null.</param>
    /// <param name="onLost">Called once when the question is answered with no. Must not be null.</param>
    /// <param name="isAllowed">
    ///     The access question of the level, asked for the signed-in user (<c>null</c> when nobody is signed in,
    ///     which <see cref="IProjectAccess"/> answers with no). Must not be null.
    /// </param>
    /// <returns><c>true</c> when the question is answered with yes; otherwise <c>false</c> after the callback.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    private static async Task<bool> GuardAsync(
        AuthenticationStateProvider authentication,
        Action onLost,
        Func<Actor?, Task<bool>> isAllowed)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(onLost);

        // Ask for the user as the circuit knows it right now, not as it was when the page loaded
        var actor = await authentication.GetActorOrNullAsync();
        if (await isAllowed(actor))
        {
            return true;
        }

        // Refused: the page drops what it holds, and the handler must stop
        onLost();
        return false;
    }
}
