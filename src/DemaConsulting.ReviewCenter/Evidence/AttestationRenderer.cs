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
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Lays out an <see cref="AttestationDocument"/> as a PDF with <see cref="PdfDocumentWriter"/>.
/// </summary>
/// <remarks>
///     The sections are: the review, the content scope, the files, the checklist, the findings and comments of the
///     report, the conclusion, the reviewer sign-offs, the reopen history (only when there is any), the ballots
///     appendix (only for a review with a checklist), the audit trail anchor and the digital signature (with what it
///     does and does not prove). A review that concluded as rejected also gets a banner at the top and the word
///     <c>REJECTED</c> in its title, so that it cannot be mistaken for evidence that the content passed. The files, the
///     findings and the comments are left out when the evidence settings of the project turn them off, and so are the
///     checklist wording and the git context in the parts that show them; the content scope names what is left out.
///     The ballots appendix follows the checklist-wording switch like the checklist. Every comment, justification and
///     conclusion text is printed in full: the paragraph wraps and breaks pages, and nothing is cut.
///     The renderer reads no clock and no environment, so the same document always renders to the same bytes.
/// </remarks>
internal sealed class AttestationRenderer : IAttestationRenderer
{
    /// <summary>
    ///     The notice printed for a key that is not an organization key.
    /// </summary>
    public const string SelfSignedNotice =
        "SELF-SIGNED KEY - NOT AN ORGANIZATION KEY. Dev/test only; do not rely on it as organizational evidence.";

    /// <summary>
    ///     The banner printed at the top of the attestation of a rejected review.
    /// </summary>
    public const string RejectedBanner =
        "REJECTED: this review concluded as rejected. This document is not evidence that the reviewed content passed.";

    /// <summary>
    ///     The statement of what the digital signature does and does not establish.
    /// </summary>
    public const string ProvesStatement =
        "The digital signature proves that this document has not changed since it was signed and that it was produced "
        + "by the holder of the signing key named above. It does not identify the reviewers: reviewers were "
        + "authenticated by ReviewCenter, which recorded their sign-offs; the sign-offs are listed in this document, "
        + "and this document vouches for them only as far as the signing key is trusted. The signing time comes from "
        + "the server clock and is not a trusted timestamp.";

    /// <inheritdoc />
    public byte[] Render(AttestationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // The metadata carries the fingerprint and the review so that a reader's property dialog identifies the review
        var rejected = document.Outcome == ReviewOutcome.Rejected;
        var info = new PdfDocumentInfo(
            $"Review attestation{(rejected ? " (REJECTED)" : string.Empty)} - {document.ReviewTitle} "
            + $"({document.ReviewId})",
            $"ReviewCenter {document.ToolVersion}",
            $"Attestation of the {(rejected ? "rejected" : "completed")} review {document.ReviewId} "
            + $"of project {document.ProjectName}",
            $"fingerprint:{document.ContentFingerprint}; review:{document.ReviewId}; project:{document.ProjectId.ToString(CultureInfo.InvariantCulture)}",
            "ReviewCenter",
            $"ReviewCenter {document.ToolVersion} built-in PDF writer",
            document.GeneratedAt);
        var writer = new PdfDocumentWriter(
            info,
            $"Review attestation - {document.ProjectName} - {document.ReviewId}",
            $"ReviewCenter {document.ToolVersion}");

        writer.Title(rejected ? "Review attestation - REJECTED" : "Review attestation");
        if (rejected)
        {
            WriteRejectedBanner(writer);
        }

        writer.Paragraph(
            rejected
                ? "This document attests that the review below concluded as rejected and is signed by all "
                  + "reviewer slots."
                : "This document attests that the review below is complete and signed by all reviewer slots.",
            PdfFont.HelveticaOblique);
        WriteReview(writer, document);
        WriteContentScope(writer, document);
        if (document.Content.IncludeFiles)
        {
            WriteFiles(writer, document);
        }

        WriteChecklist(writer, document);
        WriteFindings(writer, document);
        WriteConclusion(writer, document);
        WriteSignOffs(writer, document);
        if (document.Reopens is { Count: > 0 })
        {
            WriteReopens(writer, document.Reopens);
        }

        if (document.BallotAppendix is { Count: > 0 })
        {
            WriteBallotAppendix(writer, document);
        }

        WriteAudit(writer, document);
        WriteSignature(writer, document);
        return writer.Build();
    }

