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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Lays out an <see cref="AttestationDocument"/> as a PDF with <see cref="PdfDocumentWriter"/>.
/// </summary>
/// <remarks>
///     The sections are: the review, the content scope, the files, the checklist, the comment threads, the reviewer
///     sign-offs, the audit trail anchor and the digital signature (with what it does and does not prove). The files
///     and the comment threads are left out when the evidence settings of the project turn them off, and so are the
///     checklist wording and the git context in the parts that show them; the content scope names what is left out.
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
    ///     The statement of what the digital signature does and does not establish.
    /// </summary>
    public const string ProvesStatement =
        "The digital signature proves that this document has not changed since it was signed and that it was produced "
        + "by the holder of the signing key named above. It does not identify the reviewers: reviewers were "
        + "authenticated by ReviewCenter, which recorded their sign-offs; the sign-offs are listed in this document, "
        + "and this document vouches for them only as far as the signing key is trusted. The signing time comes from "
        + "the server clock and is not a trusted timestamp.";

    /// <summary>
    ///     The largest number of resolved threads that are listed; the counts stay exact and the rest are summed up in
    ///     one line. Checklist comments are signed evidence of what a reviewer answered and are never cut.
    /// </summary>
    public const int MaxListedResolvedThreads = 100;

    /// <inheritdoc />
    public byte[] Render(AttestationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // The metadata carries the fingerprint and the review so that a reader's property dialog identifies the review
        var info = new PdfDocumentInfo(
            $"Review attestation - {document.ReviewTitle} ({document.ReviewId})",
            $"ReviewCenter {document.ToolVersion}",
            $"Attestation of the completed review {document.ReviewId} of project {document.ProjectName}",
            $"fingerprint:{document.ContentFingerprint}; review:{document.ReviewId}; project:{document.ProjectId.ToString(CultureInfo.InvariantCulture)}",
            "ReviewCenter",
            $"ReviewCenter {document.ToolVersion} built-in PDF writer",
            document.GeneratedAt);
        var writer = new PdfDocumentWriter(
            info,
            $"Review attestation - {document.ProjectName} - {document.ReviewId}",
            $"ReviewCenter {document.ToolVersion}");

        writer.Title("Review attestation");
        writer.Paragraph(
            "This document attests that the review below is complete and signed by all reviewer slots.",
            PdfFont.HelveticaOblique);
        WriteReview(writer, document);
        WriteContentScope(writer, document);
        if (document.Content.IncludeFiles)
        {
            WriteFiles(writer, document);
        }

        WriteChecklist(writer, document);
        if (document.Content.IncludeThreads)
        {
            WriteThreads(writer, document);
        }

        WriteSignOffs(writer, document);
        if (document.Reopens is { Count: > 0 })
        {
            WriteReopens(writer, document.Reopens);
        }

        WriteAudit(writer, document);
        WriteSignature(writer, document);
        return writer.Build();
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
    ///     Writes one check with its outcome, the reviewer's comment and who answered it.
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

        if (item.AnsweredBy is not null)
        {
            writer.Paragraph($"Answered by {item.AnsweredBy}", PdfFont.HelveticaOblique, 8, 122, 1);
        }

        if (item.CarriedFrom is not null)
        {
            writer.Paragraph($"Carried from review {item.CarriedFrom}", PdfFont.HelveticaOblique, 8, 122, 1);
        }
    }

    /// <summary>
    ///     Writes how many threads are open and resolved, and the first resolved ones.
    /// </summary>
    /// <remarks>
    ///     The counts are exact. At most <see cref="MaxListedResolvedThreads"/> resolved threads are listed, in the
    ///     order of the document (the order in which the threads were created); a line states how many are left out.
    ///     The summary of a thread is already cut by the service; the renderer cuts nothing.
    /// </remarks>
    /// <param name="writer">The writer.</param>
    /// <param name="document">The document.</param>
    private static void WriteThreads(PdfDocumentWriter writer, AttestationDocument document)
    {
        writer.Heading("Comment threads");
        writer.Row("Open", document.OpenThreads.ToString(CultureInfo.InvariantCulture));
        writer.Row("Resolved", document.ResolvedThreads.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var thread in document.ResolvedThreads.Take(MaxListedResolvedThreads))
        {
            writer.Paragraph(
                $"{thread.Location} ({thread.CommentCount.ToString(CultureInfo.InvariantCulture)} comments)",
                PdfFont.HelveticaBold,
                9,
                12,
                4);
            writer.Paragraph(thread.Summary, PdfFont.Helvetica, 9, 24, 1);
            if (thread.CarriedFrom is not null)
            {
                writer.Paragraph($"Carried from review {thread.CarriedFrom}", PdfFont.HelveticaOblique, 8, 24, 1);
            }
        }

        var omitted = document.ResolvedThreads.Count - MaxListedResolvedThreads;
        if (omitted > 0)
        {
            writer.Paragraph(
                $"and {omitted.ToString(CultureInfo.InvariantCulture)} more resolved threads (see the review record)",
                PdfFont.HelveticaOblique,
                9,
                12,
                4);
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
