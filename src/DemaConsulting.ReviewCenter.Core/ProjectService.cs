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

using System.Security.Cryptography;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     A project account that was just created, with the temporary password that exists only in this value.
/// </summary>
/// <remarks>
///     The temporary password is shown to the creator once so it can be handed over; it is stored only as a hash and
///     is never audited. Callers must not keep it longer than needed. The record is immutable.
/// </remarks>
/// <param name="Member">The new member.</param>
/// <param name="TemporaryPassword">
///     The temporary password in clear text; the account must change it at first sign-in.
/// </param>
public sealed record NewMemberAccount(ProjectMember Member, string TemporaryPassword)
{
    /// <summary>
    ///     Describes the account without the temporary password, so the password is never printed by accident.
    /// </summary>
    /// <returns>The text with the member and a hidden password.</returns>
    public override string ToString() =>
        $"{nameof(NewMemberAccount)} {{ {nameof(Member)} = {Member}, [temporary secret hidden] }}";
}

/// <summary>
///     Creates and reads projects and manages who belongs to them, enforcing the project rules.
/// </summary>
/// <remarks>
///     The service is the single place where project rules live, so every caller (web pages, the API) gets the
///     same behavior. Only a system administrator creates projects; the members of a project are managed by the
///     administrators of that project and by system administrators; reads are filtered by
///     <see cref="IProjectAccess"/>. A member is added by the exact user name only and no operation lists users, so
///     a project administrator cannot browse the user base; failed adds are counted per actor in memory (at most
///     <see cref="MaxFailedAttempts"/> in <see cref="ThrottleWindow"/>, system administrators exempt) and audited.
///     Each attempt is reserved atomically per actor before the user is looked up and given back only when the
///     attempt succeeds, so parallel attempts cannot exceed the limit.
///     That throttle state is per process and lost on restart. The service is safe for concurrent use as long as
///     the injected stores are; the last project administrator is protected by the store, atomically.
///     A project also carries the "require independent reviewers" rule (on by default), which a project administrator
///     switches with <see cref="SetRequireIndependentReviewersAsync"/>; <see cref="SigningService"/> applies it. Two
///     further rules (the author does not answer the checklist, the reopener does not sign again) are applied by
///     <see cref="ChecklistService"/> and <see cref="SigningService"/>; <see cref="SetReviewRuleAsync"/> switches any
///     of the three.
/// </remarks>
public sealed class ProjectService
{
    /// <summary>
    ///     The most failed adds (unknown user, name already taken) an actor may make within
    ///     <see cref="ThrottleWindow"/>.
    /// </summary>
    public const int MaxFailedAttempts = 10;

    /// <summary>
    ///     The length of the sliding window in which failed adds are counted.
    /// </summary>
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     The action recorded when an attempt to add or create a member fails.
    /// </summary>
    public const string MemberAddFailedAction = "project.member-add-failed";

    /// <summary>
    ///     The action recorded when the "require independent reviewers" rule of a project is switched.
    /// </summary>
    public const string IndependentReviewersChangedAction = "project.independent-reviewers-changed";

    /// <summary>
    ///     The action recorded when a further review rule of a project (not "require independent reviewers") is
    ///     switched.
    /// </summary>
    public const string ReviewRuleChangedAction = "project.review-rule-changed";

    /// <summary>
    ///     The message for a setting that someone else changed while the caller was editing it.
    /// </summary>
    public const string IndependentReviewersChangedByOthersMessage =
        "The setting was changed by someone else. Reload the page and try again.";

    /// <summary>
    ///     The message for a user name that cannot be added; one text for a missing and a disabled user.
    /// </summary>
    private const string CannotAddMessage =
        "That user name cannot be added to this project. Check the spelling, or create a new account instead.";

    /// <summary>
    ///     The message for a user that is not a member; one text for an unknown name and a non-member.
    /// </summary>
    private const string NotAMemberMessage = "That user is not a member of this project.";

    /// <summary>
    ///     The message for removing or demoting the last project administrator.
    /// </summary>
    public const string LastAdminMessage =
        "You cannot remove or demote the last project administrator. Ask a system administrator to do it, " +
        "or make someone else a project administrator first.";

