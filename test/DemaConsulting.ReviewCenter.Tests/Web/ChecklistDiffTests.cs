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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for <see cref="ChecklistDiff"/>: the summary of what a candidate checklist changes, and the text
///     written for an exported file.
/// </summary>
public class ChecklistDiffTests
{
    /// <summary>
    ///     Builds a definition from a version label and sections given as identifier, title, hint and checks.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="sections">The sections.</param>
    /// <returns>The definition.</returns>
    private static ChecklistDefinition Make(
        string version,
        params (string Id, string Title, string Hint, (string Id, string Text)[] Items)[] sections)
    {
        return new ChecklistDefinition(
            version,
            sections.Select(s => new ChecklistSection(s.Id, s.Title, s.Hint, s.Items.Select(i => new ChecklistItem(i.Id, i.Text)).ToList())).ToList());
    }

    /// <summary>
    ///     The definition most tests start from.
    /// </summary>
    /// <returns>Two sections and three checks.</returns>
    private static ChecklistDefinition Baseline()
    {
        return Make(
            "v1",
            ("a", "Alpha", "Always", [("a1", "One"), ("a2", "Two")]),
            ("b", "Beta", string.Empty, [("b1", "Three")]));
    }

    /// <summary>
    ///     Test that comparing a definition with itself finds no difference.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_Identical_FindsNoChange()
    {
        // Act
        var diff = ChecklistDiff.Compare(Baseline(), Baseline());

        // Assert
        Assert.False(diff.VersionChanged);
        Assert.False(diff.HasContentChanges);
        Assert.Equal("v1", diff.CurrentVersion);
        Assert.Equal("v1", diff.NewVersion);
    }

    /// <summary>
    ///     Test that a changed version label alone is a version change without content changes.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_OnlyVersion_ReportsVersionChange()
    {
        // Arrange
        var candidate = Baseline() with { Version = "v2" };

        // Act
        var diff = ChecklistDiff.Compare(Baseline(), candidate);

        // Assert
        Assert.True(diff.VersionChanged);
        Assert.False(diff.HasContentChanges);
        Assert.Equal("v2", diff.NewVersion);
    }

    /// <summary>
    ///     Test that sections and checks added and removed are listed by identifier.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_AddedAndRemoved_ListsIdentifiers()
    {
        // Arrange: Beta is gone, Gamma is new, and Alpha lost A2 and gained A3
        var candidate = Make(
            "v1",
            ("a", "Alpha", "Always", [("a1", "One"), ("a3", "Four")]),
            ("g", "Gamma", string.Empty, [("g1", "Five")]));

        // Act
        var diff = ChecklistDiff.Compare(Baseline(), candidate);

        // Assert
        Assert.Equal(["g"], diff.AddedSections);
        Assert.Equal(["b"], diff.RemovedSections);
        Assert.Empty(diff.ChangedSections);
        Assert.Equal(["a3", "g1"], diff.AddedItems);
        Assert.Equal(["a2", "b1"], diff.RemovedItems);
        Assert.Empty(diff.ChangedItems);
        Assert.True(diff.HasContentChanges);
    }

    /// <summary>
    ///     Test that another title or hint changes a section and another wording changes a check.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_OtherWording_ListsChangedSectionsAndItems()
    {
        // Arrange
        var candidate = Make(
            "v1",
            ("a", "Alpha renamed", "Always", [("a1", "One"), ("a2", "Two reworded")]),
            ("b", "Beta", "Now with a hint", [("b1", "Three")]));

        // Act
        var diff = ChecklistDiff.Compare(Baseline(), candidate);

        // Assert
        Assert.Equal(["a", "b"], diff.ChangedSections);
        Assert.Equal(["a2"], diff.ChangedItems);
        Assert.Empty(diff.AddedItems);
        Assert.Empty(diff.RemovedItems);
    }

    /// <summary>
    ///     Test that a check that moved to another section counts as changed, not as added and removed.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_MovedItem_IsChangedNotAddedOrRemoved()
    {
        // Arrange: b1 moves to Alpha, Beta keeps a check of its own
        var current = Make(
            "v1",
            ("a", "Alpha", "Always", [("a1", "One")]),
            ("b", "Beta", string.Empty, [("b1", "Three"), ("b2", "Four")]));
        var candidate = Make(
            "v1",
            ("a", "Alpha", "Always", [("a1", "One"), ("b1", "Three")]),
            ("b", "Beta", string.Empty, [("b2", "Four")]));

        // Act
        var diff = ChecklistDiff.Compare(current, candidate);

        // Assert
        Assert.Equal(["b1"], diff.ChangedItems);
        Assert.Empty(diff.AddedItems);
        Assert.Empty(diff.RemovedItems);
        Assert.Empty(diff.ChangedSections);
    }

    /// <summary>
    ///     Test that identifiers are matched without regard to case, the order is not compared, and a missing hint
    ///     equals an empty one.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_CaseOrderAndMissingHint_AreNotDifferences()
    {
        // Arrange: upper-case identifiers, reversed order, a null hint for the empty one
        var candidate = Make(
            "v1",
            ("B", "Beta", null!, [("B1", "Three")]),
            ("A", "Alpha", "Always", [("A2", "Two"), ("A1", "One")]));

        // Act
        var diff = ChecklistDiff.Compare(Baseline(), candidate);

        // Assert
        Assert.False(diff.HasContentChanges);
    }

    /// <summary>
    ///     Test that a missing argument is refused.
    /// </summary>
    [Fact]
    public void ChecklistDiff_Compare_NullArgument_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => ChecklistDiff.Compare(null!, Baseline()));
        Assert.Throws<ArgumentNullException>(() => ChecklistDiff.Compare(Baseline(), null!));
        Assert.Throws<ArgumentNullException>(() => ChecklistDiff.ToIndentedJson(null!));
    }

    /// <summary>
    ///     Test that the exported text is indented, uses camel case names and plain characters, ends with a line
    ///     break, and reads back as the same valid definition, also for the built-in default.
    /// </summary>
    [Fact]
    public void ChecklistDiff_ToIndentedJson_RoundTripsThroughTheValidator()
    {
        // Arrange: wording with characters that JSON may escape
        var definition = Make("v1", ("a", "Alpha", "Say \"hi\" <now>", [("a1", "Is it « fine » & 'ok'?")]));

        // Act
        var text = ChecklistDiff.ToIndentedJson(definition);
        var back = ChecklistValidator.Validate(text);
        var defaultBack = ChecklistValidator.Validate(ChecklistDiff.ToIndentedJson(DefaultChecklist.Current));

        // Assert
        Assert.StartsWith("{\n", text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"v1\"", text, StringComparison.Ordinal);
        Assert.Contains("\"applicabilityHint\"", text, StringComparison.Ordinal);
        Assert.Contains("« fine » & 'ok'", text, StringComparison.Ordinal);
        Assert.True(back.IsValid);
        Assert.Equal(definition.ToJson(), back.Definition!.ToJson());
        Assert.True(defaultBack.IsValid);
        Assert.Equal(DefaultChecklist.Current.ToJson(), defaultBack.Definition!.ToJson());
    }
}
