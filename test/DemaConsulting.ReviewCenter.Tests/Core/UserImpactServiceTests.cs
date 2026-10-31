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
///     Unit tests for <see cref="UserImpactService"/> over in-memory stores and the real access rules: the open work
///     of a leaver, the projects left without an enabled project administrator, and who may ask.
/// </summary>
public class UserImpactServiceTests
{
    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that the primary and the alternate reviewer of a slot, and the primary of another slot, each find the
    ///     review as open work with the number and the role of their slot.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_PrimaryAndAlternateSlots_AreOpenWork()
    {
        // Arrange: slot 1 is ann (primary) and bob (alternate), slot 2 is cyd
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1", title: "Check the plan");
        await setup.World.AddSlotAsync("r1", "ann", "bob");
        await setup.World.AddSlotAsync("r1", "cyd");

        // Act
        var ann = await setup.ImpactOfAsync("ann");
        var bob = await setup.ImpactOfAsync("bob");
        var cyd = await setup.ImpactOfAsync("cyd");

        // Assert
        var annWork = Assert.Single(ann.OpenWork);
        Assert.Equal(setup.World.ProjectId, annWork.ProjectId);
        Assert.Equal("Apollo", annWork.ProjectName);
        Assert.Equal("r1", annWork.ReviewId);
        Assert.Equal("Check the plan", annWork.Title);
        Assert.Equal(1, annWork.SlotNumber);
        Assert.True(annWork.IsPrimary);
        Assert.False(annWork.IsAlternate);
        Assert.Equal(SlotSignatureState.NotSigned, annWork.SlotState);
        var bobWork = Assert.Single(bob.OpenWork);
        Assert.Equal((1, SlotRole.Alternate, true), (bobWork.SlotNumber, bobWork.Role, bobWork.IsAlternate));
        var cydWork = Assert.Single(cyd.OpenWork);
        Assert.Equal((2, SlotRole.Primary), (cydWork.SlotNumber, cydWork.Role));
        Assert.Empty(ann.SoleAdminProjects);
        Assert.False(ann.IsEmpty);
    }

    /// <summary>
    ///     Test that a slot with a current signature is no open work for its primary or its alternate reviewer, while
    ///     the other, unsigned slot still is.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_SignedSlot_IsNotOpenWork()
    {
        // Arrange: ann (alternate bob) signed slot 1; cyd's slot 2 is open, so the review is not approved
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AnswerAllAsync("r1");
        var first = await setup.World.AddSlotAsync("r1", "ann", "bob");
        await setup.World.AddSlotAsync("r1", "cyd");
        await setup.World.SignAsync("r1", first.Id, "ann");

        // Act
        var ann = await setup.ImpactOfAsync("ann");
        var bob = await setup.ImpactOfAsync("bob");
        var cyd = await setup.ImpactOfAsync("cyd");

        // Assert
        Assert.Empty(ann.OpenWork);
        Assert.Empty(bob.OpenWork);
        Assert.Equal(2, Assert.Single(cyd.OpenWork).SlotNumber);
        Assert.True(ann.IsEmpty);
    }

    /// <summary>
    ///     Test that a slot whose signature no longer covers the record is open work in the state out of date.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_OutOfDateSlot_IsOpenWorkAsOutOfDate()
    {
        // Arrange: ann signed, then a failed check changed the record, so the signature is out of date
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AnswerAllAsync("r1");
        var first = await setup.World.AddSlotAsync("r1", "ann");
        await setup.World.AddSlotAsync("r1", "bob");
        await setup.World.SignAsync("r1", first.Id, "ann");
        await setup.World.FailCheckAsync("r1", "eve");

        // Act
        var ann = await setup.ImpactOfAsync("ann");

        // Assert
        var work = Assert.Single(ann.OpenWork);
        Assert.Equal(SlotSignatureState.OutOfDate, work.SlotState);
    }

    /// <summary>
    ///     Test that the slots of an approved (locked) review are no open work, while the unlocked review of the same
    ///     user is listed.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_ApprovedReviewExcluded_UnlockedReviewIncluded()
    {
        // Arrange: r1 is approved by ann alone; r2 is unlocked and waits for ann
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AnswerAllAsync("r1");
        var approved = await setup.World.AddSlotAsync("r1", "ann");
        await setup.World.SignAsync("r1", approved.Id, "ann");
        await setup.World.CreateReviewAsync("r2", title: "Second");
        await setup.World.AddSlotAsync("r2", "ann");

        // Act
        var impact = await setup.ImpactOfAsync("ann");

        // Assert
        var work = Assert.Single(impact.OpenWork);
        Assert.Equal("r2", work.ReviewId);
        Assert.Equal("Second", work.Title);
    }

