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
///     The arranged world of the inbox tests: in-memory stores, the real <see cref="ProjectAccess"/> for the inbox
///     (so rights and disabled accounts matter), and write helpers that go through the real services with every
///     right granted, so a test can arrange any state of a review.
/// </summary>
/// <remarks>
///     Users: <c>ann</c>, <c>bob</c>, <c>cyd</c>, <c>dee</c>, <c>eve</c> and <c>author</c> are members of the project
///     <c>Apollo</c>; <c>aud</c> is an auditor of it; <c>off</c> and <c>tmp</c> are members that a test can disable
///     or give a temporary password; <c>out</c> is no member; <c>root</c> is a system administrator and no member.
///     The clock starts at <see cref="Start"/> and a test moves it.
/// </remarks>
internal sealed class InboxTestWorld
{
    /// <summary>
    ///     The password of every user.
    /// </summary>
    public const string Password = "correct horse battery";

    /// <summary>
    ///     The instant at which the clock starts.
    /// </summary>
    public static readonly DateTimeOffset Start = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxTestWorld"/> class; use <see cref="CreateAsync"/>.
    /// </summary>
    private InboxTestWorld()
    {
        Store = new FakeReviewStore();
        UserStore = new FakeUserStore(Store);
        Clock = new MovableTimeProvider(Start);
        Users = new UserService(UserStore, new FakePasswordHasher(), Clock);
        Access = new ProjectAccess(UserStore, Store);
        InboxStore = new FakeInboxStore(Store);
        Inbox = new InboxService(InboxStore, Store, Access, UserStore, Clock);
        Reviews = TestServices.Reviews(Store, Clock);
        Threads = TestServices.Threads(Store, Clock);
        Checklists = TestServices.Checklists(Store, Clock);
        Signing = TestServices.Signing(Store, Users, Clock);
    }

    /// <summary>
    ///     Gets the review store.
    /// </summary>
    public FakeReviewStore Store { get; }

    /// <summary>
    ///     Gets the user store.
    /// </summary>
    public FakeUserStore UserStore { get; }

    /// <summary>
    ///     Gets the clock.
    /// </summary>
    public MovableTimeProvider Clock { get; }

    /// <summary>
    ///     Gets the user service.
    /// </summary>
    public UserService Users { get; }

    /// <summary>
    ///     Gets the real access rules the inbox uses.
    /// </summary>
    public ProjectAccess Access { get; }

    /// <summary>
    ///     Gets the in-memory inbox store.
    /// </summary>
    public FakeInboxStore InboxStore { get; }

    /// <summary>
    ///     Gets the inbox service under test.
    /// </summary>
    public InboxService Inbox { get; }

    /// <summary>
    ///     Gets the review service (every right granted).
    /// </summary>
    public ReviewService Reviews { get; }

    /// <summary>
    ///     Gets the thread service (every right granted).
    /// </summary>
    public ThreadService Threads { get; }

    /// <summary>
    ///     Gets the checklist service (every right granted).
    /// </summary>
    public ChecklistService Checklists { get; }

    /// <summary>
    ///     Gets the signing service (every right granted).
    /// </summary>
    public SigningService Signing { get; }

    /// <summary>
    ///     Gets the identity of the project <c>Apollo</c>.
    /// </summary>
    public int ProjectId { get; private set; }

    /// <summary>
    ///     Gets the identities of the users by user name.
    /// </summary>
    public IReadOnlyDictionary<string, int> Ids { get; private set; } = new Dictionary<string, int>();

    /// <summary>
    ///     Creates the world: the users, the project and the memberships.
    /// </summary>
    /// <returns>The arranged world.</returns>
    public static async Task<InboxTestWorld> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = new InboxTestWorld();
        var ids = new Dictionary<string, int>();
        foreach (var name in new[] { "ann", "bob", "cyd", "dee", "eve", "author", "aud", "off", "tmp", "out", "root" })
        {
            ids[name] = (await world.Users.CreateAsync(name, Capitalize(name), name == "root", Password, cancellationToken)).Id;
        }

        world.Ids = ids;
        var project = await TestServices.Projects(world.Store, world.Clock).CreateAsync("Apollo", null, cancellationToken);
        world.ProjectId = project.Id;
        foreach (var name in new[] { "ann", "bob", "cyd", "dee", "eve", "author", "off", "tmp" })
        {
            await world.Store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await world.Store.AddMembershipAsync(ids["aud"], project.Id, ProjectRole.Auditor, cancellationToken);
        return world;
    }

