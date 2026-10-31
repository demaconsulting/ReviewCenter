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

namespace DemaConsulting.ReviewCenter.Tests;

/// <summary>
///     A helper for tests that change a review after it was partly signed: it names the slots whose signatures the
///     change makes out of date, so the test can acknowledge them.
/// </summary>
/// <remarks>
///     The slots are worked out from the public data of the snapshot, not with the code under test, so a test that
///     acknowledges with this helper does not depend on the impact calculation it exercises.
/// </remarks>
internal static class SignatureTestExtensions
{
    /// <summary>
    ///     Lists the identities of the slots that hold a signature of the current record.
    /// </summary>
    /// <param name="store">The store to read from.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The slot identities; empty when no signature is current.</returns>
    public static async Task<IReadOnlyCollection<int>> CurrentlySignedSlotIdsAsync(
        this IReviewStore store,
        int projectId,
        string reviewId)
    {
        var snapshot = await store.GetSnapshotAsync(projectId, reviewId, TestContext.Current.CancellationToken);
        if (snapshot is null)
        {
            return [];
        }

        var digest = RecordDigest.Compute(snapshot.Record);
        return snapshot.Slots
            .Where(slot => snapshot.Signatures.Any(s => s.SlotId == slot.Id && s.RecordDigest == digest))
            .Select(slot => slot.Id)
            .ToList();
    }
}

/// <summary>
///     A review whose checklist is fully answered and that has three reviewer slots, for tests of changes made while
///     some slots hold a signature; a test signs the slots it needs.
/// </summary>
/// <remarks>
///     Slot 1 has the primary <c>rita</c> and the alternate <c>alex</c>, slot 2 has <c>sam</c> and slot 3 has
///     <c>tess</c>. Nobody has signed when the world is created. The world is not thread-safe.
/// </remarks>
internal sealed class SignedReviewWorld
{
    /// <summary>
    ///     The password of every reviewer.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The identifier of the review.
    /// </summary>
    public const string ReviewId = "rev-1";

    /// <summary>
    ///     The user names of the signers by slot position: primary of slot 1, alternate of slot 1, then the primaries
    ///     of slots 2 and 3.
    /// </summary>
    private static readonly string[] _names = ["rita", "alex", "sam", "tess"];

    /// <summary>
    ///     Initializes a new instance of the <see cref="SignedReviewWorld"/> class.
    /// </summary>
    /// <param name="store">The review store.</param>
    /// <param name="checklists">The checklist service.</param>
    /// <param name="threads">The thread service.</param>
    /// <param name="signing">The signing service.</param>
    /// <param name="projectId">The project identity.</param>
    /// <param name="slots">The three slots in order.</param>
    private SignedReviewWorld(
        FakeReviewStore store,
        ChecklistService checklists,
        ThreadService threads,
        SigningService signing,
        int projectId,
        IReadOnlyList<ReviewerSlot> slots)
    {
        Store = store;
        Checklists = checklists;
        Threads = threads;
        Signing = signing;
        ProjectId = projectId;
        Slots = slots;
    }

    /// <summary>
    ///     Gets the review store.
    /// </summary>
    public FakeReviewStore Store { get; }

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
    ///     Gets the project identity.
    /// </summary>
    public int ProjectId { get; }

    /// <summary>
    ///     Gets the three reviewer slots in order.
    /// </summary>
    public IReadOnlyList<ReviewerSlot> Slots { get; }

    /// <summary>
    ///     Gets the identity of the first check of the checklist.
    /// </summary>
    public static string FirstItemId => DefaultChecklist.Current.Sections[0].Items[0].Id;

    /// <summary>
    ///     Creates the review with every check answered Pass by <c>ann</c> and the three slots.
    /// </summary>
    /// <returns>The world.</returns>
    public static async Task<SignedReviewWorld> CreateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var store = new FakeReviewStore();
        var users = new UserService(new FakeUserStore(), new FakePasswordHasher(), clock);
        var project = await TestServices.Projects(store, clock).CreateAsync("Apollo", null, cancellationToken);
        await TestServices.Reviews(store, clock).CreateDraftAsync(
            project.Id, ReviewId, "First", [new("a.txt", TestContent.A)], cancellationToken);
        var ids = new List<int>();
        foreach (var name in _names)
        {
            var user = await users.CreateAsync(name, ActorOf(name).DisplayName, UserRole.Reviewer, Password, cancellationToken);
            await store.AddMembershipAsync(user.Id, project.Id, cancellationToken);
            ids.Add(user.Id);
        }

        var checklists = TestServices.Checklists(store, clock);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                project.Id, ReviewId, item.Id, ChecklistOutcome.Pass, null, TestActors.Ann, cancellationToken);
        }

        var signing = TestServices.Signing(store, users, clock);
        var slots = new List<ReviewerSlot>
        {
            await signing.AddSlotAsync(ActorOf("rita"), project.Id, ReviewId, ids[0], ids[1], cancellationToken),
            await signing.AddSlotAsync(ActorOf("rita"), project.Id, ReviewId, ids[2], null, cancellationToken),
            await signing.AddSlotAsync(ActorOf("rita"), project.Id, ReviewId, ids[3], null, cancellationToken)
        };
        return new SignedReviewWorld(store, checklists, TestServices.Threads(store, clock), signing, project.Id, slots);
    }

    /// <summary>
    ///     Signs a slot with the record as it is now.
    /// </summary>
    /// <param name="slotNumber">The number of the slot, 1 to 3.</param>
    /// <param name="alternate"><c>true</c> to sign as the alternate of slot 1 instead of the primary.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    public async Task SignAsync(int slotNumber, bool alternate = false)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var primaryIndex = slotNumber == 1 ? 0 : slotNumber;
        var name = alternate ? "alex" : _names[primaryIndex];
        var digest = (await Signing.GetApprovalAsync(ProjectId, ReviewId, cancellationToken))!.Digest;
        await Signing.SignAsync(
            ActorOf(name), ProjectId, ReviewId, Slots[slotNumber - 1].Id, digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Reads the approval status of the review.
    /// </summary>
    /// <returns>The status.</returns>
    public async Task<ReviewApproval> ApprovalAsync()
    {
        return (await Signing.GetApprovalAsync(ProjectId, ReviewId, TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    ///     Reads the identities of the slots that hold a signature of the current record.
    /// </summary>
    /// <returns>The slot identities.</returns>
    public Task<IReadOnlyCollection<int>> SignedSlotIdsAsync()
    {
        return Store.CurrentlySignedSlotIdsAsync(ProjectId, ReviewId);
    }

    /// <summary>
    ///     Builds the signed-in user of a reviewer.
    /// </summary>
    /// <param name="name">The user name, one of the names of the world.</param>
    /// <returns>The actor; the display name is the user name with a capital first letter.</returns>
    private static Actor ActorOf(string name)
    {
        return new Actor(name, char.ToUpperInvariant(name[0]) + name[1..]);
    }
}
