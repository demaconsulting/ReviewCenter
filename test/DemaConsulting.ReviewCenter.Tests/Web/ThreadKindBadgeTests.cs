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
///     Unit tests for the <see cref="ThreadKindBadge"/> component: the icon and the word of each kind, the "In report"
///     badge of a Finding and the compact marker with its count and its accessible name. Rendered in memory with
///     bUnit.
/// </summary>
public class ThreadKindBadgeTests : BunitContext
{
    /// <summary>
    ///     Test that a Question shows a neutral speech-bubble icon that is hidden from assistive technology, the visible
    ///     word "Question" and the semantic class, and no "In report" badge.
    /// </summary>
    [Fact]
    public void ThreadKindBadge_Question_ShowsIconAndLabel()
    {
        // Act
        var cut = Render<ThreadKindBadge>(parameters => parameters.Add(badge => badge.Kind, ThreadKind.Question));

        // Assert: the semantic class, the visible word and an icon that assistive technology skips
        var badge = cut.Find("[data-testid='thread-kind']");
        Assert.Contains("kind-question", badge.ClassList);
        Assert.DoesNotContain("kind-finding", badge.ClassList);
        Assert.Equal("Question", cut.Find("[data-testid='thread-kind-label']").TextContent);
        Assert.Equal("true", badge.QuerySelector("svg")?.GetAttribute("aria-hidden"));
        Assert.Empty(cut.FindAll("[data-testid='thread-kind-in-report']"));
    }

    /// <summary>
    ///     Test that a Finding shows the amber flag, the visible word "Finding" and the "In report" badge, and that the
    ///     badge can be left out.
    /// </summary>
    [Fact]
    public void ThreadKindBadge_Finding_ShowsFlagLabelAndInReportBadge()
    {
        // Act
        var cut = Render<ThreadKindBadge>(parameters => parameters.Add(badge => badge.Kind, ThreadKind.Finding));

        // Assert
        var badge = cut.Find("[data-testid='thread-kind']");
        Assert.Contains("kind-finding", badge.ClassList);
        Assert.Equal("Finding", cut.Find("[data-testid='thread-kind-label']").TextContent);
        Assert.Equal("true", badge.QuerySelector("svg")?.GetAttribute("aria-hidden"));
        Assert.Equal("In report", cut.Find("[data-testid='thread-kind-in-report']").TextContent);

        // Act: the same without the badge
        var plain = Render<ThreadKindBadge>(parameters => parameters
            .Add(component => component.Kind, ThreadKind.Finding)
            .Add(component => component.ShowInReport, false));

        // Assert
        Assert.Empty(plain.FindAll("[data-testid='thread-kind-in-report']"));
    }

    /// <summary>
    ///     Test that the two kinds use different icons, so the kind never rests on color alone.
    /// </summary>
    [Fact]
    public void ThreadKindBadge_Kinds_UseDifferentIcons()
    {
        // Act
        var question = Render<ThreadKindBadge>(parameters => parameters.Add(badge => badge.Kind, ThreadKind.Question));
        var finding = Render<ThreadKindBadge>(parameters => parameters.Add(badge => badge.Kind, ThreadKind.Finding));

        // Assert
        var questionPath = question.Find("svg path").GetAttribute("d");
        var findingPath = finding.Find("svg path").GetAttribute("d");
        Assert.False(string.IsNullOrEmpty(questionPath));
        Assert.False(string.IsNullOrEmpty(findingPath));
        Assert.NotEqual(questionPath, findingPath);
    }

    /// <summary>
    ///     Test that the compact marker names the kind and the count in its title and accessible name, with the
    ///     singular and the plural.
    /// </summary>
    [Theory]
    [InlineData(ThreadKind.Question, 1, "1 question")]
    [InlineData(ThreadKind.Question, 2, "2 questions")]
    [InlineData(ThreadKind.Finding, 1, "1 finding")]
    [InlineData(ThreadKind.Finding, 3, "3 findings")]
    public void ThreadKindBadge_Compact_NamesKindAndCount(ThreadKind kind, int count, string expected)
    {
        // Act
        var cut = Render<ThreadKindBadge>(parameters => parameters
            .Add(badge => badge.Kind, kind)
            .Add(badge => badge.Compact, true)
            .Add(badge => badge.Count, count));

        // Assert: the accessible name and the title carry the words, the count is shown as a number
        var marker = cut.Find("[data-testid='thread-kind']");
        Assert.Equal(expected, marker.GetAttribute("aria-label"));
        Assert.Equal(expected, marker.GetAttribute("title"));
        Assert.Equal("img", marker.GetAttribute("role"));
        Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find("[data-testid='thread-kind-count']").TextContent);
        Assert.Equal(expected, ThreadKindBadge.Describe(kind, count));
    }
}
