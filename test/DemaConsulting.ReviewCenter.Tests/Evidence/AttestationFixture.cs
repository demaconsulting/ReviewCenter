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
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     A project with one review <c>rev-1</c> over in-memory stores, with every check answered and one reviewer slot
///     (ann primary, bob alternate), signed or not as the test needs; the stores the attestation reads from.
/// </summary>
internal sealed class AttestationFixture
{
    /// <summary>
    ///     The password every test user has.
    /// </summary>
    public const string Password = "correct horse battery";

    /// <summary>
    ///     The review identifier.
    /// </summary>
    public const string ReviewId = "rev-1";

    /// <summary>
    ///     Initializes a new instance of the <see cref="AttestationFixture"/> class.
    /// </summary>
    private AttestationFixture()
    {
        Store = new FakeReviewStore();
        Clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        AuditStore = new FakeAuditStore(Clock);
        Audit = new AuditService(AuditStore);
        var userStore = new FakeUserStore();
        Users = new UserService(userStore, new FakePasswordHasher(), Clock);
        Access = new AllowAllProjectAccess(Store);
        Signing = TestServices.Signing(Store, Users, Clock);
        Checklists = TestServices.Checklists(Store, Clock, userStore);
    }

    /// <summary>
    ///     Gets the access service, which lets every actor see every project.
    /// </summary>
    public AllowAllProjectAccess Access { get; }

    /// <summary>
    ///     Gets the review store.
    /// </summary>
    public FakeReviewStore Store { get; }

    /// <summary>
    ///     Gets the clock.
    /// </summary>
    public MovableTimeProvider Clock { get; }

    /// <summary>
    ///     Gets the audit store.
    /// </summary>
    public FakeAuditStore AuditStore { get; }

    /// <summary>
    ///     Gets the audit service.
    /// </summary>
    public AuditService Audit { get; }

    /// <summary>
    ///     Gets the user service.
    /// </summary>
    public UserService Users { get; }

    /// <summary>
    ///     Gets the signing service.
    /// </summary>
    public SigningService Signing { get; }

    /// <summary>
    ///     Gets the checklist service.
    /// </summary>
    public ChecklistService Checklists { get; }

    /// <summary>
    ///     Gets the project number.
    /// </summary>
    public int ProjectId { get; private set; }

    /// <summary>
    ///     Gets the identity of the only slot.
    /// </summary>
    public int SlotId { get; private set; }

    /// <summary>
    ///     Gets the identity of the user cyd, who is in no slot at first.
    /// </summary>
    public int CydUserId { get; private set; }

    /// <summary>
    ///     Gets the identity of the user ann.
    /// </summary>
    private int AnnUserId { get; set; }

    /// <summary>
    ///     Gets the identity of the user bob.
    /// </summary>
    private int BobUserId { get; set; }

