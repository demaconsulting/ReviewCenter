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
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
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
///     <see cref="SigningService"/>; the signed PDF attestation of an approved review is offered as a download, when
///     the key that signs it is usable, through the key status of <see cref="IAttestationKeyProvider"/>); the rules live there and a violation is shown next to the form that
///     caused it. File bytes are loaded one file at a time when the operator selects it, never with the file
///     list. A file is shown as numbered lines only when it is valid UTF-8 without NUL bytes (see
///     <see cref="FileText"/>); anything else is reported as a binary file rather than rendered as garbage.
///     All text from the files and the comments is rendered as plain text, never as markup. The author of a
///     comment is the signed-in user (an <see cref="Actor"/> read from the authentication state), never
///     something typed on the page. Threads are read when the page loads and after each change made here; comments posted
///     in other browsers appear on the next load or change. Each browser connection (circuit) gets its own
///     page instance, so the state here is per-user and needs no synchronization. The thread index lists
///     the threads of every file through <see cref="ThreadService.BuildIndex"/>; choosing a row selects its
///     file, highlights its lines and asks the browser to scroll them into view. The page can also be opened at a
///     file, a line of a file or a thread through a stable address (see <see cref="ReviewLinks"/>); choosing a file
///     or thread replaces the address in the address bar by the matching link, without a history entry. Every
///     thread shows a "Link" to its address and a "Copy link" button that copies the absolute address.
/// </remarks>
public partial class ReviewPage : ComponentBase, IDisposable
{
    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     How long the "Copied" or "Copy failed" feedback of the "Copy link" button stays visible after the
    ///     latest click on that thread.
    /// </summary>
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     The outcome of the last "Copy link" click, by thread identity; a thread without an entry shows none.
    /// </summary>
    private readonly Dictionary<int, CopyFeedback> _copyFeedback = [];

    /// <summary>
    ///     The pending feedback timer of each thread, by thread identity; a new click replaces and cancels the
    ///     previous one, so the latest click always gets the full duration.
    /// </summary>
    private readonly Dictionary<int, CancellationTokenSource> _copyTimers = [];

    /// <summary>
    ///     The outcome of a "Copy link" click, shown briefly next to the button.
    /// </summary>
    private enum CopyFeedback
    {
        /// <summary>No feedback is shown.</summary>
        None,

        /// <summary>The address was copied.</summary>
        Copied,

        /// <summary>The browser refused the copy.</summary>
        Failed
    }

    /// <summary>
    ///     The test id of the element to scroll into view after the next render, or <c>null</c>.
    /// </summary>
    private string? _scrollTarget;

    /// <summary>
    ///     The test id of the element that gets the keyboard focus after the next render (the comment field of the
    ///     composer that was just opened), or <c>null</c>.
    /// </summary>
    private string? _focusTarget;

    /// <summary>
    ///     The scroll position that puts the element in the middle of the window.
    /// </summary>
    private const string ScrollCenter = "center";

    /// <summary>
    ///     The key of the confirmation for the new-thread form.
    /// </summary>
    private const string NewThreadWarningKey = "new-thread";

    /// <summary>
    ///     Where the element of <see cref="_scrollTarget"/> lands in the window: <c>start</c> or <c>center</c>.
    /// </summary>
    private string _scrollBlock = ScrollCenter;

    /// <summary>
    ///     The address to show in the browser's address bar after the next render, or <c>null</c>.
    /// </summary>
    private string? _pendingUrl;

    /// <summary>
    ///     The highest comment identity the user had seen when the review was opened; comments by others above it
    ///     are shown as new for the lifetime of the page.
    /// </summary>
    private int _unreadBaselineCommentId;

    /// <summary>
    ///     The time (UTC) read before the review was loaded, remembered as the time the user saw it.
    /// </summary>
    private DateTimeOffset _seenAt;

    /// <summary>
    ///     Whether the review was loaded and the read state still has to be stored after the next render on a live
    ///     circuit.
    /// </summary>
    private bool _markSeenPending;

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
    ///     Gets or sets the path of the file to open, from the <c>.../files/{path}</c> address; already decoded by the
    ///     router, so it is used as it is. <c>null</c> or empty for the other addresses.
    /// </summary>
    [Parameter]
    public string? FilePath { get; set; }

    /// <summary>
    ///     Gets or sets the identity of the thread to open, from the <c>.../threads/{id}</c> address.
    /// </summary>
    [Parameter]
    public int? ThreadId { get; set; }

    /// <summary>
    ///     Gets or sets the <c>line</c> query value of a file address: the line to scroll to and highlight.
    /// </summary>
    /// <remarks>Kept as text so that a value that is not a number is reported on the page rather than failing it.</remarks>
    [SupplyParameterFromQuery(Name = "line")]
    public string? LineText { get; set; }

    /// <summary>
    ///     Gets or sets the <c>side</c> query value of a file address: <c>head</c> (the default) or <c>baseline</c>.
    /// </summary>
    [SupplyParameterFromQuery(Name = "side")]
    public string? SideText { get; set; }

