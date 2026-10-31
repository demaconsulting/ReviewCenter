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
///     Unit tests of the review conclusion text rules of <see cref="ChecklistService"/>: who may write the text, the
///     lock of a concluded review, the normalization, the signature impact, the audit entry with lengths only and the
///     compare-and-set against a concurrent change, using an in-memory store.
/// </summary>
/// <remarks>
///     The tests of permissions use the real access rules with the users root (the lead), usr (a plain member), alt (a
///     reviewer of a slot), mem (a plain member), out (not a member) and aud (an auditor). The tests of the lock and
///     of the signature impact use the signed-review world, in which everybody is a lead. The text under test is the
///     free-text conclusion of the record, never the approved or rejected conclusion of a ballot.
/// </remarks>
public class ChecklistServiceReviewConclusionTextTests
{
    /// <summary>
    ///     The password of every user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The identifier of the review of both worlds.
    /// </summary>
    private const string ReviewId = "rev-1";

    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds an actor whose display name is the user name.
    /// </summary>
    /// <param name="name">The user name.</param>
    /// <returns>The actor.</returns>
    private static Actor Who(string name)
    {
        return new Actor(name, name);
    }

    /// <summary>
    ///     Arranges the users, a project with them and a review led by root, with a slot for alt.
    /// </summary>
    /// <returns>The arranged world.</returns>
    private static async Task<World> WorldAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var ids = new Dictionary<string, int>();
        foreach (var (name, admin) in new[]
                 {
                     ("root", true), ("usr", false), ("alt", false), ("mem", false), ("out", false), ("aud", false)
                 })
        {
            ids[name] = (await users.CreateAsync(name, name, admin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Own", null, Who("root"), cancellationToken);
        foreach (var name in new[] { "usr", "alt", "mem" })
        {
            await store.AddMembershipAsync(ids[name], project.Id, ProjectRole.User, cancellationToken);
        }

        await store.AddMembershipAsync(ids["aud"], project.Id, ProjectRole.Auditor, cancellationToken);
        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), access, clock);
        var reviews = new ReviewService(store, access, clock, repositories, new FakeGitRepositoryProvider());
        await reviews.CreateDraftAsync(
            project.Id, ReviewId, "First", [new UploadedFile("a.txt", [1, 2, 3])], Who("root"), cancellationToken);
        var signing = new SigningService(store, users, access, clock);
        await signing.AddSlotAsync(Who("root"), project.Id, ReviewId, ids["alt"], null, cancellationToken);
        return new World(store, new ChecklistService(store, access, userStore, clock), project.Id);
    }

    /// <summary>
    ///     Reads the review conclusion text of the review as the store holds it.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The stored text, or <c>null</c> when there is none.</returns>
    private static async Task<string?> StoredTextAsync(FakeReviewStore store, int projectId)
    {
        var review = await store.GetReviewAsync(projectId, ReviewId, TestContext.Current.CancellationToken);
        return review!.ReviewConclusionText;
    }

    /// <summary>
    ///     Selects the audit entries of the review conclusion text.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <returns>The entries in order.</returns>
    private static List<AuditDraft> ConclusionAudits(FakeReviewStore store)
    {
        return [.. store.Audits.Where(audit => audit.Action == ChecklistService.ReviewConclusionTextChangedAction)];
    }

    /// <summary>
    ///     Test that a reviewer of a slot and the lead can each store a text, and that the change is audited once.
    /// </summary>
    /// <param name="user">The user name of the writer.</param>
    [Theory]
    [InlineData("alt")]
    [InlineData("root")]
    public async Task ChecklistService_SetReviewConclusionTextAsync_SlotOwnerOrLead_Stores(string user)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var changed = await world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Looks good.", Who(user), cancellationToken);

