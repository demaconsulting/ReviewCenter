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
///     Tests for the checklist definition of a project in <see cref="DemaConsulting.ReviewCenter.Storage.ReviewStore"/>
///     against a real SQLite database file: the column, the compare-and-set update with its audit entry, and the
///     race between two writers.
/// </summary>
public class ProjectChecklistStoreTests
{
    /// <summary>
    ///     The creation time used for stored rows.
    /// </summary>
    private static readonly DateTimeOffset _created = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Builds the JSON text of a definition with one section and one check.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <returns>The JSON text.</returns>
    private static string Custom(string version)
    {
        return new ChecklistDefinition(
            version,
            [new ChecklistSection("s", "Section", string.Empty, [new ChecklistItem("s-01", "Check")])]).ToJson();
    }

    /// <summary>
    ///     Test that a new project has no definition of its own, and that a project added with one keeps it.
    /// </summary>
    [Fact]
    public async Task ReviewStore_AddProjectAsync_ChecklistDefinition_IsNullByDefaultAndKeptWhenGiven()
    {
        // Arrange, Act
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var plain = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var custom = await db.Store.AddProjectAsync(
            new Project { Name = "Gemini", Created = _created, ChecklistDefinition = Custom("g-1") }, cancellationToken);

        // Assert
        Assert.Null((await db.Store.GetProjectAsync(plain.Id, cancellationToken))!.ChecklistDefinition);
        Assert.Equal(Custom("g-1"), (await db.Store.GetProjectAsync(custom.Id, cancellationToken))!.ChecklistDefinition);
    }

