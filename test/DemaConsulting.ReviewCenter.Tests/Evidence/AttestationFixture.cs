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
        Users = new UserService(new FakeUserStore(), new FakePasswordHasher(), Clock);
        Signing = new SigningService(Store, Users, Clock);
        Checklists = new ChecklistService(Store, Clock);
    }

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

        var project = await new ProjectService(fixture.Store, fixture.Clock).CreateAsync("Apollo", null, cancellationToken);
        fixture.ProjectId = project.Id;
        await new ReviewService(fixture.Store, fixture.Clock).CreateDraftAsync(
            project.Id, ReviewId, "First", [new("a.txt", TestContent.A)], cancellationToken);
        await fixture.AnswerAllAsync(ChecklistOutcome.Pass);
        var slot = await fixture.Signing.AddSlotAsync(new Actor("cyd", "Cyd"), project.Id, ReviewId, ids["ann"], ids["bob"], cancellationToken);
        fixture.SlotId = slot.Id;
        if (signed)
        {
            await fixture.SignAsync();
        }

        return fixture;
    }

    /// <summary>
    ///     Answers every check of the checklist.
    /// </summary>
    /// <param name="outcome">The answer.</param>
    /// <returns>A task that completes when all checks are answered.</returns>
    public async Task AnswerAllAsync(ChecklistOutcome outcome)
    {
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await Checklists.SetAnswerAsync(
                ProjectId, ReviewId, item.Id, outcome, outcome == ChecklistOutcome.Pass ? null : "Changed after the signature.", TestActors.Ann, TestContext.Current.CancellationToken);
        }
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
    /// <returns>The service.</returns>
    public AttestationService CreateService(IAttestationKeyProvider keys, IAttestationSigner? signer = null)
    {
        return new AttestationService(Store, Signing, Audit, keys, new AttestationRenderer(), signer ?? new AttestationSigner(), Clock);
    }
}
