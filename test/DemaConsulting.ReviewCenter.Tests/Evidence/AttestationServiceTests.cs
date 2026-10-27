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
        Assert.Throws<ArgumentNullException>(() => new AttestationService(null!, fixture.Signing, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, null!, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, null!, keys, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, fixture.Audit, null!, new AttestationRenderer(), new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, fixture.Audit, keys, null!, new AttestationSigner(), fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, fixture.Audit, keys, new AttestationRenderer(), null!, fixture.Clock));
        Assert.Throws<ArgumentNullException>(() => new AttestationService(fixture.Store, fixture.Signing, fixture.Audit, keys, new AttestationRenderer(), new AttestationSigner(), null!));
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
