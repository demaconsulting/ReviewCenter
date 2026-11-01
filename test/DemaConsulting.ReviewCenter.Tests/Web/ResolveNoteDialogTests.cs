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
///     Unit tests for the <see cref="ResolveNoteDialog"/> component: the note a Finding needs, the note a Question may
///     do without, and Cancel. Rendered in memory with bUnit.
/// </summary>
public class ResolveNoteDialogTests : BunitContext
{
    /// <summary>
    ///     Renders the dialog for a thread number 7 and records what it hands over.
    /// </summary>
    /// <param name="kind">The kind of the thread.</param>
    /// <param name="notes">The notes handed over, one per confirm.</param>
    /// <param name="cancels">The number of cancels, as a counter in the first element.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ResolveNoteDialog> RenderDialog(ThreadKind kind, List<string?> notes, int[] cancels)
    {
        return Render<ResolveNoteDialog>(parameters => parameters
            .Add(dialog => dialog.Kind, kind)
            .Add(dialog => dialog.Suffix, "7")
            .Add(dialog => dialog.OnConfirm, note => notes.Add(note))
            .Add(dialog => dialog.OnCancel, () => cancels[0]++));
    }

    /// <summary>
    ///     Test that a Finding cannot be resolved without a note: a blank note shows a message and hands nothing over,
    ///     and a typed note is handed over as typed, trimmed.
    /// </summary>
    [Fact]
    public async Task ResolveNoteDialog_Finding_RequiresNote()
    {
        // Arrange
        var notes = new List<string?>();
        var cut = RenderDialog(ThreadKind.Finding, notes, [0]);

        // Act: confirm with no note, then with only blanks
        await cut.Find("[data-testid='resolve-note-confirm-7']").ClickAsync();
        await cut.Find("[data-testid='resolve-note-text-7']").InputAsync("   ");
        await cut.Find("[data-testid='resolve-note-confirm-7']").ClickAsync();

        // Assert: refused with a message in the dialog, nothing handed over
        Assert.Empty(notes);
        Assert.Equal("alert", cut.Find("[data-testid='resolve-note-error-7']").GetAttribute("role"));
        Assert.NotEmpty(cut.Find("[data-testid='resolve-note-error-7']").TextContent);

        // Act: type a real note and confirm
        await cut.Find("[data-testid='resolve-note-text-7']").InputAsync("fixed in abc123");
        await cut.Find("[data-testid='resolve-note-confirm-7']").ClickAsync();

        // Assert: the note is handed over
        Assert.Equal("fixed in abc123", Assert.Single(notes));
    }

    /// <summary>
    ///     Test that a Question can be resolved with no note, which hands over null, and with a note.
    /// </summary>
    [Fact]
    public async Task ResolveNoteDialog_Question_NoteOptional()
    {
        // Arrange
        var notes = new List<string?>();
        var cut = RenderDialog(ThreadKind.Question, notes, [0]);

        // Act: confirm with no note
        await cut.Find("[data-testid='resolve-note-confirm-7']").ClickAsync();

        // Assert: no message, and null was handed over
        Assert.Empty(cut.FindAll("[data-testid='resolve-note-error-7']"));
        Assert.Null(Assert.Single(notes));

        // Act: confirm again with a note
        await cut.Find("[data-testid='resolve-note-text-7']").InputAsync("answered in chat");
        await cut.Find("[data-testid='resolve-note-confirm-7']").ClickAsync();

        // Assert
        Assert.Equal([null, "answered in chat"], notes);
    }

    /// <summary>
    ///     Test that Cancel tells the parent and hands over no note.
    /// </summary>
    [Fact]
    public async Task ResolveNoteDialog_Cancel_HandsOverNothing()
    {
        // Arrange
        var notes = new List<string?>();
        var cancels = new[] { 0 };
        var cut = RenderDialog(ThreadKind.Finding, notes, cancels);

        // Act
        await cut.Find("[data-testid='resolve-note-cancel-7']").ClickAsync();

        // Assert
        Assert.Equal(1, cancels[0]);
        Assert.Empty(notes);
    }
}
