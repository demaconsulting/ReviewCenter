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
using System.Text;
using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Reads and changes the checklist that a project gives to its new reviews, enforcing who may change it and
///     recording every change in the audit trail.
/// </summary>
/// <remarks>
///     <para>
///         A project without a definition of its own uses the built-in <see cref="DefaultChecklist"/>. A project
///         administrator (or a system administrator) can replace it with a definition of their own, or go back to the
///         default. The wording is signed (it is part of the record digest), so a change is strict: the text must pass
///         <see cref="ChecklistValidator"/>, a changed definition must carry another version label than the current
///         one (so two reviews with the same label always have the same wording), and only a person may change it, not
///         an API token. A change affects only reviews created afterwards; every review keeps the copy it was created
///         with, so no approved or signed review is touched.
///     </para>
///     <para>
///         A change is a compare-and-set on the hash of the stored text (the caller names the hash of what it started
///         from), and is stored together with its audit entry <c>project.checklist-changed</c>, which names the new
///         version, the number of sections and checks, a short hash of the text and the previous version, but never the
///         wording. Every call asks <see cref="IProjectAccess"/> again, so a demoted or disabled user loses the right
///         at once. The service is stateless and safe for concurrent use when the injected store and access rules are.
///     </para>
/// </remarks>
public sealed class ProjectChecklistService
{
    /// <summary>
    ///     The action recorded when the checklist definition of a project is replaced or reset to the default.
    /// </summary>
    public const string ChangedAction = "project.checklist-changed";

    /// <summary>
    ///     The message for a checklist that someone else changed while the caller was editing it.
    /// </summary>
    public const string ChangedByOthersMessage =
        "The checklist was changed by someone else. The current checklist is shown; check it and save again.";