    /// <summary>
    ///     Creates a follow-up review of the fixture's review (which carries every answer, as all are Pass), adds one
    ///     slot and signs it as ann, so the follow-up is approved.
    /// </summary>
    /// <param name="reviewId">The identifier of the follow-up review.</param>
    /// <returns>A task that completes when the follow-up is signed.</returns>
    public async Task CreateSignedFollowUpAsync(string reviewId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TestServices.Reviews(Store, Clock).CreateDraftAsync(
            ProjectId, reviewId, "Follow-up", [new("a.txt", TestContent.A)], new Actor("cyd", "Cyd"), cancellationToken, followsReviewId: ReviewId);
        var slot = await Signing.AddSlotAsync(new Actor("cyd", "Cyd"), ProjectId, reviewId, AnnUserId, BobUserId, cancellationToken);
        await SubmitAsync(reviewId, slot.Id);
        var approval = await Signing.GetApprovalAsync(ProjectId, reviewId, cancellationToken);
        await Signing.SignAsync(TestActors.Ann, ProjectId, reviewId, slot.Id, approval!.Digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Creates the fixture.
    /// </summary>
    /// <param name="signed">Whether the slot is signed, which completes the review.</param>
    /// <returns>The fixture.</returns>
    public static async Task<AttestationFixture> CreateAsync(bool signed)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = new AttestationFixture();
        var ids = new Dictionary<string, int>();
        foreach (var (username, name) in new[] { ("ann", "Ann"), ("bob", "Bob"), ("cyd", "Cyd") })
        {
            ids[username] = (await fixture.Users.CreateAsync(username, name, UserRole.Reviewer, Password, cancellationToken)).Id;
        }

        fixture.CydUserId = ids["cyd"];
        fixture.AnnUserId = ids["ann"];
        fixture.BobUserId = ids["bob"];
        var project = await TestServices.Projects(fixture.Store, fixture.Clock).CreateAsync("Apollo", null, cancellationToken);
        fixture.ProjectId = project.Id;
        foreach (var id in ids.Values)
        {
            await fixture.Store.AddMembershipAsync(id, project.Id, cancellationToken);
        }

        await TestServices.Reviews(fixture.Store, fixture.Clock).CreateDraftAsync(
            project.Id, ReviewId, "First", [new("a.txt", TestContent.A)], cancellationToken);
        var slot = await fixture.Signing.AddSlotAsync(new Actor("cyd", "Cyd"), project.Id, ReviewId, ids["ann"], ids["bob"], cancellationToken);
        fixture.SlotId = slot.Id;
        await fixture.AnswerAllAsync(ChecklistOutcome.Pass);
        if (signed)
        {
            await fixture.SignAsync();
        }

        return fixture;
    }

    /// <summary>
    ///     Answers every check of the checklist in the ballot of the first slot and hands the ballot in. The answers are
    ///     written to the store directly, so the step also works on an approved review, which the services refuse to
    ///     change: it models a change that slipped in while a document was being prepared.
    /// </summary>
    /// <param name="outcome">The answer.</param>
    /// <param name="reviewId">The review to answer; the fixture's review when <c>null</c>.</param>
    /// <returns>A task that completes when all checks are answered.</returns>
    public async Task AnswerAllAsync(ChecklistOutcome outcome, string? reviewId = null)
    {
        reviewId ??= ReviewId;
        var cancellationToken = TestContext.Current.CancellationToken;
        var slot = (await Store.ListSlotsAsync(ProjectId, reviewId, cancellationToken))[0];
        Store.IgnoreLockForBallots = true;
        try
        {
            foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
            {
                await Store.SetBallotAnswerAsync(
                    new ChecklistAnswer
                    {
                        ProjectId = ProjectId,
                        ReviewId = reviewId,
                        SlotId = slot.Id,
                        ItemId = item.Id,
                        Outcome = outcome,
                        Justification = outcome == ChecklistOutcome.Pass ? null : "Changed after the signature.",
                        AnsweredBy = TestActors.Ann.DisplayName,
                        AnsweredByUsername = TestActors.Ann.Username,
                        AnsweredAt = Clock.GetUtcNow()
                    },
                    AuditDraft.By(TestActors.Ann, "checklist.answered", item.Id, ProjectId, reviewId),
                    null,
                    cancellationToken);
            }

            await SubmitAsync(reviewId, slot.Id);
        }
        finally
        {
            Store.IgnoreLockForBallots = false;
        }
    }

    /// <summary>
    ///     Hands in the ballot of a slot straight in the store.
    /// </summary>
    /// <param name="reviewId">The review.</param>
    /// <param name="slotId">The slot.</param>
    /// <returns>A task that completes when the ballot is handed in.</returns>
    public Task SubmitAsync(string reviewId, int slotId)
    {
        return Store.SubmitBallotAsync(
            ProjectId,
            reviewId,
            slotId,
            Clock.GetUtcNow(),
            TestActors.Ann.Username,
            AuditDraft.By(TestActors.Ann, "ballot.submitted", "slot", ProjectId, reviewId),
            _ => true,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Adds an unsigned slot for cyd straight to the store, as if it slipped in while a document was prepared.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new slot.</returns>
    public Task<ReviewerSlot> AddUnsignedSlotAsync(CancellationToken cancellationToken)
    {
        return Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = ProjectId, ReviewId = ReviewId, PrimaryUserId = CydUserId },
            _ => AuditDraft.By(new Actor("cyd", "Cyd"), "slot.added", "slot 2", ProjectId, ReviewId),
            cancellationToken);
    }

    /// <summary>
    ///     Signs the slot as ann with the current record.
    /// </summary>
    /// <returns>A task that completes when the slot is signed.</returns>
    public async Task SignAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approval = await Signing.GetApprovalAsync(ProjectId, ReviewId, cancellationToken);
        await Signing.SignAsync(TestActors.Ann, ProjectId, ReviewId, SlotId, approval!.Digest, Password, cancellationToken);
    }

    /// <summary>
    ///     Creates the attestation service over the fixture and a key provider.
    /// </summary>
    /// <param name="keys">The key provider.</param>
    /// <param name="signer">The signer; the real one when <c>null</c>.</param>
    /// <param name="afterRender">A step that runs after rendering and before the second check; none when <c>null</c>.</param>
    /// <returns>The service.</returns>
    public AttestationService CreateService(
        IAttestationKeyProvider keys,
        IAttestationSigner? signer = null,
        Func<CancellationToken, Task>? afterRender = null)
    {
        return new AttestationService(
            Store, Signing, Access, Audit, keys, new AttestationRenderer(), signer ?? new AttestationSigner(), Clock)
        {
            AfterRender = afterRender
        };
    }

    /// <summary>
    ///     Adds a second slot for cyd and signs it as cyd with the current record, which keeps the review complete
    ///     but changes its set of slots.
    /// </summary>
    /// <returns>A task that completes when the slot is signed.</returns>
    public async Task AddAndSignSecondSlotAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cyd = new Actor("cyd", "Cyd");
        var slot = await AddUnsignedSlotAsync(cancellationToken);
        await EvidenceTestSupport.AnswerAllPassAsync(Checklists, ProjectId, ReviewId, cyd);
        var approval = await Signing.GetApprovalAsync(ProjectId, ReviewId, cancellationToken);
        await Signing.SignAsync(cyd, ProjectId, ReviewId, slot.Id, approval!.Digest, Password, cancellationToken);
    }
}
