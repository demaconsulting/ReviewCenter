using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Tests of what the evidence settings of a project do to the attestation PDFs that <see cref="AttestationService"/>
///     produces: each content switch, the signature over the redacted document, who may ask without signing in, and
///     the change of settings while a document is prepared.
/// </summary>
public class AttestationContentSettingsTests
{
    /// <summary>
    ///     The people of the sample review that the names switch must hide: user names and display names.
    /// </summary>
    private static readonly Regex _people = new(@"\b(ann|bob|cyd)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(10));

    /// <summary>
    ///     Test that with the default settings the PDF names the reviewers, the wording and the files, has the
    ///     content scope, and still verifies.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_DefaultSettings_IncludesEverything()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var pdf = await world.GenerateAsync(TestActors.Ann);

        // Assert
        var lines = pdf.TextLines;
        Assert.Contains("Content scope", lines);
        Assert.Contains("Nothing is left out of this document.", lines);
        Assert.Contains("Ann (ann)", lines);
        Assert.Contains(lines, l => l.Contains("Cyd", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("a.txt", StringComparison.Ordinal));
        Assert.Contains("Comment threads", lines);
        Assert.True(pdf.ByteRangeCoversEverythingButContents());
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that turning the names off replaces every person in the PDF with a label from the person's seat, names
    ///     the requester only as a signed-in user, and still gives a signature that verifies over the redacted PDF.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_NamesOff_ReplacesPeopleWithLabelsAndStillVerifies()
    {
        // Arrange
        var world = await WorldAsync(EvidenceSettings.Default with { IncludeReviewerNames = false });

        // Act
        var pdf = await world.GenerateAsync(TestActors.Bob);

        // Assert: no display name and no user name anywhere in the text
        var lines = pdf.TextLines;
        var text = string.Join("\n", lines);
        Assert.DoesNotMatch(_people, text);

        // Assert: the labels, and the always-included parts
        Assert.Contains("Reviewer 1 (primary)", lines);
        Assert.Contains("Reviewer 1 (alternate)", lines);
        Assert.Contains(lines, l => l.StartsWith("Participant 1", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("by a signed-in user", StringComparison.Ordinal));
        Assert.Contains("Left out of this document: Reviewer names.", lines);
        Assert.Contains("Reviewer sign-offs", lines);

        // Assert: the signature was made after the redaction and verifies over the delivered bytes
        Assert.True(pdf.ByteRangeCoversEverythingButContents());
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that turning the threads off removes the thread section and the comment text, and the signature verifies.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_ThreadsOff_OmitsThreadSectionAndStillVerifies()
    {
        // Arrange
        var world = await WorldAsync(EvidenceSettings.Default with { IncludeThreads = false });

        // Act
        var pdf = await world.GenerateAsync(TestActors.Ann);

        // Assert
        var lines = pdf.TextLines;
        Assert.DoesNotContain("Comment threads", lines);
        Assert.DoesNotContain(lines, l => l.Contains(SampleData.ThreadText, StringComparison.Ordinal));
        Assert.Contains("Left out of this document: Review discussion threads.", lines);
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that turning the files off removes the file list and hashes, and the signature verifies.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_FilesOff_OmitsFileListAndStillVerifies()
    {
        // Arrange
        var world = await WorldAsync(EvidenceSettings.Default with { IncludeFiles = false });

        // Act
        var pdf = await world.GenerateAsync(TestActors.Ann);

        // Assert: the thread still names its file (a thread is the reviewers' own text), the list is gone
        var lines = pdf.TextLines;
        Assert.DoesNotContain(lines, l => l.StartsWith("Files (", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("SHA-256", StringComparison.Ordinal));
        Assert.Contains("Left out of this document: File list and hashes.", lines);
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that turning the checklist wording off removes the question text and keeps identifiers and outcomes.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_ChecklistWordingOff_OmitsQuestionTextAndStillVerifies()
    {
        // Arrange
        var world = await WorldAsync(EvidenceSettings.Default with { IncludeChecklistWording = false });
        var firstItem = DefaultChecklist.Current.Sections.SelectMany(s => s.Items).First();

        // Act
        var pdf = await world.GenerateAsync(TestActors.Ann);

        // Assert
        var text = string.Join(" ", pdf.TextLines);
        Assert.DoesNotContain(firstItem.Text[..30], text, StringComparison.Ordinal);
        Assert.Contains($"{firstItem.Id} Pass", pdf.TextLines);
        Assert.Contains("Left out of this document: Checklist wording.", pdf.TextLines);
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that a request without an actor is refused as not found while the project does not allow anonymous
    ///     attestation PDFs, and that nothing is recorded.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_AnonymousToggleOff_ReturnsNotFound()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var result = await world.Service.GenerateAsync(
            world.Fixture.ProjectId, AttestationFixture.ReviewId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.NotFound, result.Status);
        Assert.Null(result.Content);
        Assert.DoesNotContain(
            (await world.Fixture.Audit.ListAsync(null, null, null, null, TestContext.Current.CancellationToken)).Entries,
            e => e.Action == AttestationService.AuditAction);
    }

    /// <summary>
    ///     Test that a request without an actor gets the PDF when the project allows it, that the PDF says the
    ///     request was anonymous, and that the audit entry names the actor as anonymous.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_AnonymousToggleOn_ReturnsPdfAndAuditsAnonymous()
    {
        // Arrange
        var world = await WorldAsync(EvidenceSettings.Default with { AnonymousAttestations = true });

        // Act
        var result = await world.Service.GenerateAsync(
            world.Fixture.ProjectId, AttestationFixture.ReviewId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var pdf = new PdfInspector(result.Content!);
        var lines = pdf.TextLines;
        Assert.Contains(
            "without signing in (the project allows anonymous access to attestation PDFs)", lines);
        Assert.Contains(lines, l => l.EndsWith("by an anonymous request", StringComparison.Ordinal));
        Assert.NotNull(pdf.VerifySignature());
        var entry = Assert.Single(
            (await world.Fixture.Audit.ListAsync(null, null, null, null, TestContext.Current.CancellationToken)).Entries,
            e => e.Action == AttestationService.AuditAction);
        Assert.Equal("anonymous", entry.ActorName);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(world.Fixture.ProjectId, entry.ProjectId);
    }

    /// <summary>
    ///     Test that a request without an actor to a project that does not exist is not found, also when the
    ///     anonymous toggle of another project is on.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_AnonymousUnknownProject_ReturnsNotFound()
    {
        // Arrange
        var world = await WorldAsync(EvidenceSettings.Default with { AnonymousAttestations = true });

        // Act: a project that does not exist
        var result = await world.Service.GenerateAsync(
            9999, AttestationFixture.ReviewId, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.NotFound, result.Status);
    }

    /// <summary>
    ///     Test that settings that change while the document is prepared give the changed result, so a document
    ///     never mixes two settings, and nothing is recorded.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_SettingsChangeWhileRendering_ReturnsChanged()
    {
        // Arrange
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(
            keys,
            afterRender: async ct => await fixture.Store.UpdateEvidenceSettingsAsync(
                fixture.ProjectId,
                EvidenceSettings.Default,
                EvidenceSettings.Default with { IncludeThreads = false },
                AuditDraft.By(TestActors.Ann, EvidenceSettingsService.ChangedAction, "x", fixture.ProjectId),
                ct));

        // Act
        var result = await service.GenerateAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.Changed, result.Status);
        Assert.Null(result.Content);
        Assert.DoesNotContain(
            (await fixture.Audit.ListAsync(null, null, null, null, TestContext.Current.CancellationToken)).Entries,
            e => e.Action == AttestationService.AuditAction);
    }

    /// <summary>
    ///     Arranges a signed review in which cyd, who holds no seat, answered a check and resolved a thread.
    /// </summary>
    /// <param name="settings">The evidence settings of the project; the defaults when <c>null</c>.</param>
    /// <returns>The arranged world.</returns>
    private static async Task<World> WorldAsync(EvidenceSettings? settings = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        var cyd = new Actor("cyd", "Cyd");
        var firstItem = DefaultChecklist.Current.Sections.SelectMany(s => s.Items).First();
        await fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, firstItem.Id, ChecklistOutcome.Pass, null, cyd, cancellationToken);
        var threads = TestServices.Threads(fixture.Store, fixture.Clock);
        var thread = await threads.StartThreadAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, "a.txt", null, null, cyd, SampleData.ThreadText, ThreadSide.Head, cancellationToken);
        await threads.ResolveAsync(fixture.ProjectId, AttestationFixture.ReviewId, thread.Id, cyd, cancellationToken);
        await fixture.SignAsync();
        if (settings is not null)
        {
            Assert.True(await fixture.Store.UpdateEvidenceSettingsAsync(
                fixture.ProjectId,
                EvidenceSettings.Default,
                settings,
                AuditDraft.By(TestActors.Ann, EvidenceSettingsService.ChangedAction, "x", fixture.ProjectId),
                cancellationToken));
        }

        var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        return new World(fixture, fixture.CreateService(keys), keys);
    }

    /// <summary>
    ///     Constants of the arranged review.
    /// </summary>
    private static class SampleData
    {
        /// <summary>
        ///     The text of the first comment of the resolved thread.
        /// </summary>
        public const string ThreadText = "Please double check the loop bound";
    }

    /// <summary>
    ///     The arranged fixture and service.
    /// </summary>
    /// <param name="Fixture">The fixture with the review.</param>
    /// <param name="Service">The attestation service.</param>
    /// <param name="Keys">The key provider, kept so that it lives as long as the world.</param>
    private sealed record World(AttestationFixture Fixture, AttestationService Service, FakeAttestationKeyProvider Keys)
    {
        /// <summary>
        ///     Creates the attestation as a user and returns the inspector over it.
        /// </summary>
        /// <param name="actor">The user who asks.</param>
        /// <returns>The inspector over the PDF.</returns>
        public async Task<PdfInspector> GenerateAsync(Actor actor)
        {
            var result = await Service.GenerateAsync(
                Fixture.ProjectId, AttestationFixture.ReviewId, actor, TestContext.Current.CancellationToken);
            Assert.Equal(AttestationStatus.Ok, result.Status);
            return new PdfInspector(result.Content!);
        }
    }
}
