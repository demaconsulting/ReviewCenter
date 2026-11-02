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
///     Creates the signed PDF attestation of a concluded review, on demand.
/// </summary>
/// <remarks>
///     An attestation is available only when the review is concluded: every reviewer slot has a signature of the
///     record as it is now that attests the agreed outcome, and no thread is open (the existing conclusion rule of
///     <see cref="SigningService"/>). The review is then either approved or rejected, and the document says which:
///     a rejected review gets an attestation that states the rejection and its reasons prominently, and is never
///     usable as evidence that the content passed. A concluded review is locked, so everything the document prints,
///     including the submitted ballot of every slot and the findings, is final and the viewer-dependent hiding of
///     ballots (see <see cref="BallotVisibility"/>) has nothing left to hide; the service nevertheless checks that
///     the review is concluded before it reads a single ballot, so no code path can print the ballots of a review
///     that is still open. A comment that was not included in the report is never printed. Nothing is stored; the
///     PDF is built, signed and returned each time, and each creation is recorded in the audit trail. A key problem
///     never produces an unsigned or partly signed document and is not recorded as an attestation. Because reading,
///     verifying the audit chain, and rendering take time, the review is read again after rendering and before
///     signing; when it is no longer concluded with the same outcome or no longer the same, nothing is signed or
///     recorded. The service is thread-safe.
/// </remarks>
internal sealed class AttestationService
{
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
    ///     request for a review that is not concluded gets that same answer, so it cannot be used to list review
    ///     identifiers or their state; a signed-in caller gets <see cref="AttestationStatus.NotApproved"/>. A
    ///     rejected review is concluded, so a signed-in caller gets its attestation; an anonymous caller does not:
    ///     the anonymous routes exist for evidence that a file set passed, a rejected attestation prints the reasons
    ///     for the rejection, and the caller gets the answer for a review that does not exist. The caller (the route)
    ///     has already decided whether an anonymous request may reach this method.
    /// </remarks>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier. Must not be null.</param>
    /// <param name="actor">The user who asks for it, or <c>null</c> for a request without signing in.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     The result: the signed PDF when the review is concluded and the key is usable; otherwise why not
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
        // Availability first: the review must exist and be concluded
        var approval = await _signing.GetApprovalAsync(projectId, reviewId, cancellationToken);
        if (approval is null)
        {
            return NotFoundResult();
        }

        // Nothing below this point reads a ballot or a finding of a review that is still open: until the review is
        // concluded the ballots of the other reviewers are hidden from most viewers, and the document would print them.
        // An anonymous caller must not learn which review identifiers exist or whether they are concluded, so an open
        // review looks like a missing one; people who are signed in get the informative answer
        if (!approval.IsConcluded)
        {
            return actor is null
                ? NotFoundResult()
                : new AttestationResult(
                    AttestationStatus.NotApproved,
                    null,
                    null,
                    "The review is not complete: not every reviewer slot has signed the current record, "
                    + "or a thread is open.");
        }