    /// <summary>
    ///     The characters a temporary password is drawn from: no digit zero or one, no letters O, I or l, so
    ///     the password can be read out and typed without mistakes.
    /// </summary>
    private const string UnambiguousCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>
    ///     The length of a temporary password; above the policy minimum of
    ///     <see cref="ReviewLimits.MinPasswordLength"/>.
    /// </summary>
    private const int TemporaryPasswordLength = 16;

    /// <summary>
    ///     The store that persists projects and memberships.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The store that reads and creates users.
    /// </summary>
    private readonly IUserStore _users;

    /// <summary>
    ///     Decides who may see and change what.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Hashes the temporary passwords of created accounts.
    /// </summary>
    private readonly IPasswordHasher _hasher;

    /// <summary>
    ///     The clock that supplies creation times and the throttle window.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Records the failed attempts; <c>null</c> for none.
    /// </summary>
    private readonly AuditService? _audit;

    /// <summary>
    ///     Guards <see cref="_failures"/>; never held across an await.
    /// </summary>
    private readonly object _failuresLock = new();

    /// <summary>
    ///     The reserved attempts of each actor, by user name without regard to case; a reservation that is not
    ///     released is a failed attempt.
    /// </summary>
    private readonly Dictionary<string, List<AttemptReservation>> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProjectService"/> class.
    /// </summary>
    /// <param name="store">The store that persists projects and memberships. Must not be null.</param>
    /// <param name="users">The store that reads and creates users. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="hasher">Hashes the temporary passwords of created accounts. Must not be null.</param>
    /// <param name="timeProvider">The clock used for creation times; injected so tests control time. Must not be null.</param>
    /// <param name="audit">Records failed attempts in the audit trail; <c>null</c> for none.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public ProjectService(
        IReviewStore store,
        IUserStore users,
        IProjectAccess access,
        IPasswordHasher hasher,
        TimeProvider timeProvider,
        AuditService? audit = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _users = users;
        _access = access;
        _hasher = hasher;
        _timeProvider = timeProvider;
        _audit = audit;
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
    ///     Adds an existing user to a project, by the exact user name.
    /// </summary>
    /// <param name="actor">The signed-in user; must administer the project (or be a system administrator).</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">
    ///     The exact user name of the user to add; leading and trailing white space is removed. There is no search:
    ///     a name that does not exist and the name of a disabled user are refused with the same message.
    /// </param>
    /// <param name="role">The role the user gets in the project.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new member.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> (see
    ///     <see cref="IProjectAccess.RequireAdministerAsync"/>), <see cref="DomainRule.TooManyAttempts"/> when the
    ///     actor failed too often lately, <see cref="DomainRule.UserNotFound"/> (one message for a missing and a
    ///     disabled user; the failure is counted and audited), <see cref="DomainRule.MembershipExists"/> or
    ///     <see cref="DomainRule.MemberHasOpenReviewWork"/> (the role is auditor and the user still has reviewer work
    ///     to do in the project).
    /// </exception>
    public async Task<ProjectMember> AddMemberAsync(
        Actor actor,
        int projectId,
        string username,
        ProjectRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ValidateRole(role);
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        var exempt = await _access.IsSystemAdminAsync(actor, cancellationToken);

        // The attempt is reserved before the lookup and counted from then on, so concurrent attempts cannot all pass
        // the check; only a success gives it back, a failure or an exception keeps it counted
        var reservation = ReserveAttempt(actor, exempt);
        var typed = username.Trim();
        var added = false;
        try
        {
            // Resolve the exact name; the one message hides whether the name exists, is disabled or is only misspelled
            var user = await _users.GetByUsernameAsync(typed, cancellationToken);
            if (user is not { Enabled: true })
            {
                await RegisterFailureAsync(actor, projectId, typed, cancellationToken);
                throw new DomainRuleException(DomainRule.UserNotFound, CannotAddMessage);
            }

            if (role == ProjectRole.Auditor
                && await _store.GetMembershipAsync(user.Id, projectId, cancellationToken) is null)
            {
                // An existing member is left to the store, which reports the duplicate (MembershipExists)
                await EnsureNoOpenReviewWorkAsync(projectId, user, cancellationToken);
            }

            await _store.AddMembershipAsync(
                new ProjectMembership(user.Id, projectId, role),
                AuditDraft.By(actor, "project.member-added", $"user '{user.Username}', role {role}", projectId),
                cancellationToken);
            added = true;
            return new ProjectMember(user.Id, user.Username, user.DisplayName, user.Enabled, role);
        }
        finally
        {
            if (added)
            {
                ReleaseAttempt(actor, reservation);
            }
        }
    }

