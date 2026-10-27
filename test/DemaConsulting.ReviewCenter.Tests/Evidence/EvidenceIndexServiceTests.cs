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
///     Unit tests for <see cref="EvidenceIndexService"/> over in-memory stores: which reviews are listed, what an
///     entry says, and the order and the duplicate rule.
/// </summary>
public class EvidenceIndexServiceTests
{
    /// <summary>
    ///     Test that a complete review planned from a review-set is listed with its set id, fingerprint and the day of
    ///     its signature.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_CompleteReviewWithSetId_ListsEntry()
    {
        // Arrange: a complete review with a review-set id
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await AddCompleteAsync(fixture, "set-1", "Core-Logic", TestContent.A);

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: one entry with the set id, the fingerprint, the UTC day of the signature and the review id
        var entry = Assert.Single(entries);
        Assert.Equal(new EvidenceIndexEntry("Core-Logic", TestContent.FingerprintA, "2025-03-04", "set-1"), entry);
    }

    /// <summary>
    ///     Test that a review with a slot that has not signed is not listed.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_IncompleteReview_IsNotListed()
    {
        // Arrange: a review with a set id and a slot that has not signed
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await EvidenceTestSupport.AddReviewAsync(fixture.Store, fixture.Clock, fixture.ProjectId, "set-1", "Core-Logic", TestContent.A);
        await EvidenceTestSupport.AnswerAllPassAsync(fixture.Checklists, fixture.ProjectId, "set-1");
        await EvidenceTestSupport.AddSlotAsync(fixture.Signing, fixture.ProjectId, "set-1", await UserIdAsync(fixture, "ann"));

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: nothing is listed
        Assert.Empty(entries);
    }

