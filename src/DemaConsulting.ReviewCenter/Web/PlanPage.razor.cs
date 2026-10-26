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
///     Code-behind for the plan page (<c>PlanPage.razor</c>): shows which review-sets of a repository's
///     <c>.reviewmark.yaml</c> need a review at a head revision and creates the ones the user ticks.
/// </summary>
/// <remarks>
///     <para>
///         The page is built for a quick read: one plain summary sentence first, the sets that need a review (ticked
///         by default) next, then the sets that cannot be planned and the ones already filed, and the sets with nothing
///         to review last, collapsed, with a count. Every failure is shown in plain English with what to do next;
///         the page never shows git output, a stack trace or a repository location.
///     </para>
///     <para>
///         The page talks to the domain only through <see cref="ReviewPlanner"/>, <see cref="ProjectService"/> and
///         <see cref="RepositoryService"/>. The plan is held in the component, so ticking rows never reads git again;
///         changing the repository, head or baseline discards the plan so the screen never disagrees with the form.
///         Reviews are created from the commits of the plan, never from branch names, and each review is stored on
///         its own: a failing row does not stop or undo the others. Each browser connection (circuit) gets its own
///         page instance, so the state here is per-user and needs no synchronization. The page requires a signed-in
///         user.
///     </para>
/// </remarks>
public partial class PlanPage : ComponentBase
{
    /// <summary>
    ///     The number of rows from which "select all" and "select none" are offered.
    /// </summary>
    private const int BulkSelectionThreshold = 3;

    /// <summary>
    ///     The number of characters of a commit shown to the user.
    /// </summary>
    private const int ShortShaLength = 7;

    /// <summary>
    ///     The message shown for a failure that is not a rule of the domain; it carries no internal detail.
    /// </summary>
    private const string UnexpectedMessage = "Something unexpected went wrong. Nothing was changed by this step; try again, and tell an administrator if it keeps happening.";

