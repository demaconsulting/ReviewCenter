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

using System.Text.Encodings.Web;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     A compact summary of how a candidate checklist definition differs from the one a project has now: the
///     version labels and the sections and checks that were added, removed or reworded, found by identifier.
/// </summary>
/// <remarks>
///     The summary is for a person who is about to replace a project's checklist and wants to see the size of the
///     change before confirming it. Identifiers are compared without regard to case, the same way the validator
///     treats them as unique; the order of sections and checks is not compared. A check counts as changed when its
///     wording differs or when it moved to another section; a section counts as changed when its title or its
///     applicability hint differs. The type is immutable and the comparison is pure, so it needs no
///     synchronization.
/// </remarks>
/// <param name="CurrentVersion">The version label of the definition the project has now.</param>
/// <param name="NewVersion">The version label of the candidate.</param>
/// <param name="AddedSections">The identifiers of the sections only the candidate has.</param>
/// <param name="RemovedSections">The identifiers of the sections only the current definition has.</param>
/// <param name="ChangedSections">The identifiers of the sections whose title or hint differ.</param>
/// <param name="AddedItems">The identifiers of the checks only the candidate has.</param>
/// <param name="RemovedItems">The identifiers of the checks only the current definition has.</param>
/// <param name="ChangedItems">The identifiers of the checks whose wording or section differ.</param>
internal sealed record ChecklistDiff(
    string CurrentVersion,
    string NewVersion,
    IReadOnlyList<string> AddedSections,
    IReadOnlyList<string> RemovedSections,
    IReadOnlyList<string> ChangedSections,
    IReadOnlyList<string> AddedItems,
    IReadOnlyList<string> RemovedItems,
    IReadOnlyList<string> ChangedItems)
{
    /// <summary>
    ///     The options that write a definition as indented JSON with the member names in camel case and without
    ///     escaping characters that are legal in JSON text, so an exported file reads like a file written by hand.
    /// </summary>
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    ///     Gets a value indicating whether the version labels differ.
    /// </summary>
    public bool VersionChanged => !string.Equals(CurrentVersion, NewVersion, StringComparison.Ordinal);

    /// <summary>
    ///     Gets a value indicating whether any section or check was added, removed or changed.
    /// </summary>
    public bool HasContentChanges =>
        AddedSections.Count + RemovedSections.Count + ChangedSections.Count
        + AddedItems.Count + RemovedItems.Count + ChangedItems.Count > 0;

    /// <summary>
    ///     Compares a candidate definition with the current one.
    /// </summary>
    /// <param name="current">The definition the project has now. Must not be null.</param>
    /// <param name="candidate">The valid candidate definition. Must not be null.</param>
    /// <returns>The summary of the differences; never null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static ChecklistDiff Compare(ChecklistDefinition current, ChecklistDefinition candidate)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);

        // Sections and checks are matched by identifier; the validator guarantees one namespace without case
        var currentSections = current.Sections.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var candidateSections = candidate.Sections.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var currentItems = ItemsOf(current);
        var candidateItems = ItemsOf(candidate);

        return new ChecklistDiff(
            current.Version,
            candidate.Version,
            candidate.Sections.Where(s => !currentSections.ContainsKey(s.Id)).Select(s => s.Id).ToList(),
            current.Sections.Where(s => !candidateSections.ContainsKey(s.Id)).Select(s => s.Id).ToList(),
            candidate.Sections
                .Where(s => currentSections.TryGetValue(s.Id, out var old)
                    && (old.Title != s.Title || (old.ApplicabilityHint ?? string.Empty) != (s.ApplicabilityHint ?? string.Empty)))
                .Select(s => s.Id)
                .ToList(),
            candidateItems.Where(i => !currentItems.ContainsKey(i.Key)).Select(i => i.Value.Item.Id).ToList(),
            currentItems.Where(i => !candidateItems.ContainsKey(i.Key)).Select(i => i.Value.Item.Id).ToList(),
            candidateItems
                .Where(i => currentItems.TryGetValue(i.Key, out var old)
                    && (old.Item.Text != i.Value.Item.Text
                        || !string.Equals(old.SectionId, i.Value.SectionId, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.Value.Item.Id)
                .ToList());
    }

    /// <summary>
    ///     Writes a definition as indented JSON text for an exported file.
    /// </summary>
    /// <param name="definition">The definition. Must not be null.</param>
    /// <returns>The JSON text; it passes the validator and has no content other than the definition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the definition is null.</exception>
    public static string ToIndentedJson(ChecklistDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, ExportOptions) + "\n";
    }

    /// <summary>
    ///     Lists the checks of a definition by identifier together with the section each one is in.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The checks, keyed by identifier without regard to case.</returns>
    private static Dictionary<string, (string SectionId, ChecklistItem Item)> ItemsOf(ChecklistDefinition definition)
    {
        return definition.Sections
            .SelectMany(section => section.Items.Select(item => (SectionId: section.Id, Item: item)))
            .ToDictionary(entry => entry.Item.Id, entry => entry, StringComparer.OrdinalIgnoreCase);
    }
}
