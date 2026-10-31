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
    ///     The actor text of the audit entry for an attestation that was requested without signing in.
    /// </summary>
    public const string AnonymousActorName = "anonymous";

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
    // The service is built by dependency injection, which supplies each collaborator as its own parameter. The eight
    // are distinct roles that no other class uses together, so there is no concept to name. In particular the renderer
    // and the signer are not one "PDF pipeline": the review is re-read between rendering and signing (AfterRender), and
    // the key is fetched before either runs, so a facade over them would hide the steps this service orders
    // deliberately. Bundling them into a parameter object would only hide what the service depends on
#pragma warning disable S107
    public AttestationService(
        IReviewStore store,
        SigningService signing,
        IProjectAccess access,
        AuditService audit,
        IAttestationKeyProvider keys,
        IAttestationRenderer renderer,
        IAttestationSigner signer,
        TimeProvider time)
#pragma warning restore S107
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
    /// <remarks>
    ///     What the PDF contains is decided by the evidence settings of the project, read once at the start; they are
    ///     read again before signing, and a change in between gives <see cref="AttestationStatus.Changed"/>, so a
    ///     document never mixes two settings. A request without an actor is allowed only when the project allows
    ///     anonymous attestation PDFs; otherwise it gets the answer for a review that does not exist. An anonymous
    ///     request for a review that is not complete gets that same answer, so it cannot be used to list review
    ///     identifiers or their state; a signed-in caller gets <see cref="AttestationStatus.NotApproved"/>. The caller
    ///     (the route) has already decided whether an anonymous request may reach this method.
    /// </remarks>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="actor">The user who asks for it, or <c>null</c> for a request without signing in.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     The result: the signed PDF when the review is complete and the key is usable; otherwise why not
    ///     (including <see cref="AttestationStatus.Changed"/> when the review changed while the document was being
    ///     prepared, and <see cref="AttestationStatus.NotFound"/> instead of
    ///     <see cref="AttestationStatus.NotApproved"/> for a request without an actor). Only the success is
    ///     recorded in the audit trail.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reviewId"/> is null.</exception>
    public async Task<AttestationResult> GenerateAsync(
        int projectId,
        string reviewId,
        Actor? actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewId);

        // Whoever may not see the project gets the same answer as for a review that does not exist; so does an
        // anonymous request to a project that does not allow it
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        if (project is null || !await MayAskAsync(actor, project, cancellationToken))
        {
            return NotFoundResult();
        }

        var settings = project.Evidence;
        // Availability first: the review must exist and be complete
        var approval = await _signing.GetApprovalAsync(projectId, reviewId, cancellationToken);
        if (approval is null)
        {
            return NotFoundResult();
        }

        // An anonymous caller must not learn which review identifiers exist or whether they are complete, so an
        // incomplete review looks like a missing one; people who are signed in get the informative answer
        if (!approval.IsApproved)
        {
            return actor is null
                ? NotFoundResult()
                : new AttestationResult(AttestationStatus.NotApproved, null, null, "The review is not complete: not every reviewer slot has signed the current record.");
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
        if (snapshot is null || review is null)
        {
            return NotFoundResult();
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
        var document = BuildDocument(
            project, review, snapshot, approval, new GenerationContext(actor, key.Info, chain, now));
        var unsigned = _renderer.Render(document);

        // The reads above and the render take time, so check again that the review is still what was rendered
        if (AfterRender is not null)
        {
            await AfterRender(cancellationToken);
        }

        if (!await IsUnchangedAsync(projectId, reviewId, approval.Digest, snapshot, settings, cancellationToken))
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
        // digest that was attested, and who asked: a request without signing in is recorded as "anonymous"
        var hash = Convert.ToHexString(SHA256.HashData(signed)).ToLowerInvariant();
        var detail =
            $"record digest {approval.Digest}, chain head {chain.HeadHash}, pdf sha256 {hash}, key {key.Info.Thumbprint}, selfSigned={key.Info.IsSelfSigned}";
        await _audit.RecordAsync(
            actor is null
                ? new AuditDraft(AuditAction, null, AnonymousActorName, projectId, reviewId, detail)
                : AuditDraft.By(actor, AuditAction, detail, projectId, reviewId),
            cancellationToken);
        return new AttestationResult(AttestationStatus.Ok, signed, FileNameFor(projectId, reviewId), null);
    }

    /// <summary>
    ///     Decides whether the asker may read attestations of the project.
    /// </summary>
    /// <param name="actor">The signed-in user, or <c>null</c> for a request without signing in.</param>
    /// <param name="project">The project.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     <c>true</c> for a user who may see the project; for nobody, only when the project allows anonymous
    ///     attestation PDFs.
    /// </returns>
    private async Task<bool> MayAskAsync(Actor? actor, Project project, CancellationToken cancellationToken)
    {
        return actor is null
            ? project.Evidence.AnonymousAttestations
            : await _access.CanViewAsync(actor, project.Id, cancellationToken);
    }

    /// <summary>
    ///     Builds the answer for a review that does not exist, or that the asker may not know about.
    /// </summary>
    /// <returns>The result with the status <see cref="AttestationStatus.NotFound"/>.</returns>
    private static AttestationResult NotFoundResult()
    {
        return new AttestationResult(AttestationStatus.NotFound, null, null, "The review was not found.");
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
    /// <param name="settings">The evidence settings the document was built with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     <c>true</c> when the review is still approved with the same record digest, and has the same slots and the
    ///     same signatures that count as the rendered one, and the evidence settings are still the ones rendered.
    /// </returns>
    private async Task<bool> IsUnchangedAsync(
        int projectId,
        string reviewId,
        string digest,
        ReviewSnapshot rendered,
        EvidenceSettings settings,
        CancellationToken cancellationToken)
    {
        var project = await _store.GetProjectAsync(projectId, cancellationToken);
        if (project is null || project.Evidence != settings)
        {
            return false;
        }

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
    /// <param name="context">Who asked, the signing key, the audit chain result and the time of generation.</param>
    /// <returns>The document.</returns>
    private static AttestationDocument BuildDocument(
        Project project,
        Review review,
        ReviewSnapshot snapshot,
        ReviewApproval approval,
        GenerationContext context)
    {
        var settings = project.Evidence;
        var (actor, key, chain, now) = context;
        var record = snapshot.Record;
        var digestVersion = RecordDigest.Canonicalize(record).Split('\n', 2)[0];
        var threads = record.Threads.ToList();
        var people = new PersonLabels(settings.IncludeReviewerNames, approval);
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
            BuildSections(record, people),
            threads.Count(t => t.State == ReviewThreadState.Open),
            threads
                .Where(t => t.State == ReviewThreadState.Resolved)
                .Select(t => SummarizeThread(t, people))
                .ToList(),
            BuildSlots(snapshot, approval, digestVersion, people),
            chain.HeadHash,
            chain.Count,
            chain.IsIntact,
            key,
            Program.Version,
            now,
            people.Requester(actor),
            settings,
            actor is null,
            (approval.Reopens ?? [])
                .Select(r => new AttestationReopen(
                    r.ReopenedAt,
                    people.Name(r.ReopenedByDisplayName, r.ReopenedBy, true),
                    r.Reason,
                    r.PreviousDigest))
                .ToList(),
            record.FollowsReviewId,
            review.CarriedAnswerCount,
            review.CarriedThreadCount);
    }

    /// <summary>
    ///     Gathers the checklist sections with their answers.
    /// </summary>
    /// <param name="record">The review record.</param>
    /// <param name="people">The labels of the people the document names.</param>
    /// <returns>The sections; empty when the review has no checklist.</returns>
    private static List<AttestationSection> BuildSections(ReviewRecord record, PersonLabels people)
    {
        if (record.Checklist is null)
        {
            return [];
        }

        var state = new ChecklistState(record.Checklist.Parse(), record.SectionStates, record.Answers);
        return state.Definition.Sections
            .Select(section => new AttestationSection(
                section.Id,
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
                        answer is null ? null : people.Name(answer.AnsweredBy, answer.AnsweredByUsername, false),
                        answer?.CarriedFromReviewId,
                        answer?.AnsweredAt);
                }).ToList(),
                state.IsApplicable(section.Id)
                    ? null
                    : record.SectionStates.FirstOrDefault(s => s.SectionId == section.Id && !s.Applicable)?.CarriedFromReviewId))
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
    /// <param name="people">The labels of the people the document names.</param>
    /// <returns>The summary.</returns>
    private static AttestationThread SummarizeThread(ReviewThread thread, PersonLabels people)
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
        var text = first is null
            ? "(no comments)"
            : $"{people.Name(first.Author, first.AuthorUsername, false)}: {first.Text}";
        return new AttestationThread(
            thread.FilePath + lines,
            text.Length <= SummaryLength ? text : text[..SummaryLength] + "...",
            thread.Comments.Count,
            thread.CarriedFromReviewId);
    }

    /// <summary>
    ///     Gathers the reviewer slots with the signatures that approve the current record.
    /// </summary>
    /// <param name="snapshot">The record, slots and signatures.</param>
    /// <param name="approval">The approval status, which names the members of each slot.</param>
    /// <param name="digestVersion">The version of the digest format.</param>
    /// <param name="people">The labels of the people the document names.</param>
    /// <returns>One entry per slot.</returns>
    private static List<AttestationSlot> BuildSlots(
        ReviewSnapshot snapshot,
        ReviewApproval approval,
        string digestVersion,
        PersonLabels people)
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
                    people.Name(signature.DisplayName, signer?.Username, true),
                    signature.Role.ToString(),
                    signature.SignedAt,
                    digestVersion,
                    signature.RecordDigest,
                    signature.Meaning);
            }

            result.Add(new AttestationSlot(
                status.Number,
                people.Name(status.Primary.DisplayName, status.Primary.Username, true),
                status.Alternate is null
                    ? null
                    : people.Name(status.Alternate.DisplayName, status.Alternate.Username, true),
                attested));
        }

        return result;
    }

    /// <summary>
    ///     What a document is built with besides the review itself: who asked, the signing key, the result of
    ///     verifying the audit chain and the time of generation.
    /// </summary>
    /// <remarks>
    ///     Groups the values that are fixed once per request so that <c>BuildDocument</c> keeps a short parameter list.
    /// </remarks>
    /// <param name="Actor">The user who asked, or <c>null</c> for a request without signing in.</param>
    /// <param name="Key">The description of the signing key.</param>
    /// <param name="Chain">The result of verifying the audit chain.</param>
    /// <param name="Now">The time of generation.</param>
    private sealed record GenerationContext(
        Actor? Actor,
        AttestationKeyInfo Key,
        AuditVerificationResult Chain,
        DateTimeOffset Now);

    /// <summary>
    ///     Writes the names of the people a document mentions, or the labels that stand in for them when the
    ///     evidence settings keep names out of PDFs.
    /// </summary>
    /// <remarks>
    ///     A label is chosen from the role a person has in the review, so the PDF still shows that the primary of
    ///     slot 1 and the signer of slot 1 are the same person: <c>Reviewer 1 (primary)</c>,
    ///     <c>Reviewer 1 (alternate)</c>. Everybody else (for example a person who commented or answered but holds
    ///     no seat) is <c>Participant N</c>, numbered in the order in which they are first named. A person is
    ///     recognized by user name, or by display name when only that is known. Only the structured name fields are
    ///     replaced: names that people typed into a comment or a justification are the author's text and stay.
    /// </remarks>
    private sealed class PersonLabels
    {
        /// <summary>
        ///     The label of the person who asked for the document when names are left out.
        /// </summary>
        private const string RequesterLabel = "a signed-in user";

        /// <summary>
        ///     Whether names are written.
        /// </summary>
        private readonly bool _includeNames;

        /// <summary>
        ///     The label of each person met so far, by user name or display name.
        /// </summary>
        private readonly Dictionary<string, string> _labels = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     The number of participants labeled so far.
        /// </summary>
        private int _participants;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PersonLabels"/> class.
        /// </summary>
        /// <param name="includeNames">Whether names are written.</param>
        /// <param name="approval">The approval, whose slot members get the first labels.</param>
        public PersonLabels(bool includeNames, ReviewApproval approval)
        {
            _includeNames = includeNames;
            foreach (var slot in approval.Slots)
            {
                _labels.TryAdd(slot.Primary.Username, $"Reviewer {slot.Number.ToString(CultureInfo.InvariantCulture)} (primary)");
                if (slot.Alternate is not null)
                {
                    _labels.TryAdd(
                        slot.Alternate.Username,
                        $"Reviewer {slot.Number.ToString(CultureInfo.InvariantCulture)} (alternate)");
                }
            }
        }

        /// <summary>
        ///     Writes a person.
        /// </summary>
        /// <param name="displayName">The display name.</param>
        /// <param name="username">The user name, or <c>null</c> or empty when it is not known.</param>
        /// <param name="withUsername">Whether the user name is written after the display name.</param>
        /// <returns>The name, or the label that stands in for it.</returns>
        public string Name(string displayName, string? username, bool withUsername)
        {
            if (_includeNames)
            {
                return withUsername && !string.IsNullOrEmpty(username) ? $"{displayName} ({username})" : displayName;
            }

            var key = string.IsNullOrEmpty(username) ? displayName : username;
            if (!_labels.TryGetValue(key, out var label))
            {
                _participants++;
                label = $"Participant {_participants.ToString(CultureInfo.InvariantCulture)}";
                _labels[key] = label;
            }

            return label;
        }

        /// <summary>
        ///     Writes who requested the document.
        /// </summary>
        /// <param name="actor">The user, or <c>null</c> for a request without signing in.</param>
        /// <returns>The name of the user, a label, or a statement that the request was anonymous.</returns>
        public string Requester(Actor? actor)
        {
            if (actor is null)
            {
                return "an anonymous request";
            }

            return _includeNames ? $"{actor.DisplayName} ({actor.Username})" : RequesterLabel;
        }
    }
}
