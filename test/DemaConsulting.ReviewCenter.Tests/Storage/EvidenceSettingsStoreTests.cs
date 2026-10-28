using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Storage;

/// <summary>
///     Tests for the evidence settings of <see cref="DemaConsulting.ReviewCenter.Storage.ReviewStore"/> against a
///     real SQLite database file.
/// </summary>
public class EvidenceSettingsStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that a new project is stored with the default settings.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_NewProject_HasDefaultEvidenceSettings()
    {
        // Arrange, Act
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var added = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);

        // Assert
        Assert.Equal(EvidenceSettings.Default, (await db.Store.GetProjectAsync(added.Id, cancellationToken))!.Evidence);
    }

    /// <summary>
    ///     Test that a change is persisted for its own project only, survives a reload, and writes one audit entry
    ///     of the project whose chain still verifies.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateEvidenceSettingsAsync_Change_IsPersistedAndAudited()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var gemini = await db.Store.AddProjectAsync(new Project { Name = "Gemini", Created = _created }, cancellationToken);
        var updated = new EvidenceSettings(true, true, false, false, false, false, false);

        // Act
        var stored = await db.Store.UpdateEvidenceSettingsAsync(
            apollo.Id,
            EvidenceSettings.Default,
            updated,
            AuditDraft.By(TestActors.Ann, EvidenceSettingsService.ChangedAction, "x", apollo.Id),
            cancellationToken);

        // Assert
        Assert.True(stored);
        Assert.Equal(updated, (await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.Evidence);
        Assert.Equal(updated, (await db.Store.ListProjectsAsync(cancellationToken)).Single(p => p.Id == apollo.Id).Evidence);
        Assert.Equal(EvidenceSettings.Default, (await db.Store.GetProjectAsync(gemini.Id, cancellationToken))!.Evidence);
        var entry = Assert.Single(
            await db.Audit.ListAsync(apollo.Id, null, null, null, 100, cancellationToken),
            e => e.Action == EvidenceSettingsService.ChangedAction);
        Assert.Equal(apollo.Id, entry.ProjectId);
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that a change based on settings that are no longer stored changes nothing and writes no audit
    ///     entry, and that so does a change to a project that does not exist.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateEvidenceSettingsAsync_ExpectedDiffers_ChangesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var draft = AuditDraft.By(TestActors.Ann, EvidenceSettingsService.ChangedAction, "x", apollo.Id);
        var first = EvidenceSettings.Default with { AnonymousIndex = true };
        Assert.True(await db.Store.UpdateEvidenceSettingsAsync(
            apollo.Id, EvidenceSettings.Default, first, draft, cancellationToken));

        // Act: a stale caller, and an unknown project
        var stale = await db.Store.UpdateEvidenceSettingsAsync(
            apollo.Id,
            EvidenceSettings.Default,
            EvidenceSettings.Default with { IncludeFiles = false },
            draft,
            cancellationToken);
        var unknown = await db.Store.UpdateEvidenceSettingsAsync(
            9999, EvidenceSettings.Default, first, draft, cancellationToken);

        // Assert
        Assert.False(stale);
        Assert.False(unknown);
        Assert.Equal(first, (await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.Evidence);
        Assert.Single(
            await db.Audit.ListAsync(null, null, null, null, 100, cancellationToken),
            e => e.Action == EvidenceSettingsService.ChangedAction);
    }
}