    /// <summary>
    ///     Creates a local account with a temporary password and adds it to a project, in one step.
    /// </summary>
    /// <param name="actor">The signed-in user; must administer the project (or be a system administrator).</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The new user name; same rules as for a user created by a system administrator.</param>
    /// <param name="displayName">The display name; same rules as for a user created by a system administrator.</param>
    /// <param name="role">The role the new user gets in the project.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new member and the temporary password, which is returned only here.</returns>
    /// <remarks>
    ///     The account can never be a system administrator and can not exist without the membership: the user and
    ///     the membership are stored in one transaction. The password is generated here (so nobody types or reuses a
    ///     weak one), is stored only as a hash and is never audited; the account must change it at the first
    ///     sign-in (<see cref="User.MustChangePassword"/>).
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.TooManyAttempts"/>, <see cref="DomainRule.UserNameInvalid"/>,
    ///     <see cref="DomainRule.DisplayNameInvalid"/> or <see cref="DomainRule.UserNameTaken"/> (counted and audited);
    ///     nothing is stored then.
    /// </exception>
    public async Task<NewMemberAccount> CreateMemberAccountAsync(
        Actor actor,
        int projectId,
        string username,
        string displayName,
        ProjectRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(displayName);
        ValidateRole(role);
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        var exempt = await _access.IsSystemAdminAsync(actor, cancellationToken);

        // Reserved before the store is asked, like in AddMemberAsync; input that is refused before any write is
        // given back, so a typing mistake is not a failed guess
        var reservation = ReserveAttempt(actor, exempt);
        var typed = username.Trim();
        string trimmedDisplayName;
        try
        {
            // Reject invalid input before any write; the same rules as every other way to create a user
            UserService.ValidateUsername(typed);
            trimmedDisplayName = UserService.ValidateDisplayName(displayName);
        }
        catch
        {
            ReleaseAttempt(actor, reservation);
            throw;
        }

        // The password is drawn from a cryptographic source and only its hash leaves this method
        var password = NewTemporaryPassword();
        var created = false;
        try
        {
            var stored = await _users.AddToProjectAsync(
                new User
                {
                    Username = typed,
                    DisplayName = trimmedDisplayName,
                    IsSystemAdmin = false,
                    Enabled = true,
                    MustChangePassword = true,
                    PasswordHash = _hasher.Hash(password),
                    Created = _timeProvider.GetUtcNow()
                },
                projectId,
                role,
                account => AuditDraft.By(
                    actor,
                    "project.account-created",
                    $"user '{account.Username}', role {role}, temporary password",
                    projectId),
                cancellationToken);
            created = true;
            return new NewMemberAccount(
                new ProjectMember(stored.Id, stored.Username, stored.DisplayName, stored.Enabled, role),
                password);
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.UserNameTaken)
        {
            // Stays counted and is audited like a failed add; the message does not say more than "not available"
            await RegisterFailureAsync(actor, projectId, typed, cancellationToken);
            throw new DomainRuleException(DomainRule.UserNameTaken, "That user name is not available.");
        }
        finally
        {
            if (created)
            {
                ReleaseAttempt(actor, reservation);
            }
        }
    }

    /// <summary>
    ///     Removes a user from a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must administer the project (or be a system administrator).</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the member to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the membership is removed.</returns>
    /// <remarks>
    ///     A project administrator may not remove the last enabled project administrator (themselves included); a
    ///     system administrator may. The store makes the check and the removal one atomic step, so two concurrent
    ///     removals cannot empty the project.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.MembershipNotFound"/> (one message for an unknown name and a non-member) or
    ///     <see cref="DomainRule.LastProjectAdmin"/>.
    /// </exception>
    public async Task RemoveMemberAsync(
        Actor actor,
        int projectId,
        string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        var exempt = await _access.IsSystemAdminAsync(actor, cancellationToken);
        var user = await FindMemberCandidateAsync(username, cancellationToken);
        try
        {
            var removed = await _store.RemoveMembershipAsync(
                projectId,
                user.Id,
                AuditDraft.By(actor, "project.member-removed", $"user '{user.Username}'", projectId),
                !exempt,
                cancellationToken);
            if (!removed)
            {
                throw NotAMember();
            }
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.LastProjectAdmin)
        {
            throw new DomainRuleException(DomainRule.LastProjectAdmin, LastAdminMessage);
        }
    }

