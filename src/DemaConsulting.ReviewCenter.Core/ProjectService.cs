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
///     Creates and reads projects and manages who belongs to them, enforcing the project rules.
/// </summary>
/// <remarks>
///     The service is the single place where project rules live, so every caller (web pages, the API) gets the
///     same behavior. Only a system administrator creates projects and changes memberships; reads are filtered by
///     <see cref="IProjectAccess"/>. It holds no state of its own and is safe for concurrent use as long as the
///     injected stores are.
/// </remarks>
public sealed class ProjectService
{
    /// <summary>
    ///     The store that persists projects and memberships.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The store that reads users.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     Decides who may see and change what.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The clock that supplies creation times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProjectService"/> class.
    /// </summary>
    /// <param name="store">The store that persists projects and memberships. Must not be null.</param>
    /// <param name="users">The store that reads users. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ProjectService(IReviewStore store, IUserStore users, IProjectAccess access, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _users = users;
        _access = access;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Creates a project.
    /// </summary>
    /// <param name="name">
    ///     The project name; leading and trailing white space is removed. Must not be empty and must
    ///     not exceed <see cref="ReviewLimits.MaxProjectNameLength"/> characters. Need not be unique.
    /// </param>
    /// <param name="description">
    ///     The description; may be null or empty, leading and trailing white space is removed and the
    ///     result must not exceed <see cref="ReviewLimits.MaxProjectDescriptionLength"/> characters.
    /// </param>
    /// <param name="actor">The signed-in user creating the project; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The created project, including its assigned identity; it has no members.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> when the actor is not a system administrator, or when
    ///     the name or description violates a rule; nothing is stored then.
    /// </exception>
    public async Task<Project> CreateAsync(
        string? name,
        string? description,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _access.RequireSystemAdminAsync(actor, cancellationToken);
        // Normalize first so that the rules apply to what would actually be stored
        var trimmedName = name?.Trim() ?? string.Empty;
        var trimmedDescription = description?.Trim() ?? string.Empty;

        // Reject invalid input before any I/O
        if (trimmedName.Length == 0)
        {
            throw new DomainRuleException(DomainRule.ProjectNameInvalid, "The project name is required.");
        }

        if (trimmedName.Length > ReviewLimits.MaxProjectNameLength)
        {
            throw new DomainRuleException(
                DomainRule.ProjectNameInvalid,
                $"The project name must not be longer than {ReviewLimits.MaxProjectNameLength} characters.");
        }

        if (trimmedDescription.Length > ReviewLimits.MaxProjectDescriptionLength)
        {
            throw new DomainRuleException(
                DomainRule.ProjectDescriptionTooLong,
                $"The project description must not be longer than {ReviewLimits.MaxProjectDescriptionLength} characters.");
        }

        // Store the project; the store assigns the identity, and saves the audit entry in the same transaction
        var project = new Project
        {
            Name = trimmedName,
            Description = trimmedDescription,
            Created = _timeProvider.GetUtcNow()
        };
        return await _store.AddProjectAsync(
            project,
            stored => AuditDraft.By(actor, "project.created", $"project '{stored.Name}'", stored.Id),
            cancellationToken);
    }

    /// <summary>
    ///     Lists the projects the actor may see.
    /// </summary>
    /// <param name="actor">The signed-in user; may be null, which sees nothing.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     Every project for a system administrator, the projects the user is a member of otherwise; ordered by name
    ///     and then identity.
    /// </returns>
    public Task<IReadOnlyList<Project>> ListAsync(Actor? actor, CancellationToken cancellationToken = default)
    {
        return _access.ListVisibleProjectsAsync(actor, cancellationToken);
    }

