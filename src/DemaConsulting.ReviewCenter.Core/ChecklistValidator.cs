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

using System.Text;
using System.Text.Json;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Checks the JSON text of a checklist definition against the strict rules that every definition must satisfy
///     before it is stored in a project or frozen into a review, and reports all problems at once.
/// </summary>
/// <remarks>
///     <para>
///         The checklist wording is signed: it is part of the record digest. A definition therefore has to be exact
///         and unambiguous, and the answers are keyed by identifier alone, so identifiers must be well-formed, short
///         enough for their columns and unique. The rules are: the text is valid JSON of at most
///         <see cref="ReviewLimits.MaxChecklistBytes"/> bytes; the version label is present and at most
///         <see cref="ReviewLimits.MaxChecklistVersionLength"/> characters; there are 1 to
///         <see cref="ReviewLimits.MaxChecklistSections"/> sections and at most
///         <see cref="ReviewLimits.MaxChecklistItems"/> checks in total, and no section is empty; identifiers consist of
///         letters, digits, dots, underscores and hyphens, start with a letter or digit, are at most
///         <see cref="ReviewLimits.MaxChecklistIdLength"/> characters, and are unique over the whole definition
///         (sections and checks together, compared without regard to case); titles are present and at most
///         <see cref="ReviewLimits.MaxChecklistTitleLength"/> characters; the wording of a check is present and at most
///         <see cref="ReviewLimits.MaxChecklistItemTextLength"/> characters; an applicability hint is at most
///         <see cref="ReviewLimits.MaxChecklistHintLength"/> characters.
///     </para>
///     <para>
///         The identifier rule accepts upper case letters because the built-in <see cref="DefaultChecklist"/> uses
///         them (<c>REQ-01</c>) and it must pass its own validator, so that the default can be exported and
///         imported unchanged. Uniqueness ignores case so two identifiers that differ only in case cannot
///         be confused. Paths in the messages use the lower-case names <c>version</c>, <c>sections</c>, <c>items</c>,
///         <c>id</c>, <c>title</c>, <c>text</c> and <c>applicabilityHint</c> and zero-based indexes; the reader matches
///         member names without regard to case. The validator is stateless and thread-safe.
///     </para>
/// </remarks>
public static class ChecklistValidator
{
    /// <summary>
    ///     The path of the text as a whole.
    /// </summary>
    private const string WholeText = "json";

