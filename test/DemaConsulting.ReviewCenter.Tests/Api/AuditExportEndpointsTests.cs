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

using System.Net;
using System.Text;
using System.Text.Json;
using DemaConsulting.ReviewCenter.Api;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Tests of the audit export endpoints over HTTP, against the real pipeline on an in-memory server: who may
///     export, the headers and the body of the file, that the query filter is honored and hostile values are safe, and
///     that every export is audited.
/// </summary>
public class AuditExportEndpointsTests
{
    /// <summary>
    ///     What a test arranged: the identities of the two projects.
    /// </summary>
    /// <param name="Apollo">The identity of the project the users belong to.</param>
    /// <param name="Gemini">The identity of the other project.</param>
    private sealed record Arranged(int Apollo, int Gemini);

    /// <summary>
    ///     Creates the projects Apollo and Gemini, the project admin <c>padm</c> and the plain member <c>mem</c> of
    ///     Apollo, the outsider <c>out</c> and the system administrator <c>root</c>, and records one event for each
    ///     project and one without a project.
    /// </summary>
    /// <param name="app">The started application.</param>
    /// <returns>The identities of the projects.</returns>
    private static async Task<Arranged> ArrangeAsync(WebApplication app)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = app.Services.GetRequiredService<UserService>();
        var store = app.Services.GetRequiredService<IReviewStore>();
        var audit = app.Services.GetRequiredService<AuditService>();
        var padm = await users.CreateAsync("padm", "Padm", UserRole.Reviewer, TestBrowser.Password, cancellationToken);
        var mem = await users.CreateAsync("mem", "Mem", UserRole.Reviewer, TestBrowser.Password, cancellationToken);
        await users.CreateAsync("out", "Out", UserRole.Reviewer, TestBrowser.Password, cancellationToken);
        await users.CreateAsync("root", "Root", UserRole.Admin, TestBrowser.Password, cancellationToken);
        var apollo = await TestWebApp.CreateProjectAsync(app, "Apollo");
        var gemini = await TestWebApp.CreateProjectAsync(app, "Gemini");
        await store.AddMembershipAsync(padm.Id, apollo.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await store.AddMembershipAsync(mem.Id, apollo.Id, ProjectRole.User, cancellationToken);
        await audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "apollo event", apollo.Id), cancellationToken);
        await audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "gemini event", gemini.Id), cancellationToken);
        await audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "global event"), cancellationToken);
        return new Arranged(apollo.Id, gemini.Id);
    }

    /// <summary>
    ///     Starts an application with a new database.
    /// </summary>
    /// <param name="connectionString">The connection string to use; <c>null</c> for a new one.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    private static Task<WebApplication> StartAsync(string? connectionString = null)
    {
        return TestWebApp.StartAsync(new CapturingLoggerProvider(), connectionString, ["serve", "--silent"]);
    }

    /// <summary>
    ///     Gets a response header value, or <c>null</c> when it is not there.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="name">The header name.</param>
    /// <returns>The value.</returns>
    private static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
    }

    /// <summary>
    ///     Reads the body of a response as text, without the byte order mark.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The text.</returns>
    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }

    /// <summary>
    ///     Test that a project admin gets a CSV of the events of the project with the headers that name the chain
    ///     status, forbid caching and sniffing, and offer the file for download.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_ProjectExport_ProjectAdmin_ServesCsvWithHeaders()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);

        // Act
        var response = await browser.Client.GetAsync($"/projects/{arranged.Apollo}/audit/export", TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var text = await TextAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Matches(
            $"^audit-project-{arranged.Apollo}-\\d{{8}}T\\d{{6}}Z-chain-intact\\.csv$",
            response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? string.Empty);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("intact", Header(response, "X-ReviewCenter-Audit-Chain"));
        Assert.Null(Header(response, "X-ReviewCenter-Audit-Chain-Broken-At"));
        Assert.StartsWith("sequence,timestampUtc,actor,action,project,review,detail,previousHash,hash\r\n", text, StringComparison.Ordinal);
        Assert.Contains("apollo event", text, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini event", text, StringComparison.Ordinal);
        Assert.DoesNotContain("global event", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a system administrator can export the audit trail of any project, and the whole trail.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_SystemAdmin_ExportsAnyProjectAndTheWholeTrail()
    {
        // Arrange: root is no member of any project
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "root");

        // Act
        var project = await browser.Client.GetAsync($"/projects/{arranged.Gemini}/audit/export", TestContext.Current.CancellationToken);
        var everything = await browser.Client.GetAsync("/audit/export", TestContext.Current.CancellationToken);
        var projectText = await TextAsync(project);
        var everythingText = await TextAsync(everything);

        // Assert
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        Assert.Contains("gemini event", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("apollo event", projectText, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, everything.StatusCode);
        Assert.Equal("intact", Header(everything, "X-ReviewCenter-Audit-Chain"));
        Assert.Matches("^audit-all-\\d{8}T\\d{6}Z-chain-intact\\.csv$", everything.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? string.Empty);
        Assert.Contains("apollo event", everythingText, StringComparison.Ordinal);
        Assert.Contains("gemini event", everythingText, StringComparison.Ordinal);
        Assert.Contains("global event", everythingText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the JSON format answers a valid document with the chain status and the entries.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_JsonFormat_ServesEnvelopeWithChainStatus()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);

        // Act
        var response = await browser.Client.GetAsync($"/projects/{arranged.Apollo}/audit/export?format=json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".json\"", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        Assert.Equal("intact", Header(response, "X-ReviewCenter-Audit-Chain"));
        Assert.Equal("intact", document.RootElement.GetProperty("chain").GetProperty("status").GetString());
        Assert.Equal(arranged.Apollo, document.RootElement.GetProperty("export").GetProperty("filter").GetProperty("projectId").GetInt32());
        Assert.Contains(
            document.RootElement.GetProperty("entries").EnumerateArray(),
            e => e.GetProperty("detail").GetString() == "apollo event");
    }

    /// <summary>
    ///     Test that the filter of the query is honored, and that the export is itself audited and can be found by
    ///     the next export.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_QueryFilter_IsHonoredAndTheExportIsAudited()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);
        var path = $"/projects/{arranged.Apollo}/audit/export";

        // Act: one export that leaves an entry, then one that asks for exactly those entries, and some that match nothing
        await browser.Client.GetAsync(path, TestContext.Current.CancellationToken);
        var exported = await TextAsync(await browser.Client.GetAsync(path + "?action=audit.exported&actor=padm", TestContext.Current.CancellationToken));
        var noActor = await TextAsync(await browser.Client.GetAsync(path + "?actor=nobody-like-that", TestContext.Current.CancellationToken));
        var future = await TextAsync(await browser.Client.GetAsync(path + "?from=2999-01-01", TestContext.Current.CancellationToken));
        var reversed = await browser.Client.GetAsync(path + "?from=2030-02-01&to=2030-01-01", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("audit.exported", exported, StringComparison.Ordinal);
        Assert.Contains("csv export, ", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("apollo event", exported, StringComparison.Ordinal);
        Assert.Single(noActor.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(future.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));

        // A reversed range falls back to no dates, exactly as on the page
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        var audit = app.Services.GetRequiredService<AuditService>();
        var page = await audit.ListAsync(new AuditFilter(ProjectId: arranged.Apollo, Action: "audit.exported"), null, null, 50, TestContext.Current.CancellationToken);
        Assert.Equal(5, page.Entries.Count);
        Assert.All(page.Entries, e => Assert.Equal("Padm (padm)", e.ActorName));
    }

    /// <summary>
    ///     Test that the project of the address wins: a project in the query is ignored on the project route.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_ProjectRoute_IgnoresProjectInQuery()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);

        // Act
        var text = await TextAsync(await browser.Client.GetAsync(
            $"/projects/{arranged.Apollo}/audit/export?project={arranged.Gemini}", TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("apollo event", text, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini event", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the whole-trail export takes the project from the query.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_GlobalExport_ProjectInQuery_LimitsAndScopesTheAuditEntry()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "root");

        // Act
        var text = await TextAsync(await browser.Client.GetAsync($"/audit/export?project={arranged.Gemini}", TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("gemini event", text, StringComparison.Ordinal);
        Assert.DoesNotContain("apollo event", text, StringComparison.Ordinal);
        var page = await app.Services.GetRequiredService<AuditService>().ListAsync(
            new AuditFilter(ProjectId: arranged.Gemini, Action: "audit.exported"), null, null, 50, TestContext.Current.CancellationToken);
        Assert.Single(page.Entries);
    }

    /// <summary>
    ///     Test that a project member without the right to read the audit trail gets 403 and no file, and that a user
    ///     who cannot see the project (or a missing project) gets the same 404.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="missing">Whether to ask for a project that does not exist.</param>
    /// <param name="expected">The expected status.</param>
    [Theory(Timeout = 120000)]
    [InlineData("mem", false, HttpStatusCode.Forbidden)]
    [InlineData("out", false, HttpStatusCode.NotFound)]
    [InlineData("padm", true, HttpStatusCode.NotFound)]
    public async Task AuditExportEndpoints_ProjectExport_NotAllowed_Answers403Or404(string username, bool missing, HttpStatusCode expected)
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, username, UserRole.Reviewer);

        // Act
        var response = await browser.Client.GetAsync(
            $"/projects/{(missing ? 9999 : arranged.Apollo)}/audit/export", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: no file, no chain header, no entry
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(Header(response, "X-ReviewCenter-Audit-Chain"));
        Assert.NotEqual("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("apollo event", body, StringComparison.Ordinal);
        var page = await app.Services.GetRequiredService<AuditService>().ListAsync(
            new AuditFilter(Action: "audit.exported"), null, null, 50, TestContext.Current.CancellationToken);
        Assert.Empty(page.Entries);
    }

    /// <summary>
    ///     Test that a member whose role is auditor may read and export the audit trail of the project (200 with the
    ///     file) but only that of the project, while a plain member still gets 403.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_ProjectExport_Auditor_ServesTheProjectTrailOnly()
    {
        // Arrange: an auditor of Apollo
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        var aud = await app.Services.GetRequiredService<UserService>().CreateAsync(
            "aud", "Aud", UserRole.Reviewer, TestBrowser.Password, TestContext.Current.CancellationToken);
        await app.Services.GetRequiredService<IReviewStore>().AddMembershipAsync(
            aud.Id, arranged.Apollo, ProjectRole.Auditor, TestContext.Current.CancellationToken);
        using var browser = await TestBrowser.SignedInAsync(app, "aud", UserRole.Reviewer);

        // Act
        var own = await browser.Client.GetAsync($"/projects/{arranged.Apollo}/audit/export", TestContext.Current.CancellationToken);
        var other = await browser.Client.GetAsync($"/projects/{arranged.Gemini}/audit/export", TestContext.Current.CancellationToken);
        var everything = await browser.Client.GetAsync("/audit/export", TestContext.Current.CancellationToken);
        var text = await TextAsync(own);

        // Assert
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("apollo event", text, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini event", text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, everything.StatusCode);
    }

    /// <summary>
    ///     Test that a visitor who is not signed in is sent to the login page by both routes and gets no file.
    /// </summary>
    /// <param name="path">The route to request.</param>
    [Theory(Timeout = 120000)]
    [InlineData("/projects/1/audit/export")]
    [InlineData("/audit/export")]
    public async Task AuditExportEndpoints_SignedOut_RedirectsToLogin(string path)
    {
        // Arrange
        await using var app = await StartAsync();
        await ArrangeAsync(app);
        using var browser = new TestBrowser(app);

        // Act
        var response = await browser.Client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Null(Header(response, "X-ReviewCenter-Audit-Chain"));
    }

    /// <summary>
    ///     Test that the whole-trail export is for system administrators only: a project admin and a member get 403.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    [Theory(Timeout = 120000)]
    [InlineData("padm")]
    [InlineData("mem")]
    [InlineData("out")]
    public async Task AuditExportEndpoints_GlobalExport_NotSystemAdmin_Answers403(string username)
    {
        // Arrange
        await using var app = await StartAsync();
        await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, username, UserRole.Reviewer);

        // Act
        var response = await browser.Client.GetAsync("/audit/export", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Header(response, "X-ReviewCenter-Audit-Chain"));
        Assert.DoesNotContain("apollo event", body, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that hostile query values never fail the export: they fall back as on the page.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_HostileQueryValues_AreSafe()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);
        var hostile = "?actor=" + Uri.EscapeDataString("<script>alert(1)</script>'; DROP TABLE AuditEntries;--") +
                      "&action=" + Uri.EscapeDataString("nope\r\nSet-Cookie: x=y") +
                      "&review=" + new string('r', 5000) +
                      "&from=" + Uri.EscapeDataString("2025-13-45") +
                      "&to=%00" +
                      "&size=-1&before=abc&after=%FF&project=-5&format=CSV";
        var unusable = "?from=" + Uri.EscapeDataString("2025-13-45") + "&to=%00&size=-1&before=abc&after=%FF&project=-5&format=CSV";

        // Act
        var response = await browser.Client.GetAsync($"/projects/{arranged.Apollo}/audit/export{hostile}", TestContext.Current.CancellationToken);
        var text = await TextAsync(response);
        var dropped = await browser.Client.GetAsync($"/projects/{arranged.Apollo}/audit/export{unusable}", TestContext.Current.CancellationToken);
        var droppedText = await TextAsync(dropped);

        // Assert: the text filters are plain values that match nothing; the unusable values were dropped
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("intact", Header(response, "X-ReviewCenter-Audit-Chain"));
        Assert.DoesNotContain("apollo event", text, StringComparison.Ordinal);
        Assert.Null(Header(response, "Set-Cookie"));
        Assert.Equal(HttpStatusCode.OK, dropped.StatusCode);
        Assert.Contains("apollo event", droppedText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unknown format is a 400 problem, and that the answer does not come before the access check.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_UnknownFormat_Answers400ToAllowedUsersOnly()
    {
        // Arrange
        await using var app = await StartAsync();
        var arranged = await ArrangeAsync(app);
        using var admin = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);
        using var outsider = await TestBrowser.SignedInAsync(app, "out", UserRole.Reviewer);
        var path = $"/projects/{arranged.Apollo}/audit/export?format=xml";

        // Act
        var allowed = await admin.Client.GetAsync(path, TestContext.Current.CancellationToken);
        var refused = await outsider.Client.GetAsync(path, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    /// <summary>
    ///     Test that a broken chain is exported and reported in the headers with its first broken sequence, in the
    ///     file name and in the document.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_TamperedChain_ReportsBrokenInHeadersAndFileName()
    {
        // Arrange: forge the detail of the first entry of the database
        var connectionString = TestDatabase.NewConnectionString();
        await using var app = await StartAsync(connectionString);
        var arranged = await ArrangeAsync(app);
        using var browser = await TestBrowser.SignedInAsync(app, "padm", UserRole.Reviewer);
        using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AuditEntries SET Detail = 'forged' WHERE Sequence = 1";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // Act
        var response = await browser.Client.GetAsync($"/projects/{arranged.Apollo}/audit/export?format=json", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("broken", Header(response, "X-ReviewCenter-Audit-Chain"));
        Assert.Equal("1", Header(response, "X-ReviewCenter-Audit-Chain-Broken-At"));
        Assert.Contains("-chain-broken.json", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        Assert.Equal("broken", document.RootElement.GetProperty("chain").GetProperty("status").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("chain").GetProperty("firstBrokenSequence").GetInt64());
    }

    /// <summary>
    ///     Test that the export routes are not part of the token API document.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task AuditExportEndpoints_Routes_AreNotInTheApiDocument()
    {
        // Arrange
        await using var app = await StartAsync();
        using var browser = new TestBrowser(app);

        // Act
        var response = await browser.Client.GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        var document = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("/audit/export", document, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a write to a closed connection after the client went away ends the request quietly, and that the
    ///     export is still audited as incomplete.
    /// </summary>
    [Fact]
    public async Task AuditExportEndpoints_StreamAsync_ClientAbortedDuringWrite_EndsQuietlyAndAuditsIncomplete()
    {
        // Arrange: the connection fails on the first write and the request is aborted at the same time
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var store = new FakeAuditStore(clock);
        var audit = new AuditService(store, null, clock);
        using var abort = new CancellationTokenSource();
        var http = new DefaultHttpContext();
        http.RequestAborted = abort.Token;
        http.Response.Body = new ClosedConnectionStream(abort);

        // Act
        var result = await AuditExportEndpoints.StreamAsync(http, audit, AuditFilter.None, "csv", TestActors.Ann);

        // Assert
        Assert.Same(Results.Empty, result);
        var last = store.Entries[^1];
        Assert.Equal(AuditService.ExportedAction, last.Action);
        Assert.Equal("csv export, 0 rows, incomplete (cancelled or failed); no filter", last.Detail);
    }

    /// <summary>
    ///     Test that a failed write is not hidden when the client did not abort the request.
    /// </summary>
    [Fact]
    public async Task AuditExportEndpoints_StreamAsync_WriteFailsWithoutAbort_IsNotSwallowed()
    {
        // Arrange
        var clock = new FixedTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var store = new FakeAuditStore(clock);
        var audit = new AuditService(store, null, clock);
        var http = new DefaultHttpContext();
        http.Response.Body = new ClosedConnectionStream(null);

        // Act / Assert
        await Assert.ThrowsAsync<IOException>(
            () => AuditExportEndpoints.StreamAsync(http, audit, AuditFilter.None, "csv", TestActors.Ann));
        Assert.Contains("incomplete", store.Entries[^1].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A response body whose first write reports an aborted request (when given a source) and then fails like a
    ///     write to a closed connection.
    /// </summary>
    private sealed class ClosedConnectionStream : Stream
    {
        /// <summary>
        ///     The source to cancel before failing, or <c>null</c> when the request is not aborted.
        /// </summary>
        private readonly CancellationTokenSource? _abort;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ClosedConnectionStream"/> class.
        /// </summary>
        /// <param name="abort">The source to cancel before the first write fails, or <c>null</c>.</param>
        public ClosedConnectionStream(CancellationTokenSource? abort)
        {
            _abort = abort;
        }

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("the connection is gone");

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _abort?.Cancel();
            return ValueTask.FromException(new IOException("the connection is gone"));
        }
    }
}
