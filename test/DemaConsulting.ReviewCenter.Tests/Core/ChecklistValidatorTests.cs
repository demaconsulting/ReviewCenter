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
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of <see cref="ChecklistValidator"/> and <see cref="ChecklistValidationResult"/>: every rule of a
///     checklist definition, the paths in the messages, and the report of all errors at once.
/// </summary>
public class ChecklistValidatorTests
{
    /// <summary>
    ///     Builds the JSON text of a definition with the given sections.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="sections">The sections.</param>
    /// <returns>The JSON text.</returns>
    private static string Json(string version, params ChecklistSection[] sections)
    {
        return new ChecklistDefinition(version, sections).ToJson();
    }

    /// <summary>
    ///     Builds a section with the given items.
    /// </summary>
    /// <param name="id">The section identifier.</param>
    /// <param name="items">The items.</param>
    /// <returns>The section.</returns>
    private static ChecklistSection Section(string id, params ChecklistItem[] items)
    {
        return new ChecklistSection(id, "Title " + id, string.Empty, items);
    }

    /// <summary>
    ///     Builds a valid one-section definition around the given item.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The JSON text.</returns>
    private static string JsonWithItem(ChecklistItem item) => Json("v1", Section("code", item));

    /// <summary>
    ///     Gets the paths of the errors of a text.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The paths, in order.</returns>
    private static string[] Paths(string json)
    {
        return ChecklistValidator.Validate(json).Errors.Select(e => e.Path).ToArray();
    }

    /// <summary>
    ///     Test that the built-in default passes its own validator and that the parsed definition equals it.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_DefaultChecklist_IsValid()
    {
        // Act
        var result = ChecklistValidator.Validate(DefaultChecklist.Current.ToJson());

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(DefaultChecklist.Current.Version, result.Definition!.Version);
        Assert.Equal(DefaultChecklist.Current.ToJson(), result.Definition.ToJson());
    }

    /// <summary>
    ///     Test that a small valid definition passes, with and without an applicability hint, and that
    ///     <see cref="ChecklistValidationResult.ThrowIfInvalid"/> returns it.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_SmallDefinition_IsValid()
    {
        // Arrange
        var json = Json(
            "my-1",
            new ChecklistSection("s1", "One", "Only for code", [new ChecklistItem("s1.a", "Check A")]),
            Section("s2", new ChecklistItem("s2_b", "Check B"), new ChecklistItem("s2-c", "Check C")));

        // Act
        var definition = ChecklistValidator.ParseOrThrow(json);

        // Assert
        Assert.Equal("my-1", definition.Version);
        Assert.Equal(2, definition.Sections.Count);
        Assert.Equal(["s1.a", "s2_b", "s2-c"], definition.Sections.SelectMany(s => s.Items).Select(i => i.Id));
    }

