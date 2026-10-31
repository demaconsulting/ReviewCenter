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
///     A project: the named container that groups the reviews of one body of work.
/// </summary>
/// <remarks>
///     Project names are not required to be unique; the numeric <see cref="Id"/> is the identity.
///     Instances are created by <see cref="ProjectService"/>, which enforces the rules, and are
///     populated by the storage layer; the properties are therefore initialization-only. The type
///     is not thread-safe.
/// </remarks>
public sealed class Project
{
    /// <summary>
    ///     Gets the identity of the project, assigned by the store when the project is added.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    ///     Gets the display name of the project; never empty, at most
    ///     <see cref="ReviewLimits.MaxProjectNameLength"/> characters.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the free-text description of the project; may be empty, at most
    ///     <see cref="ReviewLimits.MaxProjectDescriptionLength"/> characters.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the moment the project was created.
    /// </summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>
    ///     Gets the evidence settings of the project (anonymous access and the content of the attestation PDFs);
    ///     <see cref="EvidenceSettings.Default"/> for a project that was never changed.
    /// </summary>
    public EvidenceSettings Evidence { get; init; } = EvidenceSettings.Default;

    /// <summary>
    ///     Gets a value indicating whether the person who created a review is kept out of its reviewer slots and
    ///     cannot sign it; <c>true</c> (independent reviewers required) unless a project administrator switched it off.
    /// </summary>
    /// <remarks>
    ///     "Created" means the Review Center user who created the review in this tool; the authors of the git
    ///     commits of the reviewed files are not matched. See <see cref="SigningService"/> for where it is enforced.
    /// </remarks>
    public bool RequireIndependentReviewers { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether the person who created a review is kept from answering the checks of its
    ///     checklist; <c>true</c> unless a project administrator switched it off.
    /// </summary>
    /// <remarks>
    ///     <see cref="ChecklistService"/> applies it to every project, also to one that existed before the rule was
    ///     enforced, because the stored value defaults to <c>true</c>.
    /// </remarks>
    public bool RequireNonAuthorChecklistAnswers { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether the person who reopened a review is kept from signing it again;
    ///     <c>true</c> unless a project administrator switched it off.
    /// </summary>
    /// <remarks>
    ///     <see cref="SigningService"/> applies it to every project, also to one that existed before the rule was
    ///     enforced, because the stored value defaults to <c>true</c>.
    /// </remarks>
    public bool RequireNonReopenerSigning { get; init; } = true;

    /// <summary>
    ///     Gets the checklist definition of the project as JSON (the text <see cref="ChecklistDefinition.ToJson"/>
    ///     writes); <c>null</c> when the project uses the built-in <see cref="DefaultChecklist"/>.
    /// </summary>
    /// <remarks>
    ///     A stored value has passed <see cref="ChecklistValidator"/> when it was set by
    ///     <see cref="ProjectChecklistService"/>. The version label is the <c>Version</c> of the definition itself; it
    ///     is not kept in a second column, so the label and the wording cannot disagree. Every new review copies the
    ///     definition that is current at its creation (see <see cref="ReviewChecklist"/>); changing it later never
    ///     alters an existing review.
    /// </remarks>
    public string? ChecklistDefinition { get; init; }
}

/// <summary>
///     A per-project review rule that a project administrator can switch on or off.
/// </summary>
/// <remarks>
///     Each member names one boolean property of <see cref="Project"/>; see
///     <see cref="ProjectService.SetReviewRuleAsync"/> for how a rule is changed.
/// </remarks>
public enum ReviewRule
{
    /// <summary>
    ///     The person who created a review cannot be one of its reviewers and cannot sign it
    ///     (<see cref="Project.RequireIndependentReviewers"/>).
    /// </summary>
    IndependentReviewers,

    /// <summary>
    ///     The person who created a review cannot answer the checks of its checklist
    ///     (<see cref="Project.RequireNonAuthorChecklistAnswers"/>).
    /// </summary>
    NonAuthorChecklistAnswers,

    /// <summary>
    ///     The person who reopened a review cannot sign it again
    ///     (<see cref="Project.RequireNonReopenerSigning"/>).
    /// </summary>
    NonReopenerSigning,
}
