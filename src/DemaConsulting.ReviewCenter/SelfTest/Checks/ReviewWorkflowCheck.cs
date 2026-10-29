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
            // A system administrator who is also the project member who signs; the password goes through the
            // production hasher, so the later re-authentication is the real one
            var users = provider.GetRequiredService<UserService>();
            var user = await users.CreateAsync(
                "selftest", "Self Test", true, SelfTestServices.SelfTestSecret, Actor.CommandLine, cancellationToken);
            var actor = new Actor(user.Username, user.DisplayName);
            var projects = provider.GetRequiredService<ProjectService>();
            var project = await projects.CreateAsync("Self-test project", null, actor, cancellationToken);
            await projects.AddMemberAsync(actor, project.Id, user.Username, ProjectRole.User, cancellationToken);

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

            // Answer every checklist item and sign with the password
            await SignAsync(provider, project.Id, reviewId, user, actor, cancellationToken);

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

            return $"embedded SQLite; review signed; evidence index fingerprint {Short(entry!.Fingerprint)}, " +
                $"record digest {Short(approval!.Digest)}";
        }
        catch (DomainRuleException ex)
        {
            // The services report rule violations with a message that is safe to show
            throw new ValidationFailedException(ex.Message);
        }
    }

    /// <summary>
    ///     Answers every checklist item with Pass and lets the user sign with a password re-authentication.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review to sign.</param>
    /// <param name="user">The user who signs.</param>
    /// <param name="actor">The actor of the user.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the review is signed.</returns>
    private static async Task SignAsync(
        IServiceProvider provider,
        int projectId,
        string reviewId,
        User user,
        Actor actor,
        CancellationToken cancellationToken)
    {
        var checklists = provider.GetRequiredService<ChecklistService>();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                projectId, reviewId, item.Id, ChecklistOutcome.Pass, null, actor, cancellationToken);
        }

        var signing = provider.GetRequiredService<SigningService>();
        var slot = await signing.AddSlotAsync(actor, projectId, reviewId, user.Id, null, cancellationToken);
        var digest = (await signing.GetApprovalAsync(projectId, reviewId, cancellationToken))?.Digest
            ?? throw new ValidationFailedException("The review has no record digest to sign.");
        await signing.SignAsync(
            actor, projectId, reviewId, slot.Id, digest, SelfTestServices.SelfTestSecret, cancellationToken);
    }

    /// <summary>
    ///     Shortens a hash for the report.
    /// </summary>
    /// <param name="hash">The full hash text.</param>
    /// <returns>The first twelve characters followed by an ellipsis.</returns>
    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12] + "…";
}