        // A rejected attestation carries the reasons for the rejection, so it is not offered to a caller who did not
        // sign in; the anonymous routes exist for evidence that a file set passed
        if (actor is null && !approval.IsApproved)
        {
            return NotFoundResult();
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

        // Read the review once more for the content; the digest must still be the one that was concluded
        var snapshot = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        var review = await _store.GetReviewAsync(projectId, reviewId, cancellationToken);
        if (snapshot is null || review is null)
        {
            return NotFoundResult();
        }

        // The approval and the snapshot are two reads: they must describe the same record and the same slots, and
        // every slot of a review with a checklist must have handed in its ballot, as a conclusion requires
        if (RecordDigest.Compute(snapshot.Record) != approval.Digest
            || !SameSlots(approval, snapshot)
            || !AllBallotsSubmitted(snapshot))
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

        if (!await IsUnchangedAsync(
                projectId, reviewId, approval.Conclusion, approval.Digest, snapshot, settings, cancellationToken))
        {
            return ChangedResult();
        }

        // Sign; a failure to sign throws a key exception and returns nothing
        byte[] signed;
        try
        {
            var reason = approval.IsApproved
                ? $"Review {reviewId} is complete and signed by all reviewer slots"
                : $"Review {reviewId} was rejected and is signed by all reviewer slots";
            signed = _signer.Sign(unsigned, key, now, reason);
        }
        catch (AttestationKeyException ex)
        {
            return new AttestationResult(AttestationStatus.KeyUnavailable, null, null, ex.Message);
        }

        // The event is recorded strictly: an attestation that cannot be traced is not handed out. It records the
        // digest that was attested, the outcome, and who asked: a request without signing in is recorded as "anonymous"
        var hash = Convert.ToHexString(SHA256.HashData(signed)).ToLowerInvariant();
        var detail =
            $"outcome {approval.Conclusion.ToString().ToLowerInvariant()}, record digest {approval.Digest}, "
            + $"chain head {chain.HeadHash}, pdf sha256 {hash}, key {key.Info.Thumbprint}, "
            + $"selfSigned={key.Info.IsSelfSigned}";
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
    ///     Checks, from fresh reads, that the review is still concluded with the same outcome and is still exactly what
    ///     was rendered.
    /// </summary>
    /// <param name="projectId">The project number.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="conclusion">The outcome the review had when it was rendered.</param>
    /// <param name="digest">The record digest that was concluded and rendered.</param>
    /// <param name="rendered">The snapshot the document was built from.</param>
    /// <param name="settings">The evidence settings the document was built with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>
    ///     <c>true</c> when the review is still concluded with the same outcome and the same record digest, and has
    ///     the same slots and the same signatures that count as the rendered one, and the evidence settings are still
    ///     the ones rendered.
    /// </returns>
    private async Task<bool> IsUnchangedAsync(
        int projectId,
        string reviewId,
        ReviewConclusion conclusion,
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

        // Not "approved": a rejected review is rendered too, and a change of the outcome is a change of the document
        var approval = await _signing.GetApprovalAsync(projectId, reviewId, cancellationToken);
        if (approval is null || !approval.IsConcluded || approval.Conclusion != conclusion || approval.Digest != digest)
        {
            return false;
        }

        var current = await _store.GetSnapshotAsync(projectId, reviewId, cancellationToken);
        return current is not null
               && RecordDigest.Compute(current.Record) == digest
               && SlotSignature(current, digest) == SlotSignature(rendered, digest);
    }

    /// <summary>
    ///     Checks that every reviewer slot of a review with a checklist has a submitted ballot.
    /// </summary>
    /// <remarks>
    ///     A concluded review always satisfies this (the conclusion needs a submitted ballot from every slot), so a
    ///     <c>false</c> answer means the two reads of the review disagree and the document must not be built.
    /// </remarks>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns><c>true</c> when the review has no checklist, or each slot has a submitted ballot.</returns>
    private static bool AllBallotsSubmitted(ReviewSnapshot snapshot)
    {
        return snapshot.Record.Checklist is null
               || snapshot.Slots.All(slot => snapshot.Record.Ballots.Any(
                   b => b.SlotId == slot.Id && b.Status == BallotStatus.Submitted));
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
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the review is not concluded: the document prints the submitted ballot of every slot, which is
    ///     only allowed for a concluded review, so this is a programming error, not a user error.
    /// </exception>
    private static AttestationDocument BuildDocument(
        Project project,
        Review review,
        ReviewSnapshot snapshot,
        ReviewApproval approval,
        GenerationContext context)
    {
        // Fail closed: the ballots and the findings are printed for a concluded review only
        if (!approval.IsConcluded)
        {
            throw new InvalidOperationException("An attestation can only be built for a concluded review.");
        }

        var settings = project.Evidence;
        var (actor, key, chain, now) = context;
        var record = snapshot.Record;
        var digestVersion = RecordDigest.Canonicalize(record).Split('\n', 2)[0];
        var people = new PersonLabels(settings.IncludeReviewerNames, approval);

        // The labels of people who hold no seat are numbered in the order they are first named, so the parts are built
        // in the order the document prints them: checklist, findings, ballots appendix
        var sections = BuildSections(record, people);
        var findings = settings.IncludeThreads ? BuildFindings(record, people, settings.IncludeChecklistWording) : [];
        var appendix = BuildAppendix(record, approval, people);
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
            sections,
            findings,
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
            review.CarriedThreadCount,
            approval.Conclusion == ReviewConclusion.Rejected ? ReviewOutcome.Rejected : ReviewOutcome.Approved,
            record.ReviewConclusionText,
            appendix,
            appendix.Count == 0 ? null : RecordDigest.ComputeBallotFingerprint(record));
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

        var state = BallotProjection.Consolidate(record)?.State;
        if (state is null)
        {
            return [];
        }

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
                    : state.SectionStateFor(section.Id)?.CarriedFromReviewId))
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
    ///     Gathers the threads of the report: every thread with at least one comment that was included in the report.
    /// </summary>
    /// <remarks>
    ///     These are the same threads, and the same comments, that the record digest binds, so a signature never
    ///     covers text that the document does not show. A thread whose starting comment is included is a finding; a
    ///     thread whose starting comment is not included (for example a question with an included reply) is a
    ///     discussion thread and only its included comments are printed. A comment that was not included is never
    ///     printed, and a thread without an included comment is absent; that discussion stays in the review
    ///     history. A thread
    ///     about a check is labeled <c>Check {id}</c>, with the wording of the check only when the evidence settings
    ///     include the checklist wording, so that no wording reaches a document that leaves it out. The findings are
    ///     ordered by file path, then first line, then creation time (check findings after the file findings, by check
    ///     identifier), so the same review always gives the same order. Every included comment is printed in full.
    /// </remarks>
    /// <param name="record">The review record.</param>
    /// <param name="people">The labels of the people the document names.</param>
    /// <param name="wording">Whether the wording of a check may be printed.</param>
    /// <returns>The findings; empty when there is none.</returns>
    private static List<AttestationFinding> BuildFindings(ReviewRecord record, PersonLabels people, bool wording)
    {
        // The wording of the checks, only when the document may print it
        var checkText = new Dictionary<string, string>(StringComparer.Ordinal);
        if (wording && record.Checklist is not null)
        {
            foreach (var item in record.Checklist.Parse().Sections.SelectMany(section => section.Items))
            {
                checkText.TryAdd(item.Id, item.Text);
            }
        }

        return record.Threads
            .Where(thread => thread.Comments.Any(comment => comment.IncludeInReport))
            .OrderBy(thread => thread.IsCheckThread)
            .ThenBy(thread => thread.FilePath, StringComparer.Ordinal)
            .ThenBy(thread => thread.CheckItemId, StringComparer.Ordinal)
            .ThenBy(thread => thread.StartLine ?? 0)
            .ThenBy(thread => thread.Created)
            .ThenBy(thread => thread.Id)
            .Select(thread => new AttestationFinding(
                LocationOf(thread, checkText),
                IncludedComments(thread, people),
                thread.State == ReviewThreadState.Resolved,
                thread.CarriedFromReviewId))
            .ToList();
    }