    /// <summary>
    ///     Writes the banner of a rejected review: two rules around the statement, so it stands apart from the text.
    /// </summary>
    /// <param name="writer">The writer.</param>
    private static void WriteRejectedBanner(PdfDocumentWriter writer)
    {
        writer.Rule();
        writer.Paragraph(RejectedBanner, PdfFont.HelveticaBold, 11, 0, 6);
        writer.Rule();
    }

    /// <summary>
    ///     Formats a time the same way everywhere in the document.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>The time in UTC, to the second.</returns>
    private static string Time(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Writes the review identity and where its content came from.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteReview(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading("Review");
        writer.Row("Project", $"{document.ProjectName} (#{document.ProjectId.ToString(CultureInfo.InvariantCulture)})");
        writer.Row("Review", $"{document.ReviewTitle}");
        writer.Row("Review identifier", document.ReviewId);
        if (document.RepositoryName is null)
        {
            writer.Row("Source", "Files uploaded to ReviewCenter");
        }
        else if (document.Content.IncludeGitContext)
        {
            writer.Row("Repository", document.RepositoryName);
            writer.Row("Head revision", document.HeadSha ?? "-", PdfFont.Courier);
            writer.Row("Baseline revision", document.BaselineSha ?? "(none: all files are new)", PdfFont.Courier);
        }
        else
        {
            writer.Row("Source", "(git context omitted - see Content scope)");
        }

        if (document.ReviewSetId is not null)
        {
            writer.Row("Review set", document.ReviewSetId);
        }

        // A follow-up says which review it follows and how much it took over from it
        if (document.FollowsReviewId is not null)
        {
            writer.Row(
                "Follows review",
                $"{document.FollowsReviewId} ({document.CarriedAnswerCount.ToString(CultureInfo.InvariantCulture)} answers and " +
                $"{document.CarriedThreadCount.ToString(CultureInfo.InvariantCulture)} threads carried)");
        }

        writer.Row("Content fingerprint", document.ContentFingerprint, PdfFont.Courier);
        writer.Row("Record digest", $"{document.RecordDigest}\n({document.DigestVersion})", PdfFont.Courier);
        writer.Row("Generated", $"{Time(document.GeneratedAt)} by {document.GeneratedBy}");
    }

    /// <summary>
    ///     Writes what the PDF contains and what it leaves out, with the settings that decided it.
    /// </summary>
    /// <remarks>
    ///     Every PDF has this section, also when nothing is left out, so that a reader never has to guess whether a
    ///     part is missing by accident. It states that the record digest covers the whole record.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteContentScope(PdfDocumentWriter writer, AttestationDocument document)
    {
        var content = document.Content;
        writer.Heading("Content scope");
        writer.Paragraph(
            $"These are the evidence settings of the project at {Time(document.GeneratedAt)}, when this document was "
            + "generated. A part that a setting turns off is left out of this document; it is not lost from the review.",
            PdfFont.Helvetica,
            9);
        writer.Row("Requested", document.IsAnonymousRequest
            ? "without signing in (the project allows anonymous access to attestation PDFs)"
            : "by a signed-in user");
        writer.Row("Discussion threads", Switch(content.IncludeThreads));
        writer.Row("Reviewer names", Switch(content.IncludeReviewerNames));
        writer.Row("Checklist wording", Switch(content.IncludeChecklistWording));
        writer.Row(
            "File list and hashes",
            content.IncludeFiles
                ? Switch(true)
                : $"{Switch(false)} ({document.Files.Count.ToString(CultureInfo.InvariantCulture)} files; the content fingerprint covers them)");
        writer.Row("Git context", Switch(content.IncludeGitContext));
        writer.Paragraph(
            content.IsReduced
                ? $"Left out of this document: {string.Join(", ", content.OmittedSections)}."
                : "Nothing is left out of this document.",
            PdfFont.HelveticaBold,
            9);
        writer.Paragraph(
            "The record digest covers the complete record, including the parts left out of this document.",
            PdfFont.HelveticaOblique,
            9);
    }

    /// <summary>
    ///     Names the state of a content switch.
    /// </summary>
    /// <param name="included">Whether the part is included.</param>
    /// <returns><c>Included</c> or <c>Omitted</c>.</returns>
    private static string Switch(bool included) => included ? "Included" : "Omitted";

    /// <summary>
    ///     Writes the files under review with the status of each.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteFiles(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading($"Files ({document.Files.Count.ToString(CultureInfo.InvariantCulture)})");
        foreach (var file in document.Files)
        {
            writer.Paragraph($"{file.Path}", PdfFont.HelveticaBold, 9, 0, 4);
            writer.Paragraph(
                $"{file.Status}, {file.Size.ToString(CultureInfo.InvariantCulture)} bytes, SHA-256 {file.Sha256}",
                PdfFont.Courier,
                7.5,
                12,
                0);
        }
    }

