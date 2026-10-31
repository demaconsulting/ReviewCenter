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

using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.SelfTest.Checks;

/// <summary>
///     Checks the heart of the product on an embedded SQLite database: plan and create a review from a local git
///     repository, answer its checklist, sign it with a password re-authentication and read it back in the
///     evidence index.
/// </summary>
/// <remarks>
///     Every step calls the production service (user, project, repository, planner, checklist, signing and
///     evidence index) registered by <see cref="SelfTestServices"/>. The database, the git cache and the keys
///     live in a temporary folder that is deleted at the end. The class is stateless and thread-safe.
/// </remarks>
internal static class ReviewWorkflowCheck
{
    /// <summary>
    ///     The name of the check in the report and the results file.
    /// </summary>
    public const string Name = "ReviewCenter_ReviewWorkflow";

    /// <summary>
    ///     Runs the check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of the signed review and of what the evidence index recorded.</returns>
    /// <exception cref="ValidationFailedException">Thrown when a step fails or the data read back is wrong.</exception>
    public static async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        // Declared first, so it is deleted last: everything below that uses the folder is finished by then
        using var folder = new TemporaryDirectory();

        // The repository does not depend on the services, so it is built while they start; the two together
        // would otherwise add up. It has its own git runner and is awaited before the folder is deleted.
        var originRunner = new GitRunner(new GitOptions { CachePath = folder.GetFilePath("origin-git-cache"), TimeoutSeconds = 60 });
        var originTask = SelfTestGit.CreateOriginAsync(originRunner, folder, cancellationToken, secondCommit: false);