    /// <summary>
    ///     Names where a thread is: the file and lines, or the check it is about.
    /// </summary>
    /// <param name="thread">The thread.</param>
    /// <param name="checkText">The wording of the checks by identifier; empty when the wording is left out.</param>
    /// <returns>The location text.</returns>
    private static string LocationOf(ReviewThread thread, Dictionary<string, string> checkText)
    {
        // A check thread has no file: it is labeled with its check, so no empty path is ever printed
        if (thread.CheckItemId is not null)
        {
            return checkText.TryGetValue(thread.CheckItemId, out var text)
                ? $"Check {thread.CheckItemId}: {text}"
                : $"Check {thread.CheckItemId}";
        }

        var lines = string.Empty;
        if (thread.StartLine is not null)
        {
            var start = thread.StartLine.Value.ToString(CultureInfo.InvariantCulture);
            lines = thread.EndLine is not null && thread.EndLine != thread.StartLine
                ? $", lines {start}-{thread.EndLine.Value.ToString(CultureInfo.InvariantCulture)}"
                : $", line {start}";
        }

        return thread.FilePath + lines;
    }

    /// <summary>
    ///     Gathers the comments of a thread that were included in the report, in posting order.
    /// </summary>
    /// <param name="thread">The thread, which has at least one included comment.</param>
    /// <param name="people">The labels of the people the document names.</param>
    /// <returns>
    ///     The included comments: <c>Finding</c> for the starting comment, <c>Resolution</c> for a resolution note and
    ///     <c>Reply</c> for any other.
    /// </returns>
    private static List<AttestationFindingComment> IncludedComments(ReviewThread thread, PersonLabels people)
    {
        var result = new List<AttestationFindingComment>();
        for (var index = 0; index < thread.Comments.Count; index++)
        {
            var comment = thread.Comments[index];
            if (!comment.IncludeInReport)
            {
                continue;
            }

            var kind = "Reply";
            if (comment.Kind == ReviewCommentKind.Resolution)
            {
                kind = "Resolution";
            }
            else if (index == 0)
            {
                kind = "Finding";
            }

            result.Add(new AttestationFindingComment(
                kind,
                people.Name(comment.Author, comment.AuthorUsername, false),
                comment.Created,
                comment.Text));
        }

        return result;
    }

