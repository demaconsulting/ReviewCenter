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
///     Unit tests for <see cref="ProjectService"/> using an in-memory store.
/// </summary>
public class ProjectServiceTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Creates a service over the given store with a fixed clock.
    /// </summary>
    /// <param name="store">The store to use.</param>
    /// <returns>The service.</returns>
    private static ProjectService CreateService(FakeReviewStore store) => TestServices.Projects(store, new FixedTimeProvider(_now));

    /// <summary>
    ///     Test that a new project requires independent reviewers by default.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetRequireIndependentReviewersAsync_DefaultIsOn()
    {
        // Arrange: a service over an empty store
        var store = new FakeReviewStore();
        var service = CreateService(store);

        // Act: create a project
        var project = await service.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Assert: both the returned project and the stored one require independent reviewers
        Assert.True(project.RequireIndependentReviewers);
        Assert.True((await store.GetProjectAsync(project.Id, TestContext.Current.CancellationToken))!.RequireIndependentReviewers);
    }

    /// <summary>
    ///     Test that an administrator switches the setting and that each change, and only a change, is audited with
    ///     the old and the new value.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetRequireIndependentReviewersAsync_Admin_TogglesAndAudits()
    {
        // Arrange: a project that requires independent reviewers
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var service = CreateService(store);
        var project = await service.CreateAsync("Apollo", null, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: switch off, switch off again without a change, switch on
        var off = await service.SetRequireIndependentReviewersAsync(TestActors.Ann, project.Id, true, false, cancellationToken);
        var stored = (await store.GetProjectAsync(project.Id, cancellationToken))!.RequireIndependentReviewers;
        var unchanged = await service.SetRequireIndependentReviewersAsync(TestActors.Ann, project.Id, false, false, cancellationToken);
        var on = await service.SetRequireIndependentReviewersAsync(TestActors.Ann, project.Id, false, true, cancellationToken);

        // Assert: two changes and exactly two audit entries, with the documented action and detail
        Assert.True(off);
        Assert.False(stored);
        Assert.False(unchanged);
        Assert.True(on);
        var audits = store.Audits.Skip(auditBefore).ToList();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("project.independent-reviewers-changed", a.Action));
        Assert.Equal("RequireIndependentReviewers on->off", audits[0].Detail);
        Assert.Equal("RequireIndependentReviewers off->on", audits[1].Detail);
        Assert.Equal(project.Id, audits[0].ProjectId);
    }

    /// <summary>
    ///     Test that somebody who does not administer the project, and an API token, cannot switch the setting.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetRequireIndependentReviewersAsync_NotAdminOrApiToken_IsRefused()
    {
        // Arrange: real access rules with an administrator and a plain member
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        await users.CreateAsync("root", "Root", true, "correct horse battery", Actor.CommandLine, cancellationToken);
        var member = await users.CreateAsync("mem", "Mem", false, "correct horse battery", Actor.CommandLine, cancellationToken);
        var service = new ProjectService(store, userStore, new ProjectAccess(userStore, store), new FakePasswordHasher(), clock);
        var root = new Actor("root", "Root");
        var project = await service.CreateAsync("Apollo", null, root, cancellationToken);
        await store.AddMembershipAsync(member.Id, project.Id, ProjectRole.User, cancellationToken);

        // Act: a member, an administrator's token and a stranger try
        var plain = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetRequireIndependentReviewersAsync(
            new Actor("mem", "Mem"), project.Id, true, false, cancellationToken));
        var viaToken = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetRequireIndependentReviewersAsync(
            new Actor("root", "Root") { ApiTokenId = "K7M2QX9P" }, project.Id, true, false, cancellationToken));
        var stranger = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetRequireIndependentReviewersAsync(
            new Actor("nobody", "Nobody"), project.Id, true, false, cancellationToken));

        // Assert: refused each time, and the setting is unchanged
        Assert.Equal(DomainRule.ActorNotAllowed, plain.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, viaToken.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, stranger.Rule);
        Assert.True((await store.GetProjectAsync(project.Id, cancellationToken))!.RequireIndependentReviewers);
    }

    /// <summary>
    ///     Test that a change based on an outdated reading is refused with <see cref="DomainRule.RecordChanged"/>
    ///     and does not overwrite the newer value.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetRequireIndependentReviewersAsync_StaleExpected_ThrowsRecordChanged()
    {
        // Arrange: a project whose setting somebody else switched off after the caller read "on"
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var service = CreateService(store);
        var project = await service.CreateAsync("Apollo", null, cancellationToken);
        await service.SetRequireIndependentReviewersAsync(TestActors.Bob, project.Id, true, false, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: the caller still believes it is on and wants to switch it off
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetRequireIndependentReviewersAsync(
            TestActors.Ann, project.Id, true, false, cancellationToken));

        // Assert: refused as changed by someone else, nothing more recorded
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        Assert.Equal(ProjectService.IndependentReviewersChangedByOthersMessage, ex.Message);
        Assert.Equal(auditBefore, store.Audits.Count);
    }

    /// <summary>
    ///     Reads the value of one review rule from a project.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The stored value.</returns>
    private static bool ReadRule(Project project, ReviewRule rule) => rule switch
    {
        ReviewRule.IndependentReviewers => project.RequireIndependentReviewers,
        ReviewRule.NonAuthorChecklistAnswers => project.RequireNonAuthorChecklistAnswers,
        _ => project.RequireNonReopenerSigning
    };

    /// <summary>
    ///     Test that a new project has every review rule switched on.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetReviewRuleAsync_NewProject_AllRulesAreOn()
    {
        // Arrange: a service over an empty store
        var store = new FakeReviewStore();
        var service = CreateService(store);

        // Act: create a project
        var project = await service.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Assert: the returned and the stored project have all three rules on
        var stored = (await store.GetProjectAsync(project.Id, TestContext.Current.CancellationToken))!;
        foreach (var rule in Enum.GetValues<ReviewRule>())
        {
            Assert.True(ReadRule(project, rule));
            Assert.True(ReadRule(stored, rule));
        }
    }

    /// <summary>
    ///     Test that an administrator switches a further rule and that each change, and only a change, is audited
    ///     with the fixed action and the property name.
    /// </summary>
    /// <param name="rule">The rule to switch.</param>
    /// <param name="property">The property name expected in the audit detail.</param>
    [Theory]
    [InlineData(ReviewRule.NonAuthorChecklistAnswers, "RequireNonAuthorChecklistAnswers")]
    [InlineData(ReviewRule.NonReopenerSigning, "RequireNonReopenerSigning")]
    public async Task ProjectService_SetReviewRuleAsync_NewRules_AdminTogglesAndAuditsWithFixedDetail(ReviewRule rule, string property)
    {
        // Arrange: a project with all rules on
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var service = CreateService(store);
        var project = await service.CreateAsync("Apollo", null, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: switch off, switch off again without a change, switch on
        var off = await service.SetReviewRuleAsync(TestActors.Ann, project.Id, rule, true, false, cancellationToken);
        var stored = (await store.GetProjectAsync(project.Id, cancellationToken))!;
        var unchanged = await service.SetReviewRuleAsync(TestActors.Ann, project.Id, rule, false, false, cancellationToken);
        var on = await service.SetReviewRuleAsync(TestActors.Ann, project.Id, rule, false, true, cancellationToken);

        // Assert: two changes and exactly two audit entries; the other rules were never touched
        Assert.True(off);
        Assert.False(ReadRule(stored, rule));
        Assert.All(Enum.GetValues<ReviewRule>().Where(r => r != rule), other => Assert.True(ReadRule(stored, other)));
        Assert.False(unchanged);
        Assert.True(on);
        var audits = store.Audits.Skip(auditBefore).ToList();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal(ProjectService.ReviewRuleChangedAction, a.Action));
        Assert.Equal("project.review-rule-changed", audits[0].Action);
        Assert.Equal($"{property} on->off", audits[0].Detail);
        Assert.Equal($"{property} off->on", audits[1].Detail);
        Assert.Equal(project.Id, audits[0].ProjectId);
    }

    /// <summary>
    ///     Test that the independent reviewers rule keeps its original audit action and detail, whether it is
    ///     switched with the general method or the original one.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetReviewRuleAsync_IndependentReviewers_KeepsTheOriginalAuditAction()
    {
        // Arrange: a project with all rules on
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var service = CreateService(store);
        var project = await service.CreateAsync("Apollo", null, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: switch off with the general method, on with the original method
        await service.SetReviewRuleAsync(TestActors.Ann, project.Id, ReviewRule.IndependentReviewers, true, false, cancellationToken);
        await service.SetRequireIndependentReviewersAsync(TestActors.Ann, project.Id, false, true, cancellationToken);

        // Assert: both audit entries use the original action and the original detail
        var audits = store.Audits.Skip(auditBefore).ToList();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal(ProjectService.IndependentReviewersChangedAction, a.Action));
        Assert.Equal("RequireIndependentReviewers on->off", audits[0].Detail);
        Assert.Equal("RequireIndependentReviewers off->on", audits[1].Detail);
    }

    /// <summary>
    ///     Test that somebody who does not administer the project, and an API token, cannot switch any rule.
    /// </summary>
    /// <param name="rule">The rule to try.</param>
    [Theory]
    [InlineData(ReviewRule.IndependentReviewers)]
    [InlineData(ReviewRule.NonAuthorChecklistAnswers)]
    [InlineData(ReviewRule.NonReopenerSigning)]
    public async Task ProjectService_SetReviewRuleAsync_NotAdminOrApiToken_IsRefused(ReviewRule rule)
    {
        // Arrange: real access rules with an administrator and a plain member
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new FixedTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore();
        var users = new UserService(userStore, new FakePasswordHasher(), clock);
        await users.CreateAsync("root", "Root", true, "correct horse battery", Actor.CommandLine, cancellationToken);
        var member = await users.CreateAsync("mem", "Mem", false, "correct horse battery", Actor.CommandLine, cancellationToken);
        var service = new ProjectService(store, userStore, new ProjectAccess(userStore, store), new FakePasswordHasher(), clock);
        var root = new Actor("root", "Root");
        var project = await service.CreateAsync("Apollo", null, root, cancellationToken);
        await store.AddMembershipAsync(member.Id, project.Id, ProjectRole.User, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: a member, an administrator's token and a stranger try
        var plain = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetReviewRuleAsync(
            new Actor("mem", "Mem"), project.Id, rule, true, false, cancellationToken));
        var viaToken = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetReviewRuleAsync(
            new Actor("root", "Root") { ApiTokenId = "K7M2QX9P" }, project.Id, rule, true, false, cancellationToken));
        var stranger = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetReviewRuleAsync(
            new Actor("nobody", "Nobody"), project.Id, rule, true, false, cancellationToken));

        // Assert: refused each time, the value is unchanged and nothing was recorded
        Assert.Equal(DomainRule.ActorNotAllowed, plain.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, viaToken.Rule);
        Assert.Equal(DomainRule.ProjectNotFound, stranger.Rule);
        Assert.True(ReadRule((await store.GetProjectAsync(project.Id, cancellationToken))!, rule));
        Assert.Equal(auditBefore, store.Audits.Count);
    }

    /// <summary>
    ///     Test that a change of any rule based on an outdated reading is refused with
    ///     <see cref="DomainRule.RecordChanged"/> and does not overwrite the newer value.
    /// </summary>
    /// <param name="rule">The rule to try.</param>
    [Theory]
    [InlineData(ReviewRule.IndependentReviewers)]
    [InlineData(ReviewRule.NonAuthorChecklistAnswers)]
    [InlineData(ReviewRule.NonReopenerSigning)]
    public async Task ProjectService_SetReviewRuleAsync_StaleExpected_ThrowsRecordChanged(ReviewRule rule)
    {
        // Arrange: a project whose rule somebody else switched off after the caller read "on"
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new FakeReviewStore();
        var service = CreateService(store);
        var project = await service.CreateAsync("Apollo", null, cancellationToken);
        await service.SetReviewRuleAsync(TestActors.Bob, project.Id, rule, true, false, cancellationToken);
        var auditBefore = store.Audits.Count;

        // Act: the caller still believes it is on and wants to switch it off
        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => service.SetReviewRuleAsync(
            TestActors.Ann, project.Id, rule, true, false, cancellationToken));

        // Assert: refused as changed by someone else, nothing more recorded
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        Assert.Equal(ProjectService.IndependentReviewersChangedByOthersMessage, ex.Message);
        Assert.Equal(auditBefore, store.Audits.Count);
    }

    /// <summary>
    ///     Test that a rule that is not a defined value is rejected.
    /// </summary>
    [Fact]
    public async Task ProjectService_SetReviewRuleAsync_UndefinedRule_ThrowsArgumentOutOfRange()
    {
        // Arrange: a project and a value outside the enumeration
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService(new FakeReviewStore());
        var project = await service.CreateAsync("Apollo", null, cancellationToken);

        // Act and assert: the undefined rule is rejected
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetReviewRuleAsync(
            TestActors.Ann, project.Id, (ReviewRule)99, true, false, cancellationToken));
    }

    /// <summary>
    ///     Test that a valid project is stored with trimmed text and the clock's creation time.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_ValidInput_StoresTrimmedProject()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project with surrounding whitespace
        var project = await service.CreateAsync("  Apollo  ", "  Moon landing  ", TestContext.Current.CancellationToken);

        // Assert: trimmed text, assigned id, and creation time from the clock
        Assert.True(project.Id > 0);
        Assert.Equal("Apollo", project.Name);
        Assert.Equal("Moon landing", project.Description);
        Assert.Equal(_now, project.Created);
    }

    /// <summary>
    ///     Test that a missing description is stored as empty text.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_NullDescription_StoresEmptyDescription()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project without a description
        var project = await service.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Assert: the description is empty rather than null
        Assert.Equal(string.Empty, project.Description);
    }

    /// <summary>
    ///     Test that an empty or blank name is rejected.
    /// </summary>
    /// <param name="name">The invalid name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProjectService_CreateAsync_BlankName_ThrowsProjectNameInvalid(string? name)
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project with the blank name
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(name, string.Empty, TestContext.Current.CancellationToken));

        // Assert: the rule and a readable message
        Assert.Equal(DomainRule.ProjectNameInvalid, exception.Rule);
        Assert.Contains("name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a name over the limit is rejected but a name exactly at the limit is accepted.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_NameLength_LimitIsInclusive()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create projects with a name at the limit and one over it
        var atLimit = await service.CreateAsync(new string('n', ReviewLimits.MaxProjectNameLength), null, TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(new string('n', ReviewLimits.MaxProjectNameLength + 1), null, TestContext.Current.CancellationToken));

        // Assert: the longer one is rejected
        Assert.Equal(ReviewLimits.MaxProjectNameLength, atLimit.Name.Length);
        Assert.Equal(DomainRule.ProjectNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a description over the limit is rejected.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_DescriptionTooLong_ThrowsDescriptionTooLong()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project with an over-long description
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("Apollo", new string('d', ReviewLimits.MaxProjectDescriptionLength + 1), TestContext.Current.CancellationToken));

        // Assert: the description rule is reported
        Assert.Equal(DomainRule.ProjectDescriptionTooLong, exception.Rule);
    }

    /// <summary>
    ///     Provides every kind of invalid project input: a blank name, an over-long name and an over-long description.
    /// </summary>
    public static TheoryData<string?, string?> InvalidInputs => new()
    {
        { null, "Description" },
        { "   ", null },
        { new string('n', ReviewLimits.MaxProjectNameLength + 1), null },
        { "Apollo", new string('d', ReviewLimits.MaxProjectDescriptionLength + 1) }
    };

    /// <summary>
    ///     Test that a rejected project is not stored, whichever rule it broke.
    /// </summary>
    /// <param name="name">The invalid name, or a valid one when the description is the invalid part.</param>
    /// <param name="description">The description.</param>
    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task ProjectService_CreateAsync_Rejected_StoresNothing(string? name, string? description)
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: attempt an invalid create, then list
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(name, description, TestContext.Current.CancellationToken));
        var projects = await service.ListAsync(TestContext.Current.CancellationToken);

        // Assert: nothing was stored
        Assert.Empty(projects);
    }

    /// <summary>
    ///     Test that projects are listed ordered by name, and projects with the same name by identity.
    /// </summary>
    [Fact]
    public async Task ProjectService_ListAsync_SeveralProjects_ReturnsOrderedByNameThenId()
    {
        // Arrange: a service with projects created out of order, two of them sharing a name
        var service = CreateService(new FakeReviewStore());
        var zeta = await service.CreateAsync("Zeta", null, TestContext.Current.CancellationToken);
        var firstAlpha = await service.CreateAsync("Alpha", null, TestContext.Current.CancellationToken);
        var secondAlpha = await service.CreateAsync("Alpha", null, TestContext.Current.CancellationToken);

        // Act: list the projects
        var projects = await service.ListAsync(TestContext.Current.CancellationToken);

        // Assert: by name, and by identity within the same name
        Assert.Equal([firstAlpha.Id, secondAlpha.Id, zeta.Id], projects.Select(project => project.Id));
    }

    /// <summary>
    ///     Test that a created project can be fetched by id, and an unknown id returns nothing.
    /// </summary>
    [Fact]
    public async Task ProjectService_GetAsync_ExistingAndUnknown_ReturnsProjectOrNull()
    {
        // Arrange: a service with one project
        var service = CreateService(new FakeReviewStore());
        var created = await service.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: fetch the project and an unknown id
        var found = await service.GetAsync(created.Id, TestContext.Current.CancellationToken);
        var missing = await service.GetAsync(created.Id + 100, TestContext.Current.CancellationToken);

        // Assert: the known one is returned, the unknown is null
        Assert.Equal("Apollo", found?.Name);
        Assert.Null(missing);
    }
}