        try
        {
            await using var provider = SelfTestServices.Create(folder);

            // The first call of the production git runner checks the git version with a process of its own. Make
            // it now, while the user is created, so that the planner does not wait for it.
            var gitVersion = provider.GetRequiredService<GitRunner>().CheckVersionAsync(cancellationToken);
            try
            {
                return await RunWorkflowAsync(provider, originTask, gitVersion, cancellationToken);
            }
            finally
            {
                await Task.WhenAny(gitVersion);
                _ = gitVersion.Exception;
            }
        }
        finally
        {
            // Never delete the folder under a git process that is still working in it
            await Task.WhenAny(originTask);
            _ = originTask.Exception;
        }
    }

    /// <summary>
    ///     Runs the steps of the workflow on the services.
    /// </summary>
    /// <param name="provider">The production services over the embedded database.</param>
    /// <param name="originTask">The task that builds the local repository; awaited when it is registered.</param>
    /// <param name="gitVersion">The task that checks the git version; awaited before the planner runs git.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A short description of the signed review and of what the evidence index recorded.</returns>
    /// <exception cref="ValidationFailedException">Thrown when a step fails or the data read back is wrong.</exception>
    private static async Task<string> RunWorkflowAsync(
        ServiceProvider provider,
        Task<SelfTestGit.Origin> originTask,
        Task gitVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            // A system administrator who is the project member who creates the review (its author), and a second
            // project member who reviews and signs it: with independent reviewers required (the default) the author
            // cannot sign. The passwords go through the production hasher, so the later re-authentication is the real one
            var users = provider.GetRequiredService<UserService>();
            var user = await users.CreateAsync(
                "selftest", "Self Test", true, SelfTestServices.SelfTestSecret, Actor.CommandLine, cancellationToken);
            var reviewer = await users.CreateAsync(
                "selftest-reviewer", "Self Test Reviewer", false, SelfTestServices.SelfTestSecret, Actor.CommandLine, cancellationToken);
            var actor = new Actor(user.Username, user.DisplayName);
            var reviewerActor = new Actor(reviewer.Username, reviewer.DisplayName);
            var projects = provider.GetRequiredService<ProjectService>();
            var project = await projects.CreateAsync("Self-test project", null, actor, cancellationToken);
            await projects.AddMemberAsync(actor, project.Id, user.Username, ProjectRole.User, cancellationToken);
            await projects.AddMemberAsync(actor, project.Id, reviewer.Username, ProjectRole.User, cancellationToken);
            // A local repository registered like any other, then planned and created through the planner
            var origin = await originTask;
            await gitVersion;
            var repository = await provider.GetRequiredService<RepositoryService>().CreateAsync(
                project.Id, "selftest", origin.Folder, null, null, actor, cancellationToken);
            var planner = provider.GetRequiredService<ReviewPlanner>();
            var plan = await planner.PlanAsync(
                actor, project.Id, repository.Id, "main", BaselineMode.None, null, cancellationToken);
            var requests = plan.Rows
                .Where(row => row.Status == PlanRowStatus.WillCreate)
                .Select(row => new PlanCreateRequest(row.SetId, row.Title, row.Selection, row.BaselineSha))
                .ToList();
            ValidationFailedException.ThrowUnless(requests.Count == 1, "The plan did not offer exactly one review to create.");
            var created = (await planner.CreateAsync(
                project.Id, repository.Id, plan.HeadSha, requests, actor, cancellationToken)).Single();
            ValidationFailedException.ThrowUnless(
                created is { Outcome: PlanCreateOutcome.Created, ReviewId: not null },
                $"The review was not created: {created.Message}");
            var reviewId = created.ReviewId!;

            // Answer every checklist item as the separate reviewer (the author may not), show that the author cannot
            // be a reviewer, then sign with the password as the separate reviewer
            await SignAsync(provider, projects, project.Id, reviewId, new People(user, actor, reviewer, reviewerActor), cancellationToken);

            // The signed review must be in the evidence index with its fingerprint and the record digest
            var review = await provider.GetRequiredService<ReviewService>().GetAsync(project.Id, reviewId, cancellationToken);
            var approval = await provider.GetRequiredService<SigningService>()
                .GetApprovalAsync(project.Id, reviewId, cancellationToken);
            var index = await provider.GetRequiredService<EvidenceIndexService>()
                .BuildAsync(actor, project.Id, cancellationToken);
            var entry = index.SingleOrDefault(e => e.ReviewId == reviewId);
            ValidationFailedException.ThrowUnless(
                review != null && approval is { IsApproved: true } && entry != null
                    && entry.Fingerprint == review.ContentFingerprint
                    && entry.Id == review.ReviewSetId
                    && !string.IsNullOrEmpty(approval.Digest),
                "The signed review was not found in the evidence index with its fingerprint and record digest.");

            // An approved review is locked: a change is refused, and only an administrator's reopen (with a reason)
            // takes it back to review
            await CheckLockAndReopenAsync(provider, project.Id, reviewId, actor, reviewerActor, cancellationToken);

            return "embedded SQLite; author answer refused; review answered and signed by a separate reviewer; author signature refused; " +
                $"approved review locked and reopened by an administrator; evidence index fingerprint {Short(entry!.Fingerprint)}, " +
                $"record digest {Short(approval!.Digest)}";
        }
        catch (DomainRuleException ex)
        {
            // The services report rule violations with a message that is safe to show
            throw new ValidationFailedException(ex.Message);
        }
    }

    /// <summary>
    ///     The two people of the workflow: the author of the review and the separate reviewer.
    /// </summary>
    /// <param name="Author">The user who creates the review; the author cannot answer its checklist or sign it.</param>
    /// <param name="AuthorActor">The actor of the author.</param>
    /// <param name="Reviewer">The user who reviews and signs.</param>
    /// <param name="ReviewerActor">The actor of the reviewer.</param>
    private sealed record People(User Author, Actor AuthorActor, User Reviewer, Actor ReviewerActor);

    /// <summary>
    ///     Answers every checklist item with Pass as the separate reviewer (the author is refused), shows that the
    ///     author cannot be a reviewer, and lets the separate reviewer sign with a password re-authentication.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="projects">The project service, to switch the independence rule off and on again.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review to sign.</param>
    /// <param name="people">The author and the reviewer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the review is signed.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the author is not refused as reviewer and signer.</exception>
    private static async Task SignAsync(
        IServiceProvider provider,
        ProjectService projects,
        int projectId,
        string reviewId,
        People people,
        CancellationToken cancellationToken)
    {
        // The author may not answer the checklist (the default rule), so the separate reviewer answers every item
        var checklists = provider.GetRequiredService<ChecklistService>();
        var allItems = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).ToList();
        await ExpectRefusedAsync(
            DomainRule.AuthorCannotAnswer,
            () => checklists.SetAnswerAsync(
                projectId, reviewId, allItems[0].Id, ChecklistOutcome.Pass, null, people.AuthorActor, cancellationToken));
        foreach (var item in allItems)
        {
            await checklists.SetAnswerAsync(
                projectId, reviewId, item.Id, ChecklistOutcome.Pass, null, people.ReviewerActor, cancellationToken);
        }

        var signing = provider.GetRequiredService<SigningService>();
        var digest = (await signing.GetApprovalAsync(projectId, reviewId, cancellationToken))?.Digest
            ?? throw new ValidationFailedException("The review has no record digest to sign.");

        // The author is refused as a reviewer. To show that the signature is refused as well, the rule is switched
        // off just long enough to seat the author, then on again: the author's signature attempt must fail
        await ExpectRefusedAsync(
            DomainRule.ReviewerNotIndependent,
            () => signing.AddSlotAsync(people.AuthorActor, projectId, reviewId, people.Author.Id, null, cancellationToken));
        await projects.SetRequireIndependentReviewersAsync(people.AuthorActor, projectId, true, false, cancellationToken);
        var authorSlot = await signing.AddSlotAsync(
            people.AuthorActor, projectId, reviewId, people.Author.Id, null, cancellationToken);
        await projects.SetRequireIndependentReviewersAsync(people.AuthorActor, projectId, false, true, cancellationToken);
        await ExpectRefusedAsync(
            DomainRule.ReviewerNotIndependent,
            () => signing.SignAsync(
                people.AuthorActor, projectId, reviewId, authorSlot.Id, digest, SelfTestServices.SelfTestSecret, cancellationToken));
        await signing.RemoveSlotAsync(people.AuthorActor, projectId, reviewId, authorSlot.Id, cancellationToken);

        // The separate reviewer holds the one slot and signs
        var slot = await signing.AddSlotAsync(
            people.AuthorActor, projectId, reviewId, people.Reviewer.Id, null, cancellationToken);
        await signing.SignAsync(
            people.ReviewerActor, projectId, reviewId, slot.Id, digest, SelfTestServices.SelfTestSecret, cancellationToken);
    }

    /// <summary>
    ///     Shows that an approved review refuses a change, and that an administrator's reopen takes it back to review.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The approved review.</param>
    /// <param name="admin">The administrator who reopens the review.</param>
    /// <param name="reviewer">
    ///     The reviewer who answers the checklist; the change to be refused comes from the reviewer, because the author
    ///     is refused as author before the lock is reached.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the review is reopened.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the change is not refused or the review stays approved.</exception>
    private static async Task CheckLockAndReopenAsync(
        IServiceProvider provider,
        int projectId,
        string reviewId,
        Actor admin,
        Actor reviewer,
        CancellationToken cancellationToken)
    {
        var firstItem = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();
        await ExpectRefusedAsync(
            DomainRule.ReviewLocked,
            () => provider.GetRequiredService<ChecklistService>().SetAnswerAsync(
                projectId, reviewId, firstItem.Id, ChecklistOutcome.Fail, "Changed after approval", reviewer, cancellationToken));

        var signing = provider.GetRequiredService<SigningService>();
        await signing.ReopenReviewAsync(admin, projectId, reviewId, "Self-test reopen", cancellationToken);
        var approval = await signing.GetApprovalAsync(projectId, reviewId, cancellationToken);
        ValidationFailedException.ThrowUnless(
            approval is { IsApproved: false, Reopens.Count: 1 },
            "The review was still approved after the administrator reopened it.");
    }

    /// <summary>
    ///     Runs an operation that must be refused with a given rule.
    /// </summary>
    /// <param name="rule">The rule the refusal must carry.</param>
    /// <param name="operation">The operation, which must throw.</param>
    /// <returns>A task that completes when the operation was refused as expected.</returns>
    /// <exception cref="ValidationFailedException">Thrown when the operation succeeds or is refused for another reason.</exception>
    private static async Task ExpectRefusedAsync(DomainRule rule, Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (DomainRuleException ex) when (ex.Rule == rule)
        {
            return;
        }
        catch (DomainRuleException ex)
        {
            throw new ValidationFailedException($"Expected the refusal {rule} but got {ex.Rule}: {ex.Message}");
        }

        throw new ValidationFailedException($"Expected the refusal {rule} but the operation succeeded.");
    }

    /// <summary>
    ///     Shortens a hash for the report.
    /// </summary>
    /// <param name="hash">The full hash text.</param>
    /// <returns>The first twelve characters followed by an ellipsis.</returns>
    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12] + "…";
}