    /// <summary>
    ///     Changes the role of a member of a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must administer the project (or be a system administrator).</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the member.</param>
    /// <param name="role">The new role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The member as it is now.</returns>
    /// <remarks>
    ///     The last-administrator rule of <see cref="RemoveMemberAsync"/> applies to a demotion too. Making a member an
    ///     auditor is refused while the member holds an unsigned or out-of-date reviewer slot on a review that is not
    ///     approved; changing an auditor back to a user or a project administrator is always allowed. The change is
    ///     audited as <c>project.member-role-changed</c> with the role in the text.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.MembershipNotFound"/> (one message for an unknown name and a non-member),
    ///     <see cref="DomainRule.LastProjectAdmin"/> or <see cref="DomainRule.MemberHasOpenReviewWork"/>.
    /// </exception>
    public async Task<ProjectMember> SetMemberRoleAsync(
        Actor actor,
        int projectId,
        string username,
        ProjectRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        ValidateRole(role);
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        var exempt = await _access.IsSystemAdminAsync(actor, cancellationToken);
        var user = await FindMemberCandidateAsync(username, cancellationToken);
        if (role == ProjectRole.Auditor
            && await _store.GetMembershipAsync(user.Id, projectId, cancellationToken) is not null)
        {
            await EnsureNoOpenReviewWorkAsync(projectId, user, cancellationToken);
        }

        try
        {
            var changed = await _store.SetMembershipRoleAsync(
                projectId,
                user.Id,
                role,
                AuditDraft.By(actor, "project.member-role-changed", $"user '{user.Username}', role {role}", projectId),
                !exempt,
                cancellationToken);
            if (!changed)
            {
                throw NotAMember();
            }
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.LastProjectAdmin)
        {
            throw new DomainRuleException(DomainRule.LastProjectAdmin, LastAdminMessage);
        }

        return new ProjectMember(user.Id, user.Username, user.DisplayName, user.Enabled, role);
    }

    /// <summary>
    ///     Switches the "require independent reviewers" rule of a project on or off.
    /// </summary>
    /// <param name="actor">
    ///     The signed-in user; must administer the project (or be a system administrator). An API token is refused.
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expected">The value the caller read and based its change on.</param>
    /// <param name="value">The new value: <c>true</c> keeps the author of a review out of its reviewer slots.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the value was changed; <c>false</c> when <paramref name="value"/> equals
    ///     <paramref name="expected"/> and nothing was stored or recorded.
    /// </returns>
    /// <remarks>
    ///     The change is a compare-and-set on the stored value, stored together with its audit entry
    ///     <c>project.independent-reviewers-changed</c> (<c>RequireIndependentReviewers on-&gt;off</c>). The rule is not
    ///     retroactive: it is applied when a reviewer is chosen and when a review is signed, and existing signatures
    ///     stay valid.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> (see
    ///     <see cref="IProjectAccess.RequireAdministerAsync"/>; also for an API token), and with
    ///     <see cref="DomainRule.RecordChanged"/> when the stored value is no longer <paramref name="expected"/>
    ///     (someone else changed it first); nothing is stored then.
    /// </exception>
    public Task<bool> SetRequireIndependentReviewersAsync(
        Actor actor,
        int projectId,
        bool expected,
        bool value,
        CancellationToken cancellationToken = default) =>
        SetReviewRuleAsync(actor, projectId, ReviewRule.IndependentReviewers, expected, value, cancellationToken);

