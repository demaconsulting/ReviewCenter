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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the project page (<c>ProjectPage.razor</c>): shows one project and its reviews as a progress
///     overview: the stage, the signatures, what blocks, who is waited on, the last activity and the due date of every
///     review, with a summary strip, filters, sortable columns and paging whose state lives in the query string.
///     Reviews are created on the separate new review page. An administrator of the project (a
///     project admin or a system administrator) also manages its members here and opens its audit trail.
/// </summary>
/// <remarks>
///     The page talks to the domain only through <see cref="ProjectService"/>, <see cref="ReviewProgressService"/>
///     and <see cref="RepositoryService"/>. The progress of all reviews comes from one call, which reads everything it
///     needs in a constant number of queries. A project the user may not see is shown
///     exactly like a project that does not exist. Adding a member needs typing an exact user name and happens on
///     its own page, so no list of users is ever loaded here. Every handler asks the shared guard again before it
///     reads or changes anything, so a user who lost the right sees nothing stale. Each browser connection
///     (circuit) gets its own page instance, so the state here is per-user and needs no synchronization. The page
///     requires a signed-in user. Filters, sorting and paging are plain links and a plain form with a GET method, so
///     every view of the overview has an address that can be bookmarked or shared; unknown or malformed query values
///     are ignored rather than refused.
/// </remarks>
public partial class ProjectPage : ComponentBase
{
    /// <summary>
    ///     The number of reviews on one page of the overview.
    /// </summary>
    public const int PageSize = 25;

    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     Gets or sets the identity of the project to show, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the stage filter, from the <c>stage</c> query value: <c>setup</c>, <c>in-review</c> or
    ///     <c>approved</c>; anything else shows every stage.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "stage")]
    public string? StageQuery { get; set; }

