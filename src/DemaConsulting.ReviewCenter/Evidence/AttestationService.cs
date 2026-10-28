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

using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Creates the signed PDF attestation of a completed review, on demand.
/// </summary>
/// <remarks>
///     An attestation is available only when the review is complete: every reviewer slot has a signature of the
///     record as it is now (the existing completion rule of <see cref="SigningService"/>). Nothing is stored; the
///     PDF is built, signed and returned each time, and each creation is recorded in the audit trail. A key problem
///     never produces an unsigned or partly signed document and is not recorded as an attestation. Because reading,
///     verifying the audit chain, and rendering take time, the review is read again after rendering and before
///     signing; when it is no longer complete or no longer the same, nothing is signed or recorded. The service is
///     thread-safe.
/// </remarks>
internal sealed class AttestationService
{
    /// <summary>
    ///     The longest first comment, in characters, that the summary of a resolved thread keeps; the summary ends with
    ///     an ellipsis when it was cut. It applies only to resolved-thread summaries, never to checklist comments.
    /// </summary>
    private const int SummaryLength = 300;

    /// <summary>
    ///     The name of the audit event that records the creation of an attestation.
    /// </summary>
    public const string AuditAction = "attestation.generated";

    /// <summary>
    ///     The review store.
    /// </summary>
    private readonly IReviewStore _store;

    /// <summary>
    ///     The signing rules, which decide whether a review is complete.
    /// </summary>
    private readonly SigningService _signing;

    /// <summary>
    ///     Decides who may see which project.
    /// </summary>
    private readonly IProjectAccess _access;

    /// <summary>
    ///     The audit trail.
    /// </summary>
    private readonly AuditService _audit;

    /// <summary>
    ///     The supplier of the signing key.
    /// </summary>
    private readonly IAttestationKeyProvider _keys;

    /// <summary>
    ///     The PDF renderer.
    /// </summary>
    private readonly IAttestationRenderer _renderer;

    /// <summary>
    ///     The PDF signer.
    /// </summary>
    private readonly IAttestationSigner _signer;

