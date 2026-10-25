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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     One file of a repository review as the digest sees it: the path, the kind of change and the hash of each side.
/// </summary>
/// <param name="Path">The repository path.</param>
/// <param name="Kind">The kind of change.</param>
/// <param name="BaselineSha256">The SHA-256 of the baseline content, or <c>null</c> when the file had none.</param>
/// <param name="HeadSha256">The SHA-256 of the head content, or <c>null</c> when the file has none (a deleted file).</param>
public sealed record ChangedFile(string Path, ChangeKind Kind, string? BaselineSha256, string? HeadSha256);

/// <summary>
///     Where the files of a review came from, as far as a signature must cover it.
/// </summary>
/// <remarks>
///     An upload has no source data. A repository review carries the repository identity (not its name, which may be
///     renamed), both commits, the selection text and every changed file with the hashes of both sides, so a
///     signature binds to exactly what was reviewed. The record is an immutable value holder.
/// </remarks>
/// <param name="RepositoryId">The repository identity, or <c>null</c> for an upload.</param>
/// <param name="BaselineSha">The baseline commit, or <c>null</c> when there was none.</param>
/// <param name="HeadSha">The head commit, or <c>null</c> for an upload.</param>
/// <param name="Selection">The selection text, or <c>null</c> for an upload.</param>
/// <param name="Changes">The changed files; empty for an upload.</param>
public sealed record ReviewSource(
    int? RepositoryId,
    string? BaselineSha,
    string? HeadSha,
    string? Selection,
    IReadOnlyList<ChangedFile> Changes)
{
    /// <summary>
    ///     Gets the source of a review made from uploaded files.
    /// </summary>
    public static ReviewSource Upload { get; } = new(null, null, null, null, []);

    /// <summary>
    ///     Gets a value indicating whether the files were uploaded rather than read from a repository.
    /// </summary>
    public bool IsUpload => RepositoryId is null;
}

/// <summary>
///     A consistent snapshot of everything a signature covers: the content fingerprint, the source of the files,
///     the checklist with its answers and section applicability, and all threads with their comments.
/// </summary>
/// <remarks>
///     The snapshot is loaded by one store call so the digest and the signing blockers are computed from the
///     same data. Reviewer slots and signatures are deliberately not part of it: signing must not change what
///     was signed. The record is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="ContentFingerprint">The content fingerprint of the review's file set.</param>
/// <param name="Checklist">The frozen checklist of the review, or <c>null</c> when it has none.</param>
/// <param name="SectionStates">The stored applicability of the checklist sections.</param>
/// <param name="Answers">The stored checklist answers.</param>
/// <param name="Threads">All threads of the review with their comments.</param>
/// <param name="Source">Where the files came from; <see cref="ReviewSource.Upload"/> for an upload.</param>
public sealed record ReviewRecord(
    string ContentFingerprint,
    ReviewChecklist? Checklist,
    IReadOnlyList<ChecklistSectionState> SectionStates,
    IReadOnlyList<ChecklistAnswer> Answers,
    IReadOnlyList<ReviewThread> Threads,
    ReviewSource Source);

/// <summary>
///     One consistent read of a review for the approval status: the record a signature covers, together with the
///     reviewer slots and the signatures as they were at the same moment.
/// </summary>
/// <remarks>
///     The store reads all three inside one read transaction, so the digest of <paramref name="Record"/> and the
///     signatures compared with it belong to the same state of the review. The snapshot is an immutable value
///     holder and is safe to share between threads.
/// </remarks>
/// <param name="Record">The record a signature covers.</param>
/// <param name="Slots">The reviewer slots ordered by identity.</param>
/// <param name="Signatures">The signatures of all slots ordered by identity.</param>
public sealed record ReviewSnapshot(
    ReviewRecord Record,
    IReadOnlyList<ReviewerSlot> Slots,
    IReadOnlyList<ReviewSignature> Signatures);