    /// <summary>
    ///     Gathers the final submitted ballot of every reviewer slot for the ballots appendix.
    /// </summary>
    /// <remarks>
    ///     Called only for a concluded review, which has a submitted ballot for every slot and cannot change any more,
    ///     so nothing here is hidden from the reader (see <see cref="BallotVisibility"/>). Only the final submitted
    ///     ballot is printed; the history of earlier edits stays in the audit trail. A section the slot marked not
    ///     applicable shows its reason and no answers; a check the slot did not answer shows <c>Not answered</c>.
    /// </remarks>
    /// <param name="record">The review record.</param>
    /// <param name="approval">The approval status, whose slots number the ballots and name the members.</param>
    /// <param name="people">The labels of the people the document names.</param>
    /// <returns>
    ///     One ballot per slot with a submitted ballot, in slot order; empty when the review has no checklist.
    /// </returns>
    private static List<AttestationBallot> BuildAppendix(
        ReviewRecord record,
        ReviewApproval approval,
        PersonLabels people)
    {
        var result = new List<AttestationBallot>();
        if (record.Checklist is null)
        {
            return result;
        }

        var definition = record.Checklist.Parse();
        foreach (var status in approval.Slots)
        {
            var ballot = record.Ballots.FirstOrDefault(b =>
                b.SlotId == status.SlotId && b.Status == BallotStatus.Submitted);
            if (ballot is null)
            {
                continue;
            }

            // The member who handed the ballot in; the primary when the user name is not recorded
            var member = new[] { status.Primary, status.Alternate }.FirstOrDefault(m =>
                m is not null
                && string.Equals(m.Username, ballot.SubmittedByUsername, StringComparison.OrdinalIgnoreCase))
                ?? (ballot.SubmittedByUsername is null ? status.Primary : null);
            var reviewer = member is null
                ? people.Name(ballot.SubmittedByUsername!, ballot.SubmittedByUsername, false)
                : people.Name(member.DisplayName, member.Username, true);
            result.Add(new AttestationBallot(
                status.Number,
                reviewer,
                ballot.SubmittedAt,
                definition.Sections.Select(section => BallotSection(section, ballot, people)).ToList()));
        }

        return result;
    }

    /// <summary>
    ///     Describes how one ballot answered one section.
    /// </summary>
    /// <param name="section">The section of the checklist.</param>
    /// <param name="ballot">The submitted ballot.</param>
    /// <param name="people">The labels of the people the document names.</param>
    /// <returns>The section with the reason it was marked not applicable, or its answers.</returns>
    private static AttestationBallotSection BallotSection(ChecklistSection section, Ballot ballot, PersonLabels people)
    {
        var state = ballot.SectionStates.LastOrDefault(s => s.SectionId == section.Id);
        if (state is { Applicable: false })
        {
            return new AttestationBallotSection(section.Id, section.Title, false, state.Reason, []);
        }

        var answers = section.Items.Select(item =>
        {
            var answer = ballot.Answers.LastOrDefault(a => a.ItemId == item.Id);
            return new AttestationBallotAnswer(
                item.Id,
                item.Text,
                answer is null ? "Not answered" : OutcomeText(answer.Outcome),
                answer?.Justification,
                answer is null ? null : people.Name(answer.AnsweredBy, answer.AnsweredByUsername, false),
                answer?.AnsweredAt);
        }).ToList();
        return new AttestationBallotSection(section.Id, section.Title, true, null, answers);
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