    /// <summary>
    ///     Test that an empty, white-space and absent text each give one error for the text as a whole.
    /// </summary>
    /// <param name="json">The text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ChecklistValidator_Validate_EmptyText_GivesOneError(string? json)
    {
        // Act
        var result = ChecklistValidator.Validate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Null(result.Definition);
        var error = Assert.Single(result.Errors);
        Assert.Equal("json", error.Path);
        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that text that is not JSON, or JSON of the wrong shape, gives one error with a position, and that
    ///     JSON <c>null</c> gives one error for the text as a whole.
    /// </summary>
    /// <param name="json">The text.</param>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"version\": 5, \"sections\": []}")]
    [InlineData("null")]
    public void ChecklistValidator_Validate_BadJson_GivesOneError(string json)
    {
        // Act
        var result = ChecklistValidator.Validate(json);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Null(result.Definition);
    }

    /// <summary>
    ///     Test that a text above 256 KiB is refused from its size alone, and that one exactly at the limit is read.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_TextSize_LimitsAtMaxBytes()
    {
        // Arrange: a definition padded with white space to exactly the limit, and one byte more
        var json = Json("v1", Section("code", new ChecklistItem("a", "Check")));
        var atLimit = json + new string(' ', (int)ReviewLimits.MaxChecklistBytes - Encoding.UTF8.GetByteCount(json));
        var over = atLimit + " ";

        // Act
        var accepted = ChecklistValidator.Validate(atLimit);
        var refused = ChecklistValidator.Validate(over);

        // Assert
        Assert.True(accepted.IsValid);
        var error = Assert.Single(refused.Errors);
        Assert.Equal("json", error.Path);
        Assert.Contains("bytes", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the boundaries of the version label: 64 characters pass, 65 and an empty one do not.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_Version_BoundariesAndPresence()
    {
        // Arrange
        var item = new ChecklistItem("a", "Check");

        // Act
        var longest = ChecklistValidator.Validate(Json(new string('v', ReviewLimits.MaxChecklistVersionLength), Section("s", item)));
        var tooLong = Paths(Json(new string('v', ReviewLimits.MaxChecklistVersionLength + 1), Section("s", item)));
        var empty = Paths(Json(string.Empty, Section("s", item)));
        var missing = Paths("{\"sections\": [{\"id\": \"s\", \"title\": \"T\", \"items\": [{\"id\": \"a\", \"text\": \"X\"}]}]}");

        // Assert
        Assert.True(longest.IsValid);
        Assert.Equal(["version"], tooLong);
        Assert.Equal(["version"], empty);
        Assert.Equal(["version"], missing);
    }

    /// <summary>
    ///     Test that a definition without sections, or with a missing sections list, is refused.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_NoSections_IsRefused()
    {
        // Act
        var none = Paths(Json("v1"));
        var missing = Paths("{\"version\": \"v1\"}");

        // Assert
        Assert.Equal(["sections"], none);
        Assert.Equal(["sections"], missing);
    }

    /// <summary>
    ///     Test that 50 sections pass and 51 are refused.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_SectionCount_LimitsAtMaxSections()
    {
        // Arrange
        ChecklistSection[] Many(int count) =>
            Enumerable.Range(0, count).Select(i => Section($"s{i}", new ChecklistItem($"i{i}", "Check"))).ToArray();

        // Act
        var accepted = ChecklistValidator.Validate(Json("v1", Many(ReviewLimits.MaxChecklistSections)));
        var refused = ChecklistValidator.Validate(Json("v1", Many(ReviewLimits.MaxChecklistSections + 1)));

        // Assert
        Assert.True(accepted.IsValid);
        var error = Assert.Single(refused.Errors);
        Assert.Equal("sections", error.Path);
        Assert.Contains("at most 50", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that 500 checks in total pass and 501 are refused.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_ItemCount_LimitsAtMaxItems()
    {
        // Arrange: 20 sections of 25 checks is 500; one check more is 501
        ChecklistSection[] Build(int extra) =>
            Enumerable.Range(0, 20)
                .Select(s => Section(
                    $"s{s}",
                    Enumerable.Range(0, 25 + (s == 0 ? extra : 0)).Select(i => new ChecklistItem($"s{s}-{i}", "Check")).ToArray()))
                .ToArray();

        // Act
        var accepted = ChecklistValidator.Validate(Json("v1", Build(0)));
        var refused = ChecklistValidator.Validate(Json("v1", Build(1)));

        // Assert
        Assert.True(accepted.IsValid);
        var error = Assert.Single(refused.Errors);
        Assert.Equal("sections", error.Path);
        Assert.Contains("501 checks", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a section without checks is refused at the path of its items.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_EmptySection_IsRefused()
    {
        // Arrange
        var json = Json("v1", Section("full", new ChecklistItem("a", "Check")), Section("empty"));

        // Act
        var result = ChecklistValidator.Validate(json);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal("sections[1].items", error.Path);
    }

    /// <summary>
    ///     Test which identifiers are accepted and which are refused, for a section and for a check.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="valid">Whether the identifier is accepted.</param>
    [Theory]
    [InlineData("a", true)]
    [InlineData("0", true)]
    [InlineData("code-03", true)]
    [InlineData("a.b_c-d", true)]
    [InlineData("REQ-01", true)]
    [InlineData("-a", false)]
    [InlineData(".a", false)]
    [InlineData("_a", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("a\u00e9", false)]
    [InlineData("a\n", false)]
    [InlineData("a:b", false)]
    public void ChecklistValidator_Validate_IdForm_AcceptsOnlyTheAllowedCharacters(string id, bool valid)
    {
        // Act
        var section = Paths(Json("v1", Section(id, new ChecklistItem("ok", "Check"))));
        var item = Paths(JsonWithItem(new ChecklistItem(id, "Check")));

        // Assert
        Assert.Equal(valid ? [] : ["sections[0].id"], section);
        Assert.Equal(valid ? [] : ["sections[0].items[0].id"], item);
    }

    /// <summary>
    ///     Test that an identifier of 32 characters passes and one of 33 does not, and that a missing identifier is
    ///     reported.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_IdLength_LimitsAtMaxIdLength()
    {
        // Act
        var longest = Paths(JsonWithItem(new ChecklistItem(new string('a', ReviewLimits.MaxChecklistIdLength), "Check")));
        var tooLong = ChecklistValidator.Validate(
            JsonWithItem(new ChecklistItem(new string('a', ReviewLimits.MaxChecklistIdLength + 1), "Check")));
        var missing = Paths("{\"version\": \"v\", \"sections\": [{\"id\": \"s\", \"title\": \"T\", \"items\": [{\"text\": \"X\"}]}]}");

        // Assert
        Assert.Empty(longest);
        var error = Assert.Single(tooLong.Errors);
        Assert.Equal("sections[0].items[0].id", error.Path);
        Assert.Contains("33 characters", error.Message, StringComparison.Ordinal);
        Assert.Equal(["sections[0].items[0].id"], missing);
    }

    /// <summary>
    ///     Test that a repeated identifier is reported at its later use with the human-readable path, whether it
    ///     repeats a check in another section, a check in the same section, a section, or differs only in case.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_DuplicateIds_AreReportedAtTheLaterUse()
    {
        // Arrange: check ids repeat across sections and in a section; a check repeats a section id; one differs in case
        var json = Json(
            "v1",
            Section("sec-a", new ChecklistItem("code-03", "One"), new ChecklistItem("dup", "Two"), new ChecklistItem("dup", "Three")),
            Section("sec-b", new ChecklistItem("x", "Four"), new ChecklistItem("code-03", "Five")),
            Section("sec-a", new ChecklistItem("sec-b", "Six"), new ChecklistItem("DUP", "Seven")));

        // Act
        var errors = ChecklistValidator.Validate(json).Errors.Select(e => e.ToString()).ToArray();

        // Assert
        Assert.Equal(
            [
                "sections[0].items[2].id: duplicate \"dup\"",
                "sections[1].items[1].id: duplicate \"code-03\"",
                "sections[2].id: duplicate \"sec-a\"",
                "sections[2].items[0].id: duplicate \"sec-b\"",
                "sections[2].items[1].id: duplicate \"DUP\""
            ],
            errors);
    }

    /// <summary>
    ///     Test the boundaries of the section title, the check wording and the applicability hint, and that a
    ///     missing or white-space title and wording are refused while a hint may be empty.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_TitlesTextAndHint_BoundariesAndPresence()
    {
        // Arrange
        ChecklistSection Make(string title, string hint, string text) =>
            new("s", title, hint, [new ChecklistItem("a", text)]);

        // Act
        var atLimits = ChecklistValidator.Validate(Json(
            "v1",
            Make(
                new string('t', ReviewLimits.MaxChecklistTitleLength),
                new string('h', ReviewLimits.MaxChecklistHintLength),
                new string('x', ReviewLimits.MaxChecklistItemTextLength))));
        var overLimits = Paths(Json(
            "v1",
            Make(
                new string('t', ReviewLimits.MaxChecklistTitleLength + 1),
                new string('h', ReviewLimits.MaxChecklistHintLength + 1),
                new string('x', ReviewLimits.MaxChecklistItemTextLength + 1))));
        var blank = Paths(Json("v1", Make("  ", string.Empty, string.Empty)));

        // Assert
        Assert.True(atLimits.IsValid);
        Assert.Equal(["sections[0].title", "sections[0].applicabilityHint", "sections[0].items[0].text"], overLimits);
        Assert.Equal(["sections[0].title", "sections[0].items[0].text"], blank);
    }

    /// <summary>
    ///     Test that a missing section entry, a missing items list and a missing check entry are each reported.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_NullEntries_AreReported()
    {
        // Arrange
        const string json = """
            {"version": "v1", "sections": [
              null,
              {"id": "a", "title": "A"},
              {"id": "b", "title": "B", "items": [null]}
            ]}
            """;

        // Act
        var errors = ChecklistValidator.Validate(json).Errors.Select(e => e.ToString()).ToArray();

        // Assert
        Assert.Equal(
            ["sections[0]: is missing", "sections[1].items: is missing", "sections[2].items[0]: is missing"],
            errors);
    }

    /// <summary>
    ///     Test that all errors are reported together, in the order of the text, and that nothing is parsed
    ///     as valid.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_ManyProblems_AreAllReportedTogether()
    {
        // Arrange: a bad version, a bad id, a duplicate, an empty title, an over-long text and an empty section
        var json = Json(
            string.Empty,
            new ChecklistSection("Bad Id", "", string.Empty, [new ChecklistItem("a", "Check"), new ChecklistItem("a", new string('x', 1001))]),
            Section("none"));

        // Act
        var result = ChecklistValidator.Validate(json);

        // Assert
        Assert.Null(result.Definition);
        Assert.Equal(
            [
                "version", "sections[0].id", "sections[0].title", "sections[0].items[1].id",
                "sections[0].items[1].text", "sections[1].items"
            ],
            result.Errors.Select(e => e.Path));
    }

    /// <summary>
    ///     Test that member names are matched without regard to case, so the definition can be written either way.
    /// </summary>
    [Fact]
    public void ChecklistValidator_Validate_MemberNamesInAnyCase_AreRead()
    {
        // Arrange
        const string json = """
            {"version": "v1", "sections": [{"id": "a", "title": "A", "applicabilityHint": "h",
              "items": [{"id": "b", "text": "T"}]}]}
            """;

        // Act
        var definition = ChecklistValidator.ParseOrThrow(json);

        // Assert
        Assert.Equal("h", definition.Sections[0].ApplicabilityHint);
        Assert.Equal("b", definition.Sections[0].Items[0].Id);
    }

    /// <summary>
    ///     Test that an invalid text is refused with the rule <see cref="DomainRule.ChecklistDefinitionInvalid"/>,
    ///     that the message names the paths, and that it shows at most ten errors and counts the rest.
    /// </summary>
    [Fact]
    public void ChecklistValidator_ParseOrThrow_Invalid_ThrowsWithPathsAndAtMostTenErrors()
    {
        // Arrange: twelve checks with an empty wording
        var items = Enumerable.Range(0, 12).Select(i => new ChecklistItem($"i{i}", string.Empty)).ToArray();
        var json = Json("v1", Section("s", items));

        // Act
        var exception = Assert.Throws<DomainRuleException>(() => ChecklistValidator.ParseOrThrow(json));

        // Assert
        Assert.Equal(DomainRule.ChecklistDefinitionInvalid, exception.Rule);
        Assert.Contains("(12 errors)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sections[0].items[9].text: is missing or empty", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("items[10]", exception.Message, StringComparison.Ordinal);
        Assert.EndsWith("; and 2 more", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the error and its text form carry the path and the message.
    /// </summary>
    [Fact]
    public void ChecklistValidationError_ToString_IsPathAndMessage()
    {
        // Act
        var text = new ChecklistValidationError("sections[2].items[1].id", "duplicate \"code-03\"").ToString();

        // Assert
        Assert.Equal("sections[2].items[1].id: duplicate \"code-03\"", text);
    }
}
