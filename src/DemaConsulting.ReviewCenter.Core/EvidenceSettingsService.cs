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
///     Reads and changes the evidence settings of a project, enforcing who may change them and recording every
///     change in the audit trail.
/// </summary>
/// <remarks>
///     The settings decide who may read the evidence without signing in and what an attestation PDF contains, so
///     only a project administrator of the project or a system administrator may read or change them; every call
///     asks <see cref="IProjectAccess"/> again, so a demoted or disabled user loses the right at once. A change is
///     a compare-and-set on the stored value (the caller names the settings it started from), and is stored
///     together with its audit entry <c>project.evidence-settings-changed</c>, which names each changed key with
///     its old and new value. <see cref="GetEffectiveAsync"/> is the one read that takes no actor: the evidence
///     services and the API use it to apply the settings to a request, and it grants nothing by itself. The
///     service is stateless and safe for concurrent use when the injected store and access rules are.
/// </remarks>
public sealed class EvidenceSettingsService
{
    /// <summary>
    ///     The action recorded when the evidence settings of a project are changed.
    /// </summary>
    public const string ChangedAction = "project.evidence-settings-changed";

    /// <summary>
    ///     The message for settings that someone else changed while the caller was editing them.
    /// </summary>
    public const string ChangedByOthersMessage =
        "The evidence settings were changed by someone else. The current settings are shown; check them and save again.";

    /// <summary>
    ///     The store that reads and changes projects.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Decides who may administer a project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EvidenceSettingsService"/> class.
    /// </summary>
    /// <param name="store">The store that reads and changes projects. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public EvidenceSettingsService(IReviewStore store, IProjectAccess access)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        _store = store;
        _access = access;
    }

    /// <summary>
    ///     Reads the evidence settings of a project for someone who administers it.
    /// </summary>
    /// <param name="actor">The signed-in user; may be null, which is refused.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The settings of the project.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the project does not exist or the actor may not
    ///     see it, and with <see cref="DomainRule.ActorNotAllowed"/> when the actor sees it but may not administer it.
    /// </exception>
    public async Task<EvidenceSettings> GetAsync(
        Actor? actor,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        return await GetEffectiveAsync(projectId, cancellationToken)
               ?? throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
    }

    /// <summary>
    ///     Reads the evidence settings of a project without regard to who asks.
    /// </summary>
    /// <remarks>
    ///     For the services and the API that must apply the settings to a request (whether anonymous access is
    ///     allowed, what a PDF contains). It decides nothing about access by itself.
    /// </remarks>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The settings, or <c>null</c> when the project does not exist.</returns>
    public async Task<EvidenceSettings?> GetEffectiveAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        return project?.Evidence;
    }

    /// <summary>
    ///     Changes the evidence settings of a project.
    /// </summary>
    /// <param name="actor">The signed-in user; must administer the project (or be a system administrator).</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expected">The settings the caller read and based its change on. Must not be null.</param>
    /// <param name="updated">The new settings. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the settings were changed; <c>false</c> when <paramref name="updated"/> equals
    ///     <paramref name="expected"/> and nothing was stored or recorded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expected"/> or <paramref name="updated"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> as
    ///     <see cref="GetAsync"/> does, and with <see cref="DomainRule.RecordChanged"/> when the stored settings are no
    ///     longer <paramref name="expected"/> (someone else changed them first); nothing is stored then.
    /// </exception>
    public async Task<bool> SetAsync(
        Actor? actor,
        int projectId,
        EvidenceSettings expected,
        EvidenceSettings updated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);

        // The right is checked again on every call: a user demoted while a page was open must not get a change through
        // A missing actor has no access (the access rules refuse it); the explicit check keeps that true for any
        // implementation of the rules and lets the audit entry name the actor
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
        if (actor is null)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "Nobody is signed in.");
        }

        var changes = updated.ChangesFrom(expected);
        if (changes.Count == 0)
        {
            return false;
        }

        var detail = string.Join("; ", changes.Select(c => $"{c.Key} {Value(c.Old)}->{Value(c.New)}"));
        var stored = await _store.UpdateEvidenceSettingsAsync(
            projectId,
            expected,
            updated,
            AuditDraft.By(actor, ChangedAction, detail, projectId),
            cancellationToken);
        if (!stored)
        {
            throw new DomainRuleException(DomainRule.RecordChanged, ChangedByOthersMessage);
        }

        return true;
    }

    /// <summary>
    ///     Writes a switch value for the audit text.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><c>on</c> or <c>off</c>.</returns>
    private static string Value(bool value) => value ? "on" : "off";
}
