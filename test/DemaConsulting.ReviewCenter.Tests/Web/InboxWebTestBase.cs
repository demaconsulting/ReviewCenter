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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Shared arrangement of the page tests of the inbox: a project <c>Apollo</c> with a few members and helpers
///     that create reviews, reviewers, threads and signatures for them.
/// </summary>
/// <remarks>
///     The users <c>ann</c>, <c>bob</c>, <c>eve</c> and <c>author</c> are ordinary members; <c>aud</c> is an
///     auditor. The users exist in the store, so a test signs in as one of them with
///     <c>SignInAs(name, display, false)</c> and the real access rules apply.
/// </remarks>
public abstract class InboxWebTestBase : PageTestBase
{
    /// <summary>
    ///     The password of every user.
    /// </summary>
    protected const string Password = "correct horse battery";

    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Gets the identity of the project <c>Apollo</c> once <see cref="ArrangeProjectAsync"/> has run.
    /// </summary>
    protected int ProjectId { get; private set; }

    /// <summary>
    ///     Gets the identity of each user created by <see cref="ArrangeProjectAsync"/>, by user name.
    /// </summary>
    protected Dictionary<string, int> UserIds { get; } = [];

    /// <summary>
    ///     Gets the actor of a user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The actor.</returns>
    protected static Actor ActorOf(string username)
    {
        return new Actor(username, char.ToUpperInvariant(username[0]) + username[1..]);
    }

    /// <summary>
    ///     Creates the users and the project <c>Apollo</c> with its memberships.
    /// </summary>
    /// <returns>A task that completes when everything exists.</returns>
    protected async Task ArrangeProjectAsync()
    {
        foreach (var name in new[] { "ann", "bob", "eve", "author", "aud" })
        {
            var user = await Users.CreateAsync(name, ActorOf(name).DisplayName, false, Password, Ct);
            UserIds[name] = user.Id;
        }

        var project = await Projects.CreateAsync("Apollo", null, Ct);
        ProjectId = project.Id;
        foreach (var name in new[] { "ann", "bob", "eve", "author" })
        {
            await Store.AddMembershipAsync(UserIds[name], ProjectId, ProjectRole.User, Ct);
        }

        await Store.AddMembershipAsync(UserIds["aud"], ProjectId, ProjectRole.Auditor, Ct);
    }

    /// <summary>
    ///     Creates a review with one file.
    /// </summary>
    /// <param name="reviewId">The review id.</param>
    /// <param name="author">The user name of the author.</param>
    /// <param name="dueDate">The due date, or <c>null</c> for none.</param>
    /// <returns>A task that completes when the review exists.</returns>
    protected Task CreateInboxReviewAsync(string reviewId, string author = "author", DateOnly? dueDate = null)
    {
        return Reviews.CreateDraftAsync(
            ProjectId,
            reviewId,
            "Review " + reviewId,
            [new UploadedFile("a.txt", TestContent.A)],
            ActorOf(author),
            Ct,
            dueDate);
    }

    /// <summary>
    ///     Adds a reviewer slot, added by the author.
    /// </summary>
    /// <param name="reviewId">The review id.</param>
    /// <param name="primary">The user name of the primary reviewer.</param>
    /// <returns>The slot.</returns>
    protected Task<ReviewerSlot> AddSlotAsync(string reviewId, string primary)
    {
        return Signing.AddSlotAsync(ActorOf("author"), ProjectId, reviewId, UserIds[primary], null, Ct);
    }

    /// <summary>
    ///     Answers every check of the review as Pass in the ballot of a reviewer and hands the ballot in; the
    ///     reviewer must hold a slot of the review.
    /// </summary>
    /// <param name="reviewId">The review id.</param>
    /// <param name="answerer">The user name of the slot owner who answers.</param>
    /// <returns>A task that completes when the ballot is submitted.</returns>
    protected async Task AnswerAllAsync(string reviewId, string answerer)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                ProjectId, reviewId, item.Id, ChecklistOutcome.Pass, null, ActorOf(answerer), Ct);
        }

        await Checklists.SubmitBallotAsync(ProjectId, reviewId, ActorOf(answerer), Ct);
    }

    /// <summary>
    ///     Signs a slot as a user, with the digest of the record as it is now.
    /// </summary>
    /// <param name="reviewId">The review id.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="username">The user name of the signer.</param>
    /// <returns>A task that completes when the signature is stored.</returns>
    protected async Task SignSlotAsync(string reviewId, int slotId, string username)
    {
        var approval = await Signing.GetApprovalAsync(ProjectId, reviewId, Ct);
        await Signing.SignAsync(ActorOf(username), ProjectId, reviewId, slotId, approval!.Digest, Password, Ct);
    }

    /// <summary>
    ///     Starts a whole-file thread on <c>a.txt</c> and lets another user reply.
    /// </summary>
    /// <param name="reviewId">The review id.</param>
    /// <param name="starter">The user name of the user who starts the thread.</param>
    /// <param name="replier">The user name of the user who replies.</param>
    /// <returns>The thread.</returns>
    protected async Task<ReviewThread> StartAndAnswerThreadAsync(string reviewId, string starter, string replier)
    {
        var thread = await Threads.StartThreadAsync(
            ProjectId, reviewId, new ThreadAnchor("a.txt"), ActorOf(starter), "Why?", Ct);
        await Threads.ReplyAsync(ProjectId, reviewId, thread.Id, ActorOf(replier), "Because", Ct);
        return thread;
    }
}
