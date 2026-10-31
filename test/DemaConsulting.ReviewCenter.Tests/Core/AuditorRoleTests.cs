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
///     Tests of the read-only auditor role in the services: it is refused while the user still has reviewer work on a
///     review that is not approved, it is never offered as a reviewer, and an auditor can neither be put in a slot nor
///     sign.
/// </summary>
public class AuditorRoleTests
{
    /// <summary>
    ///     The password of every arranged user.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The instant reported by the clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that a user with an unsigned slot, as primary or as alternate reviewer, on a review that is not
    ///     approved cannot become an auditor, that the refusal names the review, and that nothing changes.
    /// </summary>
    /// <param name="asAlternate">Whether the user is the alternate of the slot instead of the primary.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectService_SetMemberRoleAsync_UnsignedSlot_RefusesAuditorAndNamesTheReview(bool asAlternate)
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        var primary = asAlternate ? world.Ids["alt"] : world.Ids["usr"];
        var alternate = asAlternate ? world.Ids["usr"] : (int?)null;
        await world.Signing.AddSlotAsync(root, world.Own, "rev-1", primary, alternate, cancellationToken);
        var auditsBefore = world.Store.Audits.Count;

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.SetMemberRoleAsync(root, world.Own, "usr", ProjectRole.Auditor, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.MemberHasOpenReviewWork, ex.Rule);
        Assert.Contains("'rev-1' (First)", ex.Message);
        Assert.Equal(
            ProjectRole.User,
            (await world.Store.GetMembershipAsync(world.Ids["usr"], world.Own, cancellationToken))!.Role);
        Assert.Equal(auditsBefore, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that a slot that was signed but whose signature is out of date because the record changed still
    ///     counts as open work.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetMemberRoleAsync_OutOfDateSlot_RefusesAuditor()
    {
        // Arrange: usr signs the record, then a comment changes it; a second slot keeps the review from being approved
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        var slot = await world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["usr"], null, cancellationToken);
        await world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["alt"], null, cancellationToken);
        await SignDirectlyAsync(world, slot.Id, world.Ids["usr"]);
        var acknowledged = await world.Store.CurrentlySignedSlotIdsAsync(world.Own, "rev-1");
        await world.Threads.StartThreadAsync(
            world.Own, "rev-1", new ThreadAnchor("a.txt"), root, "late remark", cancellationToken, acknowledged, true);
        var approval = await world.Signing.GetApprovalAsync(world.Own, "rev-1", cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.SetMemberRoleAsync(root, world.Own, "usr", ProjectRole.Auditor, cancellationToken));