    /// <summary>
    ///     Switches one review rule of a project on or off.
    /// </summary>
    /// <param name="actor">
    ///     The signed-in user; must administer the project (or be a system administrator). An API token is refused.
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="rule">The rule to switch.</param>
    /// <param name="expected">The value the caller read and based its change on.</param>
    /// <param name="value">The new value: <c>true</c> switches the rule on.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the value was changed; <c>false</c> when <paramref name="value"/> equals
    ///     <paramref name="expected"/> and nothing was stored or recorded.
    /// </returns>
    /// <remarks>
    ///     The change is a compare-and-set on the stored value, stored together with its audit entry. The
    ///     <see cref="ReviewRule.IndependentReviewers"/> rule records <see cref="IndependentReviewersChangedAction"/>
    ///     with the text <c>RequireIndependentReviewers on-&gt;off</c>; the other rules record
    ///     <see cref="ReviewRuleChangedAction"/> with the property name in the same form, for example
    ///     <c>RequireNonReopenerSigning on-&gt;off</c>. The rules are applied when they are checked (a reviewer is
    ///     chosen, a review is signed or reopened, a checklist answer is given); existing signatures stay valid.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rule"/> is not defined.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> (see
    ///     <see cref="IProjectAccess.RequireAdministerAsync"/>; also for an API token), and with
    ///     <see cref="DomainRule.RecordChanged"/> when the stored value is no longer <paramref name="expected"/>
    ///     (someone else changed it first); nothing is stored then.
    /// </exception>
    public async Task<bool> SetReviewRuleAsync(
        Actor actor,
        int projectId,
        ReviewRule rule,
        bool expected,
        bool value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Project settings cannot be changed with an API token; sign in to the web pages.");
        }

        // The right is checked again on every call: a user demoted while a page was open must not get a change through
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        var (action, property) = Describe(rule);
        if (expected == value)
        {
            return false;
        }

        var stored = await _store.UpdateReviewRuleAsync(
            projectId,
            rule,
            expected,
            value,
            AuditDraft.By(actor, action, $"{property} {OnOff(expected)}->{OnOff(value)}", projectId),
            cancellationToken);
        if (!stored)
        {
            throw new DomainRuleException(DomainRule.RecordChanged, IndependentReviewersChangedByOthersMessage);
        }