    /// <summary>
    ///     The store that reads and changes projects.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     Decides who may see and administer a project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProjectChecklistService"/> class.
    /// </summary>
    /// <param name="store">The store that reads and changes projects. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public ProjectChecklistService(IReviewStore store, IProjectAccess access)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(access);
        _store = store;
        _access = access;
    }

    /// <summary>
    ///     Builds the hash that a change of a project's checklist is based on.
    /// </summary>
    /// <remarks>
    ///     One definition of the hash, used by this service and by the store, so that both compare the same thing.
    ///     A project without a definition of its own is identified by the hash of the built-in default's text, so
    ///     "never changed" is a value like any other.
    /// </remarks>
    /// <param name="storedJson">The stored text of the project's definition; <c>null</c> for a project that uses the default.</param>
    /// <returns>The lower-case hexadecimal SHA-256 of the UTF-8 bytes of the text (of the default's text for <c>null</c>).</returns>
    public static string HashOf(string? storedJson)
    {
        var text = storedJson ?? DefaultChecklist.Current.ToJson();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    ///     Checks the text of a checklist definition without storing anything.
    /// </summary>
    /// <remarks>
    ///     Pure: for an import page that shows all errors and a preview before anything is saved. It checks the
    ///     definition on its own; the version-label rule needs the stored definition and is applied by
    ///     <see cref="SetAsync"/>.
    /// </remarks>
    /// <param name="json">The JSON text; <c>null</c> is reported like an empty text.</param>
    /// <returns>All errors with their paths, or the parsed definition.</returns>
    public static ChecklistValidationResult Validate(string? json) => ChecklistValidator.Validate(json);

    /// <summary>
    ///     Reads the checklist a project gives to its new reviews.
    /// </summary>
    /// <param name="actor">The signed-in user; may be null, which is refused.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The checklist: the project's own definition, or the built-in default.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> when the project does not exist or the actor may not
    ///     see it, and with <see cref="DomainRule.ChecklistDefinitionInvalid"/> when the stored text cannot be read.
    /// </exception>
    public async Task<ProjectChecklist> GetAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
    {
        await _access.RequireViewAsync(actor, projectId, cancellationToken);
        var project = await GetProjectAsync(projectId, cancellationToken);
        return Describe(project);
    }

    /// <summary>
    ///     Replaces the checklist definition of a project.
    /// </summary>
    /// <param name="actor">
    ///     The signed-in user; must administer the project (or be a system administrator). An API token is refused.
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expectedHash">The hash of the stored definition the caller read (<see cref="ProjectChecklist.Hash"/>). Must not be null.</param>
    /// <param name="json">The new definition as JSON. It is checked with <see cref="ChecklistValidator"/> and stored in its normal form.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the definition was changed; <c>false</c> when it has the same content as the current one
    ///     and nothing was stored or recorded.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> or <paramref name="expectedHash"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> (see
    ///     <see cref="IProjectAccess.RequireAdministerAsync"/>; also for an API token), with
    ///     <see cref="DomainRule.ChecklistDefinitionInvalid"/> when the text is not valid or its content differs from
    ///     the current definition but its version label is the same, and with <see cref="DomainRule.RecordChanged"/>
    ///     when the stored definition no longer has the <paramref name="expectedHash"/> (someone else changed it
    ///     first); nothing is stored then.
    /// </exception>
    public async Task<bool> SetAsync(
        Actor actor,
        int projectId,
        string expectedHash,
        string? json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(expectedHash);

        // Who may change it comes first: a refused caller learns nothing about the validation or the current value
        await RequireMayChangeAsync(actor, projectId, cancellationToken);

        // Strict validation: invalid text never reaches the store, and the store keeps the normal form of the text
        var definition = ChecklistValidator.Validate(json).ThrowIfInvalid();
        var newJson = definition.ToJson();

        var project = await GetProjectAsync(projectId, cancellationToken);
        RequireExpected(project, expectedHash);

        // The same content changes nothing; a different content must say so with another label
        var currentJson = project.ChecklistDefinition ?? DefaultChecklist.Current.ToJson();
        if (string.Equals(newJson, currentJson, StringComparison.Ordinal))
        {
            return false;
        }

        var previousVersion = VersionOf(project);
        if (string.Equals(definition.Version, previousVersion, StringComparison.Ordinal))
        {
            throw new DomainRuleException(
                DomainRule.ChecklistDefinitionInvalid,
                "The checklist definition is not valid (1 error): version: the wording differs from the current definition, " +
                $"so the version label must change; \"{definition.Version}\" is the current one");
        }

        var sections = definition.Sections.Count;
        var items = definition.Sections.Sum(section => section.Items.Count);
        var detail = $"version {definition.Version}, {sections} sections, {items} items, sha256 {AuditText.ShortHash(newJson)}, " +
                     $"previous version {previousVersion}";
        await StoreAsync(actor, projectId, expectedHash, newJson, detail, cancellationToken);
        return true;
    }

    /// <summary>
    ///     Removes the project's own checklist definition, so its new reviews use the built-in default again.
    /// </summary>
    /// <param name="actor">
    ///     The signed-in user; must administer the project (or be a system administrator). An API token is refused.
    /// </param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expectedHash">The hash of the stored definition the caller read (<see cref="ProjectChecklist.Hash"/>). Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the definition was removed; <c>false</c> when the project already uses the default and
    ///     nothing was stored or recorded.
    /// </returns>
    /// <remarks>
    ///     The change is recorded with the same action <c>project.checklist-changed</c> as a replace; its detail says
    ///     that the checklist was reset to the default, and names the default's version and the previous version.
    ///     Existing reviews keep the copy they were created with.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> or <paramref name="expectedHash"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ProjectNotFound"/> or <see cref="DomainRule.ActorNotAllowed"/> as
    ///     <see cref="SetAsync"/> does, and with <see cref="DomainRule.RecordChanged"/> when the stored definition no
    ///     longer has the <paramref name="expectedHash"/>; nothing is stored then.
    /// </exception>
    public async Task<bool> ResetAsync(
        Actor actor,
        int projectId,
        string expectedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(expectedHash);

        await RequireMayChangeAsync(actor, projectId, cancellationToken);
        var project = await GetProjectAsync(projectId, cancellationToken);
        RequireExpected(project, expectedHash);
        if (project.ChecklistDefinition is null)
        {
            return false;
        }

        var detail = $"reset to default, version {DefaultChecklist.Version}, previous version {VersionOf(project)}";
        await StoreAsync(actor, projectId, expectedHash, null, detail, cancellationToken);
        return true;
    }

    /// <summary>
    ///     Requires that the actor is a person who may change the checklist of the project.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the actor may change it.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> for an API token (a setting that decides what every
    ///     signature covers needs a person at the web pages), and as <see cref="IProjectAccess.RequireAdministerAsync"/> does.
    /// </exception>
    private async Task RequireMayChangeAsync(Actor actor, int projectId, CancellationToken cancellationToken)
    {
        if (actor.ViaApiToken)
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Project settings cannot be changed with an API token; sign in to the web pages.");
        }

        // The right is checked again on every call: a user demoted while a page was open must not get a change through
        await _access.RequireAdministerAsync(actor, projectId, cancellationToken);
    }

    /// <summary>
    ///     Reads a project that the caller has already been allowed to see.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The project.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ProjectNotFound"/> when it does not exist.</exception>
    private async Task<Project> GetProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        return await _store.GetProjectAsync(projectId, cancellationToken)
               ?? throw new DomainRuleException(DomainRule.ProjectNotFound, $"Project {projectId} does not exist.");
    }

    /// <summary>
    ///     Refuses a change that is based on a definition other than the stored one.
    /// </summary>
    /// <param name="project">The project as it is stored now.</param>
    /// <param name="expectedHash">The hash the caller based its change on.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RecordChanged"/> when the hashes differ.</exception>
    private static void RequireExpected(Project project, string expectedHash)
    {
        if (!string.Equals(HashOf(project.ChecklistDefinition), expectedHash, StringComparison.Ordinal))
        {
            throw new DomainRuleException(DomainRule.RecordChanged, ChangedByOthersMessage);
        }
    }

    /// <summary>
    ///     Stores a change with its audit entry; the store repeats the comparison atomically.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="expectedHash">The hash the caller based its change on.</param>
    /// <param name="json">The new text; <c>null</c> for a reset.</param>
    /// <param name="detail">The audit detail.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.RecordChanged"/> when the store finds another definition.</exception>
    private async Task StoreAsync(
        Actor actor,
        int projectId,
        string expectedHash,
        string? json,
        string detail,
        CancellationToken cancellationToken)
    {
        var stored = await _store.UpdateChecklistDefinitionAsync(
            projectId,
            expectedHash,
            json,
            AuditDraft.By(actor, ChangedAction, detail, projectId),
            cancellationToken);
        if (!stored)
        {
            throw new DomainRuleException(DomainRule.RecordChanged, ChangedByOthersMessage);
        }
    }

    /// <summary>
    ///     Builds the description of the checklist a project uses.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The checklist with its text and hash.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.ChecklistDefinitionInvalid"/> when the stored text cannot be read.</exception>
    private static ProjectChecklist Describe(Project project)
    {
        if (project.ChecklistDefinition is null)
        {
            var json = DefaultChecklist.Current.ToJson();
            return new ProjectChecklist(DefaultChecklist.Current, true, json, HashOf(null));
        }

        try
        {
            var definition = ChecklistDefinition.FromJson(project.ChecklistDefinition);
            return new ProjectChecklist(definition, false, project.ChecklistDefinition, HashOf(project.ChecklistDefinition));
        }
        catch (JsonException ex)
        {
            throw new DomainRuleException(
                DomainRule.ChecklistDefinitionInvalid,
                $"The stored checklist definition of project {project.Id} cannot be read: {ex.Message}");
        }
    }

    /// <summary>
    ///     Finds the version label of the definition a project uses now, for the version rule and the audit text.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>The label; <c>unreadable</c> when the stored text cannot be read (a reset or a replace is then still possible).</returns>
    private static string VersionOf(Project project)
    {
        if (project.ChecklistDefinition is null)
        {
            return DefaultChecklist.Version;
        }

        try
        {
            return ChecklistDefinition.FromJson(project.ChecklistDefinition).Version;
        }
        catch (JsonException)
        {
            return "unreadable";
        }
    }
}
