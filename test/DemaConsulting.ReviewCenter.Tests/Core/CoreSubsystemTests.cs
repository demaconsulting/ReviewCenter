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
///     Subsystem tests for the Core subsystem: the project and review services working together with the
///     fingerprint calculation over a store.
/// </summary>
public class CoreSubsystemTests
{
    /// <summary>
    ///     The instant reported by the services' clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test the complete workflow: a project and a review of three files are created through the services,
    ///     and are then listed and read back with the known fingerprint and the original bytes.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_ProjectAndReviewWorkflow_ValidInput_StoresFingerprintsAndReadsBack()
    {
        // Arrange: both services over one store
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));

        // Act: create a project, create a review of three files in it, then read everything back
        var project = await projects.CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First review",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B), new("c.bin", TestContent.C)],
            TestContext.Current.CancellationToken);
        var listedProjects = await projects.ListAsync(TestContext.Current.CancellationToken);
        var listedReviews = await reviews.ListAsync(project.Id, TestContext.Current.CancellationToken);
        var review = await reviews.GetAsync(project.Id, "rev-1", TestContext.Current.CancellationToken);
        var content = await reviews.GetFileContentAsync(project.Id, "rev-1", "c.bin", TestContext.Current.CancellationToken);

        // Assert: the project and review are listed, the fingerprint is the known answer, and the bytes are intact
        Assert.Equal("Apollo", Assert.Single(listedProjects).Name);
        Assert.Equal("rev-1", Assert.Single(listedReviews).Id);
        Assert.NotNull(review);
        Assert.Equal(ReviewState.Draft, review.State);
        Assert.Equal(TestContent.FingerprintABC, review.ContentFingerprint);
        Assert.Equal(3, review.Files.Count);
        Assert.Equal(TestContent.C, content);

        // Assert: every file carries its own size and SHA-256 hash
        var files = review.Files.ToDictionary(file => file.Path);
        Assert.Equal(TestContent.A.Length, files["a.txt"].Size);
        Assert.Equal(TestContent.HashA, files["a.txt"].Sha256);
        Assert.Equal(TestContent.B.Length, files["b.txt"].Size);
        Assert.Equal(TestContent.HashB, files["b.txt"].Sha256);
        Assert.Equal(TestContent.C.Length, files["c.bin"].Size);
        Assert.Equal(TestContent.HashC, files["c.bin"].Sha256);
    }

    /// <summary>
    ///     Test that the same file contents give the same review fingerprint when they are submitted in another
    ///     order and under other file names.
    /// </summary>
    [Fact]
    public async Task CoreSubsystem_Fingerprint_SameContentOtherNamesAndOrder_GivesSameFingerprint()
    {
        // Arrange: both services over one store, with a project
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: create two reviews with the same three contents, the second in reverse order under other names
        var first = await reviews.CreateDraftAsync(
            project.Id,
            "rev-1",
            "First",
            [new("a.txt", TestContent.A), new("b.txt", TestContent.B), new("c.bin", TestContent.C)],
            TestContext.Current.CancellationToken);
        var second = await reviews.CreateDraftAsync(
            project.Id,
            "rev-2",
            "Second",
            [new("z/three", TestContent.C), new("y/two", TestContent.B), new("x/one", TestContent.A)],
            TestContext.Current.CancellationToken);

        // Assert: both reviews have the known fingerprint of the contents
        Assert.Equal(TestContent.FingerprintABC, first.ContentFingerprint);
        Assert.Equal(first.ContentFingerprint, second.ContentFingerprint);
    }

    /// <summary>
    ///     Provides every domain rule, so that a rule added later without a scenario fails the test instead of
    ///     going unnoticed.
    /// </summary>
    public static TheoryData<DomainRule> AllRules => new(Enum.GetValues<DomainRule>());

    /// <summary>
    ///     Test that each domain rule is reported through the services as a domain rule exception that names
    ///     the rule, and that nothing is stored for the violation.
    /// </summary>
    /// <param name="rule">The rule to violate.</param>
    [Theory]
    [MemberData(nameof(AllRules))]
    public async Task CoreSubsystem_RuleViolation_IsReportedAsDomainRuleAndStoresNothing(DomainRule rule)
    {
        // Arrange: both services over one store, with one valid project and review
        var store = new FakeReviewStore();
        var projects = new ProjectService(store, new FixedTimeProvider(_now));
        var reviews = new ReviewService(store, new FixedTimeProvider(_now));
        var project = await projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await reviews.CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);

        // Act: break the rule
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => ViolateAsync(rule, projects, reviews, project.Id));

        // Assert: the rule is named, and only the first valid project and review exist
        Assert.Equal(rule, exception.Rule);
        Assert.Single(await projects.ListAsync(TestContext.Current.CancellationToken));
        Assert.Single(store.Reviews);
    }

    /// <summary>
    ///     Makes a request that breaks one rule, against a store that holds the project and the review
    ///     <c>rev-1</c> with the file <c>a.txt</c>.
    /// </summary>
    /// <param name="rule">The rule to break.</param>
    /// <param name="projects">The project service.</param>
    /// <param name="reviews">The review service.</param>
    /// <param name="projectId">The identity of the existing project.</param>
    /// <returns>The task of the request, which fails with the rule violation.</returns>
    private static Task ViolateAsync(DomainRule rule, ProjectService projects, ReviewService reviews, int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        List<UploadedFile> oneFile = [new("a.txt", TestContent.A)];

        // One shared array keeps the over-limit cases cheap: only the lengths matter to the rules
        var maxSizeContent = new byte[ReviewLimits.MaxFileSizeBytes];
        return rule switch
        {
            DomainRule.ProjectNameInvalid => projects.CreateAsync("  ", null, cancellationToken),
            DomainRule.ProjectDescriptionTooLong => projects.CreateAsync(
                "Gemini", new string('d', ReviewLimits.MaxProjectDescriptionLength + 1), cancellationToken),
            DomainRule.ProjectNotFound => reviews.CreateDraftAsync(projectId + 100, "rev-2", "T", oneFile, cancellationToken),
            DomainRule.ReviewIdInvalid => reviews.CreateDraftAsync(projectId, "bad id", "T", oneFile, cancellationToken),
            DomainRule.ReviewIdDuplicate => reviews.CreateDraftAsync(projectId, "rev-1", "T", oneFile, cancellationToken),
            DomainRule.ReviewTitleInvalid => reviews.CreateDraftAsync(projectId, "rev-2", " ", oneFile, cancellationToken),
            DomainRule.NoFiles => reviews.CreateDraftAsync(projectId, "rev-2", "T", [], cancellationToken),
            DomainRule.DuplicatePath => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", [new("a.txt", TestContent.A), new("a.txt", TestContent.B)], cancellationToken),
            DomainRule.PathInvalid => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", [new(" ", TestContent.A)], cancellationToken),
            DomainRule.FileTooLarge => reviews.CreateDraftAsync(
                projectId, "rev-2", "T", [new("big.bin", new byte[ReviewLimits.MaxFileSizeBytes + 1])], cancellationToken),
            DomainRule.TooManyFiles => reviews.CreateDraftAsync(
                projectId,
                "rev-2",
                "T",
                Enumerable.Range(0, ReviewLimits.MaxFileCount + 1).Select(i => new UploadedFile($"f{i}.txt", [0x41])).ToList(),
                cancellationToken),
            DomainRule.TotalSizeExceeded => reviews.CreateDraftAsync(
                projectId,
                "rev-2",
                "T",
                Enumerable.Range(0, (int)(ReviewLimits.MaxTotalSizeBytes / ReviewLimits.MaxFileSizeBytes) + 1)
                    .Select(i => new UploadedFile($"f{i}.bin", maxSizeContent))
                    .ToList(),
                cancellationToken),
            _ => throw new NotSupportedException($"No violation is defined for the rule '{rule}'.")
        };
    }
}