    /// <summary>
    ///     Test that a review whose record changed after the signature is not listed.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_ReviewChangedAfterSignature_IsNotListed()
    {
        // Arrange: a complete review whose checklist answers then change
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await AddCompleteAsync(fixture, "set-1", "Core-Logic", TestContent.A);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await fixture.Checklists.SetAnswerAsync(
                fixture.ProjectId, "set-1", item.Id, ChecklistOutcome.Fail, "Changed.", TestActors.Ann, TestContext.Current.CancellationToken);
        }

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: the signature no longer counts
        Assert.Empty(entries);
    }

    /// <summary>
    ///     Test that a complete review without a review-set id is skipped.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_NoSetId_IsSkipped()
    {
        // Arrange: a complete review that was not planned from a review-set (the fixture's review)
        var fixture = await AttestationFixture.CreateAsync(signed: true);

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: nothing is listed
        Assert.Empty(entries);
    }

    /// <summary>
    ///     Test that two complete reviews of the same set id and fingerprint give one entry: the one completed last.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_SameIdAndFingerprint_KeepsLatestCompletion()
    {
        // Arrange: two reviews of the same content, the second completed a day later
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await AddCompleteAsync(fixture, "set-1", "Core-Logic", TestContent.A);
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        await AddCompleteAsync(fixture, "set-2", "Core-Logic", TestContent.A);

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: one entry, the later review and its day
        var entry = Assert.Single(entries);
        Assert.Equal("set-2", entry.ReviewId);
        Assert.Equal("2025-03-05", entry.Date);
    }

    /// <summary>
    ///     Test that on the same completion time the lowest review id is kept.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_SameCompletionTime_KeepsLowestReviewId()
    {
        // Arrange: two reviews of the same content completed at the same instant, the higher id first
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await AddCompleteAsync(fixture, "set-b", "Core-Logic", TestContent.A);
        await AddCompleteAsync(fixture, "set-a", "Core-Logic", TestContent.A);

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: one entry, the lowest id
        Assert.Equal("set-a", Assert.Single(entries).ReviewId);
    }

    /// <summary>
    ///     Test that the same set id with another fingerprint is a separate entry, and that the entries are ordered by
    ///     id and then fingerprint.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_Order_IsByIdThenFingerprint()
    {
        // Arrange: complete reviews added in an order that is not the expected one
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await AddCompleteAsync(fixture, "r-1", "b-set", TestContent.A);
        await AddCompleteAsync(fixture, "r-2", "a-set", TestContent.A);
        await AddCompleteAsync(fixture, "r-3", "a-set", TestContent.A, TestContent.A);

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: by id, then by fingerprint (AA, 71..., sorts before A, 7f...)
        Assert.Equal(
            [("a-set", TestContent.FingerprintAA), ("a-set", TestContent.FingerprintA), ("b-set", TestContent.FingerprintA)],
            entries.Select(entry => (entry.Id, entry.Fingerprint)).ToArray());
    }

    /// <summary>
    ///     Test that the date is the day of the latest signature, in UTC.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_Date_IsLatestSignatureAsUtcDay()
    {
        // Arrange: a review with two slots; the first signs just before midnight UTC, the second just after
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        fixture.Clock.Advance(new DateTimeOffset(2025, 3, 4, 23, 30, 0, TimeSpan.Zero) - fixture.Clock.GetUtcNow());
        await EvidenceTestSupport.AddReviewAsync(fixture.Store, fixture.Clock, fixture.ProjectId, "set-1", "Core-Logic", TestContent.A);
        await EvidenceTestSupport.AnswerAllPassAsync(fixture.Checklists, fixture.ProjectId, "set-1");
        var annSlot = await EvidenceTestSupport.AddSlotAsync(fixture.Signing, fixture.ProjectId, "set-1", await UserIdAsync(fixture, "ann"));
        var cydSlot = await EvidenceTestSupport.AddSlotAsync(fixture.Signing, fixture.ProjectId, "set-1", fixture.CydUserId);
        await EvidenceTestSupport.SignAsync(fixture.Signing, fixture.ProjectId, "set-1", annSlot, EvidenceTestSupport.Ann, AttestationFixture.Password);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await EvidenceTestSupport.SignAsync(fixture.Signing, fixture.ProjectId, "set-1", cydSlot, new Actor("cyd", "Cyd"), AttestationFixture.Password);

        // Act: build the index
        var entries = await CreateService(fixture).BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);

        // Assert: the day of the later signature
        Assert.Equal("2025-03-05", Assert.Single(entries).Date);
    }

    /// <summary>
    ///     Test that a complete review of another project is not listed.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_OtherProject_IsNotListed()
    {
        // Arrange: a complete review with a set id in a second project
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        var other = await new ProjectService(fixture.Store, fixture.Clock).CreateAsync("Gemini", null, TestContext.Current.CancellationToken);
        await EvidenceTestSupport.AddReviewAsync(fixture.Store, fixture.Clock, other.Id, "set-1", "Core-Logic", TestContent.A);
        await EvidenceTestSupport.CompleteAsync(
            fixture.Checklists, fixture.Signing, other.Id, "set-1", await UserIdAsync(fixture, "ann"), AttestationFixture.Password);
        var service = CreateService(fixture);

        // Act: build the index of both projects
        var first = await service.BuildAsync(fixture.ProjectId, TestContext.Current.CancellationToken);
        var second = await service.BuildAsync(other.Id, TestContext.Current.CancellationToken);

        // Assert: each review shows only in its own project
        Assert.Empty(first);
        Assert.Single(second);
    }

    /// <summary>
    ///     Test that the users are read once for the whole index, however many reviews it covers, and that the
    ///     entries are the same as when the approvals are read one review at a time.
    /// </summary>
    [Fact]
    public async Task EvidenceIndexService_Build_SeveralReviews_ReadsUsersOnce()
    {
        // Arrange: three complete reviews with a set id over a user store that counts list reads
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var userStore = new CountingUserStore(new FakeUserStore());
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        var ann = await users.CreateAsync("ann", "Ann", UserRole.Reviewer, AttestationFixture.Password, cancellationToken);
        var signing = new SigningService(store, users, clock);
        var checklists = new ChecklistService(store, clock);
        var project = await new ProjectService(store, clock).CreateAsync("Apollo", null, cancellationToken);
        foreach (var (id, set) in new[] { ("r-1", "a-set"), ("r-2", "b-set"), ("r-3", "c-set") })
        {
            await EvidenceTestSupport.AddReviewAsync(store, clock, project.Id, id, set, TestContent.A);
            await EvidenceTestSupport.CompleteAsync(checklists, signing, project.Id, id, ann.Id, AttestationFixture.Password);
        }

        var service = new EvidenceIndexService(new ReviewService(store, clock), signing);
        var before = userStore.ListCount;

        // Act: build the index
        var entries = await service.BuildAsync(project.Id, cancellationToken);

        // Assert: all three are listed and the user list was read exactly once
        Assert.Equal(["a-set", "b-set", "c-set"], entries.Select(entry => entry.Id).ToArray());
        Assert.Equal(1, userStore.ListCount - before);
    }

    /// <summary>
    ///     Creates the service over the fixture.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The service.</returns>
    private static EvidenceIndexService CreateService(AttestationFixture fixture)
    {
        return new EvidenceIndexService(new ReviewService(fixture.Store, fixture.Clock), fixture.Signing);
    }

    /// <summary>
    ///     Adds a review with a review-set id to the project of the fixture and completes it.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="reviewSetId">The review-set id.</param>
    /// <param name="contents">The contents of the files of the review.</param>
    /// <returns>A task that completes when the review is complete.</returns>
    private static async Task AddCompleteAsync(
        AttestationFixture fixture,
        string reviewId,
        string reviewSetId,
        params byte[][] contents)
    {
        await EvidenceTestSupport.AddReviewAsync(fixture.Store, fixture.Clock, fixture.ProjectId, reviewId, reviewSetId, contents);
        await EvidenceTestSupport.CompleteAsync(
            fixture.Checklists, fixture.Signing, fixture.ProjectId, reviewId, await UserIdAsync(fixture, "ann"), AttestationFixture.Password);
    }

    /// <summary>
    ///     Gets the identity of a user of the fixture.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="username">The user name.</param>
    /// <returns>The identity.</returns>
    private static async Task<int> UserIdAsync(AttestationFixture fixture, string username)
    {
        return (await fixture.Users.GetAsync(username, TestContext.Current.CancellationToken))!.Id;
    }
}
