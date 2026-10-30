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

using System.Security.Cryptography;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="AttestationService"/> over in-memory stores: when an attestation is available,
///     what it contains, and what is recorded in the audit trail.
/// </summary>
public class AttestationServiceTests
{
    /// <summary>
    ///     Reopens the approved review of the fixture as cyd and signs it again, so it is approved a second time.
    /// </summary>
    /// <param name="fixture">The fixture, signed.</param>
    /// <param name="reason">The reason of the reopen.</param>
    /// <returns>The digest the first approval was for.</returns>
    private static async Task<string> ReopenAndResignAsync(AttestationFixture fixture, string reason)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var previous = (await fixture.Signing.GetApprovalAsync(fixture.ProjectId, AttestationFixture.ReviewId, cancellationToken))!.Digest;
        await fixture.Signing.ReopenReviewAsync(new Actor("cyd", "Cyd"), fixture.ProjectId, AttestationFixture.ReviewId, reason, cancellationToken);
        await fixture.SignAsync();
        return previous;
    }

    /// <summary>
    ///     Test that the attestation of a review that was reopened and approved again holds the reopen history:
    ///     who reopened, why, and the digest that was superseded.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_ReopenedReview_IncludesReopenHistory()
    {
        // Arrange: an approved review that was reopened and signed again
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        var previous = await ReopenAndResignAsync(fixture, "Missed a case.");
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: the section with the reason, the administrator and the previous digest
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var text = string.Join("\n", new PdfInspector(result.Content!).TextLines);
        Assert.Contains("Reopen history", text, StringComparison.Ordinal);
        Assert.Contains("Missed a case.", text, StringComparison.Ordinal);
        Assert.Contains("Cyd (cyd)", text, StringComparison.Ordinal);
        Assert.Contains(previous, text.Replace("\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the attestation of a review that was never reopened has no reopen section.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_NeverReopened_OmitsReopenHistory()
    {
        // Arrange: an approved review that was never reopened
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.Ok, result.Status);
        Assert.DoesNotContain("Reopen history", string.Join("\n", new PdfInspector(result.Content!).TextLines), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that with reviewer names left out the administrator who reopened is named by a label only.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_ReopenedWithoutReviewerNames_OmitsReopenedByName()
    {
        // Arrange: a reopened and re-approved review in a project that leaves reviewer names out
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        await fixture.Store.UpdateEvidenceSettingsAsync(
            fixture.ProjectId,
            EvidenceSettings.Default,
            EvidenceSettings.Default with { IncludeReviewerNames = false },
            AuditTestExtensions.Draft(),
            cancellationToken);
        await ReopenAndResignAsync(fixture, "Missed a case.");
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, cancellationToken);

        // Assert: the section is there, with the reason but without the administrator's names
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var text = string.Join("\n", new PdfInspector(result.Content!).TextLines);
        Assert.Contains("Reopen history", text, StringComparison.Ordinal);
        Assert.Contains("Missed a case.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cyd", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cyd", text, StringComparison.Ordinal);
        Assert.Contains("Participant", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the attestation of an approved (and so locked) review is still produced.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_LockedReview_StillRenders()
    {
        // Arrange: an approved review, which is locked
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        var locked = await Assert.ThrowsAsync<DomainRuleException>(() => fixture.Checklists.SetAnswerAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, "REQ-01", ChecklistOutcome.Fail, "No.", TestActors.Ann, cancellationToken));
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, cancellationToken);

        // Assert: the review is locked, and the signed PDF is delivered anyway
        Assert.Equal(DomainRule.ReviewLocked, locked.Rule);
        Assert.Equal(AttestationStatus.Ok, result.Status);
        Assert.NotNull(new PdfInspector(result.Content!).VerifySignature());
    }

    /// <summary>
    ///     Test that a review that does not exist has no attestation and leaves no audit entry.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_UnknownReview_ReturnsNotFound()
    {
        // Arrange: a fixture and a key
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act: ask for a review that does not exist
        var result = await service.GenerateAsync(fixture.ProjectId, "nope", TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: not found, nothing recorded, the key was not needed
        Assert.Equal(AttestationStatus.NotFound, result.Status);
        Assert.Null(result.Content);
        Assert.Empty(await AttestationAuditAsync(fixture));
        Assert.Equal(0, keys.Requests);
    }

    /// <summary>
    ///     Test that a review whose slot has not signed has no attestation.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_SlotNotSigned_ReturnsNotApprovedWithoutAskingForTheKey()
    {
        // Arrange: a complete record that nobody has signed
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: not approved, with the reason, nothing recorded
        Assert.Equal(AttestationStatus.NotApproved, result.Status);
        Assert.Null(result.Content);
        Assert.Contains("not complete", result.Message, StringComparison.Ordinal);
        Assert.Empty(await AttestationAuditAsync(fixture));
        Assert.Equal(0, keys.Requests);
    }

    /// <summary>
    ///     Test that a request without an actor gets the answer of a review that does not exist when the review is not
    ///     complete, so it cannot be used to list review identifiers or their state, while a signed-in caller keeps
    ///     the informative not-approved answer; nothing is recorded and the key is not asked for.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_AnonymousIncompleteReview_ReturnsNotFoundLikeMissing()
    {
        // Arrange: an incomplete review in a project that allows anonymous attestation PDFs
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);
        Assert.True(await fixture.Store.UpdateEvidenceSettingsAsync(
            fixture.ProjectId,
            EvidenceSettings.Default,
            EvidenceSettings.Default with { AnonymousAttestations = true },
            AuditDraft.By(TestActors.Ann, EvidenceSettingsService.ChangedAction, "x", fixture.ProjectId),
            TestContext.Current.CancellationToken));

        // Act: an anonymous request for the incomplete review and for a review that does not exist, and a member's request
        var incomplete = await service.GenerateAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, null, TestContext.Current.CancellationToken);
        var missing = await service.GenerateAsync(fixture.ProjectId, "nope", null, TestContext.Current.CancellationToken);
        var asMember = await service.GenerateAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: the two anonymous answers are the same, nothing was recorded, the key was not needed, and the
        // member is still told that the review is not complete
        Assert.Equal(AttestationStatus.NotFound, incomplete.Status);
        Assert.Equal(missing, incomplete);
        Assert.Equal("The review was not found.", incomplete.Message);
        Assert.Null(incomplete.Content);
        Assert.Equal(AttestationStatus.NotApproved, asMember.Status);
        Assert.Empty(await AttestationAuditAsync(fixture));
        Assert.Equal(0, keys.Requests);
    }

    /// <summary>
    ///     Test that a signature of an older record does not make the review complete: after the record changes
    ///     there is no attestation.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RecordChangedAfterSigning_ReturnsNotApproved()
    {
        // Arrange: a signed review whose checklist answers then change
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        await fixture.AnswerAllAsync(ChecklistOutcome.Fail);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: not approved
        Assert.Equal(AttestationStatus.NotApproved, result.Status);
        Assert.Empty(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a record that changes after the document was rendered and before it is signed gives no
    ///     attestation, no PDF and no audit event.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RecordChangesWhileRendering_ReturnsChangedAndRecordsNothing()
    {
        // Arrange: a complete review, and a step between rendering and signing that changes the checklist answers
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys, afterRender: _ => fixture.AnswerAllAsync(ChecklistOutcome.Fail));

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: the changed outcome with its message, nothing signed, nothing recorded
        Assert.Equal(AttestationStatus.Changed, result.Status);
        Assert.Null(result.Content);
        Assert.Null(result.FileName);
        Assert.Equal("The review changed while the attestation was being prepared; try again.", result.Message);
        Assert.Empty(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a slot added while the document is being prepared gives no attestation: the review is no longer
    ///     complete.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_SlotAddedWhileRendering_ReturnsChangedAndRecordsNothing()
    {
        // Arrange: a complete review, and a step that adds a second slot nobody has signed
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(
            keys,
            afterRender: async cancellationToken => await fixture.AddUnsignedSlotAsync(cancellationToken));

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: changed, no PDF, no audit event
        Assert.Equal(AttestationStatus.Changed, result.Status);
        Assert.Null(result.Content);
        Assert.Empty(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a different set of slots gives no attestation even when the record digest is the same and the
    ///     review is still complete: the document would list the wrong signers.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_SlotsChangeWhileRenderingButStillComplete_ReturnsChangedAndRecordsNothing()
    {
        // Arrange: a complete review, and a step that adds a second slot and signs it, so the review stays complete
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys, afterRender: _ => fixture.AddAndSignSecondSlotAsync());

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: the review is still approved, but what was rendered is out of date: changed, no PDF, no audit event
        var approval = await fixture.Signing.GetApprovalAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestContext.Current.CancellationToken);
        Assert.True(approval!.IsApproved);
        Assert.Equal(AttestationStatus.Changed, result.Status);
        Assert.Null(result.Content);
        Assert.Empty(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a review that does not change between the render and the signing is attested after the check,
    ///     and that the check runs exactly once, before the audit event.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_ReviewUnchangedWhileRendering_PassesRevalidationAndIsAttested()
    {
        // Arrange: a complete review, and a step that changes nothing but records that it ran and what was recorded then
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var calls = 0;
        var eventsWhenCalled = -1;
        var service = fixture.CreateService(
            keys,
            afterRender: async _ =>
            {
                calls++;
                eventsWhenCalled = (await AttestationAuditAsync(fixture)).Count;
            });

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: signed and recorded once; the step ran once, before anything was recorded
        Assert.Equal(AttestationStatus.Ok, result.Status);
        Assert.NotNull(result.Content);
        Assert.Equal(1, calls);
        Assert.Equal(0, eventsWhenCalled);
        Assert.Single(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a key problem gives no document, says what is wrong, and records nothing.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_KeyProblem_ReturnsKeyUnavailableAndRecordsNothing()
    {
        // Arrange: a complete review and a provider without a usable key
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider("The key store is locked.");
        var service = fixture.CreateService(keys);

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: key unavailable with the reason, no content, no audit entry
        Assert.Equal(AttestationStatus.KeyUnavailable, result.Status);
        Assert.Null(result.Content);
        Assert.Equal("The key store is locked.", result.Message);
        Assert.Empty(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a failure to sign gives no document and records nothing; an unsigned PDF is never handed out.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_SignerFails_ReturnsKeyUnavailableAndRecordsNothing()
    {
        // Arrange: a complete review and a signer that fails
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys, new FailingSigner());

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: key unavailable with the reason, no content, no audit entry
        Assert.Equal(AttestationStatus.KeyUnavailable, result.Status);
        Assert.Null(result.Content);
        Assert.Equal("The key cannot sign.", result.Message);
        Assert.Empty(await AttestationAuditAsync(fixture));
    }

    /// <summary>
    ///     Test that a complete review gives a signed PDF that holds the review and verifies, with a file name.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_CompleteReview_ReturnsSignedPdfOfTheReview()
    {
        // Arrange: a complete review and a key
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act: ask for the attestation
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: a PDF that holds the review, whose signature verifies, with the file name
        Assert.Equal(AttestationStatus.Ok, result.Status);
        Assert.NotNull(result.Content);
        var pdf = new PdfInspector(result.Content);
        Assert.True(pdf.HasPdfFraming);
        Assert.Contains("Apollo", pdf.Text, StringComparison.Ordinal);
        Assert.Contains("rev-1", pdf.Text, StringComparison.Ordinal);
        Assert.Contains("a.txt", pdf.Text, StringComparison.Ordinal);
        Assert.True(pdf.ByteRangeCoversEverythingButContents());
        Assert.NotNull(pdf.VerifySignature());
        Assert.Equal($"attestation-{fixture.ProjectId}-rev-1.pdf", result.FileName);
        Assert.Equal(1, keys.Requests);
    }

    /// <summary>
    ///     Test that the creation is recorded in the audit trail with the digest, the chain head, the hash of the
    ///     PDF and the key, under the name of the user who asked.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_CompleteReview_RecordsAuditEventWithDigestAndPdfHash()
    {
        // Arrange: a complete review and a key
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var key = TestKeys.SelfSignedRsa();
        var thumbprint = key.Info.Thumbprint;
        using var keys = new FakeAttestationKeyProvider(key);
        var service = fixture.CreateService(keys);
        var digest = (await fixture.Signing.GetApprovalAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestContext.Current.CancellationToken))!.Digest;

        // Act: ask for the attestation as bob
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, TestContext.Current.CancellationToken);

        // Assert: one event, with who, what and the hash of the delivered PDF
        var entry = Assert.Single(await AttestationAuditAsync(fixture));
        var pdfHash = Convert.ToHexString(SHA256.HashData(result.Content!)).ToLowerInvariant();
        Assert.Equal("Bob (bob)", entry.ActorName);
        Assert.Equal(fixture.ProjectId, entry.ProjectId);
        Assert.Equal(AttestationFixture.ReviewId, entry.ReviewId);
        Assert.Contains($"record digest {digest}", entry.Detail, StringComparison.Ordinal);
        Assert.Contains($"pdf sha256 {pdfHash}", entry.Detail, StringComparison.Ordinal);
        Assert.Contains($"key {thumbprint}", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("selfSigned=True", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("chain head ", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every request creates and records a new attestation: nothing is stored.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_AskedTwice_RecordsTwoEvents()
    {
        // Arrange: a complete review and a key
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act: ask twice
        await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);
        await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert: two events
        Assert.Equal(2, (await AttestationAuditAsync(fixture)).Count);
    }

    /// <summary>
    ///     Test that file names contain only safe characters.
    /// </summary>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="expected">The expected file name.</param>
    [Theory]
    [InlineData("rev-1", "attestation-7-rev-1.pdf")]
    [InlineData("REL_1.2", "attestation-7-REL_1.2.pdf")]
    [InlineData("rev 1/../x\"y", "attestation-7-rev-1-..-x-y.pdf")]
    public void AttestationService_FileNameFor_ReviewId_KeepsOnlySafeCharacters(string reviewId, string expected)
    {
        // Arrange: nothing beyond the arguments

        // Act: build the name
        var name = AttestationService.FileNameFor(7, reviewId);

        // Assert: as expected
        Assert.Equal(expected, name);
    }

    /// <summary>
    ///     Test that missing collaborators are refused.
    /// </summary>
    [Fact]
    public async Task AttestationService_Constructor_NullCollaborator_Throws()
    {
        // Arrange: a fixture
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());

        // Act and assert: each null is refused
        var access = fixture.Access;
        Assert.Throws<ArgumentNullException>(() => new AttestationService(null!, fixture.Signing, access, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, null!, access, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, null!, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, access, null!, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, access, fixture.Audit, null!, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, access, fixture.Audit, keys, null!, new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, access, fixture.Audit, keys, new AttestationRenderer(), null!, fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, access, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), null!));
    }

    /// <summary>
    ///     Reads the attestation events of the audit trail.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The events, newest first.</returns>
    private static async Task<IReadOnlyList<AuditEntry>> AttestationAuditAsync(AttestationFixture fixture)
    {
        var page = await fixture.Audit.ListAsync(null, null, null, null, TestContext.Current.CancellationToken);
        return page.Entries.Where(e => e.Action == AttestationService.AuditAction).ToList();
    }

    /// <summary>
    ///     A signer that always fails with a key problem.
    /// </summary>
    private sealed class FailingSigner : IAttestationSigner
    {
        /// <inheritdoc />
        public byte[] Sign(byte[] pdf, AttestationSigningKey key, DateTimeOffset signedAt, string reason)
        {
            throw new AttestationKeyException("The key cannot sign.");
        }
    }
}