    /// <summary>
    ///     Writes every section of the checklist with the answers and comments.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteChecklist(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading("Checklist");
        if (document.Sections.Count == 0)
        {
            writer.Paragraph("The review has no checklist.");
            return;
        }

        writer.Paragraph($"Checklist version {document.ChecklistVersion}", PdfFont.HelveticaOblique, 9);
        var wording = document.Content.IncludeChecklistWording;
        if (!wording)
        {
            writer.Paragraph(
                "The wording of the sections and checks is left out (see Content scope); the checks are named by identifier.",
                PdfFont.HelveticaOblique,
                9);
        }

        foreach (var section in document.Sections)
        {
            WriteChecklistSection(writer, section, wording);
        }
    }

    /// <summary>
    ///     Writes one section of the checklist: its heading, then its checks or the reason it does not apply.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="section">The section.</param>
    /// <param name="wording">Whether the wording of the section and its checks is included.</param>
    private static void WriteChecklistSection(PdfDocumentWriter writer, AttestationSection section, bool wording)
    {
        writer.Paragraph(wording ? section.Title : section.Id, PdfFont.HelveticaBold, 10, 0, 8);
        if (!section.IsApplicable)
        {
            writer.Paragraph($"Not applicable: {section.Reason}", PdfFont.HelveticaOblique, 9, 12);
            if (section.CarriedFrom is not null)
            {
                writer.Paragraph($"Carried from review {section.CarriedFrom}", PdfFont.HelveticaOblique, 8, 12, 1);
            }

            return;
        }

        foreach (var item in section.Items)
        {
            WriteChecklistItem(writer, item, wording);
        }
    }

    /// <summary>
    ///     Writes one check with its outcome (the agreed answer of the submitted ballots; a pending or disputed check shows as
    ///     <c>Not answered</c>), the reviewer's comment and who gave the answer shown and when.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="item">The check.</param>
    /// <param name="wording">Whether the wording of the check is included.</param>
    private static void WriteChecklistItem(PdfDocumentWriter writer, AttestationCheck item, bool wording)
    {
        if (wording)
        {
            writer.Row($"{item.Id}  {item.Outcome}", item.Text, PdfFont.Helvetica, 12, 110);
        }
        else
        {
            writer.Paragraph($"{item.Id}  {item.Outcome}", PdfFont.Helvetica, 10, 12, 3);
        }

        if (!string.IsNullOrWhiteSpace(item.Comment))
        {
            // The full comment: it is the reviewer's own words, and the paragraph wraps and breaks pages
            writer.Paragraph($"Comment: {item.Comment}", PdfFont.HelveticaOblique, 9, 122, 1);
        }

        var answered = AnsweredLine(item);
        if (answered is not null)
        {
            writer.Paragraph(answered, PdfFont.HelveticaOblique, 8, 122, 1);
        }

        if (item.CarriedFrom is not null)
        {
            writer.Paragraph($"Carried from review {item.CarriedFrom}", PdfFont.HelveticaOblique, 8, 122, 1);
        }
    }

    /// <summary>
    ///     Builds the line that says who gave the agreed answer shown for a check and when.
    /// </summary>
    /// <param name="item">The check.</param>
    /// <returns>The line, or <c>null</c> when the check has neither an answerer nor an answer time.</returns>
    private static string? AnsweredLine(AttestationCheck item)
    {
        if (item.AnsweredBy is null)
        {
            return item.AnsweredAt is { } at ? $"Answered at {Time(at)}" : null;
        }

        return item.AnsweredAt is { } time
            ? $"Answered by {item.AnsweredBy}, {Time(time)}"
            : $"Answered by {item.AnsweredBy}";
    }

    /// <summary>
    ///     Writes the findings and included comments of the report.
    /// </summary>
    /// <remarks>
    ///     Only what a reviewer included in the report is printed, each comment in full, with its author and time. Each
    ///     thread shows its status (<c>Resolved</c> or <c>Open</c>); a thread whose starting comment is not included
    ///     is headed <c>Discussion on</c> and says it is not a finding. The section is always present so that a reader
    ///     can tell a review without findings from a document that leaves the discussion out: when the evidence
    ///     settings turn the discussion off, one line says so. A comment that was not included is not in the list; a
    ///     line says the discussion stays in ReviewCenter.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteFindings(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading("Findings and comments in this report");
        if (!document.Content.IncludeThreads)
        {
            writer.Paragraph(
                "Not included by project setting (see Content scope). The findings are part of the review record.",
                PdfFont.HelveticaOblique,
                9);
            return;
        }

        if (document.Findings.Count == 0)
        {
            writer.Paragraph("No finding or comment was included in the report.");
        }

        foreach (var finding in document.Findings)
        {
            // A thread whose starting comment is not included is a discussion thread, never presented as a finding
            var status = finding.IsResolved ? "Resolved" : "Open";
            if (finding.IsDiscussion)
            {
                writer.Paragraph($"Discussion on {finding.Location}", PdfFont.HelveticaBold, 9, 0, 8);
                writer.Paragraph(
                    $"Discussion thread, not a finding (only the included comments are printed). Status: {status}",
                    PdfFont.HelveticaOblique,
                    8,
                    12,
                    2);
            }
            else
            {
                writer.Paragraph(finding.Location, PdfFont.HelveticaBold, 9, 0, 8);
                writer.Paragraph($"Status: {status}", PdfFont.HelveticaOblique, 8, 12, 2);
            }

            foreach (var comment in finding.Comments)
            {
                writer.Paragraph(
                    $"{comment.Kind} by {comment.Author}, {Time(comment.At)}",
                    PdfFont.HelveticaOblique,
                    8,
                    12,
                    4);
                writer.Paragraph(comment.Body, PdfFont.Helvetica, 9, 24, 1);
            }

            if (finding.CarriedFrom is not null)
            {
                writer.Paragraph($"Carried from review {finding.CarriedFrom}", PdfFont.HelveticaOblique, 8, 12, 1);
            }
        }

        writer.Paragraph(
            "Comments that were not included in the report are not printed here; "
            + "the discussion remains in ReviewCenter's history.",
            PdfFont.HelveticaOblique,
            8,
            0,
            8);
    }

    /// <summary>
    ///     Writes the conclusion: the outcome of the review and the conclusion text the reviewers wrote.
    /// </summary>
    /// <remarks>
    ///     The outcome is always printed. The text is the reasons for a rejection (a rejection cannot be signed without
    ///     them) or the reviewers' conclusion of an approval; nothing is printed for a blank text.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteConclusion(PdfDocumentWriter writer, AttestationDocument document)
    {
        var rejected = document.Outcome == ReviewOutcome.Rejected;
        writer.Heading("Conclusion");
        writer.Paragraph(
            $"Conclusion: {(rejected ? "Rejected" : "Approved")}",
            PdfFont.HelveticaBold,
            14,
            0,
            6);
        if (string.IsNullOrWhiteSpace(document.ConclusionText))
        {
            return;
        }

        writer.Paragraph(rejected ? "Reasons for rejection" : "Conclusion", PdfFont.HelveticaBold, 10, 0, 8);
        writer.Paragraph(document.ConclusionText, PdfFont.Helvetica, 10, 12, 2);
    }

    /// <summary>
    ///     Writes the ballots appendix: how each reviewer slot answered, with the fingerprint of the ballots.
    /// </summary>
    /// <remarks>
    ///     The checklist section shows what the reviewers agree on; the appendix shows the final submitted ballot of
    ///     every slot, so a reader can see who answered what and why. The wording of the sections and checks follows
    ///     the checklist-wording switch of the evidence settings; without it the identifiers are printed.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteBallotAppendix(PdfDocumentWriter writer, AttestationDocument document)
    {
        var wording = document.Content.IncludeChecklistWording;
        writer.Heading("Ballots appendix");
        writer.Paragraph(
            "The final submitted ballot of each reviewer slot. "
            + "The checklist above shows the answers the ballots agree on.",
            PdfFont.HelveticaOblique,
            9);
        foreach (var ballot in document.BallotAppendix!)
        {
            writer.Paragraph(
                $"Slot {ballot.SlotNumber.ToString(CultureInfo.InvariantCulture)}: {ballot.Reviewer}",
                PdfFont.HelveticaBold,
                10,
                0,
                8);
            writer.Row(
                "Ballot submitted",
                ballot.SubmittedAt is { } at ? Time(at) : "-",
                PdfFont.Helvetica,
                12,
                110);
            foreach (var section in ballot.Sections)
            {
                WriteBallotSection(writer, section, wording);
            }
        }

        if (document.BallotFingerprint is not null)
        {
            writer.Space(4);
            writer.Row(
                "Ballot fingerprint",
                $"{document.BallotFingerprint}\n({RecordDigest.BallotsHeader})",
                PdfFont.Courier);
        }
    }

    /// <summary>
    ///     Writes how one slot answered one section: the reason it does not apply, or each check with its outcome.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="section">The section of the ballot.</param>
    /// <param name="wording">Whether the wording of the section and its checks is included.</param>
    private static void WriteBallotSection(PdfDocumentWriter writer, AttestationBallotSection section, bool wording)
    {
        writer.Paragraph(wording ? section.Title : section.Id, PdfFont.HelveticaBold, 9, 12, 5);
        if (!section.IsApplicable)
        {
            writer.Paragraph(
                string.IsNullOrWhiteSpace(section.Reason)
                    ? "Not applicable"
                    : $"Not applicable: {section.Reason}",
                PdfFont.HelveticaOblique,
                9,
                24,
                1);
            return;
        }

        foreach (var answer in section.Answers)
        {
            if (wording)
            {
                writer.Row($"{answer.Id} {answer.Outcome}", answer.Text, PdfFont.Helvetica, 24, 110);
            }
            else
            {
                writer.Paragraph($"{answer.Id} {answer.Outcome}", PdfFont.Helvetica, 9, 24, 2);
            }

            if (!string.IsNullOrWhiteSpace(answer.Justification))
            {
                writer.Paragraph($"Justification: {answer.Justification}", PdfFont.HelveticaOblique, 9, 134, 1);
            }

            var by = answer.AnsweredBy is null ? null : $"Answered by {answer.AnsweredBy}";
            var line = (by, answer.AnsweredAt) switch
            {
                (null, null) => null,
                (null, { } time) => $"Answered at {Time(time)}",
                ({ } name, null) => name,
                ({ } name, { } time) => $"{name}, {Time(time)}"
            };
            if (line is not null)
            {
                writer.Paragraph(line, PdfFont.HelveticaOblique, 8, 134, 1);
            }
        }
    }

    /// <summary>
    ///     Writes the reviewer slots, who signed them and what each signature bound to.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteSignOffs(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading("Reviewer sign-offs");
        foreach (var slot in document.Slots)
        {
            writer.Paragraph($"Slot {slot.Number.ToString(CultureInfo.InvariantCulture)}", PdfFont.HelveticaBold, 10, 0, 8);
            writer.Row("Primary reviewer", slot.Primary, PdfFont.Helvetica, 12, 110);
            writer.Row("Alternate reviewer", slot.Alternate ?? "(none)", PdfFont.Helvetica, 12, 110);
            if (slot.Signature is null)
            {
                writer.Row("Signature", "none", PdfFont.Helvetica, 12, 110);
                continue;
            }

            var signature = slot.Signature;
            writer.Row("Signed by", $"{signature.Signer} ({signature.Role})", PdfFont.Helvetica, 12, 110);
            writer.Row("Signed at", Time(signature.SignedAt), PdfFont.Helvetica, 12, 110);
            writer.Row("Meaning", signature.Meaning, PdfFont.Helvetica, 12, 110);
            writer.Row("Record digest", $"{signature.RecordDigest}\n({signature.DigestVersion})", PdfFont.Courier, 12, 110);
        }
    }

    /// <summary>
    ///     Writes the reopen history: each time an administrator reopened the approved review, who, when and why.
    /// </summary>
    /// <remarks>
    ///     Written only when the review was reopened at least once. The signatures printed above bind to the current
    ///     record; signatures of earlier approvals stay in the review record as history.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="reopens">The reopens, oldest first.</param>
    private static void WriteReopens(PdfDocumentWriter writer, IReadOnlyList<AttestationReopen> reopens)
    {
        writer.Heading("Reopen history");
        writer.Paragraph(
            "This review was approved earlier and was reopened by an administrator; the sign-offs above are of the current record.",
            PdfFont.HelveticaOblique,
            9,
            12,
            4);
        foreach (var reopen in reopens)
        {
            writer.Row("Reopened at", Time(reopen.ReopenedAt), PdfFont.Helvetica, 12, 110);
            writer.Row("Reopened by", reopen.ReopenedBy, PdfFont.Helvetica, 12, 110);
            writer.Row("Reason", reopen.Reason, PdfFont.Helvetica, 12, 110);
            writer.Row("Previous digest", reopen.PreviousDigest, PdfFont.Courier, 12, 110);
        }
    }

    /// <summary>
    ///     Writes the audit chain head at the time of generation.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteAudit(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading("Audit trail");
        writer.Row("Entries", document.AuditEntryCount.ToString(CultureInfo.InvariantCulture));
        writer.Row("Chain verified", document.AuditIsIntact ? "yes, the hash chain is intact" : "NO - the hash chain is broken");
        writer.Row("Chain head hash", document.AuditHeadHash, PdfFont.Courier);
    }

    /// <summary>
    ///     Writes the signing key, the self-signed and code-signing notices when they apply, and what the signature proves.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteSignature(PdfDocumentWriter writer, AttestationDocument document)
    {
        var key = document.SigningKey;
        writer.Heading("Digital signature");
        if (key.IsSelfSigned)
        {
            writer.Paragraph(SelfSignedNotice, PdfFont.HelveticaBold, 10);
        }
        else
        {
            writer.Paragraph($"Certificate issued by {key.Issuer}. Whether to trust it depends on whether you trust that issuer.");
        }

        // A certificate that readers will not trust for documents is called out in the document itself
        if (key.IsCodeSigningOnly)
        {
            writer.Paragraph(AttestationKeyPolicy.CodeSigningNotice);
        }

        writer.Space(4);
        writer.Row("Key provider", key.Provider);
        writer.Row("Certificate subject", key.Subject);
        writer.Row("Certificate issuer", key.Issuer);
        writer.Row("Thumbprint (SHA-1)", key.Thumbprint, PdfFont.Courier);
        writer.Row("Serial number", key.SerialNumber, PdfFont.Courier);
        writer.Row("Key algorithm", key.Algorithm);
        writer.Row("Valid", $"{Time(key.NotBefore)} to {Time(key.NotAfter)}");
        writer.Row("Self-signed", key.IsSelfSigned ? "yes" : "no");
        writer.Row("Trusted key required", key.RequireTrustedKey ? "yes (Attestation:RequireTrustedKey)" : "no");
        writer.Space(4);
        writer.Paragraph("What this signature proves", PdfFont.HelveticaBold, 10, 0, 6);
        writer.Paragraph(ProvesStatement);
    }
}
