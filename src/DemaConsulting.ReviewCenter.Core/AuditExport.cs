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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The file formats an audit export can be written in.
/// </summary>
public enum AuditExportFormat
{
    /// <summary>
    ///     Comma-separated values (RFC 4180), UTF-8 with a byte order mark, for spreadsheets.
    /// </summary>
    Csv,

    /// <summary>
    ///     One JSON document with the export description, the chain status and the entries, for scripts.
    /// </summary>
    Json
}

/// <summary>
///     An export that is ready to be written: what it covers, the verified state of the chain and the snapshot limit.
/// </summary>
/// <remarks>
///     <see cref="AuditService.PrepareExportAsync"/> creates it before the first byte of the body exists, so the
///     transport can put <see cref="ChainStatus"/> and <see cref="FileName"/> into response headers. The chain
///     status is deliberately reduced to intact or broken plus the first broken sequence: the chain is global, so
///     the number of entries and the head hash would reveal facts about other projects to the exporter of one
///     project. The record is immutable and thread-safe.
/// </remarks>
/// <param name="Format">The format to write.</param>
/// <param name="Filter">The validated filter; only entries that match it are written.</param>
/// <param name="Actor">The user who exports; the actor of the audit entry the export leaves.</param>
/// <param name="Chain">The result of verifying the whole chain before the export.</param>
/// <param name="SnapshotSequence">
///     The highest sequence the export may include: the newest sequence when the export was prepared, so entries
///     appended while the export streams (including its own audit entry) are not part of it.
/// </param>
/// <param name="GeneratedUtc">When the export was prepared.</param>
public sealed record AuditExport(
    AuditExportFormat Format,
    AuditFilter Filter,
    Actor Actor,
    AuditVerificationResult Chain,
    long SnapshotSequence,
    DateTimeOffset GeneratedUtc)
{
    /// <summary>
    ///     Gets the text <c>intact</c> or <c>broken</c>, the state of the chain when the export was prepared.
    /// </summary>
    public string ChainStatus => Chain.IsIntact ? "intact" : "broken";

    /// <summary>
    ///     Gets the sequence of the first entry that failed verification; <c>null</c> when the chain is intact.
    /// </summary>
    public long? FirstBrokenSequence => Chain.FirstBadSequence;

    /// <summary>
    ///     Gets the media type of the file, with the character set for text.
    /// </summary>
    public string ContentType => Format == AuditExportFormat.Csv ? "text/csv; charset=utf-8" : "application/json";

    /// <summary>
    ///     Gets the file name extension without the dot.
    /// </summary>
    public string FileExtension => Format == AuditExportFormat.Csv ? "csv" : "json";

    /// <summary>
    ///     Gets the name offered for the download: <c>audit-&lt;scope&gt;-&lt;yyyyMMddTHHmmssZ&gt;-chain-&lt;status&gt;.&lt;ext&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     The scope is <c>project-N</c> for an export limited to a project and <c>all</c> otherwise; the status is
    ///     part of the name so a file that was saved from a broken chain says so wherever it ends up.
    /// </remarks>
    public string FileName
    {
        get
        {
            var scope = Filter.ProjectId is { } project
                ? $"project-{project.ToString(CultureInfo.InvariantCulture)}"
                : "all";
            var stamp = GeneratedUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            return $"audit-{scope}-{stamp}-chain-{ChainStatus}.{FileExtension}";
        }
    }
}
