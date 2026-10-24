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
///     Unit tests for <see cref="ChecklistService"/> and the <see cref="DefaultChecklist"/> it serves, using an
///     in-memory store.
/// </summary>
public class ChecklistServiceTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     The number of checks in each section of the template, in order.
    /// </summary>
    private static readonly int[] _itemsPerSection = [8, 5, 4, 6, 3, 4, 2, 5, 3, 8, 2, 2, 2, 4];

    /// <summary>
    ///     Creates a store with a project and the review <c>rev-1</c>, and a checklist service over it.
    /// </summary>
    /// <returns>The store, the service and the id of the project.</returns>
    private static async Task<(FakeReviewStore Store, ChecklistService Service, int ProjectId)> CreateAsync()
    {
        var store = new FakeReviewStore();
        var project = await new ProjectService(store, new FixedTimeProvider(_now))
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await new ReviewService(store, new FixedTimeProvider(_now)).CreateDraftAsync(
            project.Id, "rev-1", "First", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        return (store, new ChecklistService(store, new FixedTimeProvider(_now)), project.Id);
    }

    /// <summary>
    ///     Test that the default checklist has the 14 sections of the template with the known number of checks.
    /// </summary>
    [Fact]
    public void ChecklistService_DefaultChecklist_HasTemplateSectionsAndItemIdsInOrder()
    {
        // Arrange, Act: the default checklist
        var definition = DefaultChecklist.Current;

        // Assert: version, section order and counts, and the first and last checks verbatim
        Assert.Equal(DefaultChecklist.Version, definition.Version);
        Assert.Equal(
            ["REQ", "DES", "DOC", "CODE", "LOGIC", "ERR", "USE", "TEST", "SEC", "READ", "REQDOC", "REQIMP", "REQTEST", "CODEDOC"],
            definition.Sections.Select(section => section.Id));
        Assert.Equal(_itemsPerSection, definition.Sections.Select(section => section.Items.Count));
        Assert.Equal(58, definition.Sections.Sum(section => section.Items.Count));
        Assert.Equal("Requirements Checks", definition.Sections[0].Title);
        Assert.Equal("REQ-01", definition.Sections[0].Items[0].Id);
        Assert.Equal("CODEDOC-04", definition.Sections[^1].Items[^1].Id);
    }

    /// <summary>
    ///     Test that every check id is unique and starts with the id of its section.
    /// </summary>
    [Fact]
    public void ChecklistService_DefaultChecklist_IdsUniqueAndPrefixedBySection()
    {
        // Arrange, Act: all checks with their section
        var items = DefaultChecklist.Current.Sections
            .SelectMany(section => section.Items.Select(item => (Section: section, Item: item)))
            .ToList();

        // Assert: no duplicate ids; each id is the section id, a dash and a number; every text is present
        Assert.Equal(items.Count, items.Select(i => i.Item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(items, i => Assert.Matches($"^{i.Section.Id}-\\d{{2}}$", i.Item.Id));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Item.Text)));
    }

    /// <summary>
    ///     Test that the definition survives being written to and read from the stored JSON form.
    /// </summary>
    [Fact]
    public void ChecklistService_DefaultChecklist_RoundTripsThroughJson()
    {
        // Arrange, Act: write the default checklist to JSON and read it back
        var copy = ChecklistDefinition.FromJson(DefaultChecklist.Current.ToJson());

        // Assert: the same version, sections and checks
        Assert.Equal(DefaultChecklist.Version, copy.Version);
        Assert.Equal(
            DefaultChecklist.Current.Sections.Select(s => (s.Id, s.Title, s.ApplicabilityHint)),
            copy.Sections.Select(s => (s.Id, s.Title, s.ApplicabilityHint)));
        Assert.Equal(
            DefaultChecklist.Current.Sections.SelectMany(s => s.Items),
            copy.Sections.SelectMany(s => s.Items));
    }

    /// <summary>
    ///     Test that the service uses the snapshot stored with the review, not the current default.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Get_AfterDefaultChanges_ReturnsFrozenSnapshot()
    {
        // Arrange: a review whose stored snapshot differs from the default
        var store = new FakeReviewStore();
        var project = await new ProjectService(store, new FixedTimeProvider(_now))
            .CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        var custom = new ChecklistDefinition(
            "old-1",
            [new ChecklistSection("X", "Old section", "Hint", [new ChecklistItem("X-01", "Old check")])]);
        await store.AddReviewAsync(
            new Review
            {
                ProjectId = project.Id,
                Id = "rev-old",
                Title = "Old",
                Created = _now,
                Checklist = new ReviewChecklist
                {
                    ProjectId = project.Id,
                    ReviewId = "rev-old",
                    Version = custom.Version,
                    Definition = custom.ToJson()
                }
            },
            TestContext.Current.CancellationToken);
        var service = new ChecklistService(store, new FixedTimeProvider(_now));

        // Act: read the checklist
        var state = await service.GetAsync(project.Id, "rev-old", TestContext.Current.CancellationToken);

        // Assert: the frozen definition is used
        Assert.NotNull(state);
        Assert.Equal("old-1", state.Definition.Version);
        Assert.Equal(new ChecklistProgress(0, 1), state.Overall);
    }

    /// <summary>
    ///     Test that an unknown review has no checklist.
    /// </summary>
    [Fact]
    public async Task ChecklistService_Get_UnknownReview_ReturnsNull()
    {
        // Arrange: a store with one review
        var (_, service, projectId) = await CreateAsync();

        // Act, Assert: another review has no checklist
        Assert.Null(await service.GetAsync(projectId, "other", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that marking a section as not applicable needs a reason.
    /// </summary>
    /// <param name="reason">The missing reason.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task ChecklistService_SetSectionApplicability_NotApplicableWithoutReason_Throws(string? reason)
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: mark a section as not applicable without a reason
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", "DES", false, reason, "Ann", TestContext.Current.CancellationToken));

        // Assert: the rule is named and the section still applies
        Assert.Equal(DomainRule.ChecklistJustificationInvalid, exception.Rule);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
        Assert.True(state!.IsApplicable("DES"));
    }

    /// <summary>
    ///     Test that a not-applicable section keeps its trimmed reason and leaves the total.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicability_NotApplicableWithReason_StoresTrimmedReason()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: mark a section as not applicable
        await service.SetSectionApplicabilityAsync(
            projectId, "rev-1", "DES", false, "  No design files  ", "Ann", TestContext.Current.CancellationToken);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: not applicable with the trimmed reason
        Assert.False(state!.IsApplicable("DES"));
        Assert.Equal("No design files", state.ReasonFor("DES"));
        Assert.Equal(1, state.NotApplicableSectionCount);
    }

    /// <summary>
    ///     Test that applying a section again clears the reason.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicability_Applicable_ClearsReason()
    {
        // Arrange: a section marked as not applicable
        var (_, service, projectId) = await CreateAsync();
        await service.SetSectionApplicabilityAsync(
            projectId, "rev-1", "DES", false, "No design files", "Ann", TestContext.Current.CancellationToken);

        // Act: mark it as applicable again, with a stray reason
        await service.SetSectionApplicabilityAsync(
            projectId, "rev-1", "DES", true, "ignored", "Ann", TestContext.Current.CancellationToken);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: applicable, with no reason
        Assert.True(state!.IsApplicable("DES"));
        Assert.Null(state.ReasonFor("DES"));
    }

    /// <summary>
    ///     Test that a section that is not in the checklist is refused.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetSectionApplicability_UnknownSection_ThrowsChecklistItemNotFound()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: use an unknown section id
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", "NOPE", false, "Reason", "Ann", TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.ChecklistItemNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that a pass can be recorded without a justification, and an empty one is stored as none.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_PassWithoutJustification_Stores()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: pass a check with white space as justification
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, "  ", "Ann", TestContext.Current.CancellationToken);
        var answer = (await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01");

        // Assert: stored as a pass with no justification
        Assert.NotNull(answer);
        Assert.Equal(ChecklistOutcome.Pass, answer.Outcome);
        Assert.Null(answer.Justification);
    }

    /// <summary>
    ///     Test that a failed or not-applicable check needs a justification and stores nothing without it.
    /// </summary>
    /// <param name="outcome">The outcome that needs a justification.</param>
    [Theory]
    [InlineData(ChecklistOutcome.Fail)]
    [InlineData(ChecklistOutcome.NotApplicable)]
    public async Task ChecklistService_SetAnswer_FailOrNotApplicableWithoutJustification_Throws(ChecklistOutcome outcome)
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer without a justification
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", outcome, " ", "Ann", TestContext.Current.CancellationToken));

        // Assert: the rule is named and the check is still unanswered
        Assert.Equal(DomainRule.ChecklistJustificationInvalid, exception.Rule);
        Assert.Null((await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01"));
    }

    /// <summary>
    ///     Test that the justification limit is inclusive.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_JustificationTooLong_Throws()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer with a justification of exactly the limit, then one more character
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail,
            new string('j', ReviewLimits.MaxJustificationLength), "Ann", TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-02", ChecklistOutcome.Fail,
                new string('j', ReviewLimits.MaxJustificationLength + 1), "Ann", TestContext.Current.CancellationToken));

        // Assert: the limit is accepted, one more is refused
        Assert.Equal(DomainRule.ChecklistJustificationInvalid, exception.Rule);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);
        Assert.NotNull(state!.AnswerFor("REQ-01"));
        Assert.Null(state.AnswerFor("REQ-02"));
    }

    /// <summary>
    ///     Test that a check that is not in the checklist is refused.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_UnknownItem_ThrowsChecklistItemNotFound()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer an unknown check
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "NOPE-01", ChecklistOutcome.Pass, null, "Ann", TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.ChecklistItemNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that answering a check of a review without a checklist is refused.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_UnknownReview_ThrowsChecklistNotFound()
    {
        // Arrange: a store with one review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer a check of another review
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "other", "REQ-01", ChecklistOutcome.Pass, null, "Ann", TestContext.Current.CancellationToken));

        // Assert: the rule is named
        Assert.Equal(DomainRule.ChecklistNotFound, exception.Rule);
    }

    /// <summary>
    ///     Test that answering again replaces the earlier answer and trims the justification.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_ReanswerWithPaddedJustification_OverwritesAndTrims()
    {
        // Arrange: a check passed once
        var (_, service, projectId) = await CreateAsync();
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, "Ann", TestContext.Current.CancellationToken);

        // Act: answer it again as failed, with padding around the justification
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Fail, "  Not traceable  ", "Bob", TestContext.Current.CancellationToken);
        var state = await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken);

        // Assert: only the new answer counts
        var answer = state!.AnswerFor("REQ-01")!;
        Assert.Equal(ChecklistOutcome.Fail, answer.Outcome);
        Assert.Equal("Not traceable", answer.Justification);
        Assert.Equal("Bob", answer.AnsweredBy);
        Assert.Equal(1, state.Overall.Answered);
    }

    /// <summary>
    ///     Test that the name of the person answering is required and limited.
    /// </summary>
    /// <param name="name">The invalid name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("TooLong")]
    public async Task ChecklistService_SetAnswer_BlankOrTooLongName_ThrowsAnsweredByInvalid(string? name)
    {
        // Arrange: a review; the "too long" case is replaced by a name over the limit
        var (_, service, projectId) = await CreateAsync();
        var candidate = name == "TooLong" ? new string('n', ReviewLimits.MaxAuthorLength + 1) : name;

        // Act: answer and set a section with the invalid name
        var answerException = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetAnswerAsync(
                projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, candidate, TestContext.Current.CancellationToken));
        var sectionException = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.SetSectionApplicabilityAsync(
                projectId, "rev-1", "DES", false, "Reason", candidate, TestContext.Current.CancellationToken));

        // Assert: the rule is named for both
        Assert.Equal(DomainRule.ChecklistAnsweredByInvalid, answerException.Rule);
        Assert.Equal(DomainRule.ChecklistAnsweredByInvalid, sectionException.Rule);
    }

    /// <summary>
    ///     Test that the answer carries the trimmed name and the UTC time of the clock.
    /// </summary>
    [Fact]
    public async Task ChecklistService_SetAnswer_StoresNameAndUtcTime()
    {
        // Arrange: a review
        var (_, service, projectId) = await CreateAsync();

        // Act: answer with a padded name
        await service.SetAnswerAsync(
            projectId, "rev-1", "REQ-01", ChecklistOutcome.Pass, null, " Ann ", TestContext.Current.CancellationToken);
        var answer = (await service.GetAsync(projectId, "rev-1", TestContext.Current.CancellationToken))!.AnswerFor("REQ-01")!;

        // Assert: trimmed name and clock time
        Assert.Equal("Ann", answer.AnsweredBy);
        Assert.Equal(_now, answer.AnsweredAt);
        Assert.Equal(TimeSpan.Zero, answer.AnsweredAt.Offset);
    }

    /// <summary>
    ///     Test that the constructor refuses a missing store or clock.
    /// </summary>
    [Fact]
    public void ChecklistService_Constructor_NullArguments_Throws()
    {
        // Arrange, Act, Assert: each missing dependency is refused
        Assert.Throws<ArgumentNullException>(() => new ChecklistService(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ChecklistService(new FakeReviewStore(), null!));
    }
}

