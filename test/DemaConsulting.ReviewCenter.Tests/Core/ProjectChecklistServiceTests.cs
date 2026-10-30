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
///     Unit tests of <see cref="ProjectChecklistService"/>: reading the checklist of a project, who may change it,
///     the strict check of a new definition, the version-label rule, the audit entry, the compare-and-set rule and
///     the reset to the default.
/// </summary>
public class ProjectChecklistServiceTests
{
    /// <summary>
    ///     The instant at which the clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     A secret wording that must never appear in an audit entry.
    /// </summary>
    private const string SecretWording = "Confirm the moon phase is waxing";

    /// <summary>
    ///     Builds the JSON text of a definition with one section and one check.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="text">The wording of the check.</param>
    /// <returns>The JSON text.</returns>
    private static string Custom(string version, string text = SecretWording)
    {
        return new ChecklistDefinition(
            version,
            [new ChecklistSection("moon", "Moon", string.Empty, [new ChecklistItem("moon-01", text)])]).ToJson();
    }

    /// <summary>
    ///     Test that a new project reports the built-in default, flagged as the default, with the hash of its text.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_GetAsync_NewProject_IsDefault()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var checklist = await world.Service.GetAsync(
            new Actor("pa", "pa"), world.Project, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(checklist.IsDefault);
        Assert.Equal(DefaultChecklist.Version, checklist.Version);
        Assert.Equal(DefaultChecklist.Current.ToJson(), checklist.Json);
        Assert.Equal(ProjectChecklistService.HashOf(null), checklist.Hash);
        Assert.Equal(14, checklist.SectionCount);
        Assert.Equal(58, checklist.ItemCount);
    }

    /// <summary>
    ///     Test that the hash is the lower-case SHA-256 of the UTF-8 text, and that the hash of "no definition" is
    ///     the hash of the default's text.
    /// </summary>
    [Fact]
    public void ProjectChecklistService_HashOf_IsSha256OfTheTextAndOfTheDefaultForNull()
    {
        // Act, Assert: the well-known hash of "abc"
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ProjectChecklistService.HashOf("abc"));
        Assert.Equal(ProjectChecklistService.HashOf(DefaultChecklist.Current.ToJson()), ProjectChecklistService.HashOf(null));
    }

    /// <summary>
    ///     Test that the checklist is read by anyone who may see the project and changed by a system administrator
    ///     and a project administrator of the project only: a plain member is told it may not change, and everybody
    ///     else (and nobody) is given the answer for a project that does not exist.
    /// </summary>
    /// <param name="who">The user name of the actor, or <c>null</c> for nobody.</param>
    /// <param name="mayRead">Whether the actor may read the checklist.</param>
    /// <param name="refusal">The rule that refuses a change, or <c>null</c> when the actor may change it.</param>
    [Theory]
    [InlineData("root", true, null)]
    [InlineData("pa", true, null)]
    [InlineData("mem", true, DomainRule.ActorNotAllowed)]
    [InlineData("out", false, DomainRule.ProjectNotFound)]
    [InlineData("padm", false, DomainRule.ProjectNotFound)]
    [InlineData("off", false, DomainRule.ProjectNotFound)]
    [InlineData("offadm", false, DomainRule.ProjectNotFound)]
    [InlineData(null, false, DomainRule.ProjectNotFound)]
    public async Task ProjectChecklistService_GetAndSet_ActorKinds_AreAllowedOrRefused(
        string? who,
        bool mayRead,
        DomainRule? refusal)
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = who is null ? null : new Actor(who, who);

        // Act
        var read = world.Service.GetAsync(actor, world.Project, cancellationToken);
        var write = actor is null
            ? Task.FromException<bool>(new DomainRuleException(DomainRule.ProjectNotFound, "no actor"))
            : world.Service.SetAsync(actor, world.Project, ProjectChecklistService.HashOf(null), Custom("v2"), cancellationToken);

        // Assert
        if (mayRead)
        {
            Assert.True((await read).IsDefault);
        }
        else
        {
            Assert.Equal(DomainRule.ProjectNotFound, (await Assert.ThrowsAsync<DomainRuleException>(() => read)).Rule);
        }

        if (refusal is null)
        {
            Assert.True(await write);
            Assert.Equal("v2", (await world.Service.GetAsync(actor, world.Project, cancellationToken)).Version);
            return;
        }

        Assert.Equal(refusal, (await Assert.ThrowsAsync<DomainRuleException>(() => write)).Rule);

        // A refusal changes nothing and records nothing
        Assert.Null((await world.Store.GetProjectAsync(world.Project, cancellationToken))!.ChecklistDefinition);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a person who signed in with an API token is refused a change and a reset, even a system
    ///     administrator, and that nothing is stored or recorded.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAndReset_ApiToken_AreRefused()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var viaApi = new Actor("root", "root") { ApiTokenId = "K7M2QX9P" };
        var hash = ProjectChecklistService.HashOf(null);

        // Act
        var set = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(viaApi, world.Project, hash, Custom("v2"), cancellationToken));
        var reset = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ResetAsync(viaApi, world.Project, hash, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, set.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, reset.Rule);
        Assert.Null((await world.Store.GetProjectAsync(world.Project, cancellationToken))!.ChecklistDefinition);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a replace is stored in its normal form for its own project only, is read back as the project's
    ///     own definition, and is recorded as one audit entry that names the version, the counts, a short hash and
    ///     the previous version but not the wording.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAsync_Change_IsStoredAndAudited()
    {
        // Arrange: a text with extra white space, which the store keeps in its normal form
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var text = Custom("moon-1").Replace("{", "{ ", StringComparison.Ordinal);

        // Act
        var changed = await world.Service.SetAsync(
            new Actor("pa", "Pat Admin"), world.Project, ProjectChecklistService.HashOf(null), text, cancellationToken);

        // Assert: stored for the project only, in the normal form, and read back as its own
        Assert.True(changed);
        var checklist = await world.Service.GetAsync(new Actor("pa", "pa"), world.Project, cancellationToken);
        Assert.False(checklist.IsDefault);
        Assert.Equal("moon-1", checklist.Version);
        Assert.Equal(Custom("moon-1"), checklist.Json);
        Assert.Equal(ProjectChecklistService.HashOf(Custom("moon-1")), checklist.Hash);
        Assert.Equal((1, 1), (checklist.SectionCount, checklist.ItemCount));
        Assert.True((await world.Service.GetAsync(new Actor("root", "root"), world.Other, cancellationToken)).IsDefault);

        // Assert: one audit entry, with the project, the actor and the facts but not the wording
        var entry = Assert.Single(world.Store.Audits, a => a.Action == "project.checklist-changed");
        Assert.Equal(world.Project, entry.ProjectId);
        Assert.Null(entry.ReviewId);
        Assert.Equal("Pat Admin (pa)", entry.ActorName);
        Assert.Equal(
            $"version moon-1, 1 sections, 1 items, sha256 {AuditText.ShortHash(Custom("moon-1"))}, previous version {DefaultChecklist.Version}",
            entry.Detail);
        Assert.DoesNotContain(SecretWording, entry.Detail, StringComparison.Ordinal);
        Assert.True(entry.Detail.Length <= AuditText.MaxDetailLength);
    }

    /// <summary>
    ///     Test that a definition that breaks a rule is refused with the invalid-definition rule naming every
    ///     error with its path, before anything is stored or recorded.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAsync_InvalidDefinition_IsRefusedWithAllErrors()
    {
        // Arrange: a duplicate id and a missing title
        var world = await WorldAsync();
        const string json = """
            {"version": "v2", "sections": [
              {"id": "a", "title": "", "items": [{"id": "a", "text": "T"}]}
            ]}
            """;

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(
                new Actor("pa", "pa"),
                world.Project,
                ProjectChecklistService.HashOf(null),
                json,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.ChecklistDefinitionInvalid, exception.Rule);
        Assert.Contains("sections[0].title: is missing or empty", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sections[0].items[0].id: duplicate \"a\"", exception.Message, StringComparison.Ordinal);
        Assert.Null((await world.Store.GetProjectAsync(world.Project, TestContext.Current.CancellationToken))!.ChecklistDefinition);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that the pure check reports every error with its path and stores nothing.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_Validate_ReportsAllErrorsAndStoresNothing()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var invalid = ProjectChecklistService.Validate("{\"version\": \"\", \"sections\": []}");
        var valid = ProjectChecklistService.Validate(Custom("v2"));

        // Assert
        Assert.Equal(["version", "sections"], invalid.Errors.Select(e => e.Path));
        Assert.True(valid.IsValid);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
        Assert.Null((await world.Store.GetProjectAsync(world.Project, TestContext.Current.CancellationToken))!.ChecklistDefinition);
    }

    /// <summary>
    ///     Test that a definition whose wording differs from the current one but whose version label is the same is
    ///     refused with a clear message, for the default and for a custom definition, and that another label is
    ///     accepted.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAsync_SameVersionOtherContent_IsRefused()
    {
        // Arrange: the project has a custom definition
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = new Actor("pa", "pa");
        await world.Service.SetAsync(admin, world.Project, ProjectChecklistService.HashOf(null), Custom("moon-1"), cancellationToken);
        var hash = ProjectChecklistService.HashOf(Custom("moon-1"));

        // Act: other wording under the same label (custom), the default's label with other wording (default case is
        // covered by resetting first), and then the same wording under a new label
        var sameLabel = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(admin, world.Project, hash, Custom("moon-1", "Other wording"), cancellationToken));
        var newLabel = await world.Service.SetAsync(admin, world.Project, hash, Custom("moon-2", "Other wording"), cancellationToken);

        // Assert
        Assert.Equal(DomainRule.ChecklistDefinitionInvalid, sameLabel.Rule);
        Assert.Contains("version label must change", sameLabel.Message, StringComparison.Ordinal);
        Assert.True(newLabel);
        Assert.Equal(2, world.Store.Audits.Count(a => a.Action == ProjectChecklistService.ChangedAction));
    }

    /// <summary>
    ///     Test that a replace that keeps the default's version label but changes its wording is refused.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAsync_DefaultLabelOtherContent_IsRefused()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(
                new Actor("pa", "pa"),
                world.Project,
                ProjectChecklistService.HashOf(null),
                Custom(DefaultChecklist.Version),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DomainRule.ChecklistDefinitionInvalid, exception.Rule);
        Assert.Contains("version label must change", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that saving the content that is stored already (also the default's text for a project without a
    ///     definition) stores nothing and records nothing.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAsync_SameContent_RecordsNothing()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = new Actor("pa", "pa");

        // Act: the default's own text, then a custom text twice
        var defaultAgain = await world.Service.SetAsync(
            admin, world.Project, ProjectChecklistService.HashOf(null), DefaultChecklist.Current.ToJson(), cancellationToken);
        await world.Service.SetAsync(admin, world.Project, ProjectChecklistService.HashOf(null), Custom("moon-1"), cancellationToken);
        var customAgain = await world.Service.SetAsync(
            admin, world.Project, ProjectChecklistService.HashOf(Custom("moon-1")), Custom("moon-1"), cancellationToken);

        // Assert
        Assert.False(defaultAgain);
        Assert.False(customAgain);
        Assert.Single(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a change based on a definition that someone else changed in the meantime is refused with the
    ///     record-changed rule, for a replace and for a reset, and stores and records nothing.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_SetAndReset_ChangedMeanwhile_AreRefusedAsRecordChanged()
    {
        // Arrange: another administrator changes the checklist first
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var staleHash = ProjectChecklistService.HashOf(null);
        await world.Service.SetAsync(new Actor("pb", "pb"), world.Project, staleHash, Custom("moon-1"), cancellationToken);

        // Act: the slower administrator saves, and resets, based on the old definition
        var set = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(new Actor("pa", "pa"), world.Project, staleHash, Custom("moon-2"), cancellationToken));
        var reset = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ResetAsync(new Actor("pa", "pa"), world.Project, staleHash, cancellationToken));

        // Assert
        Assert.Equal(DomainRule.RecordChanged, set.Rule);
        Assert.Equal(DomainRule.RecordChanged, reset.Rule);
        Assert.Equal(Custom("moon-1"), (await world.Store.GetProjectAsync(world.Project, cancellationToken))!.ChecklistDefinition);
        Assert.Single(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a reset removes the project's definition, so the default applies again, and is recorded with
    ///     the same action, saying that it was a reset and naming the previous version.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_ResetAsync_CustomDefinition_RestoresDefaultAndAudits()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        await world.Service.SetAsync(
            new Actor("pa", "pa"), world.Project, ProjectChecklistService.HashOf(null), Custom("moon-1"), cancellationToken);

        // Act
        var reset = await world.Service.ResetAsync(
            new Actor("pa", "Pat Admin"), world.Project, ProjectChecklistService.HashOf(Custom("moon-1")), cancellationToken);

        // Assert
        Assert.True(reset);
        Assert.Null((await world.Store.GetProjectAsync(world.Project, cancellationToken))!.ChecklistDefinition);
        Assert.True((await world.Service.GetAsync(new Actor("pa", "pa"), world.Project, cancellationToken)).IsDefault);
        var entry = Assert.Single(world.Store.Audits, a => a.Detail.StartsWith("reset to default", StringComparison.Ordinal));
        Assert.Equal(ProjectChecklistService.ChangedAction, entry.Action);
        Assert.Equal("Pat Admin (pa)", entry.ActorName);
        Assert.Equal($"reset to default, version {DefaultChecklist.Version}, previous version moon-1", entry.Detail);
    }

    /// <summary>
    ///     Test that resetting a project that already uses the default stores nothing and records nothing.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_ResetAsync_AlreadyDefault_RecordsNothing()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var reset = await world.Service.ResetAsync(
            new Actor("pa", "pa"), world.Project, ProjectChecklistService.HashOf(null), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reset);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a project that does not exist is not found for every operation.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_UnknownProject_IsNotFound()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");

        // Act, Assert
        Assert.Equal(
            DomainRule.ProjectNotFound,
            (await Assert.ThrowsAsync<DomainRuleException>(() => world.Service.GetAsync(root, 9999, cancellationToken))).Rule);
        Assert.Equal(
            DomainRule.ProjectNotFound,
            (await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Service.SetAsync(root, 9999, ProjectChecklistService.HashOf(null), Custom("v2"), cancellationToken))).Rule);
        Assert.Equal(
            DomainRule.ProjectNotFound,
            (await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Service.ResetAsync(root, 9999, ProjectChecklistService.HashOf(null), cancellationToken))).Rule);
    }

    /// <summary>
    ///     Test that the constructor and the change methods refuse null arguments.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistService_NullArguments_AreRefused()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = new Actor("root", "root");

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ProjectChecklistService(null!, new AllowAllProjectAccess(world.Store)));
        Assert.Throws<ArgumentNullException>(() => new ProjectChecklistService(world.Store, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.SetAsync(null!, world.Project, "h", Custom("v2"), cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.SetAsync(root, world.Project, null!, Custom("v2"), cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.ResetAsync(null!, world.Project, "h", cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.ResetAsync(root, world.Project, null!, cancellationToken));
    }

    /// <summary>
    ///     Arranges users, two projects and the service over the real access rules.
    /// </summary>
    /// <returns>The arranged world.</returns>
    private static async Task<World> WorldAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clock = new MovableTimeProvider(_now);
        var store = new FakeReviewStore();
        var userStore = new FakeUserStore(store);
        var ids = new Dictionary<string, int>();
        foreach (var (name, admin) in new[]
                 {
                     ("root", true), ("pa", false), ("pb", false), ("mem", false), ("out", false), ("padm", false),
                     ("off", false), ("offadm", false)
                 })
        {
            var stored = await userStore.AddAsync(
                new User
                {
                    Username = name,
                    DisplayName = name,
                    IsSystemAdmin = admin,
                    Enabled = !name.StartsWith("off", StringComparison.Ordinal),
                    PasswordHash = "hash",
                    Created = _now
                },
                created => AuditDraft.By(Actor.CommandLine, "user.created", created.Username),
                cancellationToken);
            ids[name] = stored.Id;
        }

        var access = new ProjectAccess(userStore, store);
        var projects = new ProjectService(store, userStore, access, new FakePasswordHasher(), clock);
        var project = await projects.CreateAsync("Own", null, new Actor("root", "root"), cancellationToken);
        var other = await projects.CreateAsync("Other", null, new Actor("root", "root"), cancellationToken);
        await store.AddMembershipAsync(ids["pa"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(ids["pb"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(ids["mem"], project.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["off"], project.Id, ProjectRole.User, cancellationToken);
        await store.AddMembershipAsync(ids["offadm"], project.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(ids["padm"], other.Id, ProjectRole.ProjectAdmin, cancellationToken);
        return new World(store, new ProjectChecklistService(store, access), project.Id, other.Id);
    }

    /// <summary>
    ///     The arranged store, service and projects.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Service">The service under test, over the real <see cref="ProjectAccess"/>.</param>
    /// <param name="Project">The project the tests work on.</param>
    /// <param name="Other">A second project.</param>
    private sealed record World(FakeReviewStore Store, ProjectChecklistService Service, int Project, int Other);
}
