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

using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     One check of a review checklist.
/// </summary>
/// <param name="Id">The identifier, unique within the checklist (for example <c>REQ-01</c>).</param>
/// <param name="Text">The wording of the check.</param>
public sealed record ChecklistItem(string Id, string Text);

/// <summary>
///     A group of related checks that applies to a review as a whole or not at all.
/// </summary>
/// <param name="Id">The identifier, unique within the checklist; it is the prefix of the item identifiers (for example <c>REQ</c>).</param>
/// <param name="Title">The heading of the section.</param>
/// <param name="ApplicabilityHint">One sentence saying when the section can be skipped.</param>
/// <param name="Items">The checks of the section, in display order.</param>
public sealed record ChecklistSection(string Id, string Title, string ApplicabilityHint, IReadOnlyList<ChecklistItem> Items);

/// <summary>
///     A complete review checklist: a version label and its sections.
/// </summary>
/// <remarks>
///     The definition is immutable. A copy of it, serialized with <see cref="ToJson"/>, is stored with each
///     review when the review is created, so later changes of <see cref="DefaultChecklist"/> never alter
///     the checklist of an existing review.
/// </remarks>
/// <param name="Version">The version label of the wording (for example <c>default-1</c>).</param>
/// <param name="Sections">The sections, in display order.</param>
public sealed record ChecklistDefinition(string Version, IReadOnlyList<ChecklistSection> Sections)
{
    /// <summary>
    ///     Serializes the definition to JSON.
    /// </summary>
    /// <returns>The JSON text; <see cref="FromJson"/> reads it back.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    /// <summary>
    ///     Reads a definition from JSON written by <see cref="ToJson"/>.
    /// </summary>
    /// <param name="json">The JSON text. Must not be null.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException">Thrown when the text is not a valid definition.</exception>
    public static ChecklistDefinition FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ChecklistDefinition>(json)
               ?? throw new JsonException("The checklist definition is empty.");
    }
}
