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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the gutter markers of the <see cref="DiffView"/> component: one marker per kind with its count,
///     a different icon per kind, and a text name for every marker. Rendered in memory with bUnit.
/// </summary>
public class DiffViewMarkerTests : BunitContext
{
    /// <summary>
    ///     Builds an open thread that starts at a line of the new file.
    /// </summary>
    /// <param name="id">The identity of the thread.</param>
    /// <param name="line">The line the thread starts at.</param>
    /// <param name="finding"><c>true</c> for a Finding, <c>false</c> for a Question.</param>
    /// <returns>The thread.</returns>
    private static ReviewThread ThreadAt(int id, int line, bool finding)
    {
        return new ReviewThread
        {
            Id = id,
            ProjectId = 1,
            ReviewId = "REV-1",
            FilePath = "a.txt",
            Side = ThreadSide.Head,
            StartLine = line,
            EndLine = line,
            Comments =
            [
                new ReviewComment { Id = id * 10, ThreadId = id, Author = "Bob", AuthorUsername = "bob", Text = "Hmm", IncludeInReport = finding }
            ]
        };
    }

    /// <summary>
    ///     Renders a diff of three unchanged lines with the given threads.
    /// </summary>
    /// <param name="threads">The threads to mark.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<DiffView> RenderDiff(params ReviewThread[] threads)
    {
        var result = LineDiff.Compute(["a", "b", "c"], ["a", "b", "c"]);
        return Render<DiffView>(parameters => parameters
            .Add(view => view.Result, result)
            .Add(view => view.Threads, threads));
    }

    /// <summary>
    ///     Test that a Question and a Finding on different lines get different icons, and each marker is a counted
    ///     marker of its kind.
    /// </summary>
    [Fact]
    public void DiffView_Markers_UseDifferentIconPerKind()
    {
        // Arrange and Act: a Question on line 1 and a Finding on line 2
        var cut = RenderDiff(ThreadAt(1, 1, false), ThreadAt(2, 2, true));

        // Assert: one marker per kind with the matching class and a different icon
        var question = Assert.Single(cut.FindAll("[data-testid^='diff-marker-question-']:not([data-testid$='-count'])"));
        var finding = Assert.Single(cut.FindAll("[data-testid^='diff-marker-finding-']:not([data-testid$='-count'])"));
        Assert.Contains("kind-question", question.ClassList);
        Assert.Contains("kind-finding", finding.ClassList);
        Assert.NotEqual(question.QuerySelector("svg path")!.GetAttribute("d"), finding.QuerySelector("svg path")!.GetAttribute("d"));
        Assert.Equal("1 question", question.GetAttribute("aria-label"));
        Assert.Equal("1 finding", finding.GetAttribute("aria-label"));
    }

    /// <summary>
    ///     Test that a row where two Questions and a Finding start shows both markers, each with its own count and its
    ///     own name, for example "2 questions" and "1 finding".
    /// </summary>
    [Fact]
    public void DiffView_Markers_MixedRow_ShowsBothKindsWithCounts()
    {
        // Arrange and Act: two Questions and one Finding start on line 2
        var cut = RenderDiff(ThreadAt(1, 2, false), ThreadAt(2, 2, false), ThreadAt(3, 2, true));

        // Assert: one marker group, with both kinds and their counts
        var group = Assert.Single(cut.FindAll("[data-testid^='diff-markers-']"));
        var question = group.QuerySelector("[data-thread-kind='kind-question']")!;
        var finding = group.QuerySelector("[data-thread-kind='kind-finding']")!;
        Assert.Equal("2 questions", question.GetAttribute("aria-label"));
        Assert.Equal("2 questions", question.GetAttribute("title"));
        Assert.Equal("1 finding", finding.GetAttribute("aria-label"));
        Assert.Equal("2", question.QuerySelector("[data-testid$='-count']")!.TextContent);
        Assert.Equal("1", finding.QuerySelector("[data-testid$='-count']")!.TextContent);
    }

    /// <summary>
    ///     Test that every marker has a text name, so that no marker rests on color or icon alone, and that the icon is
    ///     hidden from assistive technology.
    /// </summary>
    [Fact]
    public void DiffView_Markers_EveryMarkerHasTextNameAndHiddenIcon()
    {
        // Arrange and Act
        var cut = RenderDiff(ThreadAt(1, 1, false), ThreadAt(2, 2, true), ThreadAt(3, 3, true), ThreadAt(4, 3, false));

        // Assert
        var markers = cut.FindAll(".kind-marker");
        Assert.Equal(4, markers.Count);
        Assert.All(markers, marker =>
        {
            Assert.False(string.IsNullOrWhiteSpace(marker.GetAttribute("aria-label")));
            Assert.False(string.IsNullOrWhiteSpace(marker.GetAttribute("title")));
            Assert.Equal("true", marker.QuerySelector("svg")!.GetAttribute("aria-hidden"));
        });
    }

    /// <summary>
    ///     Test that a diff without threads shows no marker.
    /// </summary>
    [Fact]
    public void DiffView_Markers_NoThreads_NoMarkers()
    {
        // Arrange and Act
        var cut = RenderDiff();

        // Assert
        Assert.Empty(cut.FindAll(".kind-marker"));
        Assert.Empty(cut.FindAll("[data-testid^='diff-markers-']"));
    }
}