    /// <summary>
    ///     Test that a definition of the full size limit round-trips unchanged through the column, which is not
    ///     bounded, and that it survives a reload through the list of projects.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateChecklistDefinitionAsync_LargeText_RoundTripsThroughTheColumn()
    {
        // Arrange: a text of the maximum size, with non-ASCII characters
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var json = "{\"v\":\"\u00e4\u00f6\u00fc\"}" + new string('x', (int)ReviewLimits.MaxChecklistBytes - 100);

        // Act
        var stored = await db.Store.UpdateChecklistDefinitionAsync(
            apollo.Id,
            ProjectChecklistService.HashOf(null),
            json,
            AuditDraft.By(TestActors.Ann, ProjectChecklistService.ChangedAction, "x", apollo.Id),
            cancellationToken);

        // Assert
        Assert.True(stored);
        Assert.Equal(json, (await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.ChecklistDefinition);
        Assert.Equal(json, (await db.Store.ListProjectsAsync(cancellationToken)).Single(p => p.Id == apollo.Id).ChecklistDefinition);
    }

    /// <summary>
    ///     Test that a replace and a reset are persisted for their own project only, survive a reload, and write
    ///     one audit entry each, of the project, whose chain still verifies.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateChecklistDefinitionAsync_ChangeAndReset_ArePersistedAndAudited()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var gemini = await db.Store.AddProjectAsync(new Project { Name = "Gemini", Created = _created }, cancellationToken);
        var draft = AuditDraft.By(TestActors.Ann, ProjectChecklistService.ChangedAction, "x", apollo.Id);

        // Act: replace the default, then reset
        var replaced = await db.Store.UpdateChecklistDefinitionAsync(
            apollo.Id, ProjectChecklistService.HashOf(null), Custom("a-1"), draft, cancellationToken);
        var afterReplace = (await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.ChecklistDefinition;
        var reset = await db.Store.UpdateChecklistDefinitionAsync(
            apollo.Id, ProjectChecklistService.HashOf(Custom("a-1")), null, draft, cancellationToken);

        // Assert
        Assert.True(replaced);
        Assert.Equal(Custom("a-1"), afterReplace);
        Assert.True(reset);
        Assert.Null((await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.ChecklistDefinition);
        Assert.Null((await db.Store.GetProjectAsync(gemini.Id, cancellationToken))!.ChecklistDefinition);
        var entries = (await db.Audit.ListAsync(apollo.Id, null, null, null, 100, cancellationToken))
            .Where(e => e.Action == ProjectChecklistService.ChangedAction)
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(apollo.Id, e.ProjectId));
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that a change based on a hash that no longer matches, and a change to a project that does not exist,
    ///     change nothing and write no audit entry, and that the chain still verifies.
    /// </summary>
    [Fact]
    public async Task ReviewStore_UpdateChecklistDefinitionAsync_ExpectedDiffers_ChangesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var draft = AuditDraft.By(TestActors.Ann, ProjectChecklistService.ChangedAction, "x", apollo.Id);
        Assert.True(await db.Store.UpdateChecklistDefinitionAsync(
            apollo.Id, ProjectChecklistService.HashOf(null), Custom("a-1"), draft, cancellationToken));

        // Act: a stale caller (and one with a made-up hash), and an unknown project
        var stale = await db.Store.UpdateChecklistDefinitionAsync(
            apollo.Id, ProjectChecklistService.HashOf(null), Custom("a-2"), draft, cancellationToken);
        var madeUp = await db.Store.UpdateChecklistDefinitionAsync(
            apollo.Id, "not-a-hash", Custom("a-2"), draft, cancellationToken);
        var unknown = await db.Store.UpdateChecklistDefinitionAsync(
            9999, ProjectChecklistService.HashOf(null), Custom("a-2"), draft, cancellationToken);

        // Assert
        Assert.False(stale);
        Assert.False(madeUp);
        Assert.False(unknown);
        Assert.Equal(Custom("a-1"), (await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.ChecklistDefinition);
        Assert.Single(
            await db.Audit.ListAsync(null, null, null, null, 100, cancellationToken),
            e => e.Action == ProjectChecklistService.ChangedAction);
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }

    /// <summary>
    ///     Test that of two writers that start from the same stored definition at the same time exactly one wins
    ///     and the other is told it lost, at the store and through the service, with one audit entry and an intact
    ///     chain.
    /// </summary>
    /// <param name="throughService">Whether the writers call the service, or the store.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewStore_UpdateChecklistDefinitionAsync_ConcurrentWriters_ExactlyOneWins(bool throughService)
    {
        // Arrange: two writers with the same expected hash and different new definitions, released together
        var cancellationToken = TestContext.Current.CancellationToken;
        using var db = new TestStore();
        var apollo = await db.Store.AddProjectAsync(new Project { Name = "Apollo", Created = _created }, cancellationToken);
        var service = new ProjectChecklistService(db.Store, new AllowAllProjectAccess(db.Store));
        var hash = ProjectChecklistService.HashOf(null);
        var start = new TaskCompletionSource();

        async Task<bool> WriteAsync(string version)
        {
            await start.Task;
            if (throughService)
            {
                try
                {
                    return await service.SetAsync(TestActors.Ann, apollo.Id, hash, Custom(version), cancellationToken);
                }
                catch (DomainRuleException ex) when (ex.Rule == DomainRule.RecordChanged)
                {
                    return false;
                }
            }

            return await db.Store.UpdateChecklistDefinitionAsync(
                apollo.Id,
                hash,
                Custom(version),
                AuditDraft.By(TestActors.Ann, ProjectChecklistService.ChangedAction, version, apollo.Id),
                cancellationToken);
        }

        var first = Task.Run(() => WriteAsync("w-1"), cancellationToken);
        var second = Task.Run(() => WriteAsync("w-2"), cancellationToken);

        // Act
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        // Assert: one winner, whose definition is stored, one entry, and an intact chain
        Assert.Single(results, won => won);
        var stored = (await db.Store.GetProjectAsync(apollo.Id, cancellationToken))!.ChecklistDefinition;
        Assert.Equal(results[0] ? Custom("w-1") : Custom("w-2"), stored);
        Assert.Single(
            await db.Audit.ListAsync(apollo.Id, null, null, null, 100, cancellationToken),
            e => e.Action == ProjectChecklistService.ChangedAction);
        Assert.True(AuditChain.Verify(await db.Audit.ReadAfterAsync(0, 100, cancellationToken)).IsIntact);
    }
}