/// <summary>
///     Unit tests for the progress rules of <see cref="ChecklistState"/>, over the default checklist.
/// </summary>
public class ChecklistStateTests
{
    /// <summary>
    ///     The instant used for all answers.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds a state over the default checklist.
    /// </summary>
    /// <param name="answered">The identifiers of the checks that are answered.</param>
    /// <param name="notApplicable">The identifiers of the sections marked as not applicable.</param>
    /// <returns>The state.</returns>
    private static ChecklistState Build(IEnumerable<string> answered, params string[] notApplicable)
    {
        var answers = answered.Select(id => new ChecklistAnswer
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            ItemId = id,
            Outcome = ChecklistOutcome.Pass,
            AnsweredBy = "Ann",
            AnsweredAt = _now
        });
        var sections = notApplicable.Select(id => new ChecklistSectionState
        {
            ProjectId = 1,
            ReviewId = "rev-1",
            SectionId = id,
            Applicable = false,
            Reason = "Not relevant",
            SetBy = "Ann",
            SetAt = _now
        });
        return new ChecklistState(DefaultChecklist.Current, sections, answers);
    }

    /// <summary>
    ///     Test that progress counts the answered checks per section and overall.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_CountsAnsweredPerSectionAndOverall()
    {
        // Arrange: two checks of the first section and one of the second are answered
        var state = Build(["REQ-01", "REQ-02", "DES-01"]);

        // Act, Assert: per-section and overall progress
        Assert.Equal(new ChecklistProgress(2, 8), state.SectionProgress("REQ"));
        Assert.Equal(new ChecklistProgress(1, 5), state.SectionProgress("DES"));
        Assert.Equal(new ChecklistProgress(3, 58), state.Overall);
        Assert.Equal(55, state.Overall.Unanswered);
        Assert.False(state.IsComplete);
    }

    /// <summary>
    ///     Test that a not-applicable section counts as done and leaves the total.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_NotApplicableSection_CountsAsDoneAndLeavesTotal()
    {
        // Arrange: the 5-check design section does not apply
        var state = Build([], "DES");

        // Act, Assert: the section is done with nothing to answer, and the total is smaller
        Assert.True(state.SectionProgress("DES").IsDone);
        Assert.Equal(new ChecklistProgress(0, 53), state.Overall);
    }

    /// <summary>
    ///     Test that answers kept in a section that does not apply are not counted.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_AnswersInNotApplicableSection_Ignored()
    {
        // Arrange: a design check was answered, then the design section was marked as not applicable
        var state = Build(["DES-01", "REQ-01"], "DES");

        // Act, Assert: only the requirements answer counts
        Assert.Equal(new ChecklistProgress(1, 53), state.Overall);
    }

    /// <summary>
    ///     Test that the checklist is complete when every check of every applicable section is answered.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_AllApplicableAnswered_IsComplete()
    {
        // Arrange: every check is answered except those of a section that does not apply
        var designIds = DefaultChecklist.Current.Sections.Single(s => s.Id == "DES").Items.Select(i => i.Id).ToHashSet();
        var ids = DefaultChecklist.Current.Sections.SelectMany(s => s.Items).Select(i => i.Id).Where(id => !designIds.Contains(id));
        var state = Build(ids, "DES");

        // Act, Assert: complete, with nothing left to do
        Assert.True(state.IsComplete);
        Assert.Null(state.FirstIncompleteSectionId());
        Assert.Null(state.NextUnansweredItemId());
    }

    /// <summary>
    ///     Test that a checklist where no section applies is complete.
    /// </summary>
    [Fact]
    public void ChecklistState_Progress_AllSectionsNotApplicable_IsComplete()
    {
        // Arrange: every section is marked as not applicable
        var state = Build([], DefaultChecklist.Current.Sections.Select(s => s.Id).ToArray());

        // Act, Assert: nothing to answer, so complete
        Assert.Equal(new ChecklistProgress(0, 0), state.Overall);
        Assert.True(state.IsComplete);
    }

    /// <summary>
    ///     Test that the next unanswered check is the first one of the applicable sections.
    /// </summary>
    [Fact]
    public void ChecklistState_NextUnansweredItemId_SkipsAnsweredAndNotApplicableSections()
    {
        // Arrange: the first section is fully answered and the second does not apply
        var requirementIds = DefaultChecklist.Current.Sections[0].Items.Select(i => i.Id);
        var state = Build(requirementIds, "DES");

        // Act, Assert: the first check of the third section is next
        Assert.Equal("DOC-01", state.NextUnansweredItemId());
    }

    /// <summary>
    ///     Test that the first incomplete section is the first applicable section with unanswered checks.
    /// </summary>
    [Fact]
    public void ChecklistState_FirstIncompleteSectionId_ReturnsFirstSectionWithUnanswered()
    {
        // Arrange: the first section is partly answered, the second does not apply
        var state = Build(["REQ-01"], "DES");

        // Act, Assert: the partly answered first section is first incomplete
        Assert.Equal("REQ", state.FirstIncompleteSectionId());

        // Arrange, Act, Assert: with the first section fully answered the next applicable one follows
        var all = Build(DefaultChecklist.Current.Sections[0].Items.Select(i => i.Id), "DES");
        Assert.Equal("DOC", all.FirstIncompleteSectionId());
    }
}