    /// <summary>
    ///     Test that the open work is ordered by project, review and slot, and read with one batch read of snapshots
    ///     per project instead of one read per review.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_ManyReviews_AreOrderedAndReadInOneBatchPerProject()
    {
        // Arrange: two reviews in Apollo and one in Borealis, all waiting for ann
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r2");
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AddSlotAsync("r2", "ann");
        await setup.World.AddSlotAsync("r1", "ann");
        await setup.AddBorealisReviewAsync("b1", "ann");

        // Act
        var batchBefore = setup.World.Store.SnapshotBatchReads;
        var singleBefore = setup.World.Store.SnapshotReads;
        var impact = await setup.ImpactOfAsync("ann");

        // Assert: Apollo before Borealis by name, then by review identifier; two batch reads and no single read
        Assert.Equal(["Apollo/r1", "Apollo/r2", "Borealis/b1"], impact.OpenWork.Select(w => $"{w.ProjectName}/{w.ReviewId}"));
        Assert.Equal(2, setup.World.Store.SnapshotBatchReads - batchBefore);
        Assert.Equal(0, setup.World.Store.SnapshotReads - singleBefore);
    }

    /// <summary>
    ///     Test that a project administrator who is the only enabled one is reported, and that a system administrator
    ///     who is not a member does not count as a project administrator.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_OnlyEnabledProjectAdministrator_IsSoleAdmin()
    {
        // Arrange: adm_a is the only project administrator of Apollo; root is a system administrator, not a member
        var setup = await ArrangeAsync();

        // Act
        var impact = await setup.ImpactOfAsync("adm_a");

        // Assert
        var project = Assert.Single(impact.SoleAdminProjects);
        Assert.Equal((setup.World.ProjectId, "Apollo"), (project.ProjectId, project.ProjectName));
        Assert.Empty(impact.OpenWork);
        Assert.False(impact.IsEmpty);
    }

    /// <summary>
    ///     Test that another enabled project administrator means the user is not the sole one.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_AnotherEnabledAdministrator_IsNotSoleAdmin()
    {
        // Arrange
        var setup = await ArrangeAsync();
        await setup.AddUserAsync("adm_a2", setup.World.ProjectId, ProjectRole.ProjectAdmin);

        // Act
        var first = await setup.ImpactOfAsync("adm_a");
        var second = await setup.ImpactOfAsync("adm_a2");

        // Assert
        Assert.Empty(first.SoleAdminProjects);
        Assert.Empty(second.SoleAdminProjects);
    }

    /// <summary>
    ///     Test that a disabled second administrator does not count, and that a disabled user is not a sole
    ///     administrator, because disabling again changes nothing.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_DisabledSecondAdministrator_DoesNotCount()
    {
        // Arrange: adm_a2 is a second administrator of Apollo, then disabled
        var setup = await ArrangeAsync();
        await setup.AddUserAsync("adm_a2", setup.World.ProjectId, ProjectRole.ProjectAdmin);
        await setup.World.Users.SetEnabledAsync("adm_a2", false, Ct);

        // Act
        var enabledOne = await setup.ImpactOfAsync("adm_a");
        var disabledOne = await setup.ImpactOfAsync("adm_a2");

        // Assert
        Assert.Equal("Apollo", Assert.Single(enabledOne.SoleAdminProjects).ProjectName);
        Assert.Empty(disabledOne.SoleAdminProjects);
    }

    /// <summary>
    ///     Test that a system administrator may ask about any user and sees the work and the sole administrator
    ///     projects of every project.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_SystemAdministrator_SeesEveryProject()
    {
        // Arrange: ann waits in both projects; adm_b is the sole administrator of Borealis
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AddSlotAsync("r1", "ann");
        await setup.AddBorealisReviewAsync("b1", "ann");

        // Act
        var ann = await setup.ImpactOfAsync("ann", "root");
        var pb = await setup.ImpactOfAsync("adm_b", "root");

        // Assert
        Assert.Equal(["Apollo", "Borealis"], ann.OpenWork.Select(work => work.ProjectName));
        Assert.Equal(["Borealis"], pb.SoleAdminProjects.Select(project => project.ProjectName));
    }