    /// <summary>
    ///     The clock.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AttestationService"/> class.
    /// </summary>
    /// <param name="store">The review store. Must not be null.</param>
    /// <param name="signing">The signing service. Must not be null.</param>
    /// <param name="access">The access rules. Must not be null.</param>
    /// <param name="audit">The audit service. Must not be null.</param>
    /// <param name="keys">The signing key provider. Must not be null.</param>
    /// <param name="renderer">The renderer. Must not be null.</param>
    /// <param name="signer">The signer. Must not be null.</param>
    /// <param name="time">The clock. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public AttestationService(
        IReviewStore store,
        SigningService signing,
        IProjectAccess access,
        AuditService audit,
        IAttestationKeyProvider keys,
        IAttestationRenderer renderer,
        IAttestationSigner signer,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signing);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(time);
        _store = store;
        _signing = signing;
        _access = access;
        _audit = audit;
        _keys = keys;
        _renderer = renderer;
        _signer = signer;
        _time = time;
    }

    /// <summary>
    ///     Gets a step that runs after the document is rendered and before the review is checked again; it exists
    ///     only so that a test can change the review in that moment. It is <c>null</c> in production.
    /// </summary>
    internal Func<CancellationToken, Task>? AfterRender { get; init; }

    /// <summary>
    ///     Creates the signed attestation of a review.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="actor">The user who asks for it. Must not be null.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     The result: the signed PDF when the review is complete and the key is usable; otherwise why not
    ///     (including <see cref="AttestationStatus.Changed"/> when the review changed while the document was being
    ///     prepared). Only the success is recorded in the audit trail.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reviewId"/> or <paramref name="actor"/> is null.</exception>
    public async Task<AttestationResult> GenerateAsync(
        int projectId,
        string reviewId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewId);
        ArgumentNullException.ThrowIfNull(actor);

        // Whoever may not see the project gets the same answer as for a review that does not exist
        if (!await _access.CanViewAsync(actor, projectId, cancellationToken))
        {
            return new AttestationResult(AttestationStatus.NotFound, null, null, "The review was not found.");
        }

        // Availability first: the review must exist and be complete
        var approval = await _signing.GetApprovalAsync(projectId, reviewId, cancellationToken);
        if (approval is null)
        {
            return new AttestationResult(AttestationStatus.NotFound, null, null, "The review was not found.");
        }

        if (!approval.IsApproved)
        {
            return new AttestationResult(AttestationStatus.NotApproved, null, null, "The review is not complete: not every reviewer slot has signed the current record.");
        }

        // Then the key: without a usable key there is nothing to produce, and nothing is recorded
        AttestationSigningKey key;
        try
        {
            key = _keys.GetSigningKey();
        }
        catch (AttestationKeyException ex)
        {
            return new AttestationResult(AttestationStatus.KeyUnavailable, null, null, ex.Message);
        }

        // Read the review once more for the content; the digest must still be the one that was approved
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        if (snapshot is null || review is null || project is null)
        {
            return new AttestationResult(AttestationStatus.NotFound, null, null, "The review was not found.");
        }

        // The approval and the snapshot are two reads: they must describe the same record and the same slots
        if (RecordDigest.Compute(snapshot.Record) != approval.Digest || !SameSlots(approval, snapshot))
        {
            return ChangedResult();
        }

        // The whole chain is verified on every request: the document states whether it is intact, and the check is
        // linear in the number of audit entries
        var chain = await _audit.VerifyAsync(cancellationToken);
        var now = _time.GetUtcNow();
        var document = BuildDocument(project, review, snapshot, approval, chain, key.Info, actor, now);
        var unsigned = _renderer.Render(document);

        // The reads above and the render take time, so check again that the review is still what was rendered
        if (AfterRender is not null)
        {
            await AfterRender(cancellationToken);
        }

        if (!await IsUnchangedAsync(projectId, reviewId, approval.Digest, snapshot, cancellationToken))
        {
            return ChangedResult();
        }

        // Sign; a failure to sign throws a key exception and returns nothing
        byte[] signed;
        try
        {
            signed = _signer.Sign(unsigned, key, now, $"Review {reviewId} is complete and signed by all reviewer slots");
        }
        catch (AttestationKeyException ex)
        {
            return new AttestationResult(AttestationStatus.KeyUnavailable, null, null, ex.Message);
        }

        // The event is recorded strictly: an attestation that cannot be traced is not handed out. It records the
        // digest that was attested.
        var hash = Convert.ToHexString(SHA256.HashData(signed)).ToLowerInvariant();
        await _audit.RecordAsync(
            AuditDraft.By(
                actor,
                AuditAction,
                $"record digest {approval.Digest}, chain head {chain.HeadHash}, pdf sha256 {hash}, key {key.Info.Thumbprint}, selfSigned={key.Info.IsSelfSigned}",
                projectId,
                reviewId),
            cancellationToken);
        return new AttestationResult(AttestationStatus.Ok, signed, FileNameFor(projectId, reviewId), null);
    }

    /// <summary>
    ///     Builds the answer for a review that changed while the attestation was being prepared.
    /// </summary>
    /// <returns>The result with the status <see cref="AttestationStatus.Changed"/>.</returns>
    private static AttestationResult ChangedResult()
    {
        return new AttestationResult(
            AttestationStatus.Changed,
            null,
            null,
            "The review changed while the attestation was being prepared; try again.");
    }

    /// <summary>
    ///     Checks, from fresh reads, that the review is still complete and is still exactly what was rendered.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="digest">The record digest that was approved and rendered.</param>
    /// <param name="rendered">The snapshot the document was built from.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     <c>true</c> when the review is still approved with the same record digest, and has the same slots and the
    ///     same signatures that count as the rendered one.
    /// </returns>
    private async Task<bool> IsUnchangedAsync(
        int projectId,
        string reviewId,
        string digest,
        ReviewSnapshot rendered,
        CancellationToken cancellationToken)
    {
        var approval = await _signing.GetApprovalAsync(projectId, reviewId, cancellationToken);
        if (approval is null || !approval.IsApproved || approval.Digest != digest)
        {
            return false;
        }

        var current = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        return current is not null
               && RecordDigest.Compute(current.Record) == digest
               && SlotSignature(current, digest) == SlotSignature(rendered, digest);
    }

    /// <summary>
    ///     Checks that the approval and the snapshot describe the same slots with the same members.
    /// </summary>
    /// <param name="approval">The approval status.</param>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns><c>true</c> when both have the same slots, in the same order, with the same primary and alternate.</returns>
    private static bool SameSlots(ReviewApproval approval, ReviewSnapshot snapshot)
    {
        return approval.Slots
            .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.SlotId}:{s.Primary.UserId}:{s.Alternate?.UserId}"))
            .SequenceEqual(snapshot.Slots
                .OrderBy(s => s.Id)
                .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Id}:{s.PrimaryUserId}:{s.AlternateUserId}")));
    }

    /// <summary>
    ///     Describes the slots of a snapshot and the signature that counts for each, so that two snapshots can be compared.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="digest">The record digest the signatures must bind to.</param>
    /// <returns>One text that is equal for two snapshots exactly when their slots and counting signatures are equal.</returns>
    private static string SlotSignature(ReviewSnapshot snapshot, string digest)
    {
        return string.Join(
            ";",
            snapshot.Slots.OrderBy(s => s.Id).Select(slot =>
            {
                var signature = snapshot.Signatures.Where(s => s.SlotId == slot.Id && s.RecordDigest == digest).MaxBy(s => s.Id);
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{slot.Id}:{slot.PrimaryUserId}:{slot.AlternateUserId}:{signature?.Id}:{signature?.UserId}");
            }));
    }

    /// <summary>
    ///     Gets the suggested file name of an attestation.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier; characters other than letters, digits, dot, underscore and hyphen become hyphens.</param>
    /// <returns>The file name, such as <c>attestation-3-rev-1.pdf</c>.</returns>
    public static string FileNameFor(int projectId, string reviewId)
    {
        ArgumentNullException.ThrowIfNull(reviewId);
        var safe = new string(reviewId.Select(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-' ? c : '-').ToArray());
        return $"attestation-{projectId.ToString(CultureInfo.InvariantCulture)}-{safe}.pdf";
    }

    /// <summary>
    ///     Gathers everything the attestation shows.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="review">The review with its files.</param>
    /// <param name="snapshot">The record, slots and signatures.</param>
    /// <param name="approval">The approval status, which names the slot members.</param>
    /// <param name="chain">The result of verifying the audit chain.</param>
    /// <param name="key">The description of the signing key.</param>
    /// <param name="actor">The user who asked.</param>
    /// <param name="now">The time of generation.</param>
    /// <returns>The document.</returns>
    private static AttestationDocument BuildDocument(
        Project project,
        Review review,
        ReviewSnapshot snapshot,
        ReviewApproval approval,
        AuditVerificationResult chain,
        AttestationKeyInfo key,
        Actor actor,
        DateTimeOffset now)
    {
        var record = snapshot.Record;
        var digestVersion = RecordDigest.Canonicalize(record).Split('\n', 2)[0];
        var threads = record.Threads.ToList();
        return new AttestationDocument(
            project.Id,
            project.Name,
            review.Id,
            review.Title,
            review.RepositoryName,
            review.BaselineSha,
            review.HeadSha,
            review.ReviewSetId,
            record.ContentFingerprint,
            approval.Digest,
            digestVersion,
            review.Files
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .Select(f => new AttestationFile(f.Path, review.IsFromRepository ? f.ChangeKind.ToString() : "Uploaded", f.Sha256, f.Size))
                .ToList(),
            record.Checklist?.Version,
            BuildSections(record),
            threads.Count(t => t.State == ReviewThreadState.Open),
            threads
                .Where(t => t.State == ReviewThreadState.Resolved)
                .Select(SummarizeThread)
                .ToList(),
            BuildSlots(snapshot, approval, digestVersion),
            chain.HeadHash,
            chain.Count,
            chain.IsIntact,
            key,
            Program.Version,
            now,
            $"{actor.DisplayName} ({actor.Username})");
    }

    /// <summary>
    ///     Gathers the checklist sections with their answers.
    /// </summary>
    /// <param name="record">The review record.</param>
    /// <returns>The sections; empty when the review has no checklist.</returns>
    private static List<AttestationSection> BuildSections(ReviewRecord record)
    {
        if (record.Checklist is null)
        {
            return [];
        }

        var state = new ChecklistState(record.Checklist.Parse(), record.SectionStates, record.Answers);
        return state.Definition.Sections
            .Select(section => new AttestationSection(
                section.Title,
                state.IsApplicable(section.Id),
                state.ReasonFor(section.Id),
                section.Items.Select(item =>
                {
                    var answer = state.AnswerFor(item.Id);
                    return new AttestationCheck(
                        item.Id,
                        item.Text,
                        answer is null ? "Not answered" : OutcomeText(answer.Outcome),
                        answer?.Justification,
                        answer?.AnsweredBy);
                }).ToList()))
            .ToList();
    }

    /// <summary>
    ///     Names a checklist outcome as the attestation prints it.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns><c>Pass</c>, <c>Fail</c> or <c>N/A</c>.</returns>
    private static string OutcomeText(ChecklistOutcome outcome)
    {
        return outcome switch
        {
            ChecklistOutcome.Pass => "Pass",
            ChecklistOutcome.Fail => "Fail",
            _ => "N/A"
        };
    }

    /// <summary>
    ///     Summarizes a resolved thread: where it is and what was first said.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <returns>The summary.</returns>
    private static AttestationThread SummarizeThread(ReviewThread thread)
    {
        var lines = string.Empty;
        if (thread.StartLine is not null)
        {
            var start = thread.StartLine.Value.ToString(CultureInfo.InvariantCulture);
            lines = thread.EndLine is not null && thread.EndLine != thread.StartLine
                ? $", lines {start}-{thread.EndLine.Value.ToString(CultureInfo.InvariantCulture)}"
                : $", line {start}";
        }
        var first = thread.Comments.OrderBy(c => c.Created).FirstOrDefault();
        var text = first is null ? "(no comments)" : $"{first.Author}: {first.Text}";
        return new AttestationThread(
            thread.FilePath + lines,
            text.Length <= SummaryLength ? text : text[..SummaryLength] + "...",
            thread.Comments.Count);
    }

    /// <summary>
    ///     Gathers the reviewer slots with the signatures that approve the current record.
    /// </summary>
    /// <param name="snapshot">The record, slots and signatures.</param>
    /// <param name="approval">The approval status, which names the members of each slot.</param>
    /// <param name="digestVersion">The version of the digest format.</param>
    /// <returns>One entry per slot.</returns>
    private static List<AttestationSlot> BuildSlots(ReviewSnapshot snapshot, ReviewApproval approval, string digestVersion)
    {
        var result = new List<AttestationSlot>();
        foreach (var status in approval.Slots)
        {
            // The signature that counts is the latest one of this slot that bound to the current digest
            var signature = snapshot.Signatures
                .Where(s => s.SlotId == status.SlotId && s.RecordDigest == approval.Digest)
                .MaxBy(s => s.Id);
            var signer = signature is null
                ? null
                : new[] { status.Primary, status.Alternate }.FirstOrDefault(m => m is not null && m.UserId == signature.UserId);
            AttestationSignature? attested = null;
            if (signature is not null)
            {
                attested = new AttestationSignature(
                    signer is null ? signature.DisplayName : $"{signature.DisplayName} ({signer.Username})",
                    signature.Role.ToString(),
                    signature.SignedAt,
                    digestVersion,
                    signature.RecordDigest,
                    signature.Meaning);
            }

            result.Add(new AttestationSlot(
                status.Number,
                $"{status.Primary.DisplayName} ({status.Primary.Username})",
                status.Alternate is null ? null : $"{status.Alternate.DisplayName} ({status.Alternate.Username})",
                attested));
        }

        return result;
    }
}