    /// <summary>
    ///     The rows ticked in the plan, by review-set identifier.
    /// </summary>
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);

    /// <summary>
    ///     Backing field for <see cref="InteractiveMarker"/>; flips to <c>true</c> after the first render
    ///     on a live circuit.
    /// </summary>
    private bool _interactive;

    /// <summary>
    ///     The project identity whose data is currently loaded, or <c>null</c> before the first load.
    /// </summary>
    private int? _loadedProjectId;

    /// <summary>
    ///     Counts the plans, creates and discards; a call that finds it changed after an await was overtaken (the
    ///     project or the form changed) and must not touch the page.
    /// </summary>
    private int _version;

    /// <summary>
    ///     Backing field of <see cref="RepositoryIdText"/>.
    /// </summary>
    private string _repositoryIdText = string.Empty;

    /// <summary>
    ///     Backing field of <see cref="HeadRevision"/>.
    /// </summary>
    private string _headRevision = string.Empty;

    /// <summary>
    ///     Backing field of <see cref="BaselineRevision"/>.
    /// </summary>
    private string _baselineRevision = string.Empty;

    /// <summary>
    ///     Backing field of <see cref="Mode"/>.
    /// </summary>
    private BaselineMode _mode = BaselineMode.LastReviewed;

    /// <summary>
    ///     Gets or sets the identity of the project to plan for, from the page address.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the service that reads projects, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ProjectService ProjectService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the repository registry, supplied by dependency injection; used only to list the enabled repositories.
    /// </summary>
    [Inject]
    private RepositoryService RepositoryService { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the planner that computes the plan and creates the reviews, supplied by dependency injection.
    /// </summary>
    [Inject]
    private ReviewPlanner Planner { get; set; } = null!;

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
    ///     Gets the project planned for, or <c>null</c> when it does not exist.
    /// </summary>
    private Project? Project { get; set; }

    /// <summary>
    ///     Gets the enabled repositories a plan can be made for (name only; never location or credentials).
    /// </summary>
    private IReadOnlyList<RepositorySummary> Repositories { get; set; } = [];

    /// <summary>
    ///     Gets or sets the identity of the chosen repository as the text of the select element; empty for none.
    /// </summary>
    /// <remarks>Changing it discards the plan, the results and the error.</remarks>
    private string RepositoryIdText
    {
        get => _repositoryIdText;
        set
        {
            _repositoryIdText = value;
            Discard();
        }
    }

    /// <summary>
    ///     Gets or sets the head revision typed into the form.
    /// </summary>
    /// <remarks>Changing it discards the plan, the results and the error.</remarks>
    private string HeadRevision
    {
        get => _headRevision;
        set
        {
            _headRevision = value;
            Discard();
        }
    }

    /// <summary>
    ///     Gets or sets the explicit baseline revision typed into the form.
    /// </summary>
    /// <remarks>Changing it discards the plan, the results and the error.</remarks>
    private string BaselineRevision
    {
        get => _baselineRevision;
        set
        {
            _baselineRevision = value;
            Discard();
        }
    }

    /// <summary>
    ///     Gets the chosen baseline mode.
    /// </summary>
    private BaselineMode Mode => _mode;

    /// <summary>
    ///     Gets a value indicating whether the plan is being read or the reviews are being created.
    /// </summary>
    private bool Busy { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the plan (not the creation) is what is running.
    /// </summary>
    private bool Reading { get; set; }

    /// <summary>
    ///     Gets the plan shown, or <c>null</c> when none has been made for the current form.
    /// </summary>
    private ReviewPlan? Plan { get; set; }

    /// <summary>
    ///     Gets the results of the last creation, or <c>null</c> when nothing was created for the current plan.
    /// </summary>
    private IReadOnlyList<PlanCreateResult>? Results { get; set; }

    /// <summary>
    ///     Gets the plain-English message of the last failure, or <c>null</c> when there is none.
    /// </summary>
    private string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets what to do about <see cref="ErrorMessage"/>, or <c>null</c> when there is no advice.
    /// </summary>
    private string? ErrorHint { get; set; }

    /// <summary>
    ///     Gets the kind of the last failure: the name of the domain rule, <c>Input</c> or <c>Unexpected</c>.
    /// </summary>
    private string ErrorKind { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the value rendered into the <c>data-interactive</c> attribute.
    /// </summary>
    /// <remarks>
    ///     During static prerendering no circuit exists and events cannot be handled; drivers wait for
    ///     <c>true</c> before interacting.
    /// </remarks>
    private string InteractiveMarker => _interactive ? "true" : "false";

    /// <summary>
    ///     Gets the rows that need a review: the ones a user can tick.
    /// </summary>
    private IEnumerable<PlanRow> TodoRows => RowsWith(PlanRowStatus.WillCreate);

    /// <summary>
    ///     Gets the rows that cannot be planned.
    /// </summary>
    private IEnumerable<PlanRow> ProblemRows => RowsWith(PlanRowStatus.CannotPlan);

    /// <summary>
    ///     Gets the rows whose review already exists.
    /// </summary>
    private IEnumerable<PlanRow> FiledRows => RowsWith(PlanRowStatus.AlreadyFiled);

    /// <summary>
    ///     Gets the rows with nothing to review.
    /// </summary>
    private IEnumerable<PlanRow> EmptyRows => RowsWith(PlanRowStatus.NothingToReview);

    /// <summary>
    ///     Gets the number of ticked rows.
    /// </summary>
    private int TickedCount => _ticked.Count;

    /// <summary>
    ///     Gets a value indicating whether "select all" and "select none" are offered.
    /// </summary>
    private bool OffersBulkSelection => Plan is not null && Plan.WillCreateCount > BulkSelectionThreshold;

    /// <summary>
    ///     Gets the text of the create button, with the live count of reviews it would create.
    /// </summary>
    private string CreateButtonText
    {
        get
        {
            if (Busy && !Reading)
            {
                return "Creating...";
            }

            return TickedCount == 1 ? "Create 1 review" : $"Create {TickedCount} reviews";
        }
    }

    /// <summary>
    ///     Gets the one-sentence summary of the plan, for example <c>3 sets need review, 1 already filed, 9 have nothing to review.</c>.
    /// </summary>
    private string SummaryText
    {
        get
        {
            var plan = Plan!;
            var parts = new List<string>
            {
                plan.WillCreateCount switch
                {
                    0 => "no set needs review",
                    1 => "1 set needs review",
                    var count => $"{count} sets need review"
                }
            };
            if (plan.AlreadyFiledCount > 0)
            {
                parts.Add($"{plan.AlreadyFiledCount} already filed");
            }

            if (plan.NothingCount > 0)
            {
                parts.Add(plan.NothingCount == 1 ? "1 has nothing to review" : $"{plan.NothingCount} have nothing to review");
            }

            if (plan.CannotPlanCount > 0)
            {
                parts.Add(plan.CannotPlanCount == 1 ? "1 cannot be planned" : $"{plan.CannotPlanCount} cannot be planned");
            }

            return Sentence(parts);
        }
    }

    /// <summary>
    ///     Gets the muted line that says what the plan is about: the repository, the head and the baseline.
    /// </summary>
    private string FactsText
    {
        get
        {
            var plan = Plan!;
            var baseline = plan.Mode switch
            {
                BaselineMode.None => "all files (no baseline)",
                BaselineMode.Explicit => $"changes since {plan.ExplicitBaselineRevision} ({Short(plan.ExplicitBaselineSha!)})",
                _ => "changes since the last signed review of each set"
            };
            return $"Repository {plan.RepositoryName}, head {plan.HeadRevision} ({Short(plan.HeadSha)}). Baseline: {baseline}.";
        }
    }

    /// <summary>
    ///     Gets the sentence that tells the user what to do with this plan.
    /// </summary>
    private string NextStepText
    {
        get
        {
            var plan = Plan!;
            if (plan.WillCreateCount > 0)
            {
                return "Clear the tick of the sets you do not want to review now, then create the reviews.";
            }

            return plan.CannotPlanCount > 0
                ? "Nothing can be created yet. Fix the sets under \"Cannot be planned\" in .reviewmark.yaml, or try another revision."
                : "Nothing to create here. Try another head revision or another baseline.";
        }
    }

    /// <summary>
    ///     Gets the one-sentence summary of the creation, for example <c>2 reviews created, 1 failed.</c>.
    /// </summary>
    private string ResultSummaryText
    {
        get
        {
            var results = Results!;
            var created = results.Count(result => result.Outcome == PlanCreateOutcome.Created);
            var filed = results.Count(result => result.Outcome == PlanCreateOutcome.AlreadyFiled);
            var failed = results.Count(result => result.Outcome == PlanCreateOutcome.Failed);
            var parts = new List<string>
            {
                created switch
                {
                    0 => "no review was created",
                    1 => "1 review created",
                    var count => $"{count} reviews created"
                }
            };
            if (filed > 0)
            {
                parts.Add($"{filed} already filed");
            }

            if (failed > 0)
            {
                parts.Add($"{failed} failed");
            }

            return Sentence(parts);
        }
    }

    /// <summary>
    ///     Gets the sentence that tells the user what to do after the creation.
    /// </summary>
    private string ResultNextStepText => Results!.Any(result => result.Outcome == PlanCreateOutcome.Failed)
        ? "The failed sets were not created. Plan again to offer them once more."
        : "Open a review to start, or go back to the project.";

    /// <summary>
    ///     Loads the project and the enabled repositories whenever the project identity changes.
    /// </summary>
    /// <returns>A task that completes when the data is loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        // The router reuses this instance when only the project id changes; drop the previous project's
        // data and form state so it can neither be shown nor submitted into the new project
        if (_loadedProjectId == ProjectId)
        {
            return;
        }

        _loadedProjectId = ProjectId;
        Loaded = false;
        _repositoryIdText = string.Empty;
        _headRevision = string.Empty;
        _baselineRevision = string.Empty;
        _mode = BaselineMode.LastReviewed;

        // A plan or create call of the previous project may still be awaited: forget it (its result is ignored
        // because the version moves on) and make the form usable for the new project
        Busy = false;
        Reading = false;
        Discard();
        var projectId = ProjectId;

        var project = await ProjectService.GetAsync(projectId);
        var repositories = await RepositoryService.ListEnabledAsync();
        if (_loadedProjectId != projectId)
        {
            // The project changed again while loading; the newer call loads its own data
            return;
        }

        Project = project;
        Repositories = repositories;

        // With a single repository there is nothing to choose, so one step less for the user
        if (Repositories.Count == 1)
        {
            _repositoryIdText = Repositories[0].Id.ToString(CultureInfo.InvariantCulture);
        }

        Loaded = true;
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

    /// <summary>
    ///     Chooses how baselines are picked.
    /// </summary>
    /// <param name="mode">The baseline mode.</param>
    /// <remarks>Discards the plan, the results and the error, because they were made for another mode.</remarks>
    private void ChooseMode(BaselineMode mode)
    {
        _mode = mode;
        Discard();
    }

    /// <summary>
    ///     Reads the plan for the form values and shows it.
    /// </summary>
    /// <returns>A task that completes when the plan is shown or the problem is explained.</returns>
    /// <remarks>
    ///     Every rule violation (unknown revision, no configuration, invalid configuration, disabled repository
    ///     and so on) is shown as a plain message with what to do next. The form keeps what was entered.
    /// </remarks>
    private async Task LoadPlanAsync()
    {
        // A second submit while the first still runs (a fetch can take a while) would only repeat the work
        if (Busy)
        {
            return;
        }

        Discard();

        // Check the form first, with a message that says what is missing, before any git work
        if (!int.TryParse(RepositoryIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var repositoryId))
        {
            ShowInputError("Choose a repository.");
            return;
        }

        if (string.IsNullOrWhiteSpace(HeadRevision))
        {
            ShowInputError("Enter the head revision: a branch, tag or commit, for example main.");
            return;
        }

        if (Mode == BaselineMode.Explicit && string.IsNullOrWhiteSpace(BaselineRevision))
        {
            ShowInputError("Enter the baseline revision, or choose another baseline.");
            return;
        }

        Busy = true;
        Reading = true;
        var version = _version;
        var projectId = ProjectId;
        try
        {
            var plan = await Planner.PlanAsync(projectId, repositoryId, HeadRevision, Mode, BaselineRevision);
            if (version != _version)
            {
                return;
            }

            // Every set that needs a review starts ticked: the usual case is to create them all
            _ticked.Clear();
            foreach (var row in plan.Rows.Where(row => row.Status == PlanRowStatus.WillCreate))
            {
                _ticked.Add(row.SetId);
            }

            Plan = plan;
        }
        catch (DomainRuleException ex) when (version == _version)
        {
            ShowRuleError(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && version == _version)
        {
            ShowUnexpectedError();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Overtaken: the page already shows something else, so the outcome of this call is dropped
        }
        finally
        {
            // An overtaken call must not release the busy state of the call that overtook it
            if (version == _version)
            {
                Busy = false;
                Reading = false;
            }
        }
    }

    /// <summary>
    ///     Creates the reviews of the ticked rows and shows one result per review.
    /// </summary>
    /// <returns>A task that completes when every ticked row has a result.</returns>
    /// <remarks>
    ///     The reviews are created from the commits of the plan. A row that cannot be created is reported with its
    ///     reason; the other rows are still created. If the whole batch cannot start the message is shown and the
    ///     plan stays so the user can try again.
    /// </remarks>
    private async Task CreateReviewsAsync()
    {
        if (Busy || Plan is null || _ticked.Count == 0)
        {
            return;
        }

        var plan = Plan;
        var projectId = ProjectId;
        var version = _version;
        ErrorMessage = null;
        ErrorHint = null;
        Busy = true;
        try
        {
            // The actor is audited; asking for it also refuses an ended sign-in state
            var actor = await AuthenticationState.GetActorAsync();
            var requests = TodoRows
                .Where(row => _ticked.Contains(row.SetId))
                .Select(row => new PlanCreateRequest(row.SetId, row.Title, row.Selection, row.BaselineSha))
                .ToList();
            var results = await Planner.CreateAsync(projectId, plan.RepositoryId, plan.HeadSha, requests, actor);
            if (version == _version)
            {
                Results = results;
            }
        }
        catch (DomainRuleException ex) when (version == _version)
        {
            ShowRuleError(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && version == _version)
        {
            ShowUnexpectedError();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Overtaken: the page already shows something else, so the outcome of this call is dropped
        }
        finally
        {
            // An overtaken call must not release the busy state of the call that overtook it
            if (version == _version)
            {
                Busy = false;
            }
        }
    }

    /// <summary>
    ///     Sets or clears the tick of one row.
    /// </summary>
    /// <param name="setId">The review-set identifier of the row.</param>
    /// <param name="ticked"><c>true</c> to tick the row.</param>
    private void SetTicked(string setId, bool ticked)
    {
        if (ticked)
        {
            _ticked.Add(setId);
        }
        else
        {
            _ticked.Remove(setId);
        }
    }

    /// <summary>
    ///     Ticks every row that can be created, or none.
    /// </summary>
    /// <param name="all"><c>true</c> to tick every row; <c>false</c> to clear every tick.</param>
    private void SetAllTicked(bool all)
    {
        _ticked.Clear();
        if (all)
        {
            foreach (var row in TodoRows)
            {
                _ticked.Add(row.SetId);
            }
        }
    }

    /// <summary>
    ///     Tells whether a row is ticked.
    /// </summary>
    /// <param name="setId">The review-set identifier of the row.</param>
    /// <returns><c>true</c> when the row is ticked.</returns>
    private bool IsTicked(string setId) => _ticked.Contains(setId);

    /// <summary>
    ///     Gets the title of a review-set of the current plan, for the results list.
    /// </summary>
    /// <param name="setId">The review-set identifier.</param>
    /// <returns>The title, or the identifier when the plan has no such row.</returns>
    private string TitleOf(string setId) => Plan?.Rows.FirstOrDefault(row => row.SetId == setId)?.Title ?? setId;

    /// <summary>
    ///     Describes how many files changed in a row, for example <c>3 changed files (2 added, 1 modified)</c>.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The description.</returns>
    private static string CountsText(PlanRow row)
    {
        var kinds = new List<string>();
        if (row.Added > 0)
        {
            kinds.Add($"{row.Added} added");
        }

        if (row.Modified > 0)
        {
            kinds.Add($"{row.Modified} modified");
        }

        if (row.Deleted > 0)
        {
            kinds.Add($"{row.Deleted} deleted");
        }

        var noun = row.ChangedFiles == 1 ? "changed file" : "changed files";
        return $"{row.ChangedFiles} {noun} ({string.Join(", ", kinds)})";
    }

    /// <summary>
    ///     Gets the rows of the plan with a status, in the order of the configuration file.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The rows; empty when there is no plan.</returns>
    private IEnumerable<PlanRow> RowsWith(PlanRowStatus status) =>
        Plan is null ? [] : Plan.Rows.Where(row => row.Status == status);

    /// <summary>
    ///     Joins the parts of a summary into a capitalized sentence.
    /// </summary>
    /// <param name="parts">The parts, at least one, in lower case.</param>
    /// <returns>The sentence, ending with a period.</returns>
    private static string Sentence(IReadOnlyList<string> parts)
    {
        var text = string.Join(", ", parts) + ".";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    ///     Shortens a commit identifier for display.
    /// </summary>
    /// <param name="sha">The commit identifier.</param>
    /// <returns>The first seven characters.</returns>
    private static string Short(string sha) => sha.Length > ShortShaLength ? sha[..ShortShaLength] : sha;

    /// <summary>
    ///     Discards the plan, the ticks, the results and the error, because the form no longer matches them.
    /// </summary>
    private void Discard()
    {
        // Any plan or create call still awaited was started for what is discarded: its result must be ignored
        _version++;
        Plan = null;
        Results = null;
        _ticked.Clear();
        ErrorMessage = null;
        ErrorHint = null;
        ErrorKind = string.Empty;
    }

    /// <summary>
    ///     Shows a message about the form itself.
    /// </summary>
    /// <param name="message">The message.</param>
    private void ShowInputError(string message)
    {
        ErrorMessage = message;
        ErrorHint = null;
        ErrorKind = "Input";
    }

    /// <summary>
    ///     Shows a failure that is not a rule of the domain, with no detail of it.
    /// </summary>
    private void ShowUnexpectedError()
    {
        ErrorMessage = UnexpectedMessage;
        ErrorHint = null;
        ErrorKind = "Unexpected";
    }

    /// <summary>
    ///     Shows a rule violation as a plain message with what to do next.
    /// </summary>
    /// <param name="exception">The rule violation.</param>
    /// <remarks>
    ///     The text of a domain rule is written for users. The one exception is a git access failure, whose text may
    ///     carry the first line git printed; it is replaced by a fixed sentence so that no raw git text reaches the page.
    /// </remarks>
    private void ShowRuleError(DomainRuleException exception)
    {
        ErrorKind = exception.Rule.ToString();
        (ErrorMessage, ErrorHint) = exception.Rule switch
        {
            DomainRule.GitAccessFailed => (
                ReviewPlanner.GitAccessFailedMessage,
                "Ask an administrator to check the repository's address, user name and token."),
            DomainRule.RepositoryDisabled => (
                exception.Message,
                "An administrator must enable it on the Repositories page, or choose another repository."),
            DomainRule.RepositoryNotFound => (
                exception.Message,
                "Choose another repository."),
            DomainRule.RevisionNotFound or DomainRule.RevisionInvalid => (
                exception.Message,
                "Check the spelling of the branch, tag or commit, and that it exists in this repository."),
            DomainRule.ReviewSetConfigMissing => (
                exception.Message + " There are no review-sets to plan.",
                "Add a .reviewmark.yaml with a list of review-sets to that revision, or create a review by hand from the project page."),
            DomainRule.ReviewSetConfigInvalid => (
                exception.Message,
                "Fix the file in the repository, then press Show plan again."),
            DomainRule.GitUnavailable or DomainRule.GitTimeout or DomainRule.CredentialsUnreadable => (
                exception.Message,
                "Try again. If it keeps failing, tell an administrator."),
            _ => (exception.Message, null)
        };
    }
}
