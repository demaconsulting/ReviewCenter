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

using System.Text;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Tests for <see cref="AuditExportWriter"/>: the CSV and JSON formats of an audit export, without any HTTP or
///     store.
/// </summary>
public class AuditExportWriterTests
{
    /// <summary>
    ///     The time every test export was generated.
    /// </summary>
    private static readonly DateTimeOffset Generated = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    /// <summary>
    ///     Creates an export description for a writer.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <param name="filter">The filter, or <c>null</c> for none.</param>
    /// <param name="chain">The chain result, or <c>null</c> for an intact one.</param>
    /// <returns>The export.</returns>
    private static AuditExport Export(AuditExportFormat format, AuditFilter? filter = null, AuditVerificationResult? chain = null)
    {
        var intact = new AuditVerificationResult(true, 42, null, "head-value");
        return new AuditExport(format, filter ?? AuditFilter.None, TestActors.Ann, chain ?? intact, 42, Generated);
    }

    /// <summary>
    ///     Creates an entry whose text fields are the given ones.
    /// </summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="actor">The actor text.</param>
    /// <param name="detail">The detail.</param>
    /// <param name="action">The action.</param>
    /// <param name="review">The review id, or <c>null</c>.</param>
    /// <param name="project">The project, or <c>null</c>.</param>
    /// <returns>The entry.</returns>
    private static AuditEntry Entry(
        long sequence,
        string actor = "Ann (ann)",
        string detail = "detail",
        string action = "test.event",
        string? review = null,
        int? project = null)
    {
        return new AuditEntry
        {
            Sequence = sequence,
            TimestampUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.FromHours(2)),
            ActorName = actor,
            Action = action,
            ProjectId = project,
            ReviewId = review,
            Detail = detail,
            PreviousHash = "prev" + sequence,
            Hash = "hash" + sequence
        };
    }

    /// <summary>
    ///     Writes a whole document into memory.
    /// </summary>
    /// <param name="export">The export.</param>
    /// <param name="entries">The entries, written as one batch.</param>
    /// <returns>The bytes written.</returns>
    private static async Task<byte[]> WriteAsync(AuditExport export, params AuditEntry[] entries)
    {
        using var writer = new AuditExportWriter(export);
        using var target = new MemoryStream();
        writer.WriteStart();
        await writer.FlushToAsync(target, TestContext.Current.CancellationToken);
        writer.WriteEntries(entries);
        await writer.FlushToAsync(target, TestContext.Current.CancellationToken);
        writer.WriteEnd();
        await writer.FlushToAsync(target, TestContext.Current.CancellationToken);
        return target.ToArray();
    }

    /// <summary>
    ///     Writes a CSV document and returns its text without the byte order mark.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The text.</returns>
    private static async Task<string> CsvAsync(params AuditEntry[] entries)
    {
        var bytes = await WriteAsync(Export(AuditExportFormat.Csv), entries);
        return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
    }

    /// <summary>
    ///     Gets the text cell of the only entry of a CSV document by its column index.
    /// </summary>
    /// <param name="csv">The CSV text with a header and one entry.</param>
    /// <param name="column">The zero-based column.</param>
    /// <returns>The raw cell text (quotes included).</returns>
    private static string CellOfOnlyRow(string csv, int column)
    {
        // The first line is the header; the entry cells used here contain no quoted comma before the column asked for
        var line = csv.Split("\r\n")[1];
        return line.Split(',')[column];
    }

    /// <summary>
    ///     Test that a CSV starts with the UTF-8 byte order mark and the header, and ends every line with CRLF.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_StartsWithBomAndHeader_AndUsesCrlf()
    {
        // Arrange / Act
        var bytes = await WriteAsync(Export(AuditExportFormat.Csv), Entry(1), Entry(2));

        // Assert
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        Assert.StartsWith("sequence,timestampUtc,actor,action,project,review,detail,previousHash,hash\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a row holds the values in column order with an ISO 8601 UTC timestamp and the hashes as they are.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_Row_HasColumnsInOrderAndUtcTimestamp()
    {
        // Arrange / Act
        var text = await CsvAsync(Entry(7, review: "r1", project: 5));

        // Assert: 03:04:05.678 at +02:00 is 01:04:05.678 UTC
        Assert.Equal(
            "7,2026-01-02T01:04:05.6780000Z,Ann (ann),test.event,5,r1,detail,prev7,hash7\r\n",
            text.Split("\r\n", 2)[1]);
    }

    /// <summary>
    ///     Test that a cell with a comma, a quote or a line break is quoted and its quotes are doubled (RFC 4180).
    /// </summary>
    /// <param name="detail">The detail text.</param>
    /// <param name="expected">The expected cell.</param>
    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("two\r\nlines", "\"two\r\nlines\"")]
    [InlineData("plain text", "plain text")]
    public void AuditExportWriter_Quote_SpecialCharacters_QuotedPerRfc4180(string detail, string expected)
    {
        // Arrange / Act / Assert
        Assert.Equal(expected, AuditExportWriter.Quote(detail));
    }

    /// <summary>
    ///     Test that a detail with a comma, a quote and a line break survives inside a row as one quoted cell.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_DetailWithCommaQuoteAndNewline_IsOneQuotedCell()
    {
        // Arrange / Act
        var text = await CsvAsync(Entry(1, detail: "a,\"b\"\nc"));

        // Assert
        Assert.Contains(",\"a,\"\"b\"\"\nc\",prev1,hash1\r\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a text that a spreadsheet could take as a formula gets a single quote in front.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected cell text.</param>
    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("+1", "'+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tindented", "'\tindented")]
    [InlineData("\rreturn", "'\rreturn")]
    [InlineData("benign = text", "benign = text")]
    [InlineData("a-b", "a-b")]
    [InlineData("", "")]
    [InlineData("'already", "'already")]
    [InlineData("Zoë 日本語", "Zoë 日本語")]
    public void AuditExportWriter_Neutralize_LeadingFormulaCharacter_IsPrefixed(string text, string expected)
    {
        // Arrange / Act / Assert
        Assert.Equal(expected, AuditExportWriter.Neutralize(text));
    }

    /// <summary>
    ///     Test that every text cell is neutralized, while the hashes, the sequence and the timestamp are not touched.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_TextCells_AreNeutralizedAndGeneratedCellsAreNot()
    {
        // Arrange
        var entry = Entry(3, actor: "=evil", detail: "+1", action: "-act", review: "@rev");

        // Act
        var text = await CsvAsync(entry);

        // Assert
        Assert.Equal("3,2026-01-02T01:04:05.6780000Z,'=evil,'-act,,'@rev,'+1,prev3,hash3\r\n", text.Split("\r\n", 2)[1]);
    }

    /// <summary>
    ///     Test that unicode text is written as UTF-8 and survives a round trip.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_Unicode_IsWrittenAsUtf8()
    {
        // Arrange / Act
        var bytes = await WriteAsync(Export(AuditExportFormat.Csv), Entry(1, actor: "Zoë 日本語 😀"));

        // Assert
        Assert.Contains("Zoë 日本語 😀", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an empty result is a CSV with the header only.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_NoEntries_IsHeaderOnly()
    {
        // Arrange / Act
        var text = await CsvAsync();

        // Assert
        Assert.Equal("sequence,timestampUtc,actor,action,project,review,detail,previousHash,hash\r\n", text);
    }

    /// <summary>
    ///     Test that the project cell is empty for an entry that names no project.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Csv_NoProject_LeavesProjectCellEmpty()
    {
        // Arrange / Act
        var text = await CsvAsync(Entry(1));

        // Assert
        Assert.Equal(string.Empty, CellOfOnlyRow(text, 4));
        Assert.Equal(string.Empty, CellOfOnlyRow(text, 5));
    }

    /// <summary>
    ///     Test the JSON envelope: the export description, the chain status and the entries.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Json_Envelope_HasExportChainAndEntries()
    {
        // Arrange
        var filter = new AuditFilter(ProjectId: 5, ReviewId: "r1", ActorText: "ann", ActionPrefix: "review.", From: new DateOnly(2026, 1, 1));

        // Act
        var bytes = await WriteAsync(Export(AuditExportFormat.Json, filter), Entry(1, review: "r1", project: 5), Entry(2));

        // Assert: no byte order mark and a valid document
        Assert.NotEqual(0xEF, bytes[0]);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Assert.Equal("2026-02-03T04:05:06.0000000Z", root.GetProperty("export").GetProperty("generatedUtc").GetString());
        var shown = root.GetProperty("export").GetProperty("filter");
        Assert.Equal(5, shown.GetProperty("projectId").GetInt32());
        Assert.Equal("r1", shown.GetProperty("review").GetString());
        Assert.Equal("ann", shown.GetProperty("actor").GetString());
        Assert.Equal("review.", shown.GetProperty("actionPrefix").GetString());
        Assert.Equal("2026-01-01", shown.GetProperty("from").GetString());
        Assert.False(shown.TryGetProperty("to", out _));
        Assert.False(shown.TryGetProperty("action", out _));
        Assert.Equal("intact", root.GetProperty("chain").GetProperty("status").GetString());
        var entries = root.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        var first = entries[0];
        Assert.Equal(1, first.GetProperty("sequence").GetInt64());
        Assert.Equal("2026-01-02T01:04:05.6780000Z", first.GetProperty("timestampUtc").GetString());
        Assert.Equal("Ann (ann)", first.GetProperty("actor").GetString());
        Assert.Equal("test.event", first.GetProperty("action").GetString());
        Assert.Equal(5, first.GetProperty("project").GetInt32());
        Assert.Equal("r1", first.GetProperty("review").GetString());
        Assert.Equal("detail", first.GetProperty("detail").GetString());
        Assert.Equal("prev1", first.GetProperty("previousHash").GetString());
        Assert.Equal("hash1", first.GetProperty("hash").GetString());
        Assert.Equal(JsonValueKind.Null, entries[1].GetProperty("project").ValueKind);
        Assert.Equal(JsonValueKind.Null, entries[1].GetProperty("review").ValueKind);
    }

    /// <summary>
    ///     Test that JSON text is escaped and that formula characters are not changed (JSON is no spreadsheet).
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Json_HostileText_IsEscapedAndRoundTrips()
    {
        // Arrange
        const string hostile = "=\"quoted\" \\ </script> \n\t Zoë 日本語 😀";

        // Act
        var bytes = await WriteAsync(Export(AuditExportFormat.Json), Entry(1, detail: hostile));

        // Assert
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(hostile, document.RootElement.GetProperty("entries")[0].GetProperty("detail").GetString());
        Assert.DoesNotContain("</script>", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an empty result is a valid JSON document with an empty entries array.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Json_NoEntries_IsValidDocument()
    {
        // Arrange / Act
        var bytes = await WriteAsync(Export(AuditExportFormat.Json));

        // Assert
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(0, document.RootElement.GetProperty("entries").GetArrayLength());
        Assert.Empty(document.RootElement.GetProperty("export").GetProperty("filter").EnumerateObject());
    }

    /// <summary>
    ///     Test that a broken chain is reported with its first broken sequence and, for neither status, with a count
    ///     or the head hash (the chain is global, so they would reveal other projects).
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_Json_BrokenChain_NamesFirstBrokenSequenceAndNoCounts()
    {
        // Arrange
        var broken = new AuditVerificationResult(false, 9, 6, "secret-head");

        // Act
        var brokenBytes = await WriteAsync(Export(AuditExportFormat.Json, null, broken));
        var intactBytes = await WriteAsync(Export(AuditExportFormat.Json));

        // Assert
        using var brokenDocument = JsonDocument.Parse(brokenBytes);
        var chain = brokenDocument.RootElement.GetProperty("chain");
        Assert.Equal("broken", chain.GetProperty("status").GetString());
        Assert.Equal(6, chain.GetProperty("firstBrokenSequence").GetInt64());
        Assert.Equal(["status", "firstBrokenSequence"], chain.EnumerateObject().Select(p => p.Name).ToArray());
        using var intactDocument = JsonDocument.Parse(intactBytes);
        Assert.Equal(["status"], intactDocument.RootElement.GetProperty("chain").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.DoesNotContain("secret-head", Encoding.UTF8.GetString(brokenBytes), StringComparison.Ordinal);
        Assert.DoesNotContain("head-value", Encoding.UTF8.GetString(intactBytes), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the batches of a document are passed on one by one: the buffer is emptied by every flush.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_FlushToAsync_EmptiesTheBufferEachTime()
    {
        // Arrange
        using var writer = new AuditExportWriter(Export(AuditExportFormat.Csv));
        using var target = new MemoryStream();
        writer.WriteStart();
        await writer.FlushToAsync(target, TestContext.Current.CancellationToken);
        var afterStart = target.Length;

        // Act: nothing was written, so nothing is passed on
        await writer.FlushToAsync(target, TestContext.Current.CancellationToken);
        var afterIdle = target.Length;
        writer.WriteEntries([Entry(1)]);
        await writer.FlushToAsync(target, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(afterStart, afterIdle);
        Assert.True(target.Length > afterIdle);
    }

    /// <summary>
    ///     Test that the writer and its helpers refuse null arguments.
    /// </summary>
    [Fact]
    public async Task AuditExportWriter_NullArguments_Throw()
    {
        // Arrange
        using var writer = new AuditExportWriter(Export(AuditExportFormat.Csv));

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new AuditExportWriter(null!));
        Assert.Throws<ArgumentNullException>(() => writer.WriteEntries(null!));
        Assert.Throws<ArgumentNullException>(() => AuditExportWriter.Neutralize(null!));
        Assert.Throws<ArgumentNullException>(() => AuditExportWriter.Quote(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.FlushToAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the writer can be disposed, for both formats and more than once, and that it cannot write
    ///     afterwards.
    /// </summary>
    [Theory]
    [InlineData(AuditExportFormat.Csv)]
    [InlineData(AuditExportFormat.Json)]
    public void AuditExportWriter_Dispose_ReleasesBuffersAndIsRepeatable(AuditExportFormat format)
    {
        // Arrange
        var writer = new AuditExportWriter(Export(format));
        writer.WriteStart();

        // Act
        writer.Dispose();
        var again = Record.Exception(writer.Dispose);

        // Assert: a second call is harmless, and the buffers are gone, so a further write is refused
        Assert.Null(again);
        Assert.NotNull(Record.Exception(() => writer.WriteEntries([Entry(1)])));
    }

    /// <summary>
    ///     Test that the file name holds the scope, the UTC time and the chain status, and the content type and
    ///     extension follow the format.
    /// </summary>
    [Fact]
    public void AuditExport_FileNameAndContentType_FollowScopeFormatAndStatus()
    {
        // Arrange
        var broken = new AuditVerificationResult(false, 3, 2, "h");

        // Act
        var all = Export(AuditExportFormat.Csv);
        var project = Export(AuditExportFormat.Json, new AuditFilter(ProjectId: 12), broken);

        // Assert
        Assert.Equal("audit-all-20260203T040506Z-chain-intact.csv", all.FileName);
        Assert.Equal("text/csv; charset=utf-8", all.ContentType);
        Assert.Equal("audit-project-12-20260203T040506Z-chain-broken.json", project.FileName);
        Assert.Equal("application/json", project.ContentType);
        Assert.Equal(2, project.FirstBrokenSequence);
        Assert.Equal("broken", project.ChainStatus);
    }
}
