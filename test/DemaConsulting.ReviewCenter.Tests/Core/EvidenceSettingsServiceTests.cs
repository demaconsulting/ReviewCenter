using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests of <see cref="EvidenceSettingsService"/> and <see cref="EvidenceSettings"/>: the defaults, who may
///     read and change the settings, the audit entry and the compare-and-set rule.
/// </summary>
public class EvidenceSettingsServiceTests
{
    /// <summary>
    ///     The instant at which the clock starts.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that the defaults keep the evidence private (no anonymous access) and complete (every switch on).
    /// </summary>
    [Fact]
    public void EvidenceSettings_Default_IsPrivateAndComplete()
    {
        // Act
        var defaults = EvidenceSettings.Default;

        // Assert
        Assert.False(defaults.AnonymousIndex);
        Assert.False(defaults.AnonymousAttestations);
        Assert.True(defaults.IncludeThreads);
        Assert.True(defaults.IncludeReviewerNames);
        Assert.True(defaults.IncludeChecklistWording);
        Assert.True(defaults.IncludeFiles);
        Assert.True(defaults.IncludeGitContext);
        Assert.False(defaults.IsReduced);
        Assert.Empty(defaults.OmittedSections);
        Assert.Empty(defaults.ChangesFrom(defaults));
    }

    /// <summary>
    ///     Test that every content switch that is off is named as an omitted section, in a fixed order, and that the
    ///     anonymous toggles do not count as a reduction of the content.
    /// </summary>
    [Fact]
    public void EvidenceSettings_OmittedSections_NameEachSwitchThatIsOff()
    {
        // Arrange
        var settings = EvidenceSettings.Default with
        {
            AnonymousIndex = true,
            IncludeThreads = false,
            IncludeReviewerNames = false,
            IncludeChecklistWording = false,
            IncludeFiles = false,
            IncludeGitContext = false
        };

        // Act, Assert
        Assert.True(settings.IsReduced);
        Assert.Equal(
            [
                "Review discussion threads", "Reviewer names", "Checklist wording", "File list and hashes",
                "Git context"
            ],
            settings.OmittedSections);
        Assert.False((EvidenceSettings.Default with { AnonymousAttestations = true }).IsReduced);
    }

    /// <summary>
    ///     Test that the changes between two settings name each changed key with its old and new value.
    /// </summary>
    [Fact]
    public void EvidenceSettings_ChangesFrom_NamesChangedKeysWithOldAndNewValue()
    {
        // Arrange
        var earlier = EvidenceSettings.Default;
        var later = earlier with { AnonymousIndex = true, IncludeReviewerNames = false };

        // Act
        var changes = later.ChangesFrom(earlier);

        // Assert
        Assert.Equal(
            [
                new EvidenceSettingChange(nameof(EvidenceSettings.AnonymousIndex), false, true),
                new EvidenceSettingChange(nameof(EvidenceSettings.IncludeReviewerNames), true, false)
            ],
            changes);
    }

