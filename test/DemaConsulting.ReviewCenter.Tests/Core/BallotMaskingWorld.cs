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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     A world for the tests of ballot visibility: the real access rules and the real services around one review, so
///     what a person may see depends on their role and ballot and not on a service that allows everything.
/// </summary>
/// <remarks>
///     Users: <c>lead</c> (a member who creates the review), <c>ann</c>, <c>bob</c> and <c>cyd</c> (members),
///     <c>padm</c> (project administrator), <c>aud</c> (Auditor), <c>out</c> (no member) and <c>root</c> (system
///     administrator). The review <c>rev-1</c> of the project has one file and the default checklist, and no slot.
/// </remarks>
internal sealed class BallotMaskingWorld
{
    /// <summary>
    ///     The review identifier.
    /// </summary>
    public const string ReviewId = "rev-1";

    /// <summary>
    ///     The password of every user.
    /// </summary>
    public const string Password = "correct horse battery";

    /// <summary>
    ///     Initializes a new instance of the <see cref="BallotMaskingWorld"/> class; use <see cref="CreateAsync"/>.
    /// </summary>
    private BallotMaskingWorld()
    {
        Clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        Store = new FakeReviewStore();
        UserStore = new FakeUserStore(Store);
        Users = new UserService(UserStore, new FakePasswordHasher(), Clock);
        Access = new ProjectAccess(UserStore, Store);
        AuditStore = new FakeAuditStore(Clock);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), Access, Clock);
        Reviews = new ReviewService(Store, Access, Clock, repositories, new FakeGitRepositoryProvider());
        Checklists = new ChecklistService(Store, Access, UserStore, Clock);
        Threads = new ThreadService(Store, Access, UserStore, Clock);
        Signing = new SigningService(Store, Users, Access, Clock);
        Progress = new ReviewProgressService(Reviews, Signing, AuditStore, Access);
        Status = new ReviewStatusService(Store, UserStore, Access, Signing, Checklists, Threads, AuditStore);
        Inbox = new InboxService(new FakeInboxStore(Store), Store, Access, UserStore, Clock);
    }

    /// <summary>
    ///     Gets the clock.
    /// </summary>
    public MovableTimeProvider Clock { get; }

    /// <summary>
    ///     Gets the review store.
    /// </summary>
    public FakeReviewStore Store { get; }

    /// <summary>
    ///     Gets the user store.
    /// </summary>
    public FakeUserStore UserStore { get; }

    /// <summary>
    ///     Gets the user service.
    /// </summary>
    public UserService Users { get; }

    /// <summary>
    ///     Gets the real access rules.
    /// </summary>
    public ProjectAccess Access { get; }

    /// <summary>
    ///     Gets the audit store.
    /// </summary>
    public FakeAuditStore AuditStore { get; }

    /// <summary>
    ///     Gets the review service.
    /// </summary>
    public ReviewService Reviews { get; }

    /// <summary>
    ///     Gets the checklist service.
    /// </summary>
    public ChecklistService Checklists { get; }

    /// <summary>
    ///     Gets the thread service.
    /// </summary>
    public ThreadService Threads { get; }

    /// <summary>
    ///     Gets the signing service.
    /// </summary>
    public SigningService Signing { get; }

    /// <summary>
    ///     Gets the review progress service.
    /// </summary>
    public ReviewProgressService Progress { get; }

    /// <summary>
    ///     Gets the review status service.
    /// </summary>
    public ReviewStatusService Status { get; }

    /// <summary>
    ///     Gets the inbox service.
    /// </summary>
    public InboxService Inbox { get; }

    /// <summary>
    ///     Gets the identity of the project.
    /// </summary>
    public int ProjectId { get; private set; }

    /// <summary>
    ///     Gets the identities of the users by user name.
    /// </summary>
    public IReadOnlyDictionary<string, int> Ids { get; private set; } = new Dictionary<string, int>();

    /// <summary>
    ///     Gets the identifier of the first check of the default checklist.
    /// </summary>
    public static string FirstItemId => DefaultChecklist.Current.Sections[0].Items[0].Id;

    /// <summary>
    ///     Creates the world: the users, the project, the memberships and the review.
    /// </summary>
    /// <returns>The arranged world.</returns>
    public static async Task<BallotMaskingWorld> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = new BallotMaskingWorld();
        var ids = new Dictionary<string, int>();
        foreach (var name in new[] { "lead", "ann", "bob", "cyd", "padm", "aud", "out", "root" })
        {
            ids[name] = (await world.Users.CreateAsync(name, name, name == "root", Password, Actor.CommandLine, cancellationToken)).Id;
        }

        world.Ids = ids;
        var projects = new ProjectService(world.Store, world.UserStore, world.Access, new FakePasswordHasher(), world.Clock);
        var project = await projects.CreateAsync("Apollo", null, ActorOf("root"), cancellationToken);
        world.ProjectId = project.Id;
        foreach (var name in new[] { "lead", "ann", "bob", "cyd" })
        {
            await world.Store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await world.Store.AddMembershipAsync(ids["padm"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await world.Store.AddMembershipAsync(ids["aud"], project.Id, ProjectRole.Auditor, cancellationToken);
        await world.Reviews.CreateDraftAsync(
            project.Id, ReviewId, "First", [new UploadedFile("a.txt", [1, 2, 3])], ActorOf("lead"), cancellationToken);
        return world;
    }

    /// <summary>
    ///     Gets the signed-in actor of a user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The actor.</returns>
    public static Actor ActorOf(string username)
    {
        return new Actor(username, username);
    }

    /// <summary>
    ///     Seats a user as the primary reviewer of a new slot, as the lead of the review.
    /// </summary>
    /// <param name="username">The user name of the reviewer.</param>
    /// <returns>The slot.</returns>
    public Task<ReviewerSlot> SeatAsync(string username)
    {
        return Signing.AddSlotAsync(
            ActorOf("lead"), ProjectId, ReviewId, Ids[username], null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Lets a reviewer answer every check, the first one with the given outcome and the others Pass.
    /// </summary>
    /// <param name="username">The user name of the reviewer, who holds a slot.</param>
    /// <param name="first">The answer to the first check.</param>
    /// <returns>A task that completes when the answers are stored.</returns>
    public async Task AnswerAsync(string username, ChecklistOutcome first = ChecklistOutcome.Pass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = ActorOf(username);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            var outcome = item.Id == FirstItemId ? first : ChecklistOutcome.Pass;
            var justification = outcome == ChecklistOutcome.Pass ? null : "A reason";
            await Checklists.SetAnswerAsync(ProjectId, ReviewId, item.Id, outcome, justification, actor, cancellationToken);
        }
    }

    /// <summary>
    ///     Hands in the ballot of a reviewer.
    /// </summary>
    /// <param name="username">The user name of the reviewer.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    public Task SubmitAsync(string username)
    {
        return Checklists.SubmitBallotAsync(ProjectId, ReviewId, ActorOf(username), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Arranges a review with two slots in which ann has handed in Pass everywhere and bob has answered Fail on the
    ///     first check, in a draft or handed in.
    /// </summary>
    /// <param name="bobSubmitted">Whether bob hands in the ballot.</param>
    /// <returns>A task that completes when the ballots are arranged.</returns>
    public async Task ArrangeDisputeAsync(bool bobSubmitted = true)
    {
        await SeatAsync("ann");
        await SeatAsync("bob");
        await AnswerAsync("ann");
        await SubmitAsync("ann");
        await AnswerAsync("bob", ChecklistOutcome.Fail);
        if (bobSubmitted)
        {
            await SubmitAsync("bob");
        }
    }

    /// <summary>
    ///     Starts a discussion about the first check as a user.
    /// </summary>
    /// <param name="username">The user name of the person who starts it.</param>
    /// <returns>The thread.</returns>
    public Task<ReviewThread> StartCheckThreadAsync(string username)
    {
        return Threads.StartThreadAsync(
            ProjectId,
            ReviewId,
            ThreadAnchor.ForCheck(FirstItemId),
            ActorOf(username),
            "Why Fail?",
            TestContext.Current.CancellationToken);
    }
}