    /// <summary>
    ///     Gets or sets the "overdue only" filter, from the <c>overdue</c> query value (<c>1</c> switches it on).
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "overdue")]
    public string? OverdueQuery { get; set; }

    /// <summary>
    ///     Gets or sets the "blocked only" filter, from the <c>blocked</c> query value (<c>1</c> switches it on).
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "blocked")]
    public string? BlockedQuery { get; set; }

    /// <summary>
    ///     Gets or sets the sort column, from the <c>sort</c> query value: <c>id</c>, <c>stage</c>, <c>activity</c>,
    ///     <c>due</c> or <c>signatures</c>; anything else sorts by review identifier.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "sort")]
    public string? SortQuery { get; set; }

    /// <summary>
    ///     Gets or sets the sort direction, from the <c>dir</c> query value: <c>asc</c> or <c>desc</c>.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "dir")]
    public string? DirectionQuery { get; set; }

    /// <summary>
    ///     Gets or sets the text search, from the <c>q</c> query value; matches the identifier and the title.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "q")]
    public string? SearchQuery { get; set; }

    /// <summary>
    ///     Gets or sets the page of the overview, from the <c>page</c> query value; counted from 1.
    /// </summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "page")]
    public string? PageQuery { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that computes the progress of the reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewProgressService ProgressService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the repository registry, supplied by dependency injection; used only to know whether any
    ///     enabled repository exists, which decides whether reviews can be planned.
    /// </summary>
    [Inject]
    private RepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the service that reads the checklist of a project, supplied by dependency injection; used
    ///     only for the one-line summary of an administrator.
    /// </summary>
    [Inject]
    private ProjectChecklistService Checklists { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the access service, supplied by dependency injection; decides what the user may do here.
    /// </summary>
    [Inject]
    private IProjectAccess Access { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the authentication state of the current user, supplied by dependency injection.
    /// </summary>
    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the project lookup has finished.
    /// </summary>
    private bool Loaded { get; set; }

    /// <summary>
    ///     Gets the project shown, or <c>null</c> when it does not exist or is not visible to the user.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets the progress of every review of the project, ordered by review identifier.
    /// </summary>
    private IReadOnlyList<ReviewProgress> Progress { get; set; } = [];

    /// <summary>
    ///     Gets the reviews that match the filters, in the chosen order (all pages).
    /// </summary>
    private IReadOnlyList<ReviewProgress> Matching { get; set; } = [];

    /// <summary>
    ///     Gets the reviews of the page shown.
    /// </summary>
    private IReadOnlyList<ReviewProgress> PageRows { get; set; } = [];

    /// <summary>
    ///     Gets the page shown, counted from 1, after it was brought into range.
    /// </summary>
    private int CurrentPage { get; set; } = 1;

    /// <summary>
    ///     Gets the number of pages of the matching reviews; at least 1.
    /// </summary>
    private int PageCount { get; set; } = 1;

    /// <summary>
    ///     Gets the stage filter in effect, or <c>null</c> for every stage.
    /// </summary>
    private ReviewStage? StageFilter => StageQuery?.ToLowerInvariant() switch
    {
        "setup" => ReviewStage.Setup,
        "in-review" => ReviewStage.InReview,
        "approved" => ReviewStage.Approved,
        _ => null
    };

    /// <summary>
    ///     Gets a value indicating whether only overdue reviews are shown.
    /// </summary>
    private bool OverdueOnly => OverdueQuery == "1";

    /// <summary>
    ///     Gets a value indicating whether only blocked reviews are shown.
    /// </summary>
    private bool BlockedOnly => BlockedQuery == "1";

    /// <summary>
    ///     Gets the sort column in effect: <c>id</c>, <c>stage</c>, <c>activity</c>, <c>due</c> or <c>signatures</c>.
    /// </summary>
    private string SortColumn => SortQuery?.ToLowerInvariant() is "stage" or "activity" or "due" or "signatures"
        ? SortQuery.ToLowerInvariant()
        : "id";

    /// <summary>
    ///     Gets a value indicating whether the sort is descending; without a direction, the newest activity comes
    ///     first and everything else ascends.
    /// </summary>
    private bool Descending => DirectionQuery?.ToLowerInvariant() switch
    {
        "desc" => true,
        "asc" => false,
        _ => SortColumn == "activity"
    };

    /// <summary>
    ///     Gets the text search in effect; never null, trimmed.
    /// </summary>
    private string SearchText => SearchQuery?.Trim() ?? string.Empty;

    /// <summary>
    ///     Gets a value indicating whether any filter is in effect.
    /// </summary>
    private bool Filtered => StageFilter is not null || OverdueOnly || BlockedOnly || SearchText.Length > 0;

    /// <summary>
    ///     Gets the number of reviews of the project per stage and overdue, for the summary strip; not affected by the
    ///     filters.
    /// </summary>
    private (int Setup, int InReview, int Approved, int Overdue) Summary =>
        (Progress.Count(p => p.Stage == ReviewStage.Setup),
            Progress.Count(p => p.Stage == ReviewStage.InReview),
            Progress.Count(p => p.Stage == ReviewStage.Approved),
            Progress.Count(p => p.IsOverdue));

    /// <summary>
    ///     Gets the enabled repositories (name only; never location or credentials).
    /// </summary>
    private IReadOnlyList<RepositorySummary> Repositories { get; set; } = [];

    /// <summary>
    ///     Gets a value indicating whether the user may work in this project (create and plan reviews); a member who
    ///     may only read, such as an auditor, may not.
    /// </summary>
    private bool CanContribute { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the user may manage this project (system administrator or project admin).
    /// </summary>
    private bool CanAdminister { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the user may read the audit trail of this project (an administrator or an
    ///     auditor).
    /// </summary>
    private bool CanAudit { get; set; }

    /// <summary>
    ///     Gets the members of the project; only loaded for someone who may administer the project.
    /// </summary>
    private IReadOnlyList<ProjectMember> Members { get; set; } = [];

    /// <summary>
    ///     Gets the message of the last rejected member change, or <c>null</c> when there is none.
    /// </summary>
    private string? MemberError { get; set; }

    /// <summary>
    ///     Gets the message about the last stored member change, or <c>null</c> when there is none.
    /// </summary>
    private string? MemberStatus { get; set; }

    /// <summary>
    ///     Gets the value of the independent reviewers switch as the user set it (not yet saved).
    /// </summary>
    private bool RequireIndependentChoice { get; set; } = true;

    /// <summary>
    ///     Gets the value of the "authors cannot answer the checklist" switch as the user set it (not yet saved).
    /// </summary>
    private bool RequireNonAuthorChoice { get; set; } = true;

    /// <summary>
    ///     Gets the value of the "the person who reopened a review cannot sign it again" switch as the user set it
    ///     (not yet saved).
    /// </summary>
    private bool RequireNonReopenerChoice { get; set; } = true;

    /// <summary>
    ///     Gets the message of the last rejected review rule change, or <c>null</c> when there is none.
    /// </summary>
    private string? RulesError { get; set; }

    /// <summary>
    ///     Gets the message about the last stored review rule change, or <c>null</c> when there is none.
    /// </summary>
    private string? RulesStatus { get; set; }

    /// <summary>
    ///     Gets the one-line summary of the project's checklist for the review rules card; <c>null</c> for someone
    ///     who may not administer the project.
    /// </summary>
    private string? ChecklistSummary { get; set; }

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Loads the project and its reviews whenever the project identity changes.
    /// </summary>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop the previous project's data
        Loaded = false;
        MemberError = null;
        MemberStatus = null;
        RulesError = null;
        RulesStatus = null;
        var actor = await AuthenticationState.GetActorOrNullAsync();
        Project = await ProjectService.GetAsync(actor, ProjectId);
        if (actor is null || Project is null)
        {
            ShowNoAccess();
            return;
        }

        RequireIndependentChoice = Project.RequireIndependentReviewers;
        RequireNonAuthorChoice = Project.RequireNonAuthorChecklistAnswers;
        RequireNonReopenerChoice = Project.RequireNonReopenerSigning;
        try
        {
            // One call for all reviews: a constant number of reads however many reviews the project has
            Progress = await ProgressService.GetProgressAsync(actor, ProjectId);
        }
        catch (DomainRuleException)
        {
            // The right to see the project was lost between the two reads: show what a stranger sees
            ShowNoAccess();
            return;
        }

        ApplyView();
        CanContribute = await Access.CanContributeAsync(actor, ProjectId);

        // A member who may only read (an auditor) cannot plan or create reviews, so the registry is not read for them
        Repositories = CanContribute ? await RepositoryService.ListEnabledAsync(actor, ProjectId) : [];
        CanAdminister = await Access.CanAdministerAsync(actor, ProjectId);
        CanAudit = await Access.CanAuditAsync(actor, ProjectId);
        ChecklistSummary = CanAdminister ? await DescribeChecklistAsync(actor) : null;
        await LoadMembersAsync(actor);

        Loaded = true;
    }

    /// <summary>
    ///     Applies the filters, the sort order and the paging of the query string to the progress of the reviews.
    /// </summary>
    private void ApplyView()
    {
        var rows = Progress.AsEnumerable();
        if (StageFilter is { } stage)
        {
            rows = rows.Where(p => p.Stage == stage);
        }

        if (OverdueOnly)
        {
            rows = rows.Where(p => p.IsOverdue);
        }

        if (BlockedOnly)
        {
            rows = rows.Where(p => p.IsBlocked);
        }

        if (SearchText.Length > 0)
        {
            rows = rows.Where(p =>
                p.ReviewId.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || p.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        // Ties are always broken by the review identifier, so the order is stable and predictable
        var ordered = SortColumn switch
        {
            "stage" => Order(rows, p => (int)p.Stage),
            "activity" => Order(rows, p => p.LastActivity),
            "due" => OrderDue(rows),
            "signatures" => Order(rows, p => p.SlotsRequired == 0 ? -1.0 : (double)p.SlotsSigned / p.SlotsRequired),
            _ => Order(rows, p => p.ReviewId, StringComparer.OrdinalIgnoreCase)
        };
        Matching = ordered.ToList();
        PageCount = Math.Max(1, (Matching.Count + PageSize - 1) / PageSize);
        CurrentPage = int.TryParse(PageQuery, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested)
            ? Math.Clamp(requested, 1, PageCount)
            : 1;
        PageRows = Matching.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
    }

    /// <summary>
    ///     Orders reviews by a key in the chosen direction, then by review identifier.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="rows">The reviews.</param>
    /// <param name="key">Reads the key of a review.</param>
    /// <param name="comparer">The comparer of keys, or <c>null</c> for the default.</param>
    /// <returns>The ordered reviews.</returns>
    private IOrderedEnumerable<ReviewProgress> Order<TKey>(
        IEnumerable<ReviewProgress> rows,
        Func<ReviewProgress, TKey> key,
        IComparer<TKey>? comparer = null)
    {
        var ordered = Descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);
        return ordered.ThenBy(p => p.ReviewId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Orders reviews by due date in the chosen direction with the reviews that have no due date last, whatever
    ///     the direction, then by review identifier.
    /// </summary>
    /// <param name="rows">The reviews.</param>
    /// <returns>The ordered reviews.</returns>
    private IOrderedEnumerable<ReviewProgress> OrderDue(IEnumerable<ReviewProgress> rows)
    {
        var withoutDue = rows.OrderBy(p => p.DueDate is null);
        return (Descending
                ? withoutDue.ThenByDescending(p => p.DueDate ?? DateOnly.MinValue)
                : withoutDue.ThenBy(p => p.DueDate ?? DateOnly.MinValue))
            .ThenBy(p => p.ReviewId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Builds the address of the overview with the filters and the sort order of the query string, except for the
    ///     parts the caller changes.
    /// </summary>
    /// <param name="sort">The sort column, or <c>null</c> to keep the current one.</param>
    /// <param name="descending">The sort direction when <paramref name="sort"/> is given.</param>
    /// <param name="page">The page, or <c>null</c> for the first.</param>
    /// <returns>The address, without the default values.</returns>
    private string Address(string? sort = null, bool descending = false, int? page = null)
    {
        var column = sort ?? SortColumn;
        var isDescending = sort is null ? Descending : descending;
        var parts = new List<string>();
        if (StageFilter is { } stage)
        {
            parts.Add("stage=" + StageToken(stage));
        }

        if (OverdueOnly)
        {
            parts.Add("overdue=1");
        }

        if (BlockedOnly)
        {
            parts.Add("blocked=1");
        }

        if (column != "id")
        {
            parts.Add("sort=" + column);
        }

        if (isDescending != (column == "activity"))
        {
            parts.Add("dir=" + (isDescending ? "desc" : "asc"));
        }

        if (SearchText.Length > 0)
        {
            parts.Add("q=" + Uri.EscapeDataString(SearchText));
        }

        if (page is > 1)
        {
            parts.Add("page=" + page);
        }

        return $"/projects/{ProjectId}" + (parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts));
    }

    /// <summary>
    ///     Builds the address a column header leads to: the column ascending, or flipped when it is the current one.
    /// </summary>
    /// <param name="column">The sort column of the header.</param>
    /// <returns>The address.</returns>
    private string SortAddress(string column)
    {
        var descending = column == SortColumn ? !Descending : column == "activity";
        return Address(column, descending);
    }

    /// <summary>
    ///     Gets the <c>aria-sort</c> value of a column header.
    /// </summary>
    /// <param name="column">The sort column of the header.</param>
    /// <returns>The value; <c>none</c> for a column that is not the sort column.</returns>
    private string AriaSort(string column)
    {
        if (column != SortColumn)
        {
            return "none";
        }

        return Descending ? "descending" : "ascending";
    }

    /// <summary>
    ///     Gets the query token of a stage.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The token used in the <c>stage</c> query value.</returns>
    private static string StageToken(ReviewStage stage)
    {
        return stage switch
        {
            ReviewStage.Setup => "setup",
            ReviewStage.InReview => "in-review",
            _ => "approved"
        };
    }

    /// <summary>
    ///     Gets the words of a stage for display.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The label.</returns>
    private static string StageLabel(ReviewStage stage)
    {
        return stage switch
        {
            ReviewStage.Setup => "Setup",
            ReviewStage.InReview => "In review",
            _ => "Approved"
        };
    }

    /// <summary>
    ///     Words what blocks a review.
    /// </summary>
    /// <param name="progress">The progress of the review.</param>
    /// <returns>
    ///     The missing checklist, open threads, unanswered checks and Fail answers; a dash when none or the review is
    ///     approved.
    /// </returns>
    private static string BlockersText(ReviewProgress progress)
    {
        if (progress.IsApproved)
        {
            return "-";
        }

        var parts = new List<string>();
        if (progress.NoChecklist)
        {
            parts.Add("No checklist");
        }

        if (progress.OpenThreads > 0)
        {
            parts.Add(progress.OpenThreads + (progress.OpenThreads == 1 ? " open thread" : " open threads"));
        }

        if (progress.UnansweredChecks > 0)
        {
            parts.Add(progress.UnansweredChecks + " unanswered");
        }

        if (progress.FailedChecks > 0)
        {
            parts.Add(progress.FailedChecks + " Fail");
        }

        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    /// <summary>
    ///     Describes the checklist of the project in one line, for the review rules card.
    /// </summary>
    /// <param name="actor">The signed-in user, who may administer the project.</param>
    /// <returns>
    ///     The version label and size, and whether it is the built-in default; a sentence when the stored definition
    ///     cannot be read.
    /// </returns>
    private async Task<string> DescribeChecklistAsync(Actor actor)
    {
        try
        {
            var checklist = await Checklists.GetAsync(actor, ProjectId);
            return ProjectChecklistPage.Describe(checklist) + (checklist.IsDefault ? ", built-in default" : ", custom");
        }
        catch (DomainRuleException)
        {
            // An unreadable stored definition (or a right lost just now) must not break the page
            return "cannot be read";
        }
    }

    /// <summary>
    ///     Re-checks, at the start of a member action on the open page, that the signed-in user may still view and
    ///     administer the project.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the action may go on. <c>false</c> when the view is gone (the page has dropped
    ///     everything and shows the not-found state) or only the administer right is gone (the page has dropped the
    ///     members section, and the project stays visible); the action must stop in both cases.
    /// </returns>
    /// <remarks>
    ///     The members are data only an administrator of the project may read, so they must not stay rendered once
    ///     the right is gone. The check is the shared guard, so it gives the answer a user without the right gets.
    /// </remarks>
    private async Task<bool> GuardMembersAsync()
    {
        // The view comes first: without it the whole page goes, not only the members section
        if (!await Access.GuardViewAsync(AuthenticationState, ProjectId, ShowNoAccess))
        {
            return false;
        }

        return await Access.GuardAdministerAsync(AuthenticationState, ProjectId, ShowMembersLost);
    }

    /// <summary>
    ///     Drops everything loaded and shows the same "does not exist" state as for a non-member.
    /// </summary>
    private void ShowNoAccess()
    {
        Project = null;
        Progress = [];
        Matching = [];
        PageRows = [];
        Repositories = [];
        CanContribute = false;
        ShowMembersLost();
        Loaded = true;
    }

    /// <summary>
    ///     Drops what only an administrator of the project may see: the members and the administration buttons.
    /// </summary>
    private void ShowMembersLost()
    {
        CanAdminister = false;
        CanAudit = false;
        ChecklistSummary = null;
        Members = [];
        MemberError = null;
        MemberStatus = null;
        RulesError = null;
        RulesStatus = null;
    }

    /// <summary>
    ///     Saves the review rules (independent reviewers, and the two stored-only rules).
    /// </summary>
    /// <returns>A task that completes when the rules are stored or the problem is shown.</returns>
    /// <remarks>
    ///     Only while the user may still view and administer the project. Each changed rule is a compare-and-set
    ///     against the value the page loaded, so two administrators cannot silently overwrite each other; a stale
    ///     value is shown as an error and the page reloads the stored values. The rules are saved one after the other,
    ///     not atomically: rules saved before a refused one stay saved and their confirmation stays visible.
    /// </remarks>
    private async Task SaveReviewRulesAsync()
    {
        RulesError = null;
        RulesStatus = null;
        if (!await GuardMembersAsync())
        {
            return;
        }

        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            var loaded = Project;
            var messages = new List<string>();
            foreach (var (rule, expected, choice) in new[]
                     {
                         (ReviewRule.IndependentReviewers, loaded?.RequireIndependentReviewers ?? true, RequireIndependentChoice),
                         (ReviewRule.NonAuthorChecklistAnswers, loaded?.RequireNonAuthorChecklistAnswers ?? true, RequireNonAuthorChoice),
                         (ReviewRule.NonReopenerSigning, loaded?.RequireNonReopenerSigning ?? true, RequireNonReopenerChoice),
                     })
            {
                if (expected == choice
                    || !await ProjectService.SetReviewRuleAsync(actor, ProjectId, rule, expected, choice))
                {
                    continue;
                }

                messages.Add(DescribeSavedRule(rule, choice));
                RulesStatus = string.Join(' ', messages);
            }

            Project = await ProjectService.GetAsync(actor, ProjectId);
            RulesStatus = messages.Count > 0 ? string.Join(' ', messages) : "The review rules were already set this way.";
        }
        catch (DomainRuleException ex)
        {
            // A refusal can also mean the right was lost during the call; ask again so that the error is shown only
            // when the user may still see the section
            if (await GuardMembersAsync())
            {
                RulesError = ex.Message;
                var actor = await AuthenticationState.GetActorAsync();
                Project = await ProjectService.GetAsync(actor, ProjectId);
                RequireIndependentChoice = Project?.RequireIndependentReviewers ?? true;
                RequireNonAuthorChoice = Project?.RequireNonAuthorChecklistAnswers ?? true;
                RequireNonReopenerChoice = Project?.RequireNonReopenerSigning ?? true;
            }
        }
    }

    /// <summary>
    ///     Words the confirmation of one stored review rule change.
    /// </summary>
    /// <param name="rule">The rule that was changed.</param>
    /// <param name="value">The value it now has.</param>
    /// <returns>The sentence for the status line.</returns>
    private static string DescribeSavedRule(ReviewRule rule, bool value)
    {
        var state = value ? "on" : "off";
        return rule switch
        {
            ReviewRule.IndependentReviewers => value
                ? "Independent reviewers are now required."
                : "Independent reviewers are no longer required.",
            ReviewRule.NonAuthorChecklistAnswers =>
                $"Authors cannot answer the checklist: now {state} (stored; not applied yet).",
            _ => $"The person who reopened a review cannot sign it again: now {state} (stored; not applied yet).",
        };
    }

    /// <summary>
    ///     Removes a member from the project.
    /// </summary>
    /// <param name="member">The member as shown.</param>
    /// <returns>A task that completes when the member is removed or the problem is shown.</returns>
    private Task<bool> RemoveMemberAsync(ProjectMember member)
    {
        return RunMemberChangeAsync(
            actor => ProjectService.RemoveMemberAsync(actor, ProjectId, member.Username),
            $"Removed '{member.Username}' from the project.");
    }

    /// <summary>
    ///     Switches a member between project admin and plain member; an auditor is never switched by this toggle.
    /// </summary>
    /// <param name="member">The member as shown.</param>
    /// <returns>A task that completes when the role is changed or the problem is shown.</returns>
    private Task<bool> ToggleRoleAsync(ProjectMember member)
    {
        // An auditor would otherwise be promoted by accident; the page offers explicit buttons for an auditor
        if (member.Role == ProjectRole.Auditor)
        {
            return Task.FromResult(false);
        }

        return SetRoleAsync(member, member.Role == ProjectRole.ProjectAdmin ? ProjectRole.User : ProjectRole.ProjectAdmin);
    }

    /// <summary>
    ///     Gives a member an explicit role.
    /// </summary>
    /// <param name="member">The member as shown.</param>
    /// <param name="role">The new role.</param>
    /// <returns>A task that completes when the role is changed or the problem is shown.</returns>
    /// <remarks>
    ///     A refusal, such as the last project admin or an auditor-to-be who still has reviews to sign, is shown as
    ///     the error of the section.
    /// </remarks>
    private Task<bool> SetRoleAsync(ProjectMember member, ProjectRole role)
    {
        return RunMemberChangeAsync(
            actor => ProjectService.SetMemberRoleAsync(actor, ProjectId, member.Username, role),
            $"'{member.Username}' is now {RoleText(role)}.");
    }

    /// <summary>
    ///     Gets the words used for a project role.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The text shown to the user.</returns>
    private static string RoleText(ProjectRole role)
    {
        return role switch
        {
            ProjectRole.ProjectAdmin => "project admin",
            ProjectRole.Auditor => "auditor",
            _ => "member"
        };
    }

    /// <summary>
    ///     Gets the label of the role badge of a member.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <returns>The text shown to the user.</returns>
    private static string RoleLabel(ProjectRole role)
    {
        return role switch
        {
            ProjectRole.ProjectAdmin => "Project admin",
            ProjectRole.Auditor => "Auditor",
            _ => "Member"
        };
    }

    /// <summary>
    ///     Runs a member change, shows its outcome, and reloads the member list.
    /// </summary>
    /// <param name="change">The change; it receives the signed-in user as the actor to audit.</param>
    /// <param name="success">The message to show when the change is stored.</param>
    /// <returns><c>true</c> when the change was stored.</returns>
    /// <remarks>
    ///     The change runs only while the user may still view and administer the project; when either is gone the
    ///     page drops what the user may no longer see instead of showing an error next to it. The status is shown
    ///     only after the list has been reloaded, so it never stands next to a stale list. A refusal such as the
    ///     last-project-admin rule is shown as the error of the section.
    /// </remarks>
    private async Task<bool> RunMemberChangeAsync(Func<Actor, Task> change, string success)
    {
        MemberError = null;
        MemberStatus = null;
        if (!await GuardMembersAsync())
        {
            return false;
        }

        try
        {
            var actor = await AuthenticationState.GetActorAsync();
            await change(actor);
            await LoadMembersAsync(actor);
            MemberStatus = success;
            return true;
        }
        catch (DomainRuleException ex)
        {
            // A refusal can also mean the right was lost during the call (or the sign-in ended); ask again so that
            // the error is shown only when the user may still see the section
            if (await GuardMembersAsync())
            {
                MemberError = ex.Message;
            }

            return false;
        }
    }

    /// <summary>
    ///     Loads the member list; only an administrator of the project manages members.
    /// </summary>
    /// <param name="actor">The signed-in user.</param>
    /// <returns>A task that completes when the list is loaded.</returns>
    private async Task LoadMembersAsync(Actor actor)
    {
        Members = CanAdminister ? await ProjectService.ListMembersAsync(actor, ProjectId) : [];
    }

    /// <summary>
    ///     Marks the page interactive after it has rendered on a live circuit.
    /// </summary>
    /// <param name="firstRender"><c>true</c> for the first render of this component instance.</param>
    /// <remarks>
    ///     <c>OnAfterRender</c> is never invoked during static prerendering, so reaching it proves that
    ///     the circuit is connected. A second render makes the new marker value reach the browser.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        // Only the first render transitions the marker; later renders must not loop
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }
}