    /// <summary>
    ///     Checks the JSON text of a checklist definition.
    /// </summary>
    /// <remarks>
    ///     The check never throws for bad content: every problem becomes an error so a person can fix them all in
    ///     one pass. A text above the size limit is not parsed at all (only that error is reported), so an oversized
    ///     upload costs nothing. Nothing is stored or changed.
    /// </remarks>
    /// <param name="json">The JSON text; <c>null</c> is reported as an error like an empty text.</param>
    /// <returns>
    ///     The result: all errors in the order of the text, or the parsed definition when there is none.
    /// </returns>
    public static ChecklistValidationResult Validate(string? json)
    {
        // The size limit comes first: it protects the parser, and a text that is too large needs no other finding
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(new ChecklistValidationError(WholeText, "the text is empty; expected a JSON checklist definition"));
        }

        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > ReviewLimits.MaxChecklistBytes)
        {
            return Fail(new ChecklistValidationError(
                WholeText,
                $"the text is {bytes} bytes; at most {ReviewLimits.MaxChecklistBytes} bytes (256 KiB) are allowed"));
        }

        // Read the structure; a malformed text or a value of the wrong type is a single error with its JSON position
        ChecklistDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<ChecklistDefinition>(json, ChecklistDefinition.ReadOptions);
        }
        catch (JsonException ex)
        {
            return Fail(new ChecklistValidationError(
                string.IsNullOrEmpty(ex.Path) || ex.Path == "$" ? WholeText : ex.Path,
                $"not a valid checklist definition: {ex.Message}"));
        }

        if (definition is null)
        {
            return Fail(new ChecklistValidationError(WholeText, "the text is null; expected a JSON checklist definition"));
        }

        // Check every rule and keep going after a finding, so the caller sees all errors together
        var errors = new List<ChecklistValidationError>();
        CheckDefinition(definition, errors);
        return errors.Count == 0
            ? new ChecklistValidationResult([], definition)
            : new ChecklistValidationResult(errors, null);
    }

    /// <summary>
    ///     Checks the JSON text and returns the definition, or refuses it.
    /// </summary>
    /// <param name="json">The JSON text; <c>null</c> is refused like an empty text.</param>
    /// <returns>The valid definition.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ChecklistDefinitionInvalid"/> when the text has any error; see
    ///     <see cref="ChecklistValidationResult.ThrowIfInvalid"/>.
    /// </exception>
    public static ChecklistDefinition ParseOrThrow(string? json)
    {
        return Validate(json).ThrowIfInvalid();
    }

    /// <summary>
    ///     Builds a result for a text that could not be checked any further.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>A result with that one error.</returns>
    private static ChecklistValidationResult Fail(ChecklistValidationError error)
    {
        return new ChecklistValidationResult([error], null);
    }

    /// <summary>
    ///     Checks the version, the number of sections and every section of a definition.
    /// </summary>
    /// <param name="definition">The parsed definition; its members may be null where the text left them out.</param>
    /// <param name="errors">Receives every error found.</param>
    private static void CheckDefinition(ChecklistDefinition definition, List<ChecklistValidationError> errors)
    {
        // The version label names the wording, so it must be present and fit its column
        CheckText(errors, "version", definition.Version, ReviewLimits.MaxChecklistVersionLength, required: true);

        if (definition.Sections is null)
        {
            errors.Add(new ChecklistValidationError("sections", "is missing"));
            return;
        }

        if (definition.Sections.Count == 0)
        {
            errors.Add(new ChecklistValidationError("sections", "has no section; at least one is required"));
        }
        else if (definition.Sections.Count > ReviewLimits.MaxChecklistSections)
        {
            errors.Add(new ChecklistValidationError(
                "sections",
                $"has {definition.Sections.Count} sections; at most {ReviewLimits.MaxChecklistSections} are allowed"));
        }

        // One namespace for all identifiers, because answers and section states are keyed by identifier alone
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemCount = 0;
        for (var s = 0; s < definition.Sections.Count; s++)
        {
            itemCount += CheckSection(definition.Sections[s], $"sections[{s}]", seen, errors);
        }

        if (itemCount > ReviewLimits.MaxChecklistItems)
        {
            errors.Add(new ChecklistValidationError(
                "sections",
                $"has {itemCount} checks in total; at most {ReviewLimits.MaxChecklistItems} are allowed"));
        }
    }

    /// <summary>
    ///     Checks one section and its items.
    /// </summary>
    /// <param name="section">The parsed section; null when the text had an empty entry.</param>
    /// <param name="path">The path of the section.</param>
    /// <param name="seen">The identifiers used so far in the whole definition.</param>
    /// <param name="errors">Receives every error found.</param>
    /// <returns>The number of checks the section has (zero when it has none or is missing).</returns>
    private static int CheckSection(
        ChecklistSection? section,
        string path,
        HashSet<string> seen,
        List<ChecklistValidationError> errors)
    {
        if (section is null)
        {
            errors.Add(new ChecklistValidationError(path, "is missing"));
            return 0;
        }

        CheckId(errors, $"{path}.id", section.Id, seen);
        CheckText(errors, $"{path}.title", section.Title, ReviewLimits.MaxChecklistTitleLength, required: true);
        CheckText(errors, $"{path}.applicabilityHint", section.ApplicabilityHint, ReviewLimits.MaxChecklistHintLength, required: false);

        if (section.Items is null)
        {
            errors.Add(new ChecklistValidationError($"{path}.items", "is missing"));
            return 0;
        }

        // A section without checks has nothing to answer, yet it could be marked not applicable: reject it as a mistake
        if (section.Items.Count == 0)
        {
            errors.Add(new ChecklistValidationError($"{path}.items", "has no check; a section needs at least one"));
        }

        for (var i = 0; i < section.Items.Count; i++)
        {
            var item = section.Items[i];
            var itemPath = $"{path}.items[{i}]";
            if (item is null)
            {
                errors.Add(new ChecklistValidationError(itemPath, "is missing"));
                continue;
            }

            CheckId(errors, $"{itemPath}.id", item.Id, seen);
            CheckText(errors, $"{itemPath}.text", item.Text, ReviewLimits.MaxChecklistItemTextLength, required: true);
        }

        return section.Items.Count;
    }

    /// <summary>
    ///     Checks one identifier: present, well-formed, short enough and not used before.
    /// </summary>
    /// <param name="errors">Receives every error found.</param>
    /// <param name="path">The path of the identifier.</param>
    /// <param name="id">The identifier; null when the text left it out.</param>
    /// <param name="seen">The identifiers used so far; the identifier is added when it is new.</param>
    private static void CheckId(List<ChecklistValidationError> errors, string path, string? id, HashSet<string> seen)
    {
        if (string.IsNullOrEmpty(id))
        {
            errors.Add(new ChecklistValidationError(path, "is missing or empty"));
            return;
        }

        if (id.Length > ReviewLimits.MaxChecklistIdLength)
        {
            errors.Add(new ChecklistValidationError(
                path,
                $"\"{Shorten(id)}\" is {id.Length} characters; at most {ReviewLimits.MaxChecklistIdLength} are allowed"));
        }

        if (!IsWellFormedId(id))
        {
            errors.Add(new ChecklistValidationError(
                path,
                $"\"{Shorten(id)}\" is not a valid identifier; use letters, digits, '.', '_' and '-', starting with a letter or digit"));
        }

        // A repeated identifier is reported at its later use, so the first use stays the reference
        if (!seen.Add(id))
        {
            errors.Add(new ChecklistValidationError(path, $"duplicate \"{Shorten(id)}\""));
        }
    }

    /// <summary>
    ///     Checks a text value: present when required, and not longer than its limit.
    /// </summary>
    /// <param name="errors">Receives every error found.</param>
    /// <param name="path">The path of the value.</param>
    /// <param name="value">The value; null when the text left it out.</param>
    /// <param name="maxLength">The longest allowed value, in characters.</param>
    /// <param name="required"><c>true</c> when an empty or white-space value is an error.</param>
    private static void CheckText(
        List<ChecklistValidationError> errors,
        string path,
        string? value,
        int maxLength,
        bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ChecklistValidationError(path, "is missing or empty"));
            return;
        }

        if (value is not null && value.Length > maxLength)
        {
            errors.Add(new ChecklistValidationError(path, $"is {value.Length} characters; at most {maxLength} are allowed"));
        }
    }

    /// <summary>
    ///     Tests the form of an identifier: letters, digits, dots, underscores and hyphens, starting with a letter or digit.
    /// </summary>
    /// <param name="id">The identifier; not empty.</param>
    /// <returns><c>true</c> when the identifier has the form.</returns>
    private static bool IsWellFormedId(string id)
    {
        // Checked by character, not by regular expression: ASCII only, and no end-of-line surprises
        return IsAsciiLetterOrDigit(id[0]) && id.All(c => IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    /// <summary>
    ///     Tests for an ASCII letter or digit.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for A-Z, a-z and 0-9.</returns>
    private static bool IsAsciiLetterOrDigit(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

    /// <summary>
    ///     Cuts a long value for a message, so an error about a huge identifier stays short.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or its first 40 characters and <c>...</c>.</returns>
    private static string Shorten(string value)
    {
        const int limit = 40;
        return value.Length <= limit ? value : value[..limit] + "...";
    }
}