        return true;
    }

    /// <summary>
    ///     Writes a switch value for the audit text.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>on</c> or <c>off</c>.</returns>
    private static string OnOff(bool value) => value ? "on" : "off";

    /// <summary>
    ///     Names the audit action and the project property of a review rule.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The audit action and the property name written to the audit text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the rule is not defined.</exception>
    private static (string Action, string Property) Describe(ReviewRule rule) => rule switch
    {
        ReviewRule.IndependentReviewers => (IndependentReviewersChangedAction, nameof(Project.RequireIndependentReviewers)),
        ReviewRule.NonAuthorChecklistAnswers => (ReviewRuleChangedAction, nameof(Project.RequireNonAuthorChecklistAnswers)),
        ReviewRule.NonReopenerSigning => (ReviewRuleChangedAction, nameof(Project.RequireNonReopenerSigning)),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "The rule is not a known value."),
    };

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
    ///     Refuses to make a user a read-only auditor while the user still has work to do in the project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="user">The user who would become an auditor.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the user holds no open review work.</returns>
    /// <remarks>
    ///     Open work is a reviewer slot (as primary or alternate reviewer) that has no signature of the current record,
    ///     on a review that is not approved. It is found by the one shared definition of the offboarding impact query
    ///     (<see cref="UserImpactService"/>), so what is refused here is exactly what the inbox calls a review to
    ///     sign and what the impact panel lists. The check and the role change are two steps,
    ///     not one transaction: a slot that is given to the user between them is not caught, which leaves an auditor in
    ///     a slot; such an auditor cannot sign (<see cref="SigningService"/> refuses it) and the slot shows as waiting
    ///     until a member changes it.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.MemberHasOpenReviewWork"/>; the message lists the reviews.
    /// </exception>
    private async Task EnsureNoOpenReviewWorkAsync(int projectId, User user, CancellationToken cancellationToken)
    {
        var work = await UserImpactService.FindOpenWorkAsync(
            _store,
            user.Id,
            new HashSet<int> { projectId },
            cancellationToken);
        var open = work
            .Select(item => $"'{item.ReviewId}' ({item.Title})")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (open.Count > 0)
        {
            const int listed = 5;
            var more = open.Count > listed ? $" and {open.Count - listed} more" : string.Empty;
            var names = string.Join(", ", open.Take(listed)) + more;
            throw new DomainRuleException(
                DomainRule.MemberHasOpenReviewWork,
                $"{user.DisplayName} still has reviewer work to do in {names}, so cannot become an auditor yet. " +
                "Sign it, change the reviewer of the slot, or wait until the review is concluded, then try again.");
        }
    }

    /// <summary>
    ///     Builds the failure for a user that is not a member of the project.
    /// </summary>
    /// <returns>The exception; the same message whether the name exists or not.</returns>
    private static DomainRuleException NotAMember() => new(DomainRule.MembershipNotFound, NotAMemberMessage);

    /// <summary>
    ///     Reads the user behind a name for a change of its membership.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.MembershipNotFound"/> when there is none; the message is that of a
    ///     non-member, so the name of an unrelated user is never confirmed.
    /// </exception>
    private async Task<User> FindMemberCandidateAsync(string username, CancellationToken cancellationToken)
    {
        return await _users.GetByUsernameAsync(username.Trim(), cancellationToken) ?? throw NotAMember();
    }

    /// <summary>
    ///     Reserves one attempt for the actor, or refuses an actor that has used up the attempts.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="exempt"><c>true</c> for a system administrator, who is never throttled.</param>
    /// <returns>
    ///     The reservation to give to <see cref="ReleaseAttempt"/> when the attempt succeeds; <c>null</c> for an
    ///     exempt actor, which is never counted.
    /// </returns>
    /// <remarks>
    ///     The check and the count are one step under one lock, taken before the user lookup, so concurrent attempts
    ///     of one actor cannot all pass the check before any failure is counted. An attempt that is not released
    ///     stays counted, so a failure (or an exception) needs no extra bookkeeping.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TooManyAttempts"/> over the limit; nothing is counted then.
    /// </exception>
    private AttemptReservation? ReserveAttempt(Actor actor, bool exempt)
    {
        if (exempt)
        {
            return null;
        }

        lock (_failuresLock)
        {
            var now = _timeProvider.GetUtcNow();
            if (!_failures.TryGetValue(actor.Username, out var attempts))
            {
                attempts = [];
                _failures[actor.Username] = attempts;
            }

            attempts.RemoveAll(a => now - a.Time >= ThrottleWindow);
            if (attempts.Count >= MaxFailedAttempts)
            {
                throw new DomainRuleException(
                    DomainRule.TooManyAttempts,
                    "Too many attempts. Try again in a few minutes.");
            }

            var reservation = new AttemptReservation(now);
            attempts.Add(reservation);
            return reservation;
        }
    }

    /// <summary>
    ///     Gives a reserved attempt back, because the attempt did not fail.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="reservation">The reservation from <see cref="ReserveAttempt"/>; <c>null</c> does nothing.</param>
    /// <remarks>
    ///     Only that reservation is removed; one that has left the window already is gone and nothing else changes.
    /// </remarks>
    private void ReleaseAttempt(Actor actor, AttemptReservation? reservation)
    {
        if (reservation is null)
        {
            return;
        }

        lock (_failuresLock)
        {
            if (_failures.TryGetValue(actor.Username, out var attempts))
            {
                attempts.Remove(reservation);

                // An actor without recent attempts needs no entry
                if (attempts.Count == 0)
                {
                    _failures.Remove(actor.Username);
                }
            }
        }
    }

    /// <summary>
    ///     Records a failed attempt in the audit trail; the attempt itself stays counted from its reservation.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="projectId">The project of the attempt.</param>
    /// <param name="typedName">The user name that was tried; recorded as typed (cleaned by the audit store).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the record was attempted.</returns>
    private async Task RegisterFailureAsync(
        Actor actor,
        int projectId,
        string typedName,
        CancellationToken cancellationToken)
    {
        // Best effort: the refusal itself must not fail because the trail could not be written. The throttle caps
        // how many entries one actor can cause, so no coalescing is needed
        if (_audit is not null)
        {
            await _audit.TryRecordAsync(
                AuditDraft.By(actor, MemberAddFailedAction, $"user name '{typedName}'", projectId),
                cancellationToken);
        }
    }

    /// <summary>
    ///     One reserved attempt of an actor, counted until it is released or leaves the window.
    /// </summary>
    /// <param name="time">When the attempt was reserved.</param>
    private sealed class AttemptReservation(DateTimeOffset time)
    {
        /// <summary>
        ///     Gets when the attempt was reserved.
        /// </summary>
        public DateTimeOffset Time { get; } = time;
    }

    /// <summary>
    ///     Draws a temporary password from the unambiguous alphabet with a cryptographic random source.
    /// </summary>
    /// <returns>The password, <see cref="TemporaryPasswordLength"/> characters.</returns>
    private static string NewTemporaryPassword()
    {
        return new string(RandomNumberGenerator.GetItems<char>(UnambiguousCharacters, TemporaryPasswordLength));
    }
}