    /// <summary>
    ///     Gets or sets the <c>reauth</c> query value: how the last confirmation of identity at the identity provider
    ///     ended (<c>ok</c>, <c>failed</c>, <c>missing-auth-time</c> or <c>stale-auth-time</c>), put there by the
    ///     sign-in endpoint when it sends the browser back.
    /// </summary>
    [SupplyParameterFromQuery(Name = "reauth")]
    public string? ReauthText { get; set; }

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
    ///     Gets or sets the service that reads and changes users, used to tell whether the signed-in user signs in
    ///     with an identity provider, supplied by dependency injection.
    /// </summary>
    [Inject]
    private UserService Users { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service of the optional second factor, supplied by dependency injection; it tells whether
    ///     the signed-in user must give a code to sign.
    /// </summary>
    [Inject]
    private TotpService Totp { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the settings of sign-in with an identity provider, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IOptions<OidcOptions> Oidc { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the supplier of the attestation signing key, whose status decides whether the attestation
    ///     download is offered, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IAttestationKeyProvider AttestationKeys { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the browser script access used to scroll a thread into view, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the navigation manager used to make a thread address absolute, supplied by dependency injection.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the clock that times the "Copy link" feedback, supplied by dependency injection.
    /// </summary>
    [Inject]
    private TimeProvider Time { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the project access rules that every action re-checks, supplied by dependency injection.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the inbox service that reads and stores what the user has seen of this review, supplied by
    ///     dependency injection.
    /// </summary>
    [Inject]
    private InboxService Inbox { get; set; } = null!;

    /// <summary>
    ///     Writes the note that tells what kind of key signs the attestation.
    /// </summary>
    /// <param name="key">The description of the signing key.</param>
    /// <returns>
    ///     A warning that the key is self-signed and not an organization key (worded by the provider: generated by this
    ///     server, or supplied by the administrator), or the subject and the start of the
    ///     thumbprint of the certificate.
    /// </returns>
    private static string AttestationNote(AttestationKeyInfo key)
    {
        if (!key.IsSelfSigned)
        {
            return $"Signed with certificate {key.Subject} ({key.Thumbprint[..Math.Min(8, key.Thumbprint.Length)]}).";
        }

        // Only the SelfSigned provider generates the key; a self-signed certificate from the certificate store or from a
        // file (any other provider) was supplied by the administrator
        return string.Equals(key.Provider, AttestationOptions.SelfSignedProvider, StringComparison.OrdinalIgnoreCase)
            ? "Signed with a self-signed key generated by this server - not an organization key."
            : "Signed with a self-signed certificate supplied by the administrator - not an organization key and not issued by a certificate authority.";
    }

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
    ///     Gets the identifiers of the reviews that follow this one up, oldest first; empty when there are none.
    /// </summary>
    private IReadOnlyList<string> FollowUps { get; set; } = [];

    /// <summary>
    ///     Gets the path of the file whose content is shown, or <c>null</c> when no file is selected.
    /// </summary>
    private string? SelectedPath { get; set; }

    /// <summary>
    ///     Gets the lines of the selected file, or <c>null</c> when no file is selected or it is binary.
    /// </summary>
    private IReadOnlyList<string>? SelectedLines { get; set; }

    /// <summary>
    ///     Gets the change kind of the selected file; <see cref="ChangeKind.Added"/> for an upload.
    /// </summary>
    private ChangeKind SelectedKind { get; set; } = ChangeKind.Added;

    /// <summary>
    ///     The ways the text of a changed file can be shown.
    /// </summary>
    private enum FileView
    {
        /// <summary>
        ///     The changes between baseline and head, with the threads under their lines.
        /// </summary>
        Diff,

        /// <summary>
        ///     The head text as numbered lines.
        /// </summary>
        Head,

        /// <summary>
        ///     The frozen baseline text as numbered lines, read-only.
        /// </summary>
        Baseline
    }

    /// <summary>
    ///     Gets which version of the selected file is shown.
    /// </summary>
    private FileView CurrentView { get; set; } = FileView.Head;

    /// <summary>
    ///     Gets a value indicating whether the baseline (read-only) text of the selected file is shown instead of the head text.
    /// </summary>
    private bool BaselineView => CurrentView == FileView.Baseline;

    /// <summary>
    ///     Gets a value indicating whether the diff of the selected file is the chosen view.
    /// </summary>
    private bool ShowingDiff => CurrentView == FileView.Diff;

    /// <summary>
    ///     Gets a value indicating whether the diff of the selected file is shown as rows with comment controls.
    /// </summary>
    private bool HasLineDiff => ShowingDiff && Diff is { Status: FileDiffStatus.Diff, Result: not null };

    /// <summary>
    ///     Gets a value indicating whether the selected file has a diff: a file of a repository review that froze
    ///     its baseline commit. Files of uploads and of reviews without a baseline commit have none.
    /// </summary>
    private bool CanCompare => Review is { IsFromRepository: true, BaselineSha: not null };

    /// <summary>
    ///     Gets a value indicating whether the selected file has a head version to read: every kind except deleted.
    /// </summary>
    private bool HasHeadVersion => SelectedKind != ChangeKind.Deleted;

    /// <summary>
    ///     Gets a value indicating whether the selected file has a baseline version to read: a file of a review
    ///     with a baseline commit, except an added file, which did not exist at the baseline.
    /// </summary>
    private bool HasBaselineVersion => CanCompare && SelectedKind != ChangeKind.Added;

    /// <summary>
    ///     Gets the diff of the selected file, or <c>null</c> when the file has no baseline to compare against.
    /// </summary>
    /// <remarks>It is computed once when the file is selected and kept until another file is selected.</remarks>
    private FileDiff? Diff { get; set; }

    /// <summary>
    ///     Gets the version of the file the line numbers of the new-thread form refer to.
    /// </summary>
    private ThreadSide NewSide { get; set; } = ThreadSide.Head;

    /// <summary>
    ///     Gets the lines of the baseline of the selected file, or <c>null</c> when it is not loaded or is binary.
    /// </summary>
    private IReadOnlyList<string>? BaselineLines { get; set; }

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
    ///     Gets or sets a value indicating whether the project requires that the author of a review does not review it.
    /// </summary>
    private bool RequireIndependent { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the project does not let the person who reopened a review sign it
    ///     again.
    /// </summary>
    private bool RequireNonReopener { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user reopened this review while the project does not let a
    ///     reopener sign it again, so the user is told so instead of being offered the signing form.
    /// </summary>
    /// <remarks>
    ///     This is a per-viewer fact, so it is not one of the blockers of the approval, which are the same for everyone.
    /// </remarks>
    private bool ReopenerBlocked =>
        RequireNonReopener
        && CurrentUsername.Length > 0
        && Approval?.Reopens is { } reopens
        && reopens.Any(reopen => string.Equals(reopen.ReopenedBy, CurrentUsername, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Gets or sets a value indicating whether the signed-in user may reopen a locked review (a project or system
    ///     administrator).
    /// </summary>
    private bool CanReopen { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the signed-in user may only read this project (an auditor): every
    ///     control that changes something (comments, replies, resolving, reviewers, signing) is then left out.
    /// </summary>
    private bool ReadOnly { get; set; }

    /// <summary>
    ///     Gets or sets the reason typed in the reopen form.
    /// </summary>
    private string ReopenReason { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the message shown when the review could not be reopened, or <c>null</c>.
    /// </summary>
    private string? ReopenError { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the signed-in user may change the due date of the review (a project
    ///     or system administrator, or the creator of the review).
    /// </summary>
    private bool CanSetDueDate { get; set; }

    /// <summary>
    ///     Gets or sets the text of the due date input (<c>yyyy-MM-dd</c>); empty for none.
    /// </summary>
    private string DueDateInput { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the message shown when the due date could not be saved, or <c>null</c>.
    /// </summary>
    private string? DueDateError { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the due date edit form is shown: allowed for the user, and the review is
    ///     not locked.
    /// </summary>
    private bool CanEditDueDate => CanSetDueDate && !IsLocked;

    /// <summary>
    ///     Gets a value indicating whether the review is overdue: its due date is before today (UTC) and it is not concluded.
    /// </summary>
    private bool IsOverdue => Review is not null && ReviewService.IsOverdue(Review, IsLocked);

    /// <summary>
    ///     The key of the confirmation for a change of the review conclusion text.
    /// </summary>
    private const string ConclusionWarningKey = "conclusion";

    /// <summary>
    ///     Gets or sets a value indicating whether the signed-in user may write the review conclusion text (a reviewer of
    ///     the review, or its lead).
    /// </summary>
    private bool CanWriteConclusion { get; set; }

    /// <summary>
    ///     Gets or sets the text of the conclusion editor.
    /// </summary>
    private string ConclusionInput { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the message shown when the conclusion could not be saved, or <c>null</c>.
    /// </summary>
    private string? ConclusionError { get; set; }

    /// <summary>
    ///     Writes a due date for display and for the date input.
    /// </summary>
    /// <param name="dueDate">The due date.</param>
    /// <returns>The date as <c>yyyy-MM-dd</c>.</returns>
    private static string FormatDueDate(DateOnly dueDate) => dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Gets a value indicating whether the review is concluded (approved or rejected) and therefore locked.
    /// </summary>
    private bool IsLocked => Approval?.IsConcluded == true;

    /// <summary>
    ///     Gets the label of the status badge: "Approved", "Rejected" or "In review".
    /// </summary>
    private string ConclusionLabel => Approval?.Conclusion switch
    {
        ReviewConclusion.Approved => "Approved",
        ReviewConclusion.Rejected => "Rejected",
        _ => "In review"
    };

    /// <summary>
    ///     Gets the extra CSS class of the status badge, or <c>null</c> while the review is in review.
    /// </summary>
    private string? ConclusionBadgeClass => Approval?.Conclusion switch
    {
        ReviewConclusion.Approved => "approved",
        ReviewConclusion.Rejected => "rejected",
        _ => null
    };

    /// <summary>
    ///     Gets the outcome a signature given now attests: the agreed outcome of the ballots, approved when there is none.
    /// </summary>
    private ReviewOutcome SigningOutcome => Approval?.Outcome ?? ReviewOutcome.Approved;

    /// <summary>
    ///     Gets the words of the outcome a signature given now attests, shown above the signing form.
    /// </summary>
    private string SigningOutcomeLabel => SigningOutcome == ReviewOutcome.Rejected ? "REJECTED" : "APPROVED";

    /// <summary>
    ///     Gets a value indicating whether the conclusion editor is shown: the user may write the text, may change the
    ///     review, and the review is not locked.
    /// </summary>
    private bool ShowConclusionEditor => CanWriteConclusion && !ReadOnly && !IsLocked;

    /// <summary>
    ///     Gets the value of the <c>data-locked</c> attribute of a control that is disabled by the lock.
    /// </summary>
    private string LockedMarker => IsLocked ? "true" : "false";

    /// <summary>
    ///     Gets the users that are offered for a slot: every candidate, except the author of the review when the
    ///     project requires independent reviewers.
    /// </summary>
    private IEnumerable<SlotMember> VisibleCandidates =>
        RequireIndependent && Review is not null
            ? Candidates.Where(c => !string.Equals(c.Username, Review.CreatedBy, StringComparison.OrdinalIgnoreCase))
            : Candidates;

    /// <summary>
    ///     Gets a value indicating whether to tell the signed-in user that they cannot review their own review.
    /// </summary>
    private bool ShowAuthorHint =>
        RequireIndependent
        && Review is not null
        && CurrentUsername.Length > 0
        && string.Equals(CurrentUsername, Review.CreatedBy, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Gets the user name of the signed-in user, used to tell which slots the user occupies.
    /// </summary>
    private string CurrentUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the number of comments by others that the user has not seen before this visit.
    /// </summary>
    private int UnreadCount => CurrentUsername.Length == 0
        ? 0
        : InboxService.CountUnread(Threads, CurrentUsername, _unreadBaselineCommentId);

    /// <summary>
    ///     Gets the sentence that tells how many comments are new to the user.
    /// </summary>
    private string UnreadSummary => UnreadCount == 1
        ? "1 comment by someone else is new since your last visit."
        : $"{UnreadCount} comments by others are new since your last visit.";

    /// <summary>
    ///     Reports whether a thread has a comment that is new to the user.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns><c>true</c> when a comment of the thread is by someone else and newer than the last visit.</returns>
    private bool ThreadHasUnread(ReviewThread thread)
    {
        return CurrentUsername.Length > 0
               && InboxService.CountUnread([thread], CurrentUsername, _unreadBaselineCommentId) > 0;
    }

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
    ///     Gets the authenticator code typed into each sign form, by slot identity; removed after every attempt.
    /// </summary>
    private Dictionary<int, string> SignTotpCodes { get; } = [];

    /// <summary>
    ///     Gets a value indicating whether the signed-in user has a second factor, so that a signature also needs a
    ///     current code from the authenticator app.
    /// </summary>
    private bool CurrentHasTotp { get; set; }

    /// <summary>
    ///     Gets the message of the last failed signing attempt for each slot, by slot identity.
    /// </summary>
    private Dictionary<int, string> SignErrors { get; } = [];

    /// <summary>
    ///     Gets or sets a counter that makes the browser build a fresh, empty password field after each attempt.
    /// </summary>
    private int PasswordKey { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the signed-in user has no password because the account belongs to an
    ///     identity provider; such a user confirms a signature by signing in again at the provider.
    /// </summary>
    private bool CurrentIsExternal { get; set; }

    /// <summary>
    ///     Gets the slots for which the signed-in user has confirmed their identity at the identity provider and not
    ///     yet signed.
    /// </summary>
    private HashSet<int> ConfirmedSlots { get; } = [];

    /// <summary>
    ///     Gets or sets a value indicating whether the user has acted on the page since a failed confirmation was
    ///     reported, so the report is not shown again.
    /// </summary>
    private bool ReauthReportDismissed { get; set; }

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
    ///     Gets the line of the selected file that an address points at (highlighted), or <c>null</c>.
    /// </summary>
    private int? HighlightLine { get; set; }

    /// <summary>
    ///     Gets which numbering <see cref="HighlightLine"/> uses.
    /// </summary>
    private ThreadSide HighlightSide { get; set; } = ThreadSide.Head;

    /// <summary>
    ///     Gets the friendly message shown when the address names a file, line or thread that cannot be shown, or
    ///     <c>null</c>.
    /// </summary>
    private string? DeepLinkMessage { get; set; }

    /// <summary>
    ///     Gets the reply text typed for each thread, by thread identity.
    /// </summary>
    private Dictionary<int, string> ReplyTexts { get; } = [];

    /// <summary>
    ///     Gets the message of the last failed reply or state change for each thread, by thread identity.
    /// </summary>
    private Dictionary<int, string> ReplyErrors { get; } = [];

    /// <summary>
    ///     Gets the change that waits for the person to confirm that it makes signatures out of date, or
    ///     <c>null</c>; only one such change waits at a time.
    /// </summary>
    private PendingSignatureWarning? Warning { get; set; }

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
        ResetState();
        Loaded = false;

        // The time is read before anything is loaded, so a change made while the page loads stays new
        _seenAt = Time.GetUtcNow();

        // A project the user may not see is treated like a missing one: nothing of its review is loaded
        var project = await ProjectService.GetAsync(await AuthenticationState.GetActorOrNullAsync(), ProjectId);
        ProjectName = project?.Name;
        RequireIndependent = project?.RequireIndependentReviewers == true;
        RequireNonReopener = project?.RequireNonReopenerSigning == true;
        Review = project is null ? null : await ReviewService.GetAsync(ProjectId, ReviewId);
        DueDateInput = Review?.DueDate is { } due ? FormatDueDate(due) : string.Empty;
        ConclusionInput = Review?.ReviewConclusionText ?? string.Empty;
        FollowUps = Review is null
            ? []
            : (await ReviewService.ListAsync(ProjectId))
                .Where(other => string.Equals(other.FollowsReviewId, ReviewId, StringComparison.Ordinal))
                .OrderBy(other => other.Created)
                .Select(other => other.Id)
                .ToList();
        await RefreshAsync();

        // What the user had seen before this visit becomes the baseline of the New markers; the new state is stored
        // after the first render on a live circuit (see OnAfterRenderAsync), not here, because this method also runs
        // while the page is rendered on the server before the connection is live
        var viewer = await AuthenticationState.GetActorOrNullAsync();
        if (Review is not null && viewer is not null)
        {
            _unreadBaselineCommentId = await Inbox.GetReviewUnreadAsync(viewer, ProjectId, ReviewId);
            _markSeenPending = true;
        }

        // Open what the address names (a file, a line of it, or a thread); the address stays as it is
        await ApplyAddressAsync();
        Loaded = true;
    }

    /// <summary>
    ///     Re-checks that the signed-in user may still view the project, at the start of an action on the open page.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on; <c>false</c> when the access is gone, in which case the page has
    ///     already dropped everything it loaded and shows the not-found state, and the action must stop.
    /// </returns>
    private Task<bool> GuardAsync()
    {
        return Access.GuardViewAsync(AuthenticationState, ProjectId, ShowNoAccess);
    }

    /// <summary>
    ///     Re-checks the access for a child component that asks before it shows loaded data, and renders this page
    ///     when the access is gone.
    /// </summary>
    /// <returns><c>true</c> when the child may go on; <c>false</c> when the page has dropped its data.</returns>
    /// <remarks>
    ///     A child that calls a plain delegate does not make this page render, as an event callback would, so the page
    ///     asks for the render itself; otherwise the child would stay on screen with what it already holds.
    /// </remarks>
    private async Task<bool> GuardChildAsync()
    {
        var allowed = await GuardAsync();
        if (!allowed)
        {
            StateHasChanged();
        }

        return allowed;
    }

    /// <summary>
    ///     Runs an action that only changes what the page shows of already loaded data, after the access re-check.
    /// </summary>
    /// <param name="action">The action to run while the user may still view the project.</param>
    /// <returns>A task that completes when the action has run or the page has dropped its data.</returns>
    private async Task GuardedAsync(Action action)
    {
        if (await GuardAsync())
        {
            action();
        }
    }

    /// <summary>
    ///     Runs an action that loads or shows project data, after the access re-check.
    /// </summary>
    /// <param name="action">The action to run while the user may still view the project.</param>
    /// <returns>A task that completes when the action has run or the page has dropped its data.</returns>
    private async Task GuardedAsync(Func<Task> action)
    {
        if (await GuardAsync())
        {
            await action();
        }
    }

    /// <summary>
    ///     Drops everything loaded and shows the same "does not exist" state as for a non-member.
    /// </summary>
    private void ShowNoAccess()
    {
        ResetState();
        Loaded = true;
    }

    /// <summary>
    ///     Forgets the review and everything loaded or typed for it.
    /// </summary>
    private void ResetState()
    {
        Review = null;
        FollowUps = [];
        ProjectName = null;
        SelectedPath = null;
        SelectedLines = null;
        SelectedKind = ChangeKind.Added;
        CurrentView = FileView.Head;
        BaselineLines = null;
        Diff = null;
        NewSide = ThreadSide.Head;
        Threads = [];
        Checklist = null;
        Approval = null;
        Candidates = [];
        ClearNewThreadForm();
        RequireIndependent = false;
        RequireNonReopener = false;
        CanReopen = false;
        ReadOnly = false;
        ReopenReason = string.Empty;
        ReopenError = null;
        CanSetDueDate = false;
        DueDateInput = string.Empty;
        DueDateError = null;
        CanWriteConclusion = false;
        ConclusionInput = string.Empty;
        ConclusionError = null;
        NewSlotPrimary = string.Empty;
        NewSlotAlternate = string.Empty;
        NewSlotError = null;
        SlotPrimaryChoices.Clear();
        SlotAlternateChoices.Clear();
        SlotErrors.Clear();
        SignPasswords.Clear();
        SignTotpCodes.Clear();
        SignErrors.Clear();
        ReplyTexts.Clear();
        ReplyErrors.Clear();
        Warning = null;
        IndexFilter = ReviewThreadState.Open;
        ExpandedThreads.Clear();
        FocusedThreadId = null;
        HighlightLine = null;
        DeepLinkMessage = null;
        _scrollTarget = null;
        _focusTarget = null;
        _pendingUrl = null;
        _unreadBaselineCommentId = 0;
        _markSeenPending = false;
    }

    /// <summary>
    ///     Opens the file or thread that the page address names, or explains why it cannot be shown.
    /// </summary>
    /// <returns>A task that completes when the target is shown.</returns>
    /// <remarks>
    ///     A thread address opens the file of the thread at its anchor and highlights the thread. A file address
    ///     opens the file and, with a <c>line</c> (and optionally a <c>side</c>), highlights that line. The values
    ///     are never trusted: an unknown file or thread, a thread of another review, a line outside the file, a
    ///     baseline line of a file without a baseline or a value that is not a number each give a message on the
    ///     page (with the file list still available) rather than an error. A review address names nothing.
    /// </remarks>
    private async Task ApplyAddressAsync()
    {
        if (Review is null)
        {
            return;
        }

        if (ThreadId is { } threadId)
        {
            // The threads loaded are those of this review only, so a thread of another review is simply unknown
            var thread = Threads.FirstOrDefault(t => t.Id == threadId);
            if (thread is null)
            {
                DeepLinkMessage = $"Thread #{threadId} doesn't exist in this review.";
            }
            else if (!thread.IsCheckThread && !Review.Files.Any(f => f.Path == thread.FilePath))
            {
                DeepLinkMessage = $"Thread #{threadId} belongs to '{thread.FilePath}', which is not a file of this review.";
            }
            else
            {
                await SelectThreadAsync(thread);
            }

            return;
        }

        if (string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        // The router turns an escaped slash (%2F) of a catch-all value into "/" a second time, so a file name that
        // contains a literal %2F arrives with a slash in its place; the name is still found when it is unambiguous
        var files = Review.Files.ToList();
        var index = files.FindIndex(f => f.Path == FilePath);
        if (index < 0)
        {
            var lookalikes = files
                .Where(f => f.Path.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase) == FilePath)
                .ToList();
            index = lookalikes.Count == 1 ? files.IndexOf(lookalikes[0]) : -1;
        }

        if (index < 0)
        {
            DeepLinkMessage = $"This review has no file named '{FilePath}'. Choose a file from the list.";
            return;
        }

        await SelectFileAsync(index);
        if (!string.IsNullOrWhiteSpace(LineText))
        {
            await HighlightAddressedLineAsync();
        }
    }

    /// <summary>
    ///     Shows the version of the selected file that a line link refers to and highlights the line.
    /// </summary>
    /// <returns>A task that completes when the line is shown or the problem is reported.</returns>
    private async Task HighlightAddressedLineAsync()
    {
        var side = string.Equals(SideText, ReviewLinks.BaselineSide, StringComparison.OrdinalIgnoreCase)
            ? ThreadSide.Baseline
            : ThreadSide.Head;
        if (!int.TryParse(LineText, NumberStyles.None, CultureInfo.InvariantCulture, out var line) || line < 1)
        {
            DeepLinkMessage = $"'{LineText}' is not a line number; line numbers start at 1.";
            return;
        }

        // A file without a baseline has no baseline numbering at all
        if (side == ThreadSide.Baseline && !HasBaselineVersion)
        {
            DeepLinkMessage = $"This file has no baseline version, so there is no baseline line {line}.";
            return;
        }

        // When the diff is not available (too large) the line is looked up in the text of the version it names
        if (!HasLineDiff && CanCompare)
        {
            await ShowVersionOfAsync(side);
        }

        // Look the line up in the version it names; a file that was deleted has no head lines
        if (!TryLocateLine(side, line, out var row))
        {
            DeepLinkMessage = $"Line {line} doesn't exist in this file.";
            return;
        }

        HighlightLine = line;
        HighlightSide = side;
        _scrollBlock = ScrollCenter;
        _scrollTarget = ScrollTargetOf(row, line);
    }

    /// <summary>
    ///     Shows the text of the version of the selected file that a line link names, when the file can show it.
    /// </summary>
    /// <param name="side">The version the link names.</param>
    /// <returns>A task that completes when the version is shown.</returns>
    private async Task ShowVersionOfAsync(ThreadSide side)
    {
        if (side == ThreadSide.Baseline)
        {
            await ShowBaselineAsync();
        }
        else if (HasHeadVersion)
        {
            ShowHead();
        }
    }

    /// <summary>
    ///     Looks a line up in the version of the file that it names.
    /// </summary>
    /// <param name="side">The version the line number refers to.</param>
    /// <param name="line">The line number, at least 1.</param>
    /// <param name="row">Receives the row of the line in the diff; <c>-1</c> when there is no diff.</param>
    /// <returns><c>true</c> when the line exists in that version of the file.</returns>
    private bool TryLocateLine(ThreadSide side, int line, out int row)
    {
        row = -1;
        if (HasLineDiff)
        {
            row = DiffView.RowOf(Diff!.Result!, side, line);
            return row >= 0;
        }

        if (side == ThreadSide.Baseline)
        {
            return BaselineView && line <= (BaselineLines?.Count ?? 0);
        }

        return CurrentView == FileView.Head && line <= (SelectedLines?.Count ?? 0);
    }

    /// <summary>
    ///     Gets the element that the page scrolls to for a line that was looked up.
    /// </summary>
    /// <param name="row">The row of the line in the diff; used only when the page shows the diff.</param>
    /// <param name="line">The line number.</param>
    /// <returns>The value of the <c>data-testid</c> of the element to scroll to.</returns>
    private string ScrollTargetOf(int row, int line)
    {
        if (HasLineDiff)
        {
            return $"diff-row-{row}";
        }

        return BaselineView ? $"baseline-line-{line}" : $"code-line-{line}";
    }

    /// <summary>
    ///     Reloads everything the record depends on: the threads, the checklist, the approval status and the users
    ///     that can fill a slot.
    /// </summary>
    /// <returns>A task that completes when everything is loaded.</returns>
    private async Task RefreshAsync()
    {
        // Nothing is read for a review that is not shown (it does not exist, or its project is not visible)
        var actor = await AuthenticationState.GetActorOrNullAsync();
        if (Review is not null)
        {
            Threads = await ThreadService.ListAsync(actor, ProjectId, ReviewId);
            Checklist = await ChecklistService.GetAsync(ProjectId, ReviewId, viewerUsername: actor?.Username);
            Approval = await SigningService.GetApprovalAsync(actor, ProjectId, ReviewId);
            if (IsLocked)
            {
                // A locked review accepts no change, so nothing is left to confirm
                Warning = null;
            }

            CanReopen = actor is not null && await Access.CanAdministerAsync(actor, ProjectId);
            ReadOnly = actor is not null && !await Access.CanContributeAsync(actor, ProjectId);
            if (ReadOnly)
            {
                // A reader who lost the right to comment (a role change under the open page) keeps no half-made comment
                ClearNewThreadForm();
            }
            CanSetDueDate = await ReviewService.CanSetDueDateAsync(actor, Review);
            CanWriteConclusion = await ChecklistService.CanSetReviewConclusionTextAsync(actor, ProjectId, ReviewId);
            try
            {
                Candidates = actor is null ? [] : await SigningService.ListCandidatesAsync(actor, ProjectId);
            }
            catch (DomainRuleException)
            {
                // The access was withdrawn while the page was open; there is nobody to offer
                Candidates = [];
            }
        }

        // Read the user name from the state, not from GetActorAsync: when the sign-in state of an open page has
        // just ended, this reload must not fail; the page is replaced by the session-ended message instead
        var principal = (await AuthenticationState.GetAuthenticationStateAsync()).User;
        CurrentUsername = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name ?? string.Empty : string.Empty;
        await LoadConfirmationsAsync(actor);
    }

    /// <summary>
    ///     Finds out whether the signed-in user signs in with an identity provider and which signatures they have
    ///     already confirmed their identity for.
    /// </summary>
    /// <param name="actor">The signed-in user, or <c>null</c> when the sign-in state has ended.</param>
    /// <returns>A task that completes when both are known.</returns>
    /// <remarks>
    ///     Nothing is spent: the confirmation is used up only by the signature itself. A user with a password is not
    ///     looked up for confirmations.
    /// </remarks>
    private async Task LoadConfirmationsAsync(Actor? actor)
    {
        ConfirmedSlots.Clear();
        CurrentIsExternal = false;
        CurrentHasTotp = false;
        if (actor is null || Approval is null)
        {
            return;
        }

        CurrentIsExternal = (await Users.GetAsync(actor.Username))?.IsExternal == true;
        if (!CurrentIsExternal)
        {
            // A user with a password and a confirmed second factor also gives a code to sign
            CurrentHasTotp = await Totp.IsEnrolledAsync(actor.Username);
            return;
        }

        foreach (var slotId in Approval.Slots.Where(IsOccupant).Select(slot => slot.SlotId))
        {
            var proven = await SigningService.HasReauthProofAsync(actor, ProjectId, ReviewId, slotId);
            if (proven)
            {
                ConfirmedSlots.Add(slotId);
            }
        }
    }

    /// <summary>
    ///     Gets the address the confirmation of identity returns to: this review, without any file or thread.
    /// </summary>
    private string ReviewAddress => $"/projects/{ProjectId}/reviews/{Uri.EscapeDataString(ReviewId)}";

    /// <summary>
    ///     Gets the message to show next to the sign control of a slot.
    /// </summary>
    /// <param name="slotId">The identity of the slot.</param>
    /// <returns>
    ///     The message of the last failed signing attempt, or the report of a failed confirmation of identity, or
    ///     <c>null</c> when there is nothing to report.
    /// </returns>
    /// <remarks>
    ///     A failed confirmation says why only when the provider did not report a usable sign-in time (the
    ///     <c>auth_time</c> claim is absent, not a whole number of seconds, or out of the range of dates) or the
    ///     sign-in was not recent enough; every other failure keeps the general text. A value that is none of the
    ///     known ones shows nothing, so the address cannot put text on the page.
    /// </remarks>
    private string? SignMessage(int slotId)
    {
        if (SignErrors.TryGetValue(slotId, out var error))
        {
            return error;
        }

        if (!CurrentIsExternal || ReauthReportDismissed)
        {
            return null;
        }

        var provider = Oidc.Value.DisplayName;
        return ReauthText switch
        {
            OidcSignIn.ReauthFailed =>
                $"We could not confirm your identity. Sign in again at {provider} when asked.",
            OidcSignIn.ReauthMissingAuthTime =>
                $"{provider} did not report a usable sign-in time (auth_time). An administrator must check that it "
                + "sends auth_time as a whole number of seconds since 1970 (see the Identity Providers chapter of "
                + "the user guide).",
            OidcSignIn.ReauthStaleAuthTime =>
                $"Your sign-in at {provider} was not recent enough. Try again and sign in when asked.",
            _ => null,
        };
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit, and scrolls a chosen thread into view.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <returns>A task that completes when the optional scroll has been requested.</returns>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser. The scroll
    ///     is requested once per chosen thread; a connection that is already gone is ignored. A pending address
    ///     (see <see cref="ViewFileAsync"/>) is also sent to the browser here.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }

        // The review was shown on a live circuit, so the user has seen what was loaded: remember it (never moving
        // the state backwards). The service checks the access again, so a user who may not view the review leaves
        // no row behind
        if (_markSeenPending)
        {
            _markSeenPending = false;
            var viewer = await AuthenticationState.GetActorOrNullAsync();
            if (viewer is not null)
            {
                await Inbox.MarkSeenAsync(viewer, ProjectId, ReviewId, InboxService.MaxCommentId(Threads, _seenAt), _seenAt);
            }
        }

        if (_pendingUrl is not null)
        {
            var url = _pendingUrl;
            _pendingUrl = null;
            try
            {
                // Only the address bar changes: no navigation, no history entry, and the page keeps its state
                await JS.InvokeVoidAsync("reviewCenter.replaceUrl", url);
            }
            catch (JSDisconnectedException)
            {
                // The browser went away; there is no address bar left to update
            }
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

        if (_focusTarget is not null)
        {
            var target = _focusTarget;
            _focusTarget = null;
            try
            {
                await JS.InvokeVoidAsync("reviewCenter.focusTestId", target);
            }
            catch (JSDisconnectedException)
            {
                // The browser went away; there is nothing left to focus
            }
        }
    }

    /// <summary>
    ///     Shows a file chosen in the file list and puts its address into the browser's address bar.
    /// </summary>
    /// <param name="index">The zero-based position of the file in the displayed list.</param>
    /// <returns>A task that completes when the file is shown.</returns>
    /// <remarks>
    ///     The address replaces the current one (no history entry), so the address bar is always a link to what is
    ///     shown. It is applied after the next render, when the browser can be reached.
    /// </remarks>
    private async Task ViewFileAsync(int index)
    {
        if (!await GuardAsync())
        {
            return;
        }

        await SelectFileAsync(index);
        if (Review is not null && SelectedPath is not null && index >= 0 && index < Review.Files.Count)
        {
            _pendingUrl = ReviewLinks.ForFile(ProjectId, ReviewId, SelectedPath);
        }
    }

    /// <summary>
    ///     Shows a thread chosen in the index and puts its address into the browser's address bar.
    /// </summary>
    /// <param name="thread">The thread chosen.</param>
    /// <returns>A task that completes when the thread is shown.</returns>
    private async Task ViewThreadAsync(ReviewThread thread)
    {
        if (!await GuardAsync())
        {
            return;
        }

        await SelectThreadAsync(thread);
        _pendingUrl = ReviewLinks.ForThread(ProjectId, ReviewId, thread.Id);
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

        // Load the bytes of just this file (a deleted file has no head content; its baseline is shown instead)
        var file = Review.Files[index];
        var path = file.Path;
        var content = file.ChangeKind == ChangeKind.Deleted
            ? []
            : await ReviewService.GetFileContentAsync(ProjectId, ReviewId, path) ?? [];

        // Show text only when it is clean UTF-8; otherwise tell the operator it is binary
        var text = FileText.TryDecode(content);
        SelectedPath = path;
        SelectedLines = text is null ? null : FileText.SplitLines(text);
        SelectedKind = file.ChangeKind;
        CurrentView = FileView.Head;
        BaselineLines = null;
        Diff = null;

        // A file of a repository review with a baseline commit opens as a diff, which is what a reviewer needs first;
        // the comparison is computed once here and kept while the file stays selected
        if (CanCompare)
        {
            Diff = await ReviewService.GetDiffAsync(ProjectId, ReviewId, path);
            if (Diff is { Status: not FileDiffStatus.NoBaseline })
            {
                CurrentView = FileView.Diff;
            }
            else
            {
                Diff = null;
            }
        }

        if (CurrentView != FileView.Diff && file.ChangeKind == ChangeKind.Deleted)
        {
            await ShowBaselineAsync();
        }

        FocusedThreadId = null;
        HighlightLine = null;
        DeepLinkMessage = null;
        ClearNewThreadForm();
        await RefreshAsync();

        // The code is the reviewer's task, so bring it to the top of the window; a diff that opens with unchanged
        // lines collapsed above its first change brings that change into view instead
        _scrollTarget = "selected-file-path";
        _scrollBlock = "start";
        if (HasLineDiff && DiffView.FirstChangeRow(Diff!.Result!) is > LineDiff.DefaultContextLines and var firstChange)
        {
            _scrollTarget = $"diff-row-{firstChange}";
            _scrollBlock = ScrollCenter;
        }
    }

    /// <summary>
    ///     Shows the diff of the selected file (the view that offers comments on both versions).
    /// </summary>
    private void ShowDiff()
    {
        if (Diff is not null)
        {
            CurrentView = FileView.Diff;
            ClearAnchor();
        }
    }

    /// <summary>
    ///     Shows the head text of the selected file (the view that accepts comments by line number).
    /// </summary>
    private void ShowHead()
    {
        CurrentView = FileView.Head;
        ClearAnchor();
    }

    /// <summary>
    ///     Shows the frozen baseline text of the selected file, read-only; it is loaded the first time it is needed.
    /// </summary>
    /// <returns>A task that completes when the baseline is loaded.</returns>
    private async Task ShowBaselineAsync()
    {
        if (SelectedPath is null)
        {
            return;
        }

        if (BaselineLines is null)
        {
            var bytes = await ReviewService.GetBaselineContentAsync(ProjectId, ReviewId, SelectedPath) ?? [];
            var text = FileText.TryDecode(bytes);
            BaselineLines = text is null ? null : FileText.SplitLines(text);
        }

        CurrentView = FileView.Baseline;
        ClearAnchor();
    }

    /// <summary>
    ///     Gets the size shown for a file: the head size, or the baseline size for a deleted file.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>The size in bytes.</returns>
    private static long DisplaySize(ReviewFile file)
    {
        return file.ChangeKind == ChangeKind.Deleted ? file.BaselineSize ?? 0 : file.Size;
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
    ///     Gets the outcome of the last "Copy link" click for a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The feedback to show, or <see cref="CopyFeedback.None"/> after it has been cleared.</returns>
    private CopyFeedback CopyFeedbackFor(int threadId)
    {
        return _copyFeedback.GetValueOrDefault(threadId);
    }

    /// <summary>
    ///     Gets the live-region text shown next to the "Copy link" button of a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The text to announce, or an empty string when there is nothing to report.</returns>
    private string CopyStatusFor(int threadId)
    {
        return CopyFeedbackFor(threadId) switch
        {
            CopyFeedback.Copied => "Copied",
            CopyFeedback.Failed => "Copy failed",
            _ => string.Empty
        };
    }

    /// <summary>
    ///     Copies the absolute address of a thread to the clipboard and shows brief feedback.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>A task that completes when the browser has answered; the feedback is cleared later.</returns>
    /// <remarks>
    ///     The address is made absolute with <see cref="NavigationManager.ToAbsoluteUri(string)"/> so it can be
    ///     pasted anywhere. A browser that refuses the copy, or that is gone, is reported as "Copy failed".
    /// </remarks>
    private async Task CopyThreadLinkAsync(int threadId)
    {
        var url = Navigation.ToAbsoluteUri(ReviewLinks.ForThread(ProjectId, ReviewId, threadId)).ToString();
        var copied = false;
        try
        {
            copied = await JS.InvokeAsync<bool>("reviewCenter.copyText", url);
        }
        catch (JSDisconnectedException)
        {
            // The browser went away; there is nothing to copy to
        }
        catch (JSException)
        {
            // The browser script failed; report it like a refused copy
        }

        _copyFeedback[threadId] = copied ? CopyFeedback.Copied : CopyFeedback.Failed;

        // Restart the timer of this thread: the latest click gets the full duration
        _copyTimers.Remove(threadId, out var previous);
        var timer = new CancellationTokenSource();
        _copyTimers[threadId] = timer;

        // Clear the feedback later without holding up the click handler
        _ = ClearCopyFeedbackAsync(threadId, timer);

        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }
    }

    /// <summary>
    ///     Clears the feedback of a thread after a short delay, unless a newer click has replaced the timer.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <param name="timer">The timer of the click that started this delay; cancelled by a newer click.</param>
    /// <returns>A task that completes when the feedback has been cleared, the timer replaced or the page is gone.</returns>
    private async Task ClearCopyFeedbackAsync(int threadId, CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(CopyFeedbackDuration, Time, timer.Token);
            await InvokeAsync(() =>
            {
                // A newer click may have replaced the timer while the delay was completing
                if (_copyTimers.TryGetValue(threadId, out var current) && ReferenceEquals(current, timer))
                {
                    _copyTimers.Remove(threadId);
                    timer.Dispose();
                    _copyFeedback.Remove(threadId);
                    StateHasChanged();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // A newer click or the end of the page cancelled this timer; the newer timer clears the feedback
        }
        catch (ObjectDisposedException)
        {
            // The page is gone; there is nothing left to update
        }
    }

    /// <summary>
    ///     Cancels the pending "Copy link" feedback timers when the page goes away.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Cancels the pending "Copy link" feedback timers.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (var timer in _copyTimers.Values)
        {
            timer.Cancel();
            timer.Dispose();
        }

        _copyTimers.Clear();
    }

    /// <summary>
    ///     Shows a thread chosen in the index: selects its file, highlights its lines and scrolls to it. A thread about
    ///     a check selects no file; the page scrolls to its card in the check discussions instead.
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

        HighlightLine = null;
        DeepLinkMessage = null;
        if (thread.IsCheckThread)
        {
            // A check thread has no file: nothing is selected or loaded, and the page scrolls to its own card
            FocusedThreadId = thread.Id;
            if (thread.State == ReviewThreadState.Resolved)
            {
                ExpandedThreads.Add(thread.Id);
            }

            _scrollTarget = $"thread-{thread.Id}";
            _scrollBlock = ScrollCenter;
            return;
        }

        if (thread.FilePath != SelectedPath)
        {
            await SelectFileAsync(Review.Files.ToList().FindIndex(file => file.Path == thread.FilePath));
        }

        // The diff shows both sides, so a file that has one shows its threads there; a file whose diff is not
        // available (too large) falls back to the version the thread refers to
        if (Diff is { Status: FileDiffStatus.Diff })
        {
            ShowDiff();
        }
        else if (CanCompare && thread.StartLine is not null)
        {
            if (thread.Side == ThreadSide.Baseline)
            {
                await ShowBaselineAsync();
            }
            else if (HasHeadVersion)
            {
                ShowHead();
            }
        }

        FocusedThreadId = thread.Id;
        if (thread.State == ReviewThreadState.Resolved)
        {
            ExpandedThreads.Add(thread.Id);
        }

        _scrollTarget = ThreadScrollTarget(thread);
        _scrollBlock = ScrollCenter;
    }

    /// <summary>
    ///     Gets the test id of the element a thread chosen in the index is scrolled to in the current view.
    /// </summary>
    /// <param name="thread">The thread chosen.</param>
    /// <returns>The row or line where the thread's anchor starts, or the thread itself for a whole-file thread.</returns>
    private string ThreadScrollTarget(ReviewThread thread)
    {
        var whole = $"thread-{thread.Id}";
        if (thread.StartLine is not { } start)
        {
            return whole;
        }

        if (HasLineDiff)
        {
            var row = DiffView.RowOf(Diff!.Result!, thread.Side, start);
            return row < 0 ? whole : $"diff-row-{row}";
        }

        return BaselineView ? $"baseline-line-{start}" : $"code-line-{start}";
    }

    /// <summary>
    ///     Aims the new-thread form at one line of the head text and scrolls the form into view.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    private void CommentOnLine(int line)
    {
        NewSide = ThreadSide.Head;
        NewStartLine = line.ToString(CultureInfo.InvariantCulture);
        NewEndLine = NewStartLine;
        _scrollTarget = "new-thread-form";
        _focusTarget = "thread-text-input";
        _scrollBlock = ScrollCenter;
    }

    /// <summary>
    ///     Aims the new-thread form at a line of the diff.
    /// </summary>
    /// <param name="request">The line chosen and whether Shift was held.</param>
    /// <remarks>
    ///     A plain click selects that one line. Shift-click extends the selection to the line, but only on the
    ///     same side; a range never mixes the baseline and the head numbering.
    /// </remarks>
    private void CommentOnDiffLine(DiffCommentRequest request)
    {
        var current = ComposerRange();
        if (request.Extend && current is { } range && NewSide == request.Side)
        {
            NewStartLine = Math.Min(range.Start, request.Line).ToString(CultureInfo.InvariantCulture);
            NewEndLine = Math.Max(range.End, request.Line).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            NewSide = request.Side;
            NewStartLine = request.Line.ToString(CultureInfo.InvariantCulture);
            NewEndLine = NewStartLine;
        }

        NewThreadError = null;
        _scrollTarget = "new-thread-form";
        _focusTarget = "thread-text-input";
        _scrollBlock = ScrollCenter;
    }

    /// <summary>
    ///     Closes the form under the selected line and forgets what was typed.
    /// </summary>
    private void CancelComment()
    {
        ClearNewThreadForm();
    }

    /// <summary>
    ///     Turns the comment under the selected line into a comment on the whole file; the typed text stays.
    /// </summary>
    private void CommentOnWholeFile()
    {
        ClearAnchor();
        _scrollTarget = "new-thread-form";
        _focusTarget = "thread-text-input";
        _scrollBlock = ScrollCenter;
    }

    /// <summary>
    ///     Reads the lines of the new-thread form as the range the form is aimed at.
    /// </summary>
    /// <returns>
    ///     The first and last line; the last equals the first when the second field is blank or does not
    ///     reach the first; <c>null</c> when no line is selected or the first field is not a whole number.
    /// </returns>
    private (int Start, int End)? ComposerRange()
    {
        if (!TryParseLine(NewStartLine, out var start) || start is null)
        {
            return null;
        }

        var end = TryParseLine(NewEndLine, out var parsed) && parsed is { } last && last >= start ? last : start.Value;
        return (start.Value, end);
    }

    /// <summary>
    ///     Starts a thread on the selected file from the new-thread form.
    /// </summary>
    /// <returns>A task that completes when the thread is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Both line fields blank anchors the whole file; only a start line anchors that single line. On a
    ///     rule violation the message is shown next to the form and everything typed stays, so it can be fixed.
    ///     Success clears the text and the line fields. A change that would make signatures out of date waits for
    ///     the person to confirm it (see <see cref="Warning"/>); the text stays until then.
    /// </remarks>
    private async Task StartThreadAsync()
    {
        if (!await GuardAsync() || SelectedPath is null)
        {
            return;
        }

        if (!TryParseLine(NewStartLine, out var start) || !TryParseLine(NewEndLine, out var end))
        {
            NewThreadError = "Line numbers must be whole numbers.";
            return;
        }

        var anchor = new ThreadAnchor(SelectedPath, start, end ?? start, start is null ? ThreadSide.Head : NewSide);
        await SubmitThreadAsync(anchor, NewText, null);
    }

    /// <summary>
    ///     Stores a new thread, or asks for confirmation when that would make signatures out of date.
    /// </summary>
    /// <param name="anchor">Where the thread is placed, as it was when the person submitted the form.</param>
    /// <param name="text">The comment text, as it was when the person submitted the form.</param>
    /// <param name="confirmed">The signatures the person has confirmed to make out of date; <c>null</c> for none.</param>
    /// <returns>A task that completes when the thread is stored or the warning or problem is shown.</returns>
    private async Task SubmitThreadAsync(
        ThreadAnchor anchor, string text, IReadOnlyList<AffectedSignature>? confirmed)
    {
        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            await ThreadService.StartThreadAsync(
                ProjectId, ReviewId, anchor, actor, text, acknowledgedSlotIds: SlotIds(confirmed));
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            NewThreadError = null;
            Warning = PendingSignatureWarning.Create(
                NewThreadWarningKey, confirmed, ex, slots => SubmitThreadAsync(anchor, text, slots));
            return;
        }
        catch (DomainRuleException ex)
        {
            Warning = null;
            NewThreadError = ex.Message;
            return;
        }

        Warning = null;
        NewThreadError = null;
        NewText = string.Empty;
        ClearAnchor();
        await RefreshAsync();
    }

    /// <summary>
    ///     Adds a reply to a thread, or asks for confirmation when that would make signatures out of date.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <param name="confirmed">The signatures the person has confirmed to make out of date; <c>null</c> for none.</param>
    /// <param name="confirmedText">
    ///     The text to send, as it was at the first attempt; <c>null</c> to take the text typed for the thread.
    /// </param>
    /// <returns>A task that completes when the reply is stored or the warning or problem is shown.</returns>
    private async Task ReplyAsync(
        int threadId, IReadOnlyList<AffectedSignature>? confirmed = null, string? confirmedText = null)
    {
        if (!await GuardAsync())
        {
            return;
        }

        var text = confirmedText ?? ReplyTexts.GetValueOrDefault(threadId) ?? string.Empty;
        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            await ThreadService.ReplyAsync(
                ProjectId, ReviewId, threadId, actor, text, acknowledgedSlotIds: SlotIds(confirmed));
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            ReplyErrors.Remove(threadId);
            Warning = PendingSignatureWarning.Create(
                ReplyWarningKey(threadId), confirmed, ex, slots => ReplyAsync(threadId, slots, text));
            return;
        }
        catch (DomainRuleException ex)
        {
            Warning = null;
            ReplyErrors[threadId] = ex.Message;
            return;
        }

        Warning = null;
        ReplyErrors.Remove(threadId);
        ReplyTexts.Remove(threadId);
        await RefreshAsync();
    }

    /// <summary>
    ///     Resolves an open thread or reopens a resolved one, or asks for confirmation when that would make
    ///     signatures out of date.
    /// </summary>
    /// <param name="thread">The thread as shown.</param>
    /// <param name="confirmed">The signatures the person has confirmed to make out of date; <c>null</c> for none.</param>
    /// <returns>A task that completes when the state is stored or the warning or problem is shown.</returns>
    /// <remarks>
    ///     Refused, with a message, when the sign-in state of the page has already ended. Resolving or reopening
    ///     changes the approval state, so it must not run for a user who is no longer signed in.
    /// </remarks>
    private async Task ToggleThreadAsync(ReviewThread thread, IReadOnlyList<AffectedSignature>? confirmed = null)
    {
        if (!await GuardAsync())
        {
            return;
        }

        try
        {
            // The actor is audited; asking for it also refuses an ended sign-in state
            var actor = await AuthenticationState.GetActorAsync();
            if (thread.State == ReviewThreadState.Open)
            {
                await ThreadService.ResolveAsync(
                    ProjectId, ReviewId, thread.Id, actor, acknowledgedSlotIds: SlotIds(confirmed));
                ExpandedThreads.Remove(thread.Id);
            }
            else
            {
                await ThreadService.ReopenAsync(
                    ProjectId, ReviewId, thread.Id, actor, acknowledgedSlotIds: SlotIds(confirmed));
            }
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            ReplyErrors.Remove(thread.Id);
            Warning = PendingSignatureWarning.Create(
                ToggleWarningKey(thread.Id), confirmed, ex, slots => ToggleThreadAsync(thread, slots));
            return;
        }
        catch (DomainRuleException ex)
        {
            Warning = null;
            ReplyErrors[thread.Id] = ex.Message;
            return;
        }

        Warning = null;
        ReplyErrors.Remove(thread.Id);
        await RefreshAsync();
    }

    /// <summary>
    ///     Repeats the change that waits for confirmation, now acknowledging the signatures it named.
    /// </summary>
    /// <returns>A task that completes when the change has been repeated.</returns>
    /// <remarks>
    ///     The waiting change is taken out first, so a second click cannot repeat it twice. The access is checked
    ///     again, and the service checks again: when more signatures have become affected in the meantime, the
    ///     warning is shown again with all of them, so the person only ever confirms what was shown.
    /// </remarks>
    private async Task ConfirmWarningAsync()
    {
        var warning = Warning;
        Warning = null;
        if (warning is not null && await GuardAsync())
        {
            await warning.Retry(warning.Slots);
        }
    }

    /// <summary>
    ///     Drops the waiting change; nothing is stored and what was typed stays.
    /// </summary>
    private void CancelWarning()
    {
        Warning = null;
    }

    /// <summary>
    ///     Gets the slot identities to acknowledge for a confirmation.
    /// </summary>
    /// <param name="confirmed">The confirmed signatures, or <c>null</c> when none are confirmed.</param>
    /// <returns>The identities, or <c>null</c> when there is nothing to acknowledge.</returns>
    private static int[]? SlotIds(IReadOnlyList<AffectedSignature>? confirmed)
    {
        return confirmed?.Select(slot => slot.SlotId).ToArray();
    }

    /// <summary>
    ///     Gets the key of the confirmation for a reply to a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The key.</returns>
    private static string ReplyWarningKey(int threadId)
    {
        return $"reply-{threadId}";
    }

    /// <summary>
    ///     Gets the key of the confirmation for resolving or reopening a thread.
    /// </summary>
    /// <param name="threadId">The identity of the thread.</param>
    /// <returns>The key.</returns>
    private static string ToggleWarningKey(int threadId)
    {
        return $"toggle-{threadId}";
    }

    /// <summary>
    ///     Adds a reviewer slot from the add-slot form.
    /// </summary>
    /// <returns>A task that completes when the slot is stored or the problem is shown.</returns>
    private async Task AddSlotAsync()
    {
        if (!await GuardAsync())
        {
            return;
        }

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
    /// <param name="confirmed">The signatures the person has confirmed to make out of date; <c>null</c> for none.</param>
    /// <returns>A task that completes when the change is stored or the warning or problem is shown.</returns>
    private async Task SaveSlotAsync(SlotStatus slot, IReadOnlyList<AffectedSignature>? confirmed = null)
    {
        if (!await GuardAsync())
        {
            return;
        }

        try
        {
            if (ParseUserId(PrimaryChoice(slot)) is not { } primary)
            {
                throw new DomainRuleException(DomainRule.SlotUserInvalid, "Choose a primary reviewer.");
            }

            await SigningService.UpdateSlotAsync(
                await AuthenticationState.GetActorAsync(),
                ProjectId,
                ReviewId,
                slot.SlotId,
                primary,
                ParseUserId(AlternateChoice(slot)),
                acknowledgedSlotIds: SlotIds(confirmed));
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            // A reviewer who leaves takes the own answers out of the ballot, which may change the record
            SlotErrors.Remove(slot.SlotId);
            Warning = PendingSignatureWarning.Create(
                SlotWarningKey(slot.SlotId), confirmed, ex, slots => SaveSlotAsync(slot, slots));
            return;
        }
        catch (DomainRuleException ex)
        {
            Warning = null;
            SlotErrors[slot.SlotId] = ex.Message;
            return;
        }

        Warning = null;
        SlotErrors.Remove(slot.SlotId);
        SlotPrimaryChoices.Remove(slot.SlotId);
        SlotAlternateChoices.Remove(slot.SlotId);
        await RefreshAsync();
    }

    /// <summary>
    ///     Removes an unsigned slot, or asks for confirmation when that would make signatures out of date.
    /// </summary>
    /// <param name="slotId">The identity of the slot.</param>
    /// <param name="confirmed">The signatures the person has confirmed to make out of date; <c>null</c> for none.</param>
    /// <returns>A task that completes when the slot is removed or the warning or problem is shown.</returns>
    private async Task RemoveSlotAsync(int slotId, IReadOnlyList<AffectedSignature>? confirmed = null)
    {
        if (!await GuardAsync())
        {
            return;
        }

        try
        {
            await SigningService.RemoveSlotAsync(
                await AuthenticationState.GetActorAsync(),
                ProjectId,
                ReviewId,
                slotId,
                acknowledgedSlotIds: SlotIds(confirmed));
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            // The ballot of the slot leaves the record with it, which may change what the other signatures cover
            SlotErrors.Remove(slotId);
            Warning = PendingSignatureWarning.Create(
                SlotWarningKey(slotId), confirmed, ex, slots => RemoveSlotAsync(slotId, slots));
            return;
        }
        catch (DomainRuleException ex)
        {
            Warning = null;
            SlotErrors[slotId] = ex.Message;
            return;
        }

        Warning = null;
        SlotErrors.Remove(slotId);
        await RefreshAsync();
    }

    /// <summary>
    ///     Gets the key of the confirmation for a change of a slot.
    /// </summary>
    /// <param name="slotId">The identity of the slot.</param>
    /// <returns>The key.</returns>
    private static string SlotWarningKey(int slotId)
    {
        return $"slot-{slotId}";
    }

    /// <summary>
    ///     Saves the typed due date, or clears it when the input is empty.
    /// </summary>
    /// <returns>A task that completes when the date is stored or the problem is shown.</returns>
    private Task SaveDueDateAsync()
    {
        return ChangeDueDateAsync(DueDateInput);
    }

    /// <summary>
    ///     Clears the due date of the review.
    /// </summary>
    /// <returns>A task that completes when the date is cleared or the problem is shown.</returns>
    private Task ClearDueDateAsync()
    {
        return ChangeDueDateAsync(string.Empty);
    }

    /// <summary>
    ///     Stores a due date, as the signed-in user, and reloads the review.
    /// </summary>
    /// <param name="text">The text of the date (<c>yyyy-MM-dd</c>); empty clears the date.</param>
    /// <returns>A task that completes when the change is stored or the problem is shown.</returns>
    /// <remarks>
    ///     Only a user who may change the date is offered the form, and the service checks the same again (and refuses a
    ///     locked review). After a refusal the typed text stays so it can be fixed; after a change by someone else the
    ///     current date is loaded and shown with the message.
    /// </remarks>
    private async Task ChangeDueDateAsync(string text)
    {
        if (!await GuardAsync())
        {
            return;
        }

        try
        {
            var dueDate = ReviewService.ParseDueDate(text);
            await ReviewService.SetDueDateAsync(ProjectId, ReviewId, dueDate, await AuthenticationState.GetActorAsync());
        }
        catch (DomainRuleException ex)
        {
            DueDateError = ex.Message;

            // Someone else changed the date, or the review got locked: show what is stored now
            if (ex.Rule is DomainRule.RecordChanged or DomainRule.ReviewLocked)
            {
                Review = await ReviewService.GetAsync(ProjectId, ReviewId);
                DueDateInput = Review?.DueDate is { } current ? FormatDueDate(current) : string.Empty;
                await RefreshAsync();
            }

            return;
        }

        DueDateError = null;
        Review = await ReviewService.GetAsync(ProjectId, ReviewId);
        DueDateInput = Review?.DueDate is { } stored ? FormatDueDate(stored) : string.Empty;
        await RefreshAsync();
    }

    /// <summary>
    ///     Saves the typed review conclusion text, or asks for confirmation when that would make signatures out of date.
    /// </summary>
    /// <returns>A task that completes when the text is stored or the warning or problem is shown.</returns>
    /// <remarks>
    ///     The service checks who may write the text and refuses a locked review. After a refusal the typed text stays so
    ///     it can be fixed; after a change by someone else or a lock the stored review is loaded and shown with the
    ///     message.
    /// </remarks>
    private async Task SaveConclusionAsync()
    {
        if (!await GuardAsync())
        {
            return;
        }

        await SubmitConclusionAsync(ConclusionInput, null);
    }

    /// <summary>
    ///     Stores the review conclusion text, or asks for confirmation when that would make signatures out of date.
    /// </summary>
    /// <param name="text">The text, as it was when the person submitted the form.</param>
    /// <param name="confirmed">The signatures the person has confirmed to make out of date; <c>null</c> for none.</param>
    /// <returns>A task that completes when the text is stored or the warning or problem is shown.</returns>
    private async Task SubmitConclusionAsync(string text, IReadOnlyList<AffectedSignature>? confirmed)
    {
        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            await ChecklistService.SetReviewConclusionTextAsync(
                ProjectId, ReviewId, text, actor, acknowledgedSlotIds: SlotIds(confirmed));
        }
        catch (DomainRuleException ex) when (ex.Rule == DomainRule.SignaturesWouldBecomeOutOfDate)
        {
            ConclusionError = null;
            Warning = PendingSignatureWarning.Create(
                ConclusionWarningKey, confirmed, ex, slots => SubmitConclusionAsync(text, slots));
            return;
        }
        catch (DomainRuleException ex)
        {
            Warning = null;
            ConclusionError = ex.Message;

            // Someone else changed the text, or the review got locked: show what is stored now
            if (ex.Rule is DomainRule.RecordChanged or DomainRule.ReviewLocked)
            {
                Review = await ReviewService.GetAsync(ProjectId, ReviewId);
                ConclusionInput = Review?.ReviewConclusionText ?? string.Empty;
                await RefreshAsync();
            }

            return;
        }

        Warning = null;
        ConclusionError = null;
        Review = await ReviewService.GetAsync(ProjectId, ReviewId);
        ConclusionInput = Review?.ReviewConclusionText ?? string.Empty;
        await RefreshAsync();
    }

    /// <summary>
    ///     Reopens the locked review with the typed reason.
    /// </summary>
    /// <returns>A task that completes when the review is reopened or the problem is shown.</returns>
    /// <remarks>
    ///     Only a project or system administrator is offered the form, and the service checks the same again. After a
    ///     refusal the typed reason stays so it can be fixed; after success it is cleared and the page is reloaded, so
    ///     the review shows as in review with its history.
    /// </remarks>
    private async Task ReopenAsync()
    {
        if (!await GuardAsync())
        {
            return;
        }

        try
        {
            await SigningService.ReopenReviewAsync(
                await AuthenticationState.GetActorAsync(), ProjectId, ReviewId, ReopenReason);
        }
        catch (DomainRuleException ex)
        {
            ReopenError = ex.Message;
            return;
        }

        ReopenError = null;
        ReopenReason = string.Empty;
        await RefreshAsync();
    }

    /// <summary>
    ///     Signs the review for a slot with the typed password, for the record digest that was on screen.
    /// </summary>
    /// <param name="slotId">The identity of the slot.</param>
    /// <returns>A task that completes when the signature is stored or the problem is shown.</returns>
    /// <remarks>
    ///     The password is cleared after every attempt, whatever its outcome, and the field is rebuilt empty. It is
    ///     only passed to the service, never kept, logged or rendered. A user of an identity provider has no password
    ///     field; the service spends the confirmation of identity that the user made, and the page shows the result
    ///     like any other.
    /// </remarks>
    private async Task SignAsync(int slotId)
    {
        if (!await GuardAsync())
        {
            return;
        }

        ReauthReportDismissed = true;

        try
        {
            await SigningService.SignWithCredentialsAsync(
                await AuthenticationState.GetActorAsync(),
                ProjectId,
                ReviewId,
                slotId,
                Approval!.Digest,
                new SignerCredentials(SignPasswords.GetValueOrDefault(slotId), SignTotpCodes.GetValueOrDefault(slotId)));
            SignErrors.Remove(slotId);
        }
        catch (DomainRuleException ex)
        {
            SignErrors[slotId] = ex.Message;
        }
        finally
        {
            SignPasswords.Remove(slotId);
            SignTotpCodes.Remove(slotId);
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
    ///     Tells whether a slot's reviewer is one of the users offered in the reviewer lists.
    /// </summary>
    /// <param name="member">The primary or alternate reviewer of a slot.</param>
    /// <returns>
    ///     <c>false</c> for a reviewer who is not offered (disabled, no longer a member, or the review author while
    ///     independent reviewers are required), so the list needs an extra option for the current choice (otherwise
    ///     the list would show nothing as selected).
    /// </returns>
    private bool IsOffered(SlotMember member)
    {
        return VisibleCandidates.Any(candidate => candidate.UserId == member.UserId);
    }

    /// <summary>
    ///     Gets the text of the extra list option for a reviewer who cannot be chosen any more.
    /// </summary>
    /// <param name="member">The reviewer.</param>
    /// <returns>
    ///     The display name and user name, and why the reviewer is not in the list: an enabled member is only left
    ///     out because of the independence rule (the review author), anyone else is disabled or no longer a member.
    /// </returns>
    private string UnavailableLabel(SlotMember member)
    {
        var enabledMember = Candidates.Any(candidate => candidate.UserId == member.UserId);
        var reason = enabledMember ? "not offered for a new choice (review author)" : "disabled or no longer a member";
        return $"{member.DisplayName} ({member.Username}), {reason}";
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

        var lines = end is null || end == start ? $"line {start}" : $"lines {start}-{end}";
        if (!HasLineDiff)
        {
            return $"Comment on {lines}";
        }

        return NewSide == ThreadSide.Baseline ? $"Comment on {lines} (before)" : $"Comment on {lines} (after)";
    }

    /// <summary>
    ///     Describes the anchor of a thread for display.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>
    ///     "Check 1.2" for a thread about a check; "Whole file"; "Line 4" or "Lines 4-9" for a file shown as one text;
    ///     "Before: line 4" or "After: lines 4-9" for a file that can be compared, where the same number could mean
    ///     either version.
    /// </returns>
    private string AnchorLabel(ReviewThread thread)
    {
        if (thread.CheckItemId is not null)
        {
            return $"Check {thread.CheckItemId}";
        }

        if (thread.StartLine is null)
        {
            return "Whole file";
        }

        var text = thread.EndLine == thread.StartLine ? $"line {thread.StartLine}" : $"lines {thread.StartLine}-{thread.EndLine}";
        if (thread.Side == ThreadSide.Baseline)
        {
            return $"Before: {text}";
        }

        return IsComparable(thread.FilePath) ? $"After: {text}" : char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    ///     Tells whether a file of the review has a baseline to compare against.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns><c>true</c> for a file of a repository review that froze its baseline commit.</returns>
    private bool IsComparable(string path)
    {
        var file = Review?.Files.FirstOrDefault(f => f.Path == path);
        return file is not null && Review is { IsFromRepository: true, BaselineSha: not null };
    }

    /// <summary>
    ///     Words the accessible name of a thread index row, carrying the same facts as the visible cells.
    /// </summary>
    /// <param name="thread">The thread of the row.</param>
    /// <returns>For example "Show Line 4 of src/a.cs, Open, 2 comments, first by Ann, last activity 2026-01-02 03:04 UTC".</returns>
    private string IndexRowLabel(ReviewThread thread)
    {
        return $"Show {AnchorLabel(thread)} of {IndexPathLabel(thread)}, {thread.State}, {CommentCountLabel(thread)}, " +
               $"first by {thread.Comments.FirstOrDefault()?.Author}, last activity {FormatTime(thread.LastActivity)}";
    }

    /// <summary>
    ///     Words the place cell of a thread index row: the file path, or a fixed label for a thread about a check.
    /// </summary>
    /// <param name="thread">The thread of the row.</param>
    /// <returns>The file path, or "Check discussion" for a thread about a check (which has no file).</returns>
    private static string IndexPathLabel(ReviewThread thread)
    {
        return thread.CheckItemId is null ? thread.FilePath : "Check discussion";
    }

    /// <summary>
    ///     Gets the threads about a check of the checklist, in creation order.
    /// </summary>
    /// <returns>The check threads; empty when the review has none.</returns>
    private IReadOnlyList<ReviewThread> CheckThreads()
    {
        return Threads.Where(t => t.IsCheckThread).ToList();
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
    ///     Empties the new-thread form fields and its message, and drops a waiting confirmation for it.
    /// </summary>
    private void ClearNewThreadForm()
    {
        ClearAnchor();
        NewText = string.Empty;
        NewThreadError = null;
        if (Warning?.Key == NewThreadWarningKey)
        {
            Warning = null;
        }
    }

    /// <summary>
    ///     Empties the line fields of the new-thread form and aims it at the head version again; the typed text stays.
    /// </summary>
    private void ClearAnchor()
    {
        NewStartLine = string.Empty;
        NewEndLine = string.Empty;
        NewSide = ThreadSide.Head;
    }
}