    /// <summary>
    ///     Gets the signed-in actor of a user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The actor.</returns>
    public static Actor ActorOf(string username)
    {
        return new Actor(username, Capitalize(username));
    }

    /// <summary>
    ///     Creates a review with one file.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="author">The user name of the author.</param>
    /// <param name="dueDate">The due date, or <c>null</c> for none.</param>
    /// <param name="title">The title.</param>
    /// <returns>A task that completes when the review exists.</returns>
    public async Task CreateReviewAsync(
        string reviewId,
        string author = "author",
        DateOnly? dueDate = null,
        string title = "A review")
    {
        await Reviews.CreateDraftAsync(
            ProjectId,
            reviewId,
            title,
            [new("a.txt", TestContent.A)],
            ActorOf(author),
            TestContext.Current.CancellationToken,
            dueDate);
    }

    /// <summary>
    ///     Answers every check of the review as Pass.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>A task that completes when all checks are answered.</returns>
    public async Task AnswerAllAsync(string reviewId)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                ProjectId, reviewId, item.Id, ChecklistOutcome.Pass, null, ActorOf("eve"), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Answers one check of the review as Fail.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="by">The user name of the user who answers.</param>
    /// <param name="itemIndex">The position of the check within the first section.</param>
    /// <returns>A task that completes when the answer is stored.</returns>
    public async Task FailCheckAsync(string reviewId, string by, int itemIndex = 0)
    {
        var item = DefaultChecklist.Current.Sections[0].Items[itemIndex];

        // The change may make a signature out of date; the signed slots are acknowledged
        await Checklists.SetAnswerAsync(
            ProjectId,
            reviewId,
            item.Id,
            ChecklistOutcome.Fail,
            "It is wrong",
            ActorOf(by),
            TestContext.Current.CancellationToken,
            await Store.CurrentlySignedSlotIdsAsync(ProjectId, reviewId));
    }

    /// <summary>
    ///     Adds a reviewer slot.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="primary">The user name of the primary reviewer.</param>
    /// <param name="alternate">The user name of the alternate reviewer, or <c>null</c> for none.</param>
    /// <returns>The slot.</returns>
    public Task<ReviewerSlot> AddSlotAsync(string reviewId, string primary, string? alternate = null)
    {
        return Signing.AddSlotAsync(
            ActorOf("author"),
            ProjectId,
            reviewId,
            Ids[primary],
            alternate is null ? null : Ids[alternate],
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Signs a slot as a user, with the digest of the record as it is now.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="slotId">The slot identity.</param>
    /// <param name="username">The user name of the signer.</param>
    /// <returns>The signature.</returns>
    public async Task<ReviewSignature> SignAsync(string reviewId, int slotId, string username)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approval = await Signing.GetApprovalAsync(ProjectId, reviewId, cancellationToken);
        return await Signing.SignAsync(
            ActorOf(username), ProjectId, reviewId, slotId, approval!.Digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Starts a whole-file thread on <c>a.txt</c>.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="username">The user name of the commenter.</param>
    /// <param name="text">The comment text.</param>
    /// <returns>The thread.</returns>
    public Task<ReviewThread> StartThreadAsync(string reviewId, string username, string text = "Please look")
    {
        return Threads.StartThreadAsync(
            ProjectId, reviewId, new ThreadAnchor("a.txt"), ActorOf(username), text, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds a reply to a thread.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="threadId">The thread identity.</param>
    /// <param name="username">The user name of the commenter.</param>
    /// <param name="text">The comment text.</param>
    /// <returns>The comment.</returns>
    public Task<ReviewComment> ReplyAsync(string reviewId, int threadId, string username, string text = "Done")
    {
        return Threads.ReplyAsync(
            ProjectId, reviewId, threadId, ActorOf(username), text, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reads the inbox of a user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The inbox.</returns>
    public Task<InboxView> InboxOfAsync(string username)
    {
        return Inbox.GetInboxAsync(ActorOf(username), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Reads the inbox of a user for one section of one review.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="section">The section.</param>
    /// <returns>The rows of the section, in order.</returns>
    public async Task<IReadOnlyList<InboxItem>> RowsAsync(string username, InboxSection section)
    {
        return (await InboxOfAsync(username)).Items.Where(item => item.Section == section).ToList();
    }

    /// <summary>
    ///     Makes the first letter of a user name upper case, which gives the display name.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <returns>The display name.</returns>
    private static string Capitalize(string name)
    {
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