    /// <summary>
    ///     Test that a project administrator may ask about any user, but the answer is limited to the projects the
    ///     asker administers, for the open work and for the sole administrator projects.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_ProjectAdministrator_SeesOnlyOwnProjects()
    {
        // Arrange: ann waits in both projects; adm_b is the sole administrator of Borealis
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AddSlotAsync("r1", "ann");
        await setup.AddBorealisReviewAsync("b1", "ann");

        // Act
        var annForPa = await setup.ImpactOfAsync("ann", "adm_a");
        var annForPb = await setup.ImpactOfAsync("ann", "adm_b");
        var pbForPa = await setup.ImpactOfAsync("adm_b", "adm_a");
        var pbForPb = await setup.ImpactOfAsync("adm_b", "adm_b");

        // Assert
        Assert.Equal(["Apollo"], annForPa.OpenWork.Select(work => work.ProjectName));
        Assert.Equal(["Borealis"], annForPb.OpenWork.Select(work => work.ProjectName));
        Assert.True(pbForPa.IsEmpty);
        Assert.Equal(["Borealis"], pbForPb.SoleAdminProjects.Select(project => project.ProjectName));
    }

    /// <summary>
    ///     Test that everyone who administers no project is refused as not allowed: a member, a read-only member, a
    ///     user outside every project, an unknown user and the command line.
    /// </summary>
    /// <param name="who">The user name of the actor; the empty text stands for the command line.</param>
    [Theory]
    [InlineData("ann")]
    [InlineData("aud")]
    [InlineData("out")]
    [InlineData("ghost")]
    [InlineData("")]
    public async Task UserImpactService_GetImpactAsync_ActorWithoutAdministration_IsRefused(string who)
    {
        // Arrange
        var setup = await ArrangeAsync();
        var actor = who.Length == 0 ? Actor.CommandLine : InboxTestWorld.ActorOf(who);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => setup.Service.GetImpactAsync(actor, setup.World.Ids["bob"], Ct));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
    }

    /// <summary>
    ///     Test that a project administrator whose account is disabled has no rights and is refused.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_DisabledProjectAdministrator_IsRefused()
    {
        // Arrange
        var setup = await ArrangeAsync();
        await setup.World.Users.SetEnabledAsync("adm_a", false, Ct);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(() => setup.ImpactOfAsync("bob", "adm_a"));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
    }

    /// <summary>
    ///     Test that an unknown user gives the not-found rule to an administrator, and that an actor without the right
    ///     is refused before anything is said about whether the user exists.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_UnknownUser_IsNotFoundForAnAdministratorOnly()
    {
        // Arrange
        var setup = await ArrangeAsync();

        // Act
        var notFound = await Assert.ThrowsAsync<DomainRuleException>(
            () => setup.Service.GetImpactAsync(InboxTestWorld.ActorOf("root"), 9999, Ct));
        var refused = await Assert.ThrowsAsync<DomainRuleException>(
            () => setup.Service.GetImpactAsync(InboxTestWorld.ActorOf("ann"), 9999, Ct));

        // Assert
        Assert.Equal(DomainRule.UserNotFound, notFound.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, refused.Rule);
    }

    /// <summary>
    ///     Test that a user without any slot or administration has an empty impact, and that a disabled user
    ///     can still be asked about, because the review work of a leaver is wanted after the account was disabled.
    /// </summary>
    [Fact]
    public async Task UserImpactService_GetImpactAsync_DisabledUserWithSlot_StillShowsOpenWork()
    {
        // Arrange: bob has a slot, cyd has nothing; bob is then disabled
        var setup = await ArrangeAsync();
        await setup.World.CreateReviewAsync("r1");
        await setup.World.AddSlotAsync("r1", "bob");
        await setup.World.Users.SetEnabledAsync("bob", false, Ct);

        // Act
        var bob = await setup.ImpactOfAsync("bob");
        var cyd = await setup.ImpactOfAsync("cyd");

        // Assert
        Assert.Single(bob.OpenWork);
        Assert.True(cyd.IsEmpty);
    }

    /// <summary>
    ///     Test that a null actor and null collaborators are refused with an argument exception.
    /// </summary>
    [Fact]
    public async Task UserImpactService_NullArguments_AreRefused()
    {
        // Arrange
        var setup = await ArrangeAsync();
        var world = setup.World;

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => setup.Service.GetImpactAsync(null!, 1, Ct));
        Assert.Throws<ArgumentNullException>(() => new UserImpactService(null!, world.UserStore, world.Access));
        Assert.Throws<ArgumentNullException>(() => new UserImpactService(world.Store, null!, world.Access));
        Assert.Throws<ArgumentNullException>(() => new UserImpactService(world.Store, world.UserStore, null!));
    }

    /// <summary>
    ///     Arranges the world of the inbox tests with a second project <c>Borealis</c>, the project administrators
    ///     <c>adm_a</c> (of Apollo) and <c>adm_b</c> (of Borealis), and ann as a member of both projects.
    /// </summary>
    /// <returns>The arranged setup.</returns>
    private static async Task<Setup> ArrangeAsync()
    {
        var world = await InboxTestWorld.CreateAsync();
        var borealis = await TestServices.Projects(world.Store, world.Clock).CreateAsync("Borealis", null, Ct);
        var setup = new Setup(
            world,
            new UserImpactService(world.Store, world.UserStore, world.Access),
            borealis.Id);
        await setup.AddUserAsync("adm_a", world.ProjectId, ProjectRole.ProjectAdmin);
        await setup.AddUserAsync("adm_b", borealis.Id, ProjectRole.ProjectAdmin);
        await world.Store.AddMembershipAsync(setup.IdOf("ann"), borealis.Id, Ct);
        return setup;
    }

    /// <summary>
    ///     The arranged world of the tests and the service under test.
    /// </summary>
    /// <param name="World">The world of the inbox tests, with every right granted for arranging.</param>
    /// <param name="Service">The service under test, over the real access rules.</param>
    /// <param name="BorealisId">The identity of the second project.</param>
    private sealed record Setup(InboxTestWorld World, UserImpactService Service, int BorealisId)
    {
        /// <summary>
        ///     Gets the identities of the users this setup created, by user name.
        /// </summary>
        private Dictionary<string, int> Extra { get; } = [];

        /// <summary>
        ///     Gets the identity of a user of the world or of this setup.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <returns>The identity.</returns>
        public int IdOf(string username)
        {
            return Extra.TryGetValue(username, out var id) ? id : World.Ids[username];
        }

        /// <summary>
        ///     Asks for the impact on a user.
        /// </summary>
        /// <param name="username">The user name of the user who may leave.</param>
        /// <param name="asker">The user name of the actor who asks; a system administrator by default.</param>
        /// <returns>The impact.</returns>
        public Task<UserImpact> ImpactOfAsync(string username, string asker = "root")
        {
            return Service.GetImpactAsync(InboxTestWorld.ActorOf(asker), IdOf(username), Ct);
        }

        /// <summary>
        ///     Creates a user, who is also remembered in the world, and makes the user a member of a project.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="projectId">The project to join.</param>
        /// <param name="role">The role in the project.</param>
        /// <returns>A task that completes when the user is a member.</returns>
        public async Task AddUserAsync(string username, int projectId, ProjectRole role)
        {
            var user = await World.Users.CreateAsync(
                username, username, false, InboxTestWorld.Password, Ct);
            await World.Store.AddMembershipAsync(user.Id, projectId, role, Ct);
            Extra[username] = user.Id;
        }

        /// <summary>
        ///     Creates a review in Borealis with one slot.
        /// </summary>
        /// <param name="reviewId">The review identifier.</param>
        /// <param name="primary">The user name of the primary reviewer.</param>
        /// <returns>A task that completes when the slot exists.</returns>
        public async Task AddBorealisReviewAsync(string reviewId, string primary)
        {
            await World.Reviews.CreateDraftAsync(
                BorealisId,
                reviewId,
                "Borealis review",
                [new("a.txt", TestContent.A)],
                InboxTestWorld.ActorOf("author"),
                Ct);
            await World.Signing.AddSlotAsync(
                InboxTestWorld.ActorOf("author"), BorealisId, reviewId, IdOf(primary), null, Ct);
        }
    }
}