        // Assert
        Assert.True(changed);
        Assert.Equal("Looks good.", await StoredTextAsync(world.Store, world.Project));
        Assert.Equal(user, Assert.Single(ConclusionAudits(world.Store)).ActorUsername);
    }

    /// <summary>
    ///     Test that a plain member who is neither a reviewer of a slot nor the lead is refused, and nothing is stored.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_OtherMember_ThrowsActorNotAllowed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Looks good.", Who("mem"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Null(await StoredTextAsync(world.Store, world.Project));
        Assert.Empty(ConclusionAudits(world.Store));
    }

    /// <summary>
    ///     Test that a person outside the project learns nothing: the project is reported as not found.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_NonMember_ThrowsProjectNotFound()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Looks good.", Who("out"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ProjectNotFound, ex.Rule);
        Assert.Null(await StoredTextAsync(world.Store, world.Project));
    }

    /// <summary>
    ///     Test that an auditor, who is read-only, cannot write the text.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_Auditor_Refused()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Looks good.", Who("aud"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Null(await StoredTextAsync(world.Store, world.Project));
    }

    /// <summary>
    ///     Test that an API token is refused even for the lead, because the text is written in the web pages only.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_ApiToken_Refused()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var viaToken = Who("root") with { ApiTokenId = "token-1" };

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Looks good.", viaToken, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Null(await StoredTextAsync(world.Store, world.Project));
    }

    /// <summary>
    ///     Test that a concluded review refuses the change as locked, stores nothing and records nothing.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_Concluded_ThrowsReviewLocked()
    {
        // Arrange: every slot is signed and no thread is open, so the review is concluded
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);
        await world.SignAsync(2);
        await world.SignAsync(3);
        var audits = world.Store.Audits.Count;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, ReviewId, "Too late.", TestActors.Ann, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ReviewLocked, ex.Rule);
        Assert.Null(await StoredTextAsync(world.Store, world.ProjectId));
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that the text is normalized (CRLF to LF, trimmed, blank to none) and that a text that is too long or
    ///     has control characters is refused with the review conclusion text rule.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_CrlfBlankLongControl_NormalizesOrThrows()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var root = Who("root");
        var longest = new string('x', ReviewLimits.MaxConclusionTextLength);

        // Act: CRLF and surrounding white space are normalized
        await world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "  Line one\r\nLine two \r\n", root, cancellationToken);
        var normalized = await StoredTextAsync(world.Store, world.Project);
        await world.Checklists.SetReviewConclusionTextAsync(world.Project, ReviewId, longest, root, cancellationToken);
        var atLimit = await StoredTextAsync(world.Store, world.Project);
        var tooLong = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, longest + "y", root, cancellationToken));
        var control = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "a\u0001b", root, cancellationToken));
        var loneCarriageReturn = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Checklists.SetReviewConclusionTextAsync(
                world.Project, ReviewId, "a\rb", root, cancellationToken));
        var cleared = await world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, " \r\n\t ", root, cancellationToken);

        // Assert: the longest text is stored, the refused texts leave it alone, and a blank text clears it
        Assert.Equal("Line one\nLine two", normalized);
        Assert.Equal(longest, atLimit);
        Assert.Equal(DomainRule.ConclusionTextInvalid, tooLong.Rule);
        Assert.Equal(DomainRule.ConclusionTextInvalid, control.Rule);
        Assert.Equal(DomainRule.ConclusionTextInvalid, loneCarriageReturn.Rule);
        Assert.True(cleared);
        Assert.Null(await StoredTextAsync(world.Store, world.Project));
    }

    /// <summary>
    ///     Test that a carriage return at the start or the end of the text, or alone, is refused: the control character
    ///     check runs before the trim, so the trim cannot hide it.
    /// </summary>
    /// <param name="text">The text with the stray carriage return.</param>
    [Theory]
    [InlineData("\r")]
    [InlineData("\ra")]
    [InlineData("a\r")]
    [InlineData("  \r  ")]
    [InlineData("a\r\r\nb")]
    public async Task ChecklistService_SetReviewConclusionTextAsync_StrayCarriageReturn_ThrowsAndStoresNothing(string text)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var audits = world.Store.Audits.Count;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, text, Who("root"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ConclusionTextInvalid, ex.Rule);
        Assert.Null(await StoredTextAsync(world.Store, world.Project));
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that changing the text while a signature is current needs the acknowledgement that names the affected
    ///     reviewer, and that the acknowledged change makes that signature out of date.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_AfterSignature_NeedsAcknowledgement()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.SignAsync(1);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, ReviewId, "Looks good.", TestActors.Ann, cancellationToken));
        var storedBefore = await StoredTextAsync(world.Store, world.ProjectId);
        var changed = await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId,
            ReviewId,
            "Looks good.",
            TestActors.Ann,
            cancellationToken,
            await world.SignedSlotIdsAsync());

        // Assert
        Assert.Equal(DomainRule.SignaturesWouldBecomeOutOfDate, ex.Rule);
        Assert.Contains("slot 1", ex.Message, StringComparison.Ordinal);
        Assert.Null(storedBefore);
        Assert.True(changed);
        Assert.Equal("Looks good.", await StoredTextAsync(world.Store, world.ProjectId));
        Assert.NotEqual(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that writing the text it already has changes nothing: it needs no acknowledgement, keeps the
    ///     signature and records no audit entry.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_SameValue_NoOpNoAudit()
    {
        // Arrange: a text, then slot 1 signs
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await SignedReviewWorld.CreateAsync();
        await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, ReviewId, "Looks good.", TestActors.Ann, cancellationToken);
        await world.SignAsync(1);
        var audits = world.Store.Audits.Count;

        // Act: the same text with the line break and white space of a browser form
        var same = await world.Checklists.SetReviewConclusionTextAsync(
            world.ProjectId, ReviewId, " Looks good.\r\n", TestActors.Ann, cancellationToken);

        // Assert
        Assert.False(same);
        Assert.Equal(audits, world.Store.Audits.Count);
        Assert.Equal(SlotSignatureState.Signed, (await world.ApprovalAsync()).Slots[0].State);
    }

    /// <summary>
    ///     Test that the audit entry names the old and the new length only and never the text.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_Audit_RecordsLengthsOnly()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        var root = Who("root");

        // Act
        await world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Secret wording.", root, cancellationToken);
        await world.Checklists.SetReviewConclusionTextAsync(world.Project, ReviewId, "Other", root, cancellationToken);
        await world.Checklists.SetReviewConclusionTextAsync(world.Project, ReviewId, null, root, cancellationToken);

        // Assert
        var audits = ConclusionAudits(world.Store);
        Assert.Equal(["length 0 -> 15", "length 15 -> 5", "length 5 -> 0"], audits.Select(audit => audit.Detail));
        Assert.All(audits, audit => Assert.Equal("review.conclusion-changed", audit.Action));
        Assert.All(audits, audit => Assert.Equal(ReviewId, audit.ReviewId));
        Assert.All(audits, audit => Assert.Equal(world.Project, audit.ProjectId));
        Assert.DoesNotContain(world.Store.Audits, audit => audit.Detail.Contains("Secret", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a change by someone else between the read and the store is not overwritten: the second writer
    ///     gets the record changed rule, and only the first change is stored and audited.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetReviewConclusionTextAsync_ConcurrentChange_ThrowsRecordChanged()
    {
        // Arrange: another lead stores a text after this writer read the review and before it stores its own
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();
        world.Store.BeforeReviewConclusionTextUpdate = async () =>
        {
            world.Store.BeforeReviewConclusionTextUpdate = null;
            await world.Store.UpdateReviewConclusionTextAsync(
                world.Project,
                ReviewId,
                null,
                "First writer.",
                AuditDraft.By(
                    Who("alt"),
                    ChecklistService.ReviewConclusionTextChangedAction,
                    "length 0 -> 13",
                    world.Project,
                    ReviewId),
                cancellationToken);
        };

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => world.Checklists.SetReviewConclusionTextAsync(
            world.Project, ReviewId, "Second writer.", Who("root"), cancellationToken));

        // Assert
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        Assert.Equal("First writer.", await StoredTextAsync(world.Store, world.Project));
        Assert.Equal("alt", Assert.Single(ConclusionAudits(world.Store)).ActorUsername);
    }

    /// <summary>
    ///     Test the answer of the person check: a reviewer of a slot and the lead may write the text, everyone else
    ///     may not, and a missing person, an API token and a missing review give no.
    /// </summary>
    [Fact]
    public async Task ChecklistService_CanSetReviewConclusionTextAsync_Roles()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var world = await WorldAsync();

        // Act
        async Task<bool> CanAsync(Actor? actor, string reviewId = ReviewId)
        {
            return await world.Checklists.CanSetReviewConclusionTextAsync(
                actor, world.Project, reviewId, cancellationToken);
        }

        // Assert
        Assert.True(await CanAsync(Who("root")));
        Assert.True(await CanAsync(Who("alt")));
        Assert.False(await CanAsync(Who("usr")));
        Assert.False(await CanAsync(Who("mem")));
        Assert.False(await CanAsync(Who("aud")));
        Assert.False(await CanAsync(Who("out")));
        Assert.False(await CanAsync(null));
        Assert.False(await CanAsync(Who("root") with { ApiTokenId = "token-1" }));
        Assert.False(await CanAsync(Who("root"), "missing"));
    }

    /// <summary>
    ///     The arranged world of the permission tests.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Checklists">The checklist service under test.</param>
    /// <param name="Project">The project identity.</param>
    private sealed record World(FakeReviewStore Store, ChecklistService Checklists, int Project);
}
