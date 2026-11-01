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
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ThreadComposer"/> component: the choice between a Question and a Finding when a
///     thread is started, the "Add to report" checkbox of a reply, the text, the submit and the data attribute that
///     lets Ctrl+Enter submit. Rendered in memory with bUnit.
/// </summary>
public class ThreadComposerTests : BunitContext
{
    /// <summary>
    ///     Renders the composer for a new thread and records what it hands to the parent.
    /// </summary>
    /// <param name="includeInReport">The choice the parent holds.</param>
    /// <param name="texts">The texts typed, one per input.</param>
    /// <param name="choices">The choices made, one per change.</param>
    /// <param name="submits">The number of submits, as a counter in the first element.</param>
    /// <param name="isReply">Whether the composer is the reply box.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ThreadComposer> RenderComposer(
        bool includeInReport, List<string> texts, List<bool> choices, int[] submits, bool isReply = false)
    {
        return Render<ThreadComposer>(parameters => parameters
            .Add(composer => composer.IsReply, isReply)
            .Add(composer => composer.IncludeInReport, includeInReport)
            .Add(composer => composer.TextChanged, text => texts.Add(text))
            .Add(composer => composer.IncludeInReportChanged, choice => choices.Add(choice))
            .Add(composer => composer.OnSubmit, () => submits[0]++));
    }

    /// <summary>
    ///     Test that the new-thread form offers a Question and a Finding, with the Question chosen when the parent has
    ///     not asked for a report, and with the words that say what each one means.
    /// </summary>
    [Fact]
    public void ThreadComposer_DefaultsToQuestion()
    {
        // Act
        var cut = RenderComposer(false, [], [], [0]);

        // Assert: both choices with their words, the Question is on
        var question = cut.Find("[data-testid='thread-kind-choice-question']");
        var finding = cut.Find("[data-testid='thread-kind-choice-finding']");
        Assert.True(question.HasAttribute("checked"));
        Assert.False(finding.HasAttribute("checked"));
        Assert.Contains("Question", cut.Find("[data-testid='thread-kind-choice']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Finding (goes in the report)", cut.Find("[data-testid='thread-kind-choice']").TextContent, StringComparison.Ordinal);
        Assert.Equal("start-thread-button", cut.Find("[data-testid='thread-text-input']").GetAttribute("data-ctrl-enter-submits"));
    }

    /// <summary>
    ///     Test that choosing Finding tells the parent that the thread goes in the report, that choosing Question
    ///     takes it back, and that the text and the submit reach the parent.
    /// </summary>
    [Fact]
    public async Task ThreadComposer_FindingChoice_StartsFindingThread()
    {
        // Arrange
        var texts = new List<string>();
        var choices = new List<bool>();
        var submits = new[] { 0 };
        var cut = RenderComposer(false, texts, choices, submits);

        // Act: choose Finding, type, choose Question again and submit
        await cut.Find("[data-testid='thread-kind-choice-finding']").ChangeAsync(new ChangeEventArgs { Value = "finding" });
        await cut.Find("[data-testid='thread-text-input']").InputAsync("Please fix this");
        await cut.Find("[data-testid='thread-kind-choice-question']").ChangeAsync(new ChangeEventArgs { Value = "question" });
        await cut.Find("[data-testid='new-thread-form']").SubmitAsync();

        // Assert
        Assert.Equal([true, false], choices);
        Assert.Equal(["Please fix this"], texts);
        Assert.Equal(1, submits[0]);
    }

    /// <summary>
    ///     Test that the reply box has an "Add to report" checkbox that is off by default (a reply is chat) and
    ///     reports the choice to the parent, and that the Reply button submits.
    /// </summary>
    [Fact]
    public async Task ThreadComposer_ReplyAddToReport_SetsIncludeInReport()
    {
        // Arrange
        var choices = new List<bool>();
        var submits = new[] { 0 };
        var cut = RenderComposer(false, [], choices, submits, isReply: true);

        // Assert: the checkbox is there, off, and the new-thread choice is not
        var box = cut.Find("[data-testid='reply-report']");
        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("Add to report", cut.Find("label.composer-report").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='thread-kind-choice']"));

        // Act: tick the box and press Reply
        await box.ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("[data-testid='start-thread-button']").ClickAsync();

        // Assert
        Assert.Equal([true], choices);
        Assert.Equal(1, submits[0]);
    }

    /// <summary>
    ///     Test that a message from the parent is shown as an alert with its own test id.
    /// </summary>
    [Fact]
    public void ThreadComposer_Error_IsShownAsAlert()
    {
        // Act
        var cut = Render<ThreadComposer>(parameters => parameters.Add(composer => composer.Error, "The comment text is required."));

        // Assert
        var error = cut.Find("[data-testid='thread-error']");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal("The comment text is required.", error.TextContent);
    }
}