        // Assert
        Assert.Equal(SlotSignatureState.OutOfDate, approval!.Slots[0].State);
        Assert.Equal(DomainRule.MemberHasOpenReviewWork, ex.Rule);
    }

    /// <summary>
    ///     Test that a user with no slot, with a signed slot on a review that is approved, or with a slot only in a
    ///     review that is approved can become an auditor, and that the change is audited with the role.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetMemberRoleAsync_NoOpenWork_AllowsAuditor()
    {
        // Arrange: alt has no slot; usr has the only slot, which is signed, so the review is approved
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        var slot = await world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["usr"], null, cancellationToken);
        await SignDirectlyAsync(world, slot.Id, world.Ids["usr"]);

        // Act
        await world.Projects.SetMemberRoleAsync(root, world.Own, "alt", ProjectRole.Auditor, cancellationToken);
        await world.Projects.SetMemberRoleAsync(root, world.Own, "usr", ProjectRole.Auditor, cancellationToken);

        // Assert
        foreach (var name in new[] { "alt", "usr" })
        {
            Assert.Equal(
                ProjectRole.Auditor,
                (await world.Store.GetMembershipAsync(world.Ids[name], world.Own, cancellationToken))!.Role);
        }

        Assert.Contains(
            world.Store.Audits,
            audit => audit.Action == "project.member-role-changed" && audit.Detail.Contains("user 'usr', role Auditor"));
    }

    /// <summary>
    ///     Test that the last project administrator cannot make themselves an auditor.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetMemberRoleAsync_LastProjectAdminToAuditor_IsRefused()
    {
        // Arrange: padm is the only project administrator
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        await world.Projects.AddMemberAsync(root, world.Own, "padm", ProjectRole.ProjectAdmin, cancellationToken);

        // Act: a system administrator is exempt from the rule, a project administrator is not
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.SetMemberRoleAsync(
                new Actor("padm", "padm"), world.Own, "padm", ProjectRole.Auditor, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.LastProjectAdmin, ex.Rule);
        Assert.Equal(
            ProjectRole.ProjectAdmin,
            (await world.Store.GetMembershipAsync(world.Ids["padm"], world.Own, cancellationToken))!.Role);
    }

    /// <summary>
    ///     Test that a new member can be added as an auditor even though the open-work rule exists, because a new
    ///     member has no slot.
    /// </summary>
    [Fact]
    public async Task ProjectService_AddMemberAsync_Auditor_IsAllowedForANewMember()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");

        // Act
        await world.Projects.AddMemberAsync(root, world.Own, "out", ProjectRole.Auditor, cancellationToken);

        // Assert
        Assert.Equal(
            ProjectRole.Auditor,
            (await world.Store.GetMembershipAsync(world.Ids["out"], world.Own, cancellationToken))!.Role);
    }

    /// <summary>
    ///     Test that adding a user who already is a member as an auditor reports the duplicate membership, even when
    ///     the user holds open review work, and that a removed member who still holds a slot is refused on return.
    /// </summary>
    [Fact]
    public async Task ProjectService_AddMemberAsync_Auditor_ExistingMemberReportsDuplicate_RemovedMemberWithSlotIsRefused()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        await world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["usr"], null, cancellationToken);

        // Act
        var duplicate = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(root, world.Own, "usr", ProjectRole.Auditor, cancellationToken));
        await world.Projects.RemoveMemberAsync(root, world.Own, "usr", cancellationToken);
        var returning = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Projects.AddMemberAsync(root, world.Own, "usr", ProjectRole.Auditor, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.MembershipExists, duplicate.Rule);
        Assert.Equal(DomainRule.MemberHasOpenReviewWork, returning.Rule);
        Assert.Contains("'rev-1' (First)", returning.Message);
    }

    /// <summary>
    ///     Test that an auditor is not offered as a reviewer, cannot be put in a slot as primary or alternate, and
    ///     cannot be moved into one by an update.
    /// </summary>
    [Fact]
    public async Task SigningService_Slots_Auditor_IsNeitherCandidateNorAssignable()
    {
        // Arrange: aud is an auditor of the project
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        await world.Projects.AddMemberAsync(root, world.Own, "aud", ProjectRole.Auditor, cancellationToken);
        var slot = await world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["usr"], null, cancellationToken);

        // Act
        var candidates = await world.Signing.ListCandidatesAsync(root, world.Own, cancellationToken);
        var asPrimary = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["aud"], null, cancellationToken));
        var asAlternate = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Signing.AddSlotAsync(
                root, world.Own, "rev-1", world.Ids["usr"], world.Ids["aud"], cancellationToken));
        var update = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Signing.UpdateSlotAsync(
                root, world.Own, "rev-1", slot.Id, world.Ids["usr"], world.Ids["aud"], cancellationToken));

        // Assert
        Assert.DoesNotContain(candidates, candidate => candidate.Username == "aud");
        Assert.Contains(candidates, candidate => candidate.Username == "usr");
        Assert.All([asPrimary, asAlternate, update], e => Assert.Equal(DomainRule.SlotUserInvalid, e.Rule));
        Assert.Single(await world.Store.ListSlotsAsync(world.Own, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that a user whose slot still names them cannot sign once their role is auditor, even when they are a
    ///     system administrator, and that nothing is signed.
    /// </summary>
    /// <param name="systemAdmin">Whether the auditor is also a system administrator.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SigningService_SignAsync_AuditorStillNamedInASlot_IsRefused(bool systemAdmin)
    {
        // Arrange: the slot names usr; the role is then changed behind the service's back, as an old row could be
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        if (systemAdmin)
        {
            await world.Users.SetSystemAdminAsync("usr", true, Actor.CommandLine, cancellationToken);
        }

        var slot = await world.Signing.AddSlotAsync(root, world.Own, "rev-1", world.Ids["usr"], null, cancellationToken);
        var digest = (await world.Signing.GetApprovalAsync(world.Own, "rev-1", cancellationToken))!.Digest;
        await world.Store.SetMembershipRoleAsync(
            world.Own,
            world.Ids["usr"],
            ProjectRole.Auditor,
            AuditDraft.By(root, "project.member-role-changed", "test", world.Own),
            false,
            cancellationToken);

        // Act
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Signing.SignAsync(
                new Actor("usr", "usr"), world.Own, "rev-1", slot.Id, digest, Password, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, ex.Rule);
        Assert.Empty(await world.Store.ListSignaturesAsync(world.Own, "rev-1", cancellationToken));
    }

    /// <summary>
    ///     Test that an auditor is refused every member management operation, as not allowed, and that nothing
    ///     changes: only project administrators and system administrators manage members.
    /// </summary>
    [Fact]
    public async Task ProjectService_MemberManagement_Auditor_IsRefused()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        await world.Projects.AddMemberAsync(root, world.Own, "aud", ProjectRole.Auditor, cancellationToken);
        var auditor = new Actor("aud", "aud");
        var membersBefore = (await world.Store.ListMembershipsAsync(world.Own, cancellationToken)).Count;

        // Act
        var operations = new Func<Task>[]
        {
            () => world.Projects.ListMembersAsync(auditor, world.Own, cancellationToken),
            () => world.Projects.AddMemberAsync(auditor, world.Own, "out", ProjectRole.User, cancellationToken),
            () => world.Projects.CreateMemberAccountAsync(
                auditor, world.Own, "fresh", "Fresh", ProjectRole.User, cancellationToken),
            () => world.Projects.SetMemberRoleAsync(auditor, world.Own, "usr", ProjectRole.Auditor, cancellationToken),
            () => world.Projects.SetMemberRoleAsync(auditor, world.Own, "aud", ProjectRole.ProjectAdmin, cancellationToken),
            () => world.Projects.RemoveMemberAsync(auditor, world.Own, "usr", cancellationToken)
        };
        var rules = new List<DomainRule>();
        foreach (var operation in operations)
        {
            rules.Add((await Assert.ThrowsAsync<DomainRuleException>(operation)).Rule);
        }

        // Assert
        Assert.All(rules, rule => Assert.Equal(DomainRule.ActorNotAllowed, rule));
        Assert.Equal(membersBefore, (await world.Store.ListMembershipsAsync(world.Own, cancellationToken)).Count);
    }

    /// <summary>
    ///     Test that an auditor sees the progress overview of the project, like every member who may view it.
    /// </summary>
    [Fact]
    public async Task ReviewProgressService_GetProgressAsync_Auditor_SeesTheOverview()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");
        await world.Projects.AddMemberAsync(root, world.Own, "aud", ProjectRole.Auditor, cancellationToken);
        var progress = new ReviewProgressService(
            world.Reviews, world.Signing, new FakeAuditStore(TimeProvider.System), world.Access);

        // Act
        var own = await progress.GetProgressAsync(new Actor("aud", "aud"), world.Own, cancellationToken);
        var other = await Assert.ThrowsAsync<DomainRuleException>(
            () => progress.GetProgressAsync(new Actor("aud", "aud"), world.Other, cancellationToken));

        // Assert: the overview of the own project, and a project of which they are no member does not exist
        Assert.Equal(["rev-1"], own.Select(p => p.ReviewId));
        Assert.Equal(DomainRule.ProjectNotFound, other.Rule);
    }

    /// <summary>
    ///     Stores a signature of the current record for a slot, as the signing service would.
    /// </summary>
    /// <param name="world">The arranged world.</param>
    /// <param name="slotId">The slot.</param>
    /// <param name="userId">The signer.</param>
    /// <returns>A task that completes when the signature is stored.</returns>
    private static async Task SignDirectlyAsync(World world, int slotId, int userId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var record = (await world.Store.GetRecordAsync(world.Own, "rev-1", cancellationToken))!;
        await world.Store.AddSignatureAsync(
            new ReviewSignature
            {
                ProjectId = world.Own,
                ReviewId = "rev-1",
                SlotId = slotId,
                UserId = userId,
                DisplayName = "signer",
                Role = SlotRole.Primary,
                SignedAt = _now,
                RecordDigest = RecordDigest.Compute(record),
                Meaning = "test"
            },
            AuditDraft.By(new Actor("root", "root"), "signature.added", "test", world.Own, "rev-1"),
            cancellationToken);
    }

    /// <summary>
    ///     Arranges two projects, the users and a review of the first project without slots. The users usr, alt and
    ///     padm are not members yet unless stated: usr and alt are users of the own project.
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
                     ("root", true), ("usr", false), ("alt", false), ("out", false), ("aud", false), ("padm", false)
                 })
        {
            ids[name] = (await users.CreateAsync(name, name, admin, Password, Actor.CommandLine, cancellationToken)).Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var root = new Actor("root", "root");
        var own = await projects.CreateAsync("Own", null, root, cancellationToken);
        var other = await projects.CreateAsync("Other", null, root, cancellationToken);
        await store.AddMembershipAsync(ids["usr"], own.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["alt"], own.Id, ProjectRole.User, cancellationToken);

        var repositories = new RepositoryService(new FakeRepositoryStore(), new FakeCredentialProtector(), access, clock);
        var reviews = new ReviewService(store, access, clock, repositories, new FakeGitRepositoryProvider());
        var threads = new ThreadService(store, access, userStore, clock);
        var signing = new SigningService(store, users, access, clock);
        await reviews.CreateDraftAsync(
            own.Id, "rev-1", "First", [new UploadedFile("a.txt", [1, 2, 3])], root, cancellationToken);
        return new World(store, users, reviews, threads, signing, projects, access, own.Id, other.Id, ids);
    }

    /// <summary>
    ///     The arranged services and data.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Users">The user service.</param>
    /// <param name="Reviews">The review service.</param>
    /// <param name="Threads">The thread service.</param>
    /// <param name="Signing">The signing service.</param>
    /// <param name="Projects">The project service.</param>
    /// <param name="Access">The access rules.</param>
    /// <param name="Own">The project with the review.</param>
    /// <param name="Other">A second project without the users.</param>
    /// <param name="Ids">The identities of the users by user name.</param>
    private sealed record World(
        FakeReviewStore Store,
        UserService Users,
        ReviewService Reviews,
        ThreadService Threads,
        SigningService Signing,
        ProjectService Projects,
        ProjectAccess Access,
        int Own,
        int Other,
        IReadOnlyDictionary<string, int> Ids);
}