    /// <summary>
    ///     Reads one project the actor may see.
    /// </summary>
    /// <param name="actor">The signed-in user; may be null, which sees nothing.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The project, or <c>null</c> when it does not exist or the actor may not see it (the two are not told apart).
    /// </returns>
    public async Task<Project?> GetAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        return await _access.CanViewAsync(actor, projectId, cancellationToken)
            ? await _store.GetProjectAsync(projectId, cancellationToken)
            : null;
    }

    /// <summary>
    ///     Lists the members of a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must be able to administer the project.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The members ordered by user name.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> (see
    ///     <see cref="IProjectAccess.RequireAdministerAsync"/>).
    /// </exception>
    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(
        Actor actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        var memberships = await _store.ListMembershipsAsync(projectId, cancellationToken);
        var users = (await _users.ListAsync(cancellationToken)).ToDictionary(u => u.Id);
        return memberships
            .Where(m => users.ContainsKey(m.UserId))
            .Select(m => new ProjectMember(
                m.UserId,
                users[m.UserId].Username,
                users[m.UserId].DisplayName,
                users[m.UserId].Enabled,
                m.Role))
            .OrderBy(m => m.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    ///     Adds a user to a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must be a system administrator.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the user to add.</param>
    /// <param name="role">The role the user gets in the project.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the membership is stored.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> for anyone but a system administrator,
    ///     <see cref="DomainRule.ProjectNotFound"/>, <see cref="DomainRule.UserNotFound"/> or
    ///     <see cref="DomainRule.MembershipExists"/>.
    /// </exception>
    public async Task AddMemberAsync(
        Actor actor,
        int projectId,
        string username,
        ProjectRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ValidateRole(role);
        await _access.RequireSystemAdminAsync(actor, cancellationToken);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var user = await FindUserAsync(username, cancellationToken);
        await _store.AddMembershipAsync(
            new ProjectMembership(user.Id, projectId, role),
            AuditDraft.By(actor, "project.member-added", $"user '{user.Username}', role {role}", projectId),
            cancellationToken);
    }

    /// <summary>
    ///     Removes a user from a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must be a system administrator.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the member to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the membership is removed.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> for anyone but a system administrator,
    ///     <see cref="DomainRule.ProjectNotFound"/>, <see cref="DomainRule.UserNotFound"/> or
    ///     <see cref="DomainRule.MembershipNotFound"/>.
    /// </exception>
    public async Task RemoveMemberAsync(
        Actor actor,
        int projectId,
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        await _access.RequireSystemAdminAsync(actor, cancellationToken);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var user = await FindUserAsync(username, cancellationToken);
        var removed = await _store.RemoveMembershipAsync(
            projectId,
            user.Id,
            AuditDraft.By(actor, "project.member-removed", $"user '{user.Username}'", projectId),
            cancellationToken);
        if (!removed)
        {
            throw NotAMember(user);
        }
    }

    /// <summary>
    ///     Changes the role of a member of a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must be a system administrator.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the member.</param>
    /// <param name="role">The new role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the role is stored.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> for anyone but a system administrator,
    ///     <see cref="DomainRule.ProjectNotFound"/>, <see cref="DomainRule.UserNotFound"/> or
    ///     <see cref="DomainRule.MembershipNotFound"/>.
    /// </exception>
    public async Task SetMemberRoleAsync(
        Actor actor,
        int projectId,
        string username,
        ProjectRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ValidateRole(role);
        await _access.RequireSystemAdminAsync(actor, cancellationToken);
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var user = await FindUserAsync(username, cancellationToken);
        var changed = await _store.SetMembershipRoleAsync(
            projectId,
            user.Id,
            role,
            AuditDraft.By(actor, "project.member-role-changed", $"user '{user.Username}', role {role}", projectId),
            cancellationToken);
        if (!changed)
        {
            throw NotAMember(user);
        }
    }

    /// <summary>
    ///     Rejects a role that is not a defined value.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the role is not defined.</exception>
    private static void ValidateRole(ProjectRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "The role is not a known value.");
        }
    }

    /// <summary>
    ///     Builds the failure for a user that is not a member of the project.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>The exception.</returns>
    private static DomainRuleException NotAMember(User user) =>
        new(DomainRule.MembershipNotFound, $"The user '{user.Username}' is not a member of the project.");

    /// <summary>
    ///     Reads a user by name.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.UserNotFound"/> when there is none.</exception>
    private async Task<User> FindUserAsync(string username, CancellationToken cancellationToken)
    {
        return await _users.GetByUsernameAsync(username, cancellationToken)
               ?? throw new DomainRuleException(DomainRule.UserNotFound, $"The user '{username}' does not exist.");
    }
}
