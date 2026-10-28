### PlanPage

![Web Structure](WebView.svg)

#### Purpose

`PlanPage` is the page at `/projects/{id}/plan`. Its responsibility is to show which review-sets of a git
repository of the project need a review at a head revision, and to create the reviews the user ticks. It is built so
that a user can
see at once what matters and what to do next: one plain summary sentence, then the sets that need a review (ticked by
default), then the rest in groups that are shown only when they are not empty, with the sets that have nothing to
review collapsed behind a count. It contains no planning rule: the baseline choice, the classification of the sets, the
identifiers, and the creation are in `ReviewPlanner`. It requires a signed-in user, and a project the user may not see
looks like a project that does not exist (the not-found message, no form, no repository). It is a Razor component
split into
presentation (`PlanPage.razor`) and behavior (`PlanPage.razor.cs`).

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**: `int` — the route parameter.

**Loaded**, **Project**, **Repositories**: whether loading finished, the project (`null` when it does not exist or the
user may not see it), and
the enabled repositories of the project (`RepositorySummary` values, which carry no location and no token). A single
enabled
repository is preselected.

**RepositoryIdText**, **HeadRevision**, **BaselineRevision**, **Mode**: the form values. The mode is one of `None`,
`Explicit`, and `LastReviewed` (the default). Setting any of them discards the plan, the results, the ticks, and the
error, so the screen never disagrees with the form.

**Busy**, **Reading**: set while the plan is read or the reviews are created, so a second submit is ignored; `Reading`
tells the plan from the creation.

**Version**: a counter that every discard increments (a form change, a new plan request, and a change of the project).
A plan or create call remembers it before it awaits and ignores its outcome (plan, results, error and the release of the
busy state) when the counter moved, so an answer for the previous project or inputs can never be shown. Changing the
project also clears `Busy` and `Reading`, so the form of the new project is usable at once.

**Plan**: the `ReviewPlan` shown, or `null`. **Ticked**: the identifiers of the ticked rows. **Results**: the
`PlanCreateResult` values of the last creation, or `null`.

**ErrorMessage**, **ErrorHint**, **ErrorKind**: the plain-English message of the last failure, what to do about it, and
its kind (the name of the domain rule, `Input`, or `Unexpected`), which the error box carries as `data-error-kind`.

**InteractiveMarker**: as for the other pages.

Test identifiers: `plan-page` (root), `breadcrumb-projects-link`, `plan-breadcrumb-project`, `plan-page-loading`,
`plan-project-not-found`, `plan-heading`, `plan-help`, `plan-no-repository`; the form: `plan-form`,
`plan-repository-select`, `plan-head-input`, `plan-baseline` (the group), `plan-baseline-none`,
`plan-baseline-explicit`, `plan-baseline-last`, `plan-baseline-input` (only for the explicit mode), and
`plan-load-button`; the waiting state and the errors: `plan-loading`, `plan-error` (with `data-error-kind`),
`plan-error-message`, `plan-error-hint`, and `plan-error-project-link`; the overview: `plan-overview`, `plan-summary`,
`plan-facts`, and `plan-next-step`; the sets that need a review: `plan-todo`, `plan-todo-list`, `plan-select-all` and
`plan-select-none` (for more than three rows), per row `plan-row-{set}`, `plan-row-check-{set}`,
`plan-row-title-{set}`, `plan-row-note-{set}`, and `plan-row-counts-{set}`, then `plan-create-button` and
`plan-create-hint`; the sets that cannot be planned: `plan-problems`, `plan-problem-list`, `plan-problem-{set}`, and
`plan-problem-reason-{set}`; the sets already filed: `plan-filed-list`, `plan-filed-toggle`, `plan-filed-{set}`, and
`plan-filed-link-{set}`; the sets with nothing to review: `plan-empty-list`, `plan-empty-toggle`, and
`plan-empty-{set}`; and the results: `plan-results`, `plan-result-summary`, `plan-result-next-step`,
`plan-result-list`, per result `plan-result-{set}` (with `data-outcome`), `plan-result-status-{set}`,
`plan-result-message-{set}`, and `plan-result-link-{set}`, then `plan-back-link` and `plan-again-button`. `{set}` is the
review-set identifier.