    /// <summary>
    ///     Test that a new project has the default settings.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsService_GetAsync_NewProject_HasDefaults()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var settings = await world.Service.GetAsync(
            new Actor("pa", "pa"), world.Project, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EvidenceSettings.Default, settings);
    }

    /// <summary>
    ///     Test that the settings are read and changed by a system administrator and by a project administrator of
    ///     the project only: a plain member is told it may not, and everybody else (and nobody) is refused.
    /// </summary>
    /// <param name="who">The user name of the actor, or <c>null</c> for nobody.</param>
    /// <param name="refusal">The rule that refuses the actor, or <c>null</c> when the actor is allowed.</param>
    [Theory]
    [InlineData("root", null)]
    [InlineData("pa", null)]
    [InlineData("mem", DomainRule.ActorNotAllowed)]
    [InlineData("out", DomainRule.ProjectNotFound)]
    [InlineData("padm", DomainRule.ProjectNotFound)]
    [InlineData("off", DomainRule.ProjectNotFound)]
    [InlineData("offadm", DomainRule.ProjectNotFound)]
    [InlineData(null, DomainRule.ProjectNotFound)]
    public async Task EvidenceSettingsService_GetAndSet_ActorKinds_AreAllowedOrRefused(string? who, DomainRule? refusal)
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = who is null ? null : new Actor(who, who);
        var updated = EvidenceSettings.Default with { AnonymousIndex = true };

        // Act
        var read = world.Service.GetAsync(actor, world.Project, cancellationToken);
        var write = world.Service.SetAsync(actor, world.Project, EvidenceSettings.Default, updated, cancellationToken);

        // Assert
        if (refusal is null)
        {
            Assert.Equal(EvidenceSettings.Default, await read);
            Assert.True(await write);
            Assert.Equal(updated, await world.Service.GetEffectiveAsync(world.Project, cancellationToken));
            return;
        }

        Assert.Equal(refusal, (await Assert.ThrowsAsync<DomainRuleException>(() => read)).Rule);
        Assert.Equal(refusal, (await Assert.ThrowsAsync<DomainRuleException>(() => write)).Rule);

        // A refusal changes nothing and records nothing
        Assert.Equal(EvidenceSettings.Default, await world.Service.GetEffectiveAsync(world.Project, cancellationToken));
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == EvidenceSettingsService.ChangedAction);
    }

    /// <summary>
    ///     Test that a change is stored for its own project only and is recorded as one audit entry that names the
    ///     project, the actor and each changed key with its old and new value.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsService_SetAsync_Change_IsStoredAndAudited()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var updated = EvidenceSettings.Default with { AnonymousAttestations = true, IncludeThreads = false };

        // Act
        var changed = await world.Service.SetAsync(
            new Actor("pa", "Pat Admin"), world.Project, EvidenceSettings.Default, updated, cancellationToken);

        // Assert: stored for the project only
        Assert.True(changed);
        Assert.Equal(updated, await world.Service.GetEffectiveAsync(world.Project, cancellationToken));
        Assert.Equal(EvidenceSettings.Default, await world.Service.GetEffectiveAsync(world.Other, cancellationToken));

        // Assert: one audit entry with the project and the changed keys
        var entry = Assert.Single(world.Store.Audits, a => a.Action == "project.evidence-settings-changed");
        Assert.Equal(world.Project, entry.ProjectId);
        Assert.Null(entry.ReviewId);
        Assert.Equal("Pat Admin (pa)", entry.ActorName);
        Assert.Equal("AnonymousAttestations off->on; IncludeThreads on->off", entry.Detail);
    }

    /// <summary>
    ///     Test that saving settings that equal the stored ones stores nothing and records nothing.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsService_SetAsync_NoChange_RecordsNothing()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var changed = await world.Service.SetAsync(
            new Actor("pa", "pa"),
            world.Project,
            EvidenceSettings.Default,
            EvidenceSettings.Default,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(changed);
        Assert.DoesNotContain(world.Store.Audits, a => a.Action == EvidenceSettingsService.ChangedAction);
    }

    /// <summary>
    ///     Test that a change based on settings that someone else changed in the meantime is refused with the
    ///     record-changed rule and stores nothing.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsService_SetAsync_ChangedMeanwhile_IsRefusedAsRecordChanged()
    {
        // Arrange: another administrator changes the settings first
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = EvidenceSettings.Default with { AnonymousIndex = true };
        await world.Service.SetAsync(
            new Actor("pb", "pb"), world.Project, EvidenceSettings.Default, first, cancellationToken);

        // Act: the slower administrator saves a change based on the old settings
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(
                new Actor("pa", "pa"),
                world.Project,
                EvidenceSettings.Default,
                EvidenceSettings.Default with { IncludeFiles = false },
                cancellationToken));

        // Assert
        Assert.Equal(DomainRule.RecordChanged, ex.Rule);
        Assert.Equal(first, await world.Service.GetEffectiveAsync(world.Project, cancellationToken));
        Assert.Single(world.Store.Audits, a => a.Action == EvidenceSettingsService.ChangedAction);
    }

    /// <summary>
    ///     Test that the effective settings of a project that does not exist are <c>null</c>, and that a change to
    ///     such a project is refused as not found.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsService_UnknownProject_IsNotFound()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act, Assert
        Assert.Null(await world.Service.GetEffectiveAsync(9999, cancellationToken));
        var ex = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetAsync(
                new Actor("root", "root"),
                9999,
                EvidenceSettings.Default,
                EvidenceSettings.Default with { AnonymousIndex = true },
                cancellationToken));
        Assert.Equal(DomainRule.ProjectNotFound, ex.Rule);
    }

    /// <summary>
    ///     Test that the constructor and the change method refuse null arguments.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsService_NullArguments_AreRefused()
    {
        // Arrange
        var world = await WorldAsync();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new EvidenceSettingsService(null!, new AllowAllProjectAccess(world.Store)));
        Assert.Throws<ArgumentNullException>(() => new EvidenceSettingsService(world.Store, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.SetAsync(
                new Actor("root", "root"), world.Project, null!, EvidenceSettings.Default, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.SetAsync(
                new Actor("root", "root"), world.Project, EvidenceSettings.Default, null!, cancellationToken));
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
        return new World(store, new EvidenceSettingsService(store, access), project.Id, other.Id);
    }

    /// <summary>
    ///     The arranged store, service and projects.
    /// </summary>
    /// <param name="Store">The review store.</param>
    /// <param name="Service">The service under test, over the real <see cref="ProjectAccess"/>.</param>
    /// <param name="Project">The project the tests work on.</param>
    /// <param name="Other">A second project.</param>
    private sealed record World(FakeReviewStore Store, EvidenceSettingsService Service, int Project, int Other);
}
