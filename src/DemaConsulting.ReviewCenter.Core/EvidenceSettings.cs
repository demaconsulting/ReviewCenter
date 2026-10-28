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
///     One changed setting: the key, and its value before and after the change.
/// </summary>
/// <param name="Key">The name of the setting, as recorded in the audit trail (for example <c>AnonymousIndex</c>).</param>
/// <param name="Old">The value before the change.</param>
/// <param name="New">The value after the change.</param>
public sealed record EvidenceSettingChange(string Key, bool Old, bool New);

/// <summary>
///     The evidence settings of one project: who may read the evidence without signing in, and which sections the
///     attestation PDFs of the project contain.
/// </summary>
/// <remarks>
///     The two anonymous switches are independent and default to off, so evidence is private unless a project
///     administrator opens it. The five content switches default to on, so a PDF is complete unless a project
///     administrator reduces it. Whatever the switches say, a PDF always carries the review and project
///     identifiers, the content fingerprint, the checklist outcomes, the signatures, the record digest and the
///     signing-key note, and the record digest always covers the complete record. The record is immutable and
///     safe to share between threads.
/// </remarks>
/// <param name="AnonymousIndex">Anyone may read the evidence index of the project without signing in.</param>
/// <param name="AnonymousAttestations">Anyone may download the attestation PDFs of the project without signing in.</param>
/// <param name="IncludeThreads">The PDF includes the review discussion threads and their comments.</param>
/// <param name="IncludeReviewerNames">The PDF names the people (reviewers, answerers, commenters, the requester).</param>
/// <param name="IncludeChecklistWording">The PDF includes the wording of the checklist questions and section titles.</param>
/// <param name="IncludeFiles">The PDF includes the list of files with their hashes.</param>
/// <param name="IncludeGitContext">The PDF includes the repository name and the baseline and head revisions.</param>
public sealed record EvidenceSettings(
    bool AnonymousIndex,
    bool AnonymousAttestations,
    bool IncludeThreads,
    bool IncludeReviewerNames,
    bool IncludeChecklistWording,
    bool IncludeFiles,
    bool IncludeGitContext)
{
    /// <summary>
    ///     Gets the settings of a new project: nothing anonymous, nothing omitted from the PDFs.
    /// </summary>
    public static EvidenceSettings Default { get; } = new(false, false, true, true, true, true, true);

    /// <summary>
    ///     Gets a value indicating whether a PDF made with these settings omits or redacts anything.
    /// </summary>
    public bool IsReduced => OmittedSections.Count > 0;

    /// <summary>
    ///     Gets the names of the content sections these settings leave out of a PDF, in a fixed order.
    /// </summary>
    /// <remarks>
    ///     The names are the ones the user sees: the settings page, the Content scope section of every PDF and the
    ///     audit trail use the same words, so the three can be compared.
    /// </remarks>
    public IReadOnlyList<string> OmittedSections
    {
        get
        {
            var omitted = new List<string>();
            if (!IncludeThreads)
            {
                omitted.Add("Review discussion threads");
            }

            if (!IncludeReviewerNames)
            {
                omitted.Add("Reviewer names");
            }

            if (!IncludeChecklistWording)
            {
                omitted.Add("Checklist wording");
            }

            if (!IncludeFiles)
            {
                omitted.Add("File list and hashes");
            }

            if (!IncludeGitContext)
            {
                omitted.Add("Git context");
            }

            return omitted;
        }
    }

    /// <summary>
    ///     Lists the settings that differ between an earlier state and this one.
    /// </summary>
    /// <param name="earlier">The earlier settings. Must not be null.</param>
    /// <returns>One entry per differing setting, in the order of the properties; empty when they are equal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="earlier"/> is null.</exception>
    public IReadOnlyList<EvidenceSettingChange> ChangesFrom(EvidenceSettings earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);

        var changes = new List<EvidenceSettingChange>();
        Add(changes, nameof(AnonymousIndex), earlier.AnonymousIndex, AnonymousIndex);
        Add(changes, nameof(AnonymousAttestations), earlier.AnonymousAttestations, AnonymousAttestations);
        Add(changes, nameof(IncludeThreads), earlier.IncludeThreads, IncludeThreads);
        Add(changes, nameof(IncludeReviewerNames), earlier.IncludeReviewerNames, IncludeReviewerNames);
        Add(changes, nameof(IncludeChecklistWording), earlier.IncludeChecklistWording, IncludeChecklistWording);
        Add(changes, nameof(IncludeFiles), earlier.IncludeFiles, IncludeFiles);
        Add(changes, nameof(IncludeGitContext), earlier.IncludeGitContext, IncludeGitContext);
        return changes;
    }

    /// <summary>
    ///     Adds a change to the list when the two values differ.
    /// </summary>
    /// <param name="changes">The list to add to.</param>
    /// <param name="key">The setting name.</param>
    /// <param name="old">The earlier value.</param>
    /// <param name="current">The current value.</param>
    private static void Add(List<EvidenceSettingChange> changes, string key, bool old, bool current)
    {
        if (old != current)
        {
            changes.Add(new EvidenceSettingChange(key, old, current));
        }
    }
}