The form asks for the repository (a drop-down of enabled repositories by name), the head revision, and the baseline: **None**
(every selected file counts as added), **Explicit revision** (a text box appears), or **Last reviewed** (the changes
since the last signed review of each set, the default).

#### Key Methods

**OnParametersSetAsync**: Loads the project, for the signed-in user, and the enabled repositories of the project when
the project identity changes. When the
router reuses the component for another project, it first discards the form values, the plan, the results, and the error.
A project that does not exist, or that the user may not see, leaves `Project` null and the page shows the same message;
when the project has no enabled repository the page
shows a plain message ("This project has no repository; ask a project administrator to add one.") instead of the form.

**LoadPlanAsync**: Checks the form (a repository, a head revision, and for the explicit mode a baseline revision), each
with a message that says what is missing, and then calls `ReviewPlanner.PlanAsync` on behalf of the signed-in user. On
success every row that needs a
review is ticked. The button shows "Reading repository..." while git is read, and every form control is disabled while
the page is busy (reading or creating), so the inputs cannot change under a request that is still running and a late
answer cannot replace the plan of other inputs.

**CreateReviewsAsync**: Asks for the signed-in user (`GetActorAsync`, which also refuses an ended sign-in state), builds
one `PlanCreateRequest` for every ticked row, and calls `ReviewPlanner.CreateAsync` with the commit of the plan, never a
branch name. The plan is then replaced by the results: one row per request, with a link to the review for a created or an
already filed one, and the reason for a failed one, with a button to plan again and a link back to the project. When the
whole batch is refused (for example the repository was disabled after the plan was shown), the message is shown next to
the create button and the plan and ticks stay, so the user can try again. While the batch is created the form, the ticks,
and select all and none are disabled.

**SetTicked**, **SetAllTicked**: Set or clear the tick of one row, or of all rows. Ticking never reads git again. Select
all and select none are offered only for more than three rows.

**SummaryText**, **FactsText**, **NextStepText**: Build the one-sentence summary ("3 sets need review, 1 already
filed, 9
have nothing to review."; a part for the sets that cannot be planned appears only when there is one), the line with the
repository, the head, and the baseline used, and the sentence that says what to do next.

**ShowRuleError**: Maps a `DomainRuleException` to a message and a hint in plain English, for a repository that is not
registered or disabled, a revision that is invalid or unknown, a head without a `.reviewmark.yaml` (with a link to the
project page, where a review can be made by hand), an invalid configuration, and the git rules. The text of a git access
failure may carry the first line that git printed, so it is replaced by a fixed sentence
(`ReviewPlanner.GitAccessFailedMessage`, the same sentence a failed row of a creation shows).

**OnAfterRender**: Marks the page interactive after the first render on a live connection.

#### Error Handling

A `DomainRuleException` is shown as a plain message with what to do next. Any other exception that is not a
cancellation is shown as a generic message that carries no exception text. The page never shows text printed by git, a
repository location, a credential, or a stack trace. A failure while a plan is read creates nothing. A failure of one
review of a creation is a result of its own, with the same fixed sentence for a git access failure; the others are still
created.

**Known limitation**: The "already filed" check is made again just before each review is created, but the database has no
uniqueness on the set, the head, and the baseline, so two users (or two tabs) who create the same range at the same
instant can both succeed; the second review gets the identifier suffix `-2`. The page also passes no cancellation token,
so when a circuit is closed a running read finishes until the git timeout.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **ProjectService** — reads the project for the signed-in user.
- **RepositoryService** — lists the enabled repositories of the project (names only).
- **ReviewPlanner** (Core) — makes the plan and creates the reviews.
- **SignIn** — `GetActorAsync` supplies the signed-in user.
- **ASP.NET Core Blazor components** — the component base class, the authorization attribute, and the interactive
  server render mode.

#### Callers

- **App** — the router renders the page for `/projects/{id}/plan`; an anonymous visitor is sent to the login page first.
- **ProjectPage** — links to the page.
