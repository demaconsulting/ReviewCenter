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

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the two reads behind the offboarding impact of
///     <see cref="DemaConsulting.ReviewCenter.Storage.ReviewStore"/> (the slots of a user and the projects the user
///     alone administers) against a real SQLite database file.
/// </summary>
public class ReviewStoreUserImpactTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Gets the cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Adds a user to the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="username">The user name.</param>
    /// <param name="enabled">Whether the account is enabled.</param>
    /// <returns>The identity of the user.</returns>
    private static async Task<int> AddUserAsync(TestStore db, string username, bool enabled = true)
    {
        var user = await db.Users.AddAsync(
            new User
            {
                Username = username,
                DisplayName = username,
                Enabled = enabled,
                PasswordHash = "hash",
                Created = _created
            },
            Ct);
        return user.Id;
    }

    /// <summary>
    ///     Adds a project to the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="name">The project name.</param>
    /// <returns>The identity of the project.</returns>
    private static async Task<int> AddProjectAsync(TestStore db, string name)
    {
        return (await db.Store.AddProjectAsync(new Project { Name = name, Created = _created }, Ct)).Id;
    }

    /// <summary>
    ///     Adds a review and a slot to the database.
    /// </summary>
    /// <param name="db">The database.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="primary">The primary reviewer.</param>
    /// <param name="alternate">The alternate reviewer, or <c>null</c>.</param>
    /// <returns>The slot identity.</returns>
    private static async Task<int> AddSlotAsync(TestStore db, int projectId, string reviewId, int primary, int? alternate)
    {
        byte[] content = [1, 2, 3];
        var review = new Review
        {
            ProjectId = projectId,
            Id = reviewId,
            Title = "Title " + reviewId,
            Created = _created,
            ContentFingerprint = Fingerprint.Compute([content]),
            Files =
            [
                new ReviewFile
                {
                    ProjectId = projectId,
                    ReviewId = reviewId,
                    Path = "a.txt",
                    Content = content,
                    Size = content.Length,
                    Sha256 = Fingerprint.ComputeFileHash(content)
                }
            ]
        };
        await db.Store.AddReviewAsync(review, AuditTestExtensions.Draft(), Ct);
        var slot = await db.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = projectId, ReviewId = reviewId, PrimaryUserId = primary, AlternateUserId = alternate },
            _ => AuditTestExtensions.Draft(),
            Ct);
        return slot.Id;
    }

    /// <summary>
    ///     Test that the slots of a user are found as primary and as alternate with the review and the project, that
    ///     the slots of other users are left out, and that the order is by project, review and slot.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSlotsOfUserAsync_PrimaryAndAlternate_AreFoundInOrder()
    {
        // Arrange: ann is primary of Apollo/r2 and Gemini/g1, alternate of Apollo/r1; r3 is bob's alone
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var apollo = await AddProjectAsync(db, "Apollo");
        var gemini = await AddProjectAsync(db, "Gemini");
        var r2 = await AddSlotAsync(db, apollo, "r2", ann, null);
        var r1 = await AddSlotAsync(db, apollo, "r1", bob, ann);
        await AddSlotAsync(db, apollo, "r3", bob, null);
        var g1 = await AddSlotAsync(db, gemini, "g1", ann, bob);

        // Act
        var found = await db.Store.ListSlotsOfUserAsync(ann, Ct);

        // Assert
        Assert.Equal(
            [
                new UserReviewSlot(apollo, "Apollo", "r1", "Title r1", r1, SlotRole.Alternate),
                new UserReviewSlot(apollo, "Apollo", "r2", "Title r2", r2, SlotRole.Primary),
                new UserReviewSlot(gemini, "Gemini", "g1", "Title g1", g1, SlotRole.Primary)
            ],
            found);
    }

    /// <summary>
    ///     Test that a user without any slot and an unknown user have no slots.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSlotsOfUserAsync_NoSlot_IsEmpty()
    {
        // Arrange
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var apollo = await AddProjectAsync(db, "Apollo");
        await AddSlotAsync(db, apollo, "r1", bob, null);

        // Act, Assert
        Assert.Empty(await db.Store.ListSlotsOfUserAsync(ann, Ct));
        Assert.Empty(await db.Store.ListSlotsOfUserAsync(9999, Ct));
    }

    /// <summary>
    ///     Test that an enabled project administrator who is the only enabled one is found, ordered by project name,
    ///     and that the projects where the user is no administrator are left out.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSoleAdminProjectsAsync_OnlyEnabledAdministrator_IsFound()
    {
        // Arrange: ann alone administers Gemini and Apollo, is a user of Borealis, and shares Cygnus with bob
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var bob = await AddUserAsync(db, "bob");
        var gemini = await AddProjectAsync(db, "Gemini");
        var apollo = await AddProjectAsync(db, "Apollo");
        var borealis = await AddProjectAsync(db, "Borealis");
        var cygnus = await AddProjectAsync(db, "Cygnus");
        await db.Store.AddMembershipAsync(ann, gemini, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(ann, apollo, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(ann, borealis, ProjectRole.User, Ct);
        await db.Store.AddMembershipAsync(ann, cygnus, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(bob, cygnus, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(bob, borealis, ProjectRole.ProjectAdmin, Ct);

        // Act
        var found = await db.Store.ListSoleAdminProjectsAsync(ann, Ct);

        // Assert
        Assert.Equal(["Apollo", "Gemini"], found.Select(project => project.Name));
        Assert.Equal([apollo, gemini], found.Select(project => project.Id));
    }

    /// <summary>
    ///     Test that a disabled second administrator does not count, that an ordinary member next to the user does not
    ///     count either, and that a disabled user is not reported at all.
    /// </summary>
    [Fact]
    public async Task ReviewStore_ListSoleAdminProjectsAsync_DisabledOrOrdinaryOthers_DoNotCount()
    {
        // Arrange: Apollo has ann and a disabled administrator; Gemini has ann and an ordinary member;
        // the disabled user is the only administrator of Borealis
        using var db = new TestStore();
        var ann = await AddUserAsync(db, "ann");
        var gone = await AddUserAsync(db, "gone", enabled: false);
        var bob = await AddUserAsync(db, "bob");
        var apollo = await AddProjectAsync(db, "Apollo");
        var gemini = await AddProjectAsync(db, "Gemini");
        var borealis = await AddProjectAsync(db, "Borealis");
        await db.Store.AddMembershipAsync(ann, apollo, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(gone, apollo, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(ann, gemini, ProjectRole.ProjectAdmin, Ct);
        await db.Store.AddMembershipAsync(bob, gemini, ProjectRole.User, Ct);
        await db.Store.AddMembershipAsync(gone, borealis, ProjectRole.ProjectAdmin, Ct);

        // Act
        var forAnn = await db.Store.ListSoleAdminProjectsAsync(ann, Ct);
        var forGone = await db.Store.ListSoleAdminProjectsAsync(gone, Ct);

        // Assert
        Assert.Equal([apollo, gemini], forAnn.Select(project => project.Id).Order());
        Assert.Empty(forGone);
        Assert.Empty(await db.Store.ListSoleAdminProjectsAsync(bob, Ct));
        Assert.Empty(await db.Store.ListSoleAdminProjectsAsync(9999, Ct));
    }
}
