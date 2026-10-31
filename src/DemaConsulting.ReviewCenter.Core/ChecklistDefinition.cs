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
    ///     The options every reader of a definition uses: member names are matched without regard to case, so a
    ///     definition written by hand with <c>version</c> and <c>sections</c> reads like one written by
    ///     <see cref="ToJson"/> (which uses the member names of the types).
    /// </summary>
    internal static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

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
    /// <remarks>
    ///     This reader is deliberately lenient about content: it is how the frozen copy of an existing review is read,
    ///     and an existing review must stay readable whatever rules are applied to new definitions. The strict rules
    ///     (unique identifiers, limits, lengths) are in <see cref="ChecklistValidator"/>, which every definition
    ///     passes before it is stored in a project or frozen into a new review.
    /// </remarks>
    /// <param name="json">The JSON text. Must not be null.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException">
    ///     Thrown when the text is not valid JSON or not a complete definition: the version, the list of sections, a
    ///     section, its identifier, title or list of items, an item, or its identifier or text is missing. The
    ///     applicability hint is not required.
    /// </exception>
    public static ChecklistDefinition FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var definition = JsonSerializer.Deserialize<ChecklistDefinition>(json, ReadOptions)
                         ?? throw new JsonException("The checklist definition is empty.");
        Validate(definition);
        return definition;
    }

    /// <summary>
    ///     Checks that nothing a reader depends on is missing; the deserializer leaves the members that the text does
    ///     not mention null even though the types say otherwise.
    /// </summary>
    /// <param name="definition">The deserialized definition.</param>
    /// <exception cref="JsonException">Thrown when a required part is missing.</exception>
    private static void Validate(ChecklistDefinition definition)
    {
        if (definition.Version is null)
        {
            throw new JsonException("The checklist definition has no version.");
        }

        if (definition.Sections is null)
        {
            throw new JsonException("The checklist definition has no sections.");
        }

        foreach (var section in definition.Sections)
        {
            ValidateSection(section);
        }
    }

    /// <summary>
    ///     Checks one section of a deserialized definition and the items in it.
    /// </summary>
    /// <param name="section">The deserialized section; null when the text had an empty entry.</param>
    /// <exception cref="JsonException">Thrown when a required part of the section or of an item is missing.</exception>
    private static void ValidateSection(ChecklistSection? section)
    {
        if (section is null)
        {
            throw new JsonException("The checklist definition has an empty section.");
        }

        if (section.Id is null)
        {
            throw new JsonException("A section of the checklist definition has no identifier.");
        }

        if (section.Title is null)
        {
            throw new JsonException($"Section '{section.Id}' of the checklist definition has no title.");
        }

        if (section.Items is null)
        {
            throw new JsonException($"Section '{section.Id}' of the checklist definition has no items.");
        }

        foreach (var item in section.Items)
        {
            if (item is null)
            {
                throw new JsonException($"Section '{section.Id}' of the checklist definition has an empty item.");
            }

            if (item.Id is null)
            {
                throw new JsonException($"An item of section '{section.Id}' of the checklist definition has no identifier.");
            }

            if (item.Text is null)
            {
                throw new JsonException($"Item '{item.Id}' of the checklist definition has no text.");
            }
        }
    }
}
