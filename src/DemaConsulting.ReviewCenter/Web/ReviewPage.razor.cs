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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the review page (<c>ReviewPage.razor</c>): the view of one review, of the content of its
///     files, and of the comment threads on them.
/// </summary>
/// <remarks>
///     The page reads reviews through <see cref="ReviewService"/> and reads and changes threads through
///     <see cref="ThreadService"/> only (plus the checklist progress, read through <see cref="ChecklistService"/>
///     for the checklist card, and the reviewer slots, signatures and approval status, read and changed through
///     <see cref="SigningService"/>); the rules live there and a violation is shown next to the form that
///     caused it. File bytes are loaded one file at a time when the operator selects it, never with the file
///     list. A file is shown as numbered lines only when it is valid UTF-8 without NUL bytes (see
///     <see cref="FileText"/>); anything else is reported as a binary file rather than rendered as garbage.
///     All text from the files and the comments is rendered as plain text, never as markup. The author of a
///     comment is the signed-in user (an <see cref="Actor"/> read from the authentication state), never
///     something typed on the page. Threads are read when the page loads and after each change made here; comments posted
///     in other browsers appear on the next load or change. Each browser connection (circuit) gets its own
///     page instance, so the state here is per-user and needs no synchronization. The thread index lists
///     the threads of every file through <see cref="ThreadService.BuildIndex"/>; choosing a row selects its
///     file, highlights its lines and asks the browser to scroll them into view.
/// </remarks>
public partial class ReviewPage : ComponentBase
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     The test id of the element to scroll into view after the next render, or <c>null</c>.
    /// </summary>
    private string? _scrollTarget;

    /// <summary>
    ///     Where the element of <see cref="_scrollTarget"/> lands in the window: <c>start</c> or <c>center</c>.
    /// </summary>
    private string _scrollBlock = "center";

    /// <summary>
    ///     Gets or sets the identity of the owning project, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the review identifier, from the page address.
    /// </summary>
    [Parameter]
    public string ReviewId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the service that reads reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewService ReviewService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads and changes comment threads, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ThreadService ThreadService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads the review checklist, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ChecklistService ChecklistService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that manages reviewer slots and signatures, supplied by dependency injection.
    /// </summary>
    [Inject]
    private SigningService SigningService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the browser script access used to scroll a thread into view, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets the name of the owning project shown in the breadcrumb, or <c>null</c> when it is unknown.
    /// </summary>
    private string? ProjectName { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the review lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the review shown, or <c>null</c> when it does not exist.
    /// </summary>
    private Review? Review { get; set; }

    /// <summary>
    ///     Gets the path of the file whose content is shown, or <c>null</c> when no file is selected.
    /// </summary>
    private string? SelectedPath { get; set; }

    /// <summary>
    ///     Gets the lines of the selected file, or <c>null</c> when no file is selected or it is binary.
    /// </summary>
    private IReadOnlyList<string>? SelectedLines { get; set; }

    /// <summary>
    ///     Gets all threads of the review (every file), in creation order.
    /// </summary>
    private IReadOnlyList<ReviewThread> Threads { get; set; } = [];

    /// <summary>
    ///     Gets the checklist state of the review, or <c>null</c> when the review has no checklist.
    /// </summary>
    private ChecklistState? Checklist { get; set; }

    /// <summary>
    ///     Gets the approval status of the review (slots, record digest, blockers), or <c>null</c> before it is loaded.
    /// </summary>
    private ReviewApproval? Approval { get; set; }

    /// <summary>
    ///     Gets the users that can be chosen for a slot.
    /// </summary>
    private IReadOnlyList<SlotMember> Candidates { get; set; } = [];

    /// <summary>
    ///     Gets the user name of the signed-in user, used to tell which slots the user occupies.
    /// </summary>
    private string CurrentUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the primary reviewer chosen in the add-slot form (a user id, or blank).
    /// </summary>
    private string NewSlotPrimary { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the alternate reviewer chosen in the add-slot form (a user id, or blank for none).
    /// </summary>
    private string NewSlotAlternate { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the message of the last failed attempt to add a slot, or <c>null</c>.
    /// </summary>
    private string? NewSlotError { get; set; }

    /// <summary>
    ///     Gets the primary reviewer chosen for each slot being edited, by slot identity.
    /// </summary>
    private Dictionary<int, string> SlotPrimaryChoices { get; } = [];

    /// <summary>
    ///     Gets the alternate reviewer chosen for each slot being edited, by slot identity.
    /// </summary>
    private Dictionary<int, string> SlotAlternateChoices { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed slot change for each slot, by slot identity.
    /// </summary>
    private Dictionary<int, string> SlotErrors { get; } = [];

    /// <summary>
    ///     Gets the password typed into each sign form, by slot identity; removed after every attempt.
    /// </summary>
    private Dictionary<int, string> SignPasswords { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed signing attempt for each slot, by slot identity.
    /// </summary>
    private Dictionary<int, string> SignErrors { get; } = [];

    /// <summary>
    ///     Gets or sets a counter that makes the browser build a fresh, empty password field after each attempt.
    /// </summary>
    private int PasswordKey { get; set; }

    /// <summary>
    ///     Gets or sets the text of the first line field of the new-thread form (blank for the whole file).
    /// </summary>
    private string NewStartLine { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the text of the last line field of the new-thread form (blank means the start line only).
    /// </summary>
    private string NewEndLine { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the comment text of the new-thread form.
    /// </summary>
    private string NewText { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the message of the last failed attempt to start a thread, or <c>null</c>.
    /// </summary>
    private string? NewThreadError { get; set; }

    /// <summary>
    ///     Gets the state the thread index is filtered to, or <c>null</c> for all threads; open by default.
    /// </summary>
    private ReviewThreadState? IndexFilter { get; set; } = ReviewThreadState.Open;

    /// <summary>
    ///     Gets the identities of the resolved threads the reader has expanded.
    /// </summary>
    private HashSet<int> ExpandedThreads { get; } = [];

    /// <summary>
    ///     Gets the identity of the thread chosen in the index (its lines are highlighted), or <c>null</c>.
    /// </summary>
    private int? FocusedThreadId { get; set; }

    /// <summary>
    ///     Gets the reply text typed for each thread, by thread identity.
    /// </summary>
    private Dictionary<int, string> ReplyTexts { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed reply or state change for each thread, by thread identity.
    /// </summary>
    private Dictionary<int, string> ReplyErrors { get; } = [];

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the review and its threads whenever the address parameters change, and clears the file
    ///     selection and everything typed.
    /// </summary>
    /// <returns>A task that completes when the review is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // Clear the previous review before the first await so the loading state is shown, and a stale
        // review is never displayed or acted on under the new address
        Loaded = false;
        Review = null;
        ProjectName = null;
        SelectedPath = null;
        SelectedLines = null;
        Threads = [];
        Checklist = null;
        Approval = null;
        Candidates = [];
        ClearNewThreadForm();
        NewSlotPrimary = string.Empty;
        NewSlotAlternate = string.Empty;
        NewSlotError = null;
        SlotPrimaryChoices.Clear();
        SlotAlternateChoices.Clear();
        SlotErrors.Clear();
        SignPasswords.Clear();
        SignErrors.Clear();
        ReplyTexts.Clear();
        ReplyErrors.Clear();
        IndexFilter = ReviewThreadState.Open;
        ExpandedThreads.Clear();
        FocusedThreadId = null;
        _scrollTarget = null;

        Review = await ReviewService.GetAsync(ProjectId, ReviewId);
        ProjectName = (await ProjectService.GetAsync(ProjectId))?.Name;
        await RefreshAsync();
        Loaded = true;
    }

    /// <summary>
    ///     Reloads everything the record depends on: the threads, the checklist, the approval status and the users
    ///     that can fill a slot.
    /// </summary>
    /// <returns>A task that completes when everything is loaded.</returns>
    private async Task RefreshAsync()
    {
        Threads = await ThreadService.ListAsync(ProjectId, ReviewId);
        Checklist = await ChecklistService.GetAsync(ProjectId, ReviewId);
        Approval = await SigningService.GetApprovalAsync(ProjectId, ReviewId);
        Candidates = await SigningService.ListCandidatesAsync();

        // Read the user name from the state, not from GetActorAsync: when the sign-in state of an open page has
        // just ended, this reload must not fail; the page is replaced by the session-ended message instead
        var principal = (await AuthenticationState.GetAuthenticationStateAsync()).User;
        CurrentUsername = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name ?? string.Empty : string.Empty;
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit, and scrolls a chosen thread into view.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <returns>A task that completes when the optional scroll has been requested.</returns>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser. The scroll
    ///     is requested once per chosen thread; a connection that is already gone is ignored.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }

        if (_scrollTarget is not null)
        {
            var target = _scrollTarget;
            _scrollTarget = null;
            try
            {
                await JS.InvokeVoidAsync("reviewCenter.scrollToTestId", target, _scrollBlock);
            }
            catch (JSDisconnectedException)
            {
                // The browser went away; there is nothing left to scroll
            }
        }
    }

    /// <summary>
    ///     Loads and shows the content of one file of the review.
    /// </summary>
    /// <param name="index">The zero-based position of the file in the displayed list.</param>
    /// <returns>A task that completes when the content is loaded.</returns>
    /// <remarks>
    ///     An index outside the list is ignored. A file that cannot be found in the store (removed since the
    ///     page was loaded) is treated as an empty file, so the empty-file notice is shown rather than failing
    ///     the circuit. Selecting a file reloads the threads and clears the new-thread form.
    /// </remarks>
    private async Task SelectFileAsync(int index)
    {
        if (Review is null || index < 0 || index >= Review.Files.Count)
        {
            return;
        }

        // Load the bytes of just this file
        var path = Review.Files[index].Path;
        var content = await ReviewService.GetFileContentAsync(ProjectId, ReviewId, path) ?? [];

        // Show text only when it is clean UTF-8; otherwise tell the operator it is binary
        var text = FileText.TryDecode(content);
        SelectedPath = path;
        SelectedLines = text is null ? null : FileText.SplitLines(text);
        FocusedThreadId = null;
        ClearNewThreadForm();
        await RefreshAsync();

        // The code is the reviewer's task, so bring it to the top of the window
        _scrollTarget = "selected-file-path";
        _scrollBlock = "start";
    }

    /// <summary>
    ///     Filters the thread index.
    /// </summary>
    /// <param name="state">The state to show, or <c>null</c> for all threads.</param>
    private void SetFilter(ReviewThreadState? state)
    {
        IndexFilter = state;
    }

    /// <summary>
    ///     Expands a collapsed resolved thread, or collapses an expanded one.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    private void ToggleThreadExpanded(int threadId)
    {
        if (!ExpandedThreads.Remove(threadId))
        {
            ExpandedThreads.Add(threadId);
        }
    }

    /// <summary>
    ///     Shows a thread chosen in the index: selects its file, highlights its lines and scrolls to it.
    /// </summary>
    /// <param name="thread">The thread chosen.</param>
    /// <returns>A task that completes when the file is shown.</returns>
    /// <remarks>
    ///     The file is only loaded when it is not already selected, so text typed in the new-thread form is kept.
    ///     A resolved thread is expanded so its comments are visible.
    /// </remarks>
    private async Task SelectThreadAsync(ReviewThread thread)
    {
        if (Review is null)
        {
            return;
        }

        if (thread.FilePath != SelectedPath)
        {
            await SelectFileAsync(Review.Files.ToList().FindIndex(file => file.Path == thread.FilePath));
        }

        FocusedThreadId = thread.Id;
        if (thread.State == ReviewThreadState.Resolved)
        {
            ExpandedThreads.Add(thread.Id);
        }

        _scrollTarget = thread.StartLine is null ? $"thread-{thread.Id}" : $"code-line-{thread.StartLine}";
        _scrollBlock = "center";
    }

    /// <summary>
    ///     Aims the new-thread form at one line and scrolls the form into view.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    private void CommentOnLine(int line)
    {
        NewStartLine = line.ToString(CultureInfo.InvariantCulture);
        NewEndLine = NewStartLine;
        _scrollTarget = "new-thread-form";
        _scrollBlock = "center";
    }

    /// <summary>
    ///     Starts a thread on the selected file from the new-thread form.
    /// </summary>
    /// <returns>A task that completes when the thread is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Both line fields blank anchors the whole file; only a start line anchors that single line. On a
    ///     rule violation the message is shown next to the form and everything typed stays, so it can be fixed.
    ///     Success clears the text and the line fields.
    /// </remarks>
    private async Task StartThreadAsync()
    {
        if (SelectedPath is null)
        {
            return;
        }

        if (!TryParseLine(NewStartLine, out var start) || !TryParseLine(NewEndLine, out var end))
        {
            NewThreadError = "Line numbers must be whole numbers.";
            return;
        }

        try
        {
            await ThreadService.StartThreadAsync(
                ProjectId, ReviewId, SelectedPath, start, end ?? start, await AuthenticationState.GetActorAsync(), NewText);
        }
        catch (DomainRuleException ex)
        {
            NewThreadError = ex.Message;
            return;
        }

        NewThreadError = null;
        NewText = string.Empty;
        NewStartLine = string.Empty;
        NewEndLine = string.Empty;
        await RefreshAsync();
    }

    /// <summary>
    ///     Adds the typed reply to a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>A task that completes when the reply is stored or the problem is shown.</returns>
    private async Task ReplyAsync(int threadId)
    {
        try
        {
            await ThreadService.ReplyAsync(
                ProjectId, ReviewId, threadId, await AuthenticationState.GetActorAsync(),
                ReplyTexts.GetValueOrDefault(threadId));
        }
        catch (DomainRuleException ex)
        {
            ReplyErrors[threadId] = ex.Message;
            return;
        }

        ReplyErrors.Remove(threadId);
        ReplyTexts.Remove(threadId);
        await RefreshAsync();
    }

    /// <summary>
    ///     Resolves an open thread or reopens a resolved one.
    /// </summary>
    /// <param name="thread">The thread as shown.</param>
    /// <returns>A task that completes when the state is stored or the problem is shown.</returns>
    private async Task ToggleThreadAsync(ReviewThread thread)
    {
        try
        {
            if (thread.State == ReviewThreadState.Open)
            {
                await ThreadService.ResolveAsync(ProjectId, ReviewId, thread.Id);
                ExpandedThreads.Remove(thread.Id);
            }
            else
            {
                await ThreadService.ReopenAsync(ProjectId, ReviewId, thread.Id);
            }
        }
        catch (DomainRuleException ex)
        {
            ReplyErrors[thread.Id] = ex.Message;
            return;
        }

        ReplyErrors.Remove(thread.Id);
        await RefreshAsync();
    }

    /// <summary>
    ///     Adds a reviewer slot from the add-slot form.
    /// </summary>
    /// <returns>A task that completes when the slot is stored or the problem is shown.</returns>
    private async Task AddSlotAsync()
    {
        if (ParseUserId(NewSlotPrimary) is not { } primary)
        {
            NewSlotError = "Choose a primary reviewer.";
            return;
        }

        try
        {
            await SigningService.AddSlotAsync(
                await AuthenticationState.GetActorAsync(), ProjectId, ReviewId, primary, ParseUserId(NewSlotAlternate));
        }
        catch (DomainRuleException ex)
        {
            NewSlotError = ex.Message;
            return;
        }

        NewSlotError = null;
        NewSlotPrimary = string.Empty;
        NewSlotAlternate = string.Empty;
        await RefreshAsync();
    }

    /// <summary>
    ///     Saves the primary and alternate chosen for an unsigned slot.
    /// </summary>
    /// <param name="slot">The slot as shown.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    private async Task SaveSlotAsync(SlotStatus slot)
    {
        try
        {
            if (ParseUserId(PrimaryChoice(slot)) is not { } primary)
            {
                throw new DomainRuleException(DomainRule.SlotUserInvalid, "Choose a primary reviewer.");
            }

            await SigningService.UpdateSlotAsync(
                await AuthenticationState.GetActorAsync(), ProjectId, ReviewId, slot.SlotId, primary, ParseUserId(AlternateChoice(slot)));
        }
        catch (DomainRuleException ex)
        {
            SlotErrors[slot.SlotId] = ex.Message;
            return;
        }

        SlotErrors.Remove(slot.SlotId);
        SlotPrimaryChoices.Remove(slot.SlotId);
        SlotAlternateChoices.Remove(slot.SlotId);
        await RefreshAsync();
    }

    /// <summary>
    ///     Removes an unsigned slot.
    /// </summary>
    /// <param name="slotId">The identity of the slot.</param>
    /// <returns>A task that completes when the slot is removed or the problem is shown.</returns>
    private async Task RemoveSlotAsync(int slotId)
    {
        try
        {
            await SigningService.RemoveSlotAsync(await AuthenticationState.GetActorAsync(), ProjectId, ReviewId, slotId);
        }
        catch (DomainRuleException ex)
        {
            SlotErrors[slotId] = ex.Message;
            return;
        }

        SlotErrors.Remove(slotId);
        await RefreshAsync();
    }

    /// <summary>
    ///     Signs the review for a slot with the typed password, for the record digest that was on screen.
    /// </summary>
    /// <param name="slotId">The identity of the slot.</param>
    /// <returns>A task that completes when the signature is stored or the problem is shown.</returns>
    /// <remarks>
    ///     The password is cleared after every attempt, whatever its outcome, and the field is rebuilt empty. It is
    ///     only passed to the service, never kept, logged or rendered.
    /// </remarks>
    private async Task SignAsync(int slotId)
    {
        try
        {
            await SigningService.SignAsync(
                await AuthenticationState.GetActorAsync(),
                ProjectId,
                ReviewId,
                slotId,
                Approval!.Digest,
                SignPasswords.GetValueOrDefault(slotId));
            SignErrors.Remove(slotId);
        }
        catch (DomainRuleException ex)
        {
            SignErrors[slotId] = ex.Message;
        }
        finally
        {
            SignPasswords.Remove(slotId);
            PasswordKey++;
        }

        await RefreshAsync();
    }

    /// <summary>
    ///     Gets the primary reviewer currently chosen in the edit fields of a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The chosen user id as text; the stored primary until the operator changes it.</returns>
    private string PrimaryChoice(SlotStatus slot)
    {
        return SlotPrimaryChoices.GetValueOrDefault(slot.SlotId) ?? slot.Primary.UserId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Gets the alternate reviewer currently chosen in the edit fields of a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The chosen user id as text, or blank for none; the stored alternate until the operator changes it.</returns>
    private string AlternateChoice(SlotStatus slot)
    {
        return SlotAlternateChoices.GetValueOrDefault(slot.SlotId)
               ?? slot.Alternate?.UserId.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    ///     Tells whether the signed-in user is the primary or the alternate of a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns><c>true</c> when the user occupies the slot.</returns>
    private bool IsOccupant(SlotStatus slot)
    {
        return string.Equals(slot.Primary.Username, CurrentUsername, StringComparison.OrdinalIgnoreCase)
               || (slot.Alternate is not null
                   && string.Equals(slot.Alternate.Username, CurrentUsername, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Words the status of a slot.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>"Not signed", "Signed by Ann on 2026-01-02 03:04 UTC" or "Signature out of date".</returns>
    private static string SlotStatusLabel(SlotStatus slot)
    {
        return slot.State switch
        {
            SlotSignatureState.Signed => $"Signed by {slot.SignerName} on {FormatTime(slot.SignedAt!.Value)}",
            SlotSignatureState.OutOfDate => "Signature out of date",
            _ => "Not signed"
        };
    }

    /// <summary>
    ///     Gets the extra CSS class of a slot's status badge.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The class name, or null for none.</returns>
    private static string? SlotBadgeClass(SlotStatus slot)
    {
        return slot.State switch
        {
            SlotSignatureState.Signed => "approved",
            SlotSignatureState.OutOfDate => "outdated",
            _ => null
        };
    }

    /// <summary>
    ///     Parses the user id of a user selection.
    /// </summary>
    /// <param name="text">The option value.</param>
    /// <returns>The user id, or <c>null</c> when nobody is chosen.</returns>
    private static int? ParseUserId(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    /// <summary>
    ///     Shortens a record digest for display; the full value stays available in the details.
    /// </summary>
    /// <param name="digest">The full digest.</param>
    /// <returns>The first 16 characters.</returns>
    private static string ShortDigest(string digest)
    {
        return digest.Length > 16 ? digest[..16] : digest;
    }

    /// <summary>
    ///     Words the number of comments of a thread.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>"1 comment" or, for example, "3 comments".</returns>
    private static string CommentCountLabel(ReviewThread thread)
    {
        return thread.Comments.Count == 1 ? "1 comment" : $"{thread.Comments.Count} comments";
    }

    /// <summary>
    ///     Words the message shown when the thread index has no rows, from the current filter.
    /// </summary>
    /// <returns>"No threads yet.", "No open threads." or "No resolved threads.".</returns>
    private string IndexEmptyMessage()
    {
        return IndexFilter switch
        {
            ReviewThreadState.Open => "No open threads.",
            ReviewThreadState.Resolved => "No resolved threads.",
            _ => "No threads yet."
        };
    }

    /// <summary>
    ///     Tells whether a line is inside the anchor of the thread chosen in the index.
    /// </summary>
    /// <param name="focused">The chosen thread when it is on the selected file, otherwise <c>null</c>.</param>
    /// <param name="line">The 1-based line number.</param>
    /// <returns><c>true</c> when the line is covered by the chosen thread.</returns>
    private static bool IsFocusedLine(ReviewThread? focused, int line)
    {
        return focused?.StartLine is not null && line >= focused.StartLine && line <= focused.EndLine;
    }

    /// <summary>
    ///     Counts the open threads anchored to a file and words the count for display.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>For example "0 open threads" or "1 open thread".</returns>
    private string OpenThreadLabel(string path)
    {
        var count = Threads.Count(t => t.State == ReviewThreadState.Open && t.FilePath == path);
        return count == 1 ? "1 open thread" : $"{count} open threads";
    }

    /// <summary>
    ///     Describes where the new thread would be anchored, from the current line fields.
    /// </summary>
    /// <returns>A short sentence such as "Comment on lines 3-5"; the whole file when the fields are blank or unusable.</returns>
    private string NewThreadTarget()
    {
        if (!TryParseLine(NewStartLine, out var start) || !TryParseLine(NewEndLine, out var end) || start is null)
        {
            return "Comment on the whole file";
        }

        return end is null || end == start ? $"Comment on line {start}" : $"Comment on lines {start}-{end}";
    }

    /// <summary>
    ///     Describes the anchor of a thread for display.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>"Whole file", "Line 4" or "Lines 4-9".</returns>
    private static string AnchorLabel(ReviewThread thread)
    {
        if (thread.StartLine is null)
        {
            return "Whole file";
        }

        return thread.EndLine == thread.StartLine ? $"Line {thread.StartLine}" : $"Lines {thread.StartLine}-{thread.EndLine}";
    }

    /// <summary>
    ///     Words the accessible name of a thread index row, carrying the same facts as the visible cells.
    /// </summary>
    /// <param name="thread">The thread of the row.</param>
    /// <returns>For example "Show Line 4 of src/a.cs, Open, 2 comments, first by Ann, last activity 2026-01-02 03:04 UTC".</returns>
    private static string IndexRowLabel(ReviewThread thread)
    {
        return $"Show {AnchorLabel(thread)} of {thread.FilePath}, {thread.State}, {CommentCountLabel(thread)}, " +
               $"first by {thread.Comments.FirstOrDefault()?.Author}, last activity {FormatTime(thread.LastActivity)}";
    }

    /// <summary>
    ///     Words the checklist progress for the checklist card.
    /// </summary>
    /// <returns>For example "37 of 58 checks answered", or "Checklist complete" when nothing is left to answer.</returns>
    private string ChecklistSummary()
    {
        var overall = Checklist!.Overall;
        return overall.IsDone ? "Checklist complete" : $"{overall.Answered} of {overall.Total} checks answered";
    }

    /// <summary>
    ///     Formats a comment time for display.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The UTC time as "yyyy-MM-dd HH:mm UTC".</returns>
    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Parses an optional line number field.
    /// </summary>
    /// <param name="text">The field text.</param>
    /// <param name="line">The number, or <c>null</c> when the field is blank.</param>
    /// <returns><c>false</c> when the field is not blank and not a whole number.</returns>
    private static bool TryParseLine(string text, out int? line)
    {
        line = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        line = number;
        return true;
    }

    /// <summary>
    ///     Empties the new-thread form fields and its message.
    /// </summary>
    private void ClearNewThreadForm()
    {
        NewStartLine = string.Empty;
        NewEndLine = string.Empty;
        NewText = string.Empty;
        NewThreadError = null;
    }
}
