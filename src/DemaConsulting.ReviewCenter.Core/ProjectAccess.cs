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
///     The default <see cref="IProjectAccess"/>: decides access from the stored users and project memberships.
/// </summary>
/// <remarks>
///     The user is looked up by <see cref="Actor.Username"/> on every call and nothing is cached, so a demotion, a
///     disabled account or a removed membership takes effect on the next call. An actor that came from a personal
///     access token carries the user name of the token owner, so a token inherits exactly the memberships of its
///     owner. The class holds no state and is thread-safe as long as the stores are.
/// </remarks>
public sealed class ProjectAccess : IProjectAccess
{
    /// <summary>
    ///     The message of the not-found failure; one text for a missing project and a project that is not visible.
    /// </summary>
    private const string ProjectNotFoundMessage = "The project does not exist.";

    /// <summary>
    ///     The store that reads users.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     The store that reads projects and memberships.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProjectAccess"/> class.
    /// </summary>
    /// <param name="users">The store that reads users. Must not be null.</param>
    /// <param name="store">The store that reads projects and memberships. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ProjectAccess(IUserStore users, IReviewStore store)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(store);
        _users = users;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<bool> IsSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        return (await ResolveAsync(actor, cancellationToken))?.IsSystemAdmin == true;
    }

    /// <inheritdoc />
    public async Task<bool> CanViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        return await RoleOfAsync(actor, projectId, cancellationToken) is not null;
    }

    /// <inheritdoc />
    public async Task<bool> CanContributeAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return await RoleOfAsync(actor, projectId, cancellationToken) is Level.Contribute or Level.Administer;
    }

    /// <inheritdoc />
    public async Task<bool> CanAdministerAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        return await RoleOfAsync(actor, projectId, cancellationToken) == Level.Administer;
    }

    /// <inheritdoc />
    public async Task<bool> CanAuditAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        return await RoleOfAsync(actor, projectId, cancellationToken) is Level.Read or Level.Administer;
    }

    /// <inheritdoc />
    public async Task<bool> IsMemberAsync(int userId, int projectId, CancellationToken cancellationToken = default)
    {
        return await _store.GetMembershipAsync(userId, projectId, cancellationToken) is not null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> ListVisibleProjectsAsync(
        Actor? actor,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveAsync(actor, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var projects = await _store.ListProjectsAsync(cancellationToken);
        if (user.IsSystemAdmin)
        {
            return projects;
        }

        var memberships = await _store.ListMembershipsForUserAsync(user.Id, cancellationToken);
        var visible = memberships.Select(m => m.ProjectId).ToHashSet();
        return projects.Where(p => visible.Contains(p.Id)).ToList();
    }

    /// <inheritdoc />
    public async Task RequireViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        if (await RoleOfAsync(actor, projectId, cancellationToken) is null)
        {
            throw ProjectNotFound();
        }
    }

    /// <inheritdoc />
    public async Task RequireContributeAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var level = await RoleOfAsync(actor, projectId, cancellationToken);
        if (level is null)
        {
            throw ProjectNotFound();
        }

        if (level == Level.Read)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Your role in the project is read-only.");
        }
    }

    /// <inheritdoc />
    public async Task RequireAdministerAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var level = await RoleOfAsync(actor, projectId, cancellationToken);
        if (level is null)
        {
            throw ProjectNotFound();
        }

        if (level != Level.Administer)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only an administrator of the project may do this.");
        }
    }

    /// <inheritdoc />
    public async Task RequireAuditAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        var level = await RoleOfAsync(actor, projectId, cancellationToken);
        if (level is null)
        {
            throw ProjectNotFound();
        }

        if (level == Level.Contribute)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only an administrator or an auditor of the project may do this.");
        }
    }

    /// <inheritdoc />
    public async Task RequireSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        if (!await IsSystemAdminAsync(actor, cancellationToken))
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "Only a system administrator may do this.");
        }
    }

    /// <summary>
    ///     Builds the failure for a project that is missing or not visible; the same for both.
    /// </summary>
    /// <returns>The exception.</returns>
    private static DomainRuleException ProjectNotFound() => new(DomainRule.ProjectNotFound, ProjectNotFoundMessage);

    /// <summary>
    ///     Finds the enabled user behind an actor.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The user, or <c>null</c> for no actor, the command line, an unknown user, a disabled user and a user that
    ///     still has a temporary password (such a user has no access to anything until the password is changed).
    /// </returns>
    private async Task<User?> ResolveAsync(Actor? actor, CancellationToken cancellationToken)
    {
        if (actor is null || string.IsNullOrEmpty(actor.Username) || actor.Username == Actor.CommandLine.Username)
        {
            return null;
        }

        var user = await _users.GetByUsernameAsync(actor.Username, cancellationToken);
        return user is { Enabled: true, MustChangePassword: false } ? user : null;
    }

    /// <summary>
    ///     Works out what the actor may do in a project.
    /// </summary>
    /// <param name="actor">The actor; may be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The level of access, or <c>null</c> when the project does not exist or is not visible.</returns>
    private async Task<Level?> RoleOfAsync(Actor? actor, int projectId, CancellationToken cancellationToken)
    {
        var user = await ResolveAsync(actor, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // A system administrator sees every project that exists
        if (user.IsSystemAdmin)
        {
            return await _store.GetProjectAsync(projectId, cancellationToken) is null ? null : Level.Administer;
        }

        var membership = await _store.GetMembershipAsync(user.Id, projectId, cancellationToken);
        if (membership is null)
        {
            return null;
        }

        // An undefined stored role fails closed: it may see the project but never work in it
        return membership.Role switch
        {
            ProjectRole.ProjectAdmin => Level.Administer,
            ProjectRole.User => Level.Contribute,
            ProjectRole.Auditor => Level.Read,
            _ => Level.Read
        };
    }

    /// <summary>
    ///     What an actor may do in a project; the levels are ordered, each includes the one before it except that
    ///     reading the audit trail is open to <see cref="Read"/> and <see cref="Administer"/> but not to
    ///     <see cref="Contribute"/>.
    /// </summary>
    private enum Level
    {
        /// <summary>See the project and read its audit trail; change nothing.</summary>
        Read,

        /// <summary>Also work in the project: create reviews, comment, answer checklists and sign.</summary>
        Contribute,

        /// <summary>Also manage the project.</summary>
        Administer
    }
}
