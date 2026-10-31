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
///     The one place that decides who may see and change what, per project.
/// </summary>
/// <remarks>
///     Every page, API endpoint and service that touches project data asks this contract instead of deciding for
///     itself. A system administrator sees, works in and administers every project; any other user sees only the
///     projects they are a member of, works in those where they are a user or a project administrator, and
///     administers those where they are a project administrator. A member whose role is auditor sees the project
///     and reads its audit trail but may not work in it. A missing actor, the
///     command line, an unknown user and a disabled user have no access at all. The rules are evaluated from the
///     stored user and memberships on every call, so removing a member or disabling a user takes effect at once.
///     The <c>Require...</c> methods throw <see cref="DomainRuleException"/> with <see cref="DomainRule.ProjectNotFound"/>
///     and one fixed message for a project that does not exist and for a project the actor may not see, so a caller
///     cannot tell the two apart (404, never 403).
/// </remarks>
public interface IProjectAccess
{
    /// <summary>
    ///     Reports whether the actor is an enabled system administrator.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the actor is an enabled system administrator.</returns>
    Task<bool> IsSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether the actor may see a project and everything in it.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the project exists and the actor is a system administrator or a member of it, whatever the
    ///     role.
    /// </returns>
    Task<bool> CanViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether the actor may work in a project (create reviews, comment, answer checklists, sign).
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the project exists and the actor is a system administrator or a member whose role is user
    ///     or project administrator; <c>false</c> for a member who may only read.
    /// </returns>
    Task<bool> CanContributeAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether the actor may administer a project (manage its repositories).
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the project exists and the actor is a system administrator or a project administrator of it.
    /// </returns>
    Task<bool> CanAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether the actor may read the audit trail of a project.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the project exists and the actor is a system administrator, a project administrator of it
    ///     or a member whose role is auditor; <c>false</c> for a member whose role is user.
    /// </returns>
    Task<bool> CanAuditAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether a user is a member of a project (an explicit membership; being a system administrator
    ///     does not count).
    /// </summary>
    /// <param name="userId">The identity of the user.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the user has a membership in the project.</returns>
    Task<bool> IsMemberAsync(int userId, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the projects the actor may see.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     Every project for a system administrator, the projects of the memberships for any other enabled user, and
    ///     nothing otherwise; ordered by name and then identity.
    /// </returns>
    Task<IReadOnlyList<Project>> ListVisibleProjectsAsync(Actor? actor, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requires that the actor may see the project.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor may see the project.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> otherwise.</exception>
    Task RequireViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requires that the actor may work in the project.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor may work in the project.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not even see the project, and with
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the actor sees it but may only read.
    /// </exception>
    Task RequireContributeAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requires that the actor may administer the project.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor may administer the project.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not even see the project, and with
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the actor sees it but may not administer it.
    /// </exception>
    Task RequireAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requires that the actor may read the audit trail of the project.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor may read the audit trail of the project.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the actor may not even see the project, and with
    ///     <see cref="DomainRule.ActorNotAllowed"/> when the actor sees it but is neither an administrator nor an
    ///     auditor of it.
    /// </exception>
    Task RequireAuditAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Requires that the actor is an enabled system administrator.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor is a system administrator.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ActorNotAllowed"/> otherwise.</exception>
    Task RequireSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default);
}
