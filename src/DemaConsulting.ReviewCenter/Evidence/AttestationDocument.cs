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

namespace DemaConsulting.ReviewCenter.Evidence;

/// <summary>
///     Everything an attestation says about a completed review, as plain values: the input of
///     <see cref="IAttestationRenderer"/>.
/// </summary>
/// <remarks>
///     The document is assembled from one consistent read of the review by <see cref="AttestationService"/>. It holds no
///     domain object, so the renderer depends on nothing but this shape and is deterministic for the same document.
/// </remarks>
/// <param name="ProjectId">The project number.</param>
/// <param name="ProjectName">The project name.</param>
/// <param name="ReviewId">The review identifier.</param>
/// <param name="ReviewTitle">The review title.</param>
/// <param name="RepositoryName">The name of the source repository, or <c>null</c> for an uploaded review.</param>
/// <param name="BaselineSha">The baseline revision, or <c>null</c>.</param>
/// <param name="HeadSha">The head revision, or <c>null</c>.</param>
/// <param name="ReviewSetId">The review set the review was planned from, or <c>null</c>.</param>
/// <param name="ContentFingerprint">The content fingerprint of the files under review.</param>
/// <param name="RecordDigest">The digest of the review record that every signature binds to.</param>
/// <param name="DigestVersion">The version of the digest format, such as <c>ReviewCenter record v1</c>.</param>
/// <param name="Files">The files under review.</param>
/// <param name="ChecklistVersion">The version of the checklist, or <c>null</c> when the review has none.</param>
/// <param name="Sections">The sections of the checklist with their answers.</param>
/// <param name="OpenThreads">The number of open comment threads.</param>
/// <param name="ResolvedThreads">The comment threads that are resolved.</param>
/// <param name="Slots">The reviewer slots with their signatures.</param>
/// <param name="AuditHeadHash">The head hash of the audit chain when the document was generated.</param>
/// <param name="AuditEntryCount">The number of entries of the audit chain.</param>
/// <param name="AuditIsIntact">Whether the audit chain verified as intact.</param>
/// <param name="SigningKey">The key that signs the document.</param>
/// <param name="ToolVersion">The ReviewCenter version.</param>
/// <param name="GeneratedAt">The time of generation.</param>
/// <param name="GeneratedBy">The user who requested the document.</param>
internal sealed record AttestationDocument(
    int ProjectId,
    string ProjectName,
    string ReviewId,
    string ReviewTitle,
    string? RepositoryName,
    string? BaselineSha,
    string? HeadSha,
    string? ReviewSetId,
    string ContentFingerprint,
    string RecordDigest,
    string DigestVersion,
    IReadOnlyList<AttestationFile> Files,
    string? ChecklistVersion,
    IReadOnlyList<AttestationSection> Sections,
    int OpenThreads,
    IReadOnlyList<AttestationThread> ResolvedThreads,
    IReadOnlyList<AttestationSlot> Slots,
    string AuditHeadHash,
    long AuditEntryCount,
    bool AuditIsIntact,
    AttestationKeyInfo SigningKey,
    string ToolVersion,
    DateTimeOffset GeneratedAt,
    string GeneratedBy);

/// <summary>A file under review.</summary>
/// <param name="Path">The path of the file.</param>
/// <param name="Status">The status: <c>Added</c>, <c>Modified</c>, <c>Deleted</c> or <c>Uploaded</c>.</param>
/// <param name="Sha256">The SHA-256 of the file content as reviewed.</param>
/// <param name="Size">The size in bytes.</param>
internal sealed record AttestationFile(string Path, string Status, string Sha256, long Size);

/// <summary>A checklist section with its answers.</summary>
/// <param name="Title">The section title.</param>
/// <param name="IsApplicable">Whether the section applies to the review.</param>
/// <param name="Reason">The reason the section does not apply, or <c>null</c>.</param>
/// <param name="Items">The checks of the section.</param>
internal sealed record AttestationSection(string Title, bool IsApplicable, string? Reason, IReadOnlyList<AttestationCheck> Items);

/// <summary>A check of the checklist and its answer.</summary>
/// <param name="Id">The identifier of the check.</param>
/// <param name="Text">The text of the check.</param>
/// <param name="Outcome">The outcome: <c>Pass</c>, <c>Fail</c>, <c>N/A</c> or <c>Not answered</c>.</param>
/// <param name="Comment">The justification the reviewer gave, or <c>null</c>.</param>
/// <param name="AnsweredBy">The display name of the user who answered, or <c>null</c>.</param>
internal sealed record AttestationCheck(string Id, string Text, string Outcome, string? Comment, string? AnsweredBy);

/// <summary>A comment thread that was resolved.</summary>
/// <param name="Location">Where the thread is: the file and lines.</param>
/// <param name="Summary">The first comment, shortened, with its author.</param>
/// <param name="CommentCount">The number of comments in the thread.</param>
internal sealed record AttestationThread(string Location, string Summary, int CommentCount);

/// <summary>A reviewer slot and the signature that approves the record.</summary>
/// <param name="Number">The number of the slot.</param>
/// <param name="Primary">The primary reviewer, as name and user name.</param>
/// <param name="Alternate">The alternate reviewer, or <c>null</c>.</param>
/// <param name="Signature">The signature of the current record, or <c>null</c> when there is none.</param>
internal sealed record AttestationSlot(int Number, string Primary, string? Alternate, AttestationSignature? Signature);

/// <summary>A reviewer's signature of the record.</summary>
/// <param name="Signer">The display name of the signer, with the user name when known.</param>
/// <param name="Role">The seat the signer held: <c>Primary</c> or <c>Alternate</c>.</param>
/// <param name="SignedAt">The time of signing.</param>
/// <param name="DigestVersion">The version of the digest format the signature bound to.</param>
/// <param name="RecordDigest">The record digest the signature bound to.</param>
/// <param name="Meaning">The meaning of the signature, as the signer saw it.</param>
internal sealed record AttestationSignature(
    string Signer,
    string Role,
    DateTimeOffset SignedAt,
    string DigestVersion,
    string RecordDigest,
    string Meaning);
