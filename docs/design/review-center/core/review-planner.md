### ReviewPlanner

#### Purpose

`ReviewPlanner` works out which reviews a head revision of a registered repository needs and creates the ones that a
user picks. Its single responsibility is the planning: it reads the review-sets from the `.reviewmark.yaml` of the head
commit, chooses a baseline for each set, tells what each set needs, and hands the creation of every review to
`ReviewService`, so that a planned review has exactly the rules, the frozen content, and the audit entry of a review
created by hand.

#### Data Model

`ReviewPlanner` is a public sealed class that holds no state and is safe for concurrent use. Its dependencies are the
review store (to list the reviews of a project), `ReviewService`, `RepositoryService`, `IGitRepositoryProvider`,
`SigningService`, and `IReviewSetParser`; all are given to the constructor and none may be null.

**Types** (in `ReviewPlan.cs`): `BaselineMode` (`None`, `Explicit`, `LastReviewed`); `PlanRowStatus` (`WillCreate`,
`AlreadyFiled`, `NothingToReview`, `CannotPlan`); `PlanRow` (set id, title, normalized selection, status, baseline
commit, a note, the numbers of added, modified, and deleted files, the id of an existing review, and a problem);
`ReviewPlan` (repository, head revision and commit, mode, an explicit baseline, and the rows); `PlanCreateRequest` (set
id, title, selection, baseline commit); `PlanCreateOutcome` (`Created`, `AlreadyFiled`, `Failed`); and `PlanCreateResult`.
All are immutable records. `ReviewSetDefinition` and `IReviewSetParser` are defined with the planner.

**Limits**: a batch has at most `ReviewLimits.MaxReviewSets` requests. A generated review id has a base of at most 96
characters, so that a suffix fits in the limit of review ids (`ReviewLimits.MaxReviewIdLength`), and at most five
suffixes are tried.

#### Key Methods

**PlanAsync**: Computes the plan for a head revision. It only reads.

- *Parameters*: `int projectId`, `int repositoryId`, `string? headRevision`, `BaselineMode mode`,
  `string? baselineRevision`, `CancellationToken cancellationToken`.
- *Returns*: `ReviewPlan` — one row per review-set, in file order.
- *Preconditions*: The revisions pass `RevisionValidator`; the baseline revision is required for `Explicit` and ignored
  otherwise; the project exists; the repository is registered and enabled.
- *Postconditions*: Nothing is stored. The repository was fetched once, because one session served the whole plan.
- *Algorithm*:
  1. Validate the revisions, check the project, and get the access data from `RepositoryService`; open one session;
     resolve the head and, for `Explicit`, the baseline to commits.
  2. List the head commit as a full listing. It is the change list of every full review and is where the configuration
     is found: the file `.reviewmark.yaml` at the root. A missing file is `ReviewSetConfigMissing`; a file over the limit
     (the size is read before the bytes) or one that the parser refuses is `ReviewSetConfigInvalid`.
  3. List the reviews of the project that belong to the repository.
  4. For every set, normalize its paths into a selection text (a set whose paths are invalid, or select too many files,
     becomes a `CannotPlan` row with the problem and does not stop the plan) and choose its baseline (below).
  5. List the changes once for every distinct baseline, count the added, modified, and deleted files of each selection,
     and classify each set: `AlreadyFiled` when a review of this repository with the same set, head commit, and
     baseline exists (the id of that review is given); `NothingToReview` when no selected file changed, or none matches;
     otherwise `WillCreate`.
- *Baseline choice*: With `None` there is no baseline, and with `Explicit` it is the given commit, for every set. With
  `LastReviewed` the planner looks, among the reviews of the same repository and set, for the signed ones (as
  `SigningService` decides) and takes the one that was signed last (ties go to the later creation). Without a signed
  review the set gets a full review with a note. Otherwise the head of that review is the baseline only when it is the
  new head (nothing to review), or is an ancestor of the new head (`IsAncestorAsync`), and the selection of the set is
  the same as that of the review. A head that is not an ancestor, an ancestor check that fails, and a changed selection
  each give a full review with a note that says so.

**CreateAsync**: Creates the reviews that a user picked from a plan.

- *Parameters*: `int projectId`, `int repositoryId`, `string headSha`, `IReadOnlyList<PlanCreateRequest> requests`,
  `Actor actor`, `CancellationToken cancellationToken`.
- *Returns*: one `PlanCreateResult` for every request, in the same order.
- *Preconditions*: `requests` and `actor` are not null; there are at most `MaxReviewSets` requests; `headSha` is a full
  commit id; the project and the repository are usable.
- *Postconditions*: Every created review has its review-set id and is stored in its own transaction by
  `ReviewService`, with its own audit entry; the batch is not one transaction.
- *Algorithm*: Open one session for the batch. For each request, build the id (below) and the title, and call
  `ReviewService.CreateFromSessionAsync` with the head commit and the baseline commit of the request, never a branch
  name. A domain rule violation of a request becomes a `Failed` result with its message, and does not stop the others. A
  duplicate id (someone took it a moment ago) is retried with the next suffix, up to five times. The same range that
  already exists is an `AlreadyFiled` result.

**GenerateId** (internal): Makes the review id of a set: characters other than letters, digits, `.`, and `_` become `-`,
runs of `-` collapse, a leading character that is not a letter or a digit and trailing `-`, `.`, and `_` are removed, an
empty result becomes `review`, the base is cut to 96 characters, and when the id is taken `-2`, `-3`, and so on are
appended until one is free.

**GenerateTitle** (internal): Makes the title `Set title (all files at ccccccc)` for a full review and
`Set title (bbbbbbb..ccccccc)` for a review since a baseline, with the commits shortened to seven characters; the set
title is cut when needed so that the range stays visible and the title fits.

#### Error Handling

Problems that stop a plan are `DomainRuleException` values: `ProjectNotFound`, `RepositoryNotFound`,
`RepositoryDisabled`, `RevisionInvalid`, `RevisionNotFound`, the git rules, `ReviewSetConfigMissing`, and
`ReviewSetConfigInvalid`. A problem with one set is a `CannotPlan` row; a problem with one request of a batch is a
`Failed` result. Only domain rule violations are turned into rows and results; other exceptions are faults and
propagate. A null argument is `ArgumentNullException`, and a batch with too many requests is `ArgumentException`.

#### Dependencies

- **IReviewStore**, **ReviewService**, **RepositoryService**, **SigningService** — reviews, creation, access data, and
  the signed state.
- **IGitRepositoryProvider** and **IGitRepository** — the session, the commits, the listings, the configuration blob,
  and the ancestor check.
- **IReviewSetParser** — the review-sets of the configuration.
- **SelectionMatcher**, **RevisionValidator**, **ReviewLimits** — selections, revisions, and limits.

#### Callers

- **ServerHost** registers it as a singleton; the pages that offer the plan to a user call it.
