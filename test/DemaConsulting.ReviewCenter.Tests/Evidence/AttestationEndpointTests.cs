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
using DemaConsulting.ReviewCenter.Cli;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Hosting;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Tests of the attestation download endpoint over HTTP, against the real pipeline on an in-memory server:
///     who may download, which status codes it answers with, and what the downloaded file is.
/// </summary>
public class AttestationEndpointTests
{
    /// <summary>
    ///     Test that a visitor who is not signed in is sent to the login page and gets no document.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task AttestationEndpoint_Get_SignedOut_RedirectsToLogin()
    {
        // Arrange: an application with a complete review and a visitor who is not signed in
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        await using var app = await StartAsync(keys);
        var projectId = await CreateReviewAsync(app, signed: true);
        var requestsAtStart = keys.Requests;
        using var browser = new TestBrowser(app);

        // Act: request the attestation
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);

        // Assert: redirected to the login page, nothing generated and the key not used
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Equal(requestsAtStart, keys.Requests);
        Assert.Empty(await AttestationEventsAsync(app));
    }

    /// <summary>
    ///     Test that a review or project that does not exist answers 404.
    /// </summary>
    /// <param name="projectOffset">The number to add to the project number.</param>
    /// <param name="reviewId">The review identifier to ask for.</param>
    [Theory(Timeout = 180000)]
    [InlineData(0, "NOPE")]
    [InlineData(99, "REV-1")]
    public async Task AttestationEndpoint_Get_UnknownReview_Answers404(int projectOffset, string reviewId)
    {
        // Arrange: an application with a complete review and a signed-in user
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        await using var app = await StartAsync(keys);
        var projectId = await CreateReviewAsync(app, signed: true);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: request the attestation of a review that does not exist
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId + projectOffset, reviewId), TestContext.Current.CancellationToken);

        // Assert: not found, nothing recorded
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await AttestationEventsAsync(app));
    }

    /// <summary>
    ///     Test that a review that is not complete answers 409 Conflict with the reason, and no document.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task AttestationEndpoint_Get_ReviewNotSigned_Answers409()
    {
        // Arrange: an application with a review that nobody has signed
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        await using var app = await StartAsync(keys);
        var projectId = await CreateReviewAsync(app, signed: false);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: request the attestation
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: conflict with the reason, not a PDF, nothing recorded
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("not complete", body, StringComparison.Ordinal);
        Assert.Empty(await AttestationEventsAsync(app));
    }

    /// <summary>
    ///     Test that a review that changes while the document is being prepared answers 409 Conflict with the
    ///     "changed" problem, no document, and no audit event.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task AttestationEndpoint_Get_ReviewChangesDuringGeneration_Answers409AndRecordsNothing()
    {
        // Arrange: an application whose attestation service changes the checklist answers after rendering
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var projectId = 0;
        await using var app = await StartAsync(
            keys,
            services => services.AddSingleton(provider => new AttestationService(
                provider.GetRequiredService<IReviewStore>(),
                provider.GetRequiredService<SigningService>(),
                provider.GetRequiredService<IProjectAccess>(),
                provider.GetRequiredService<AuditService>(),
                provider.GetRequiredService<IAttestationKeyProvider>(),
                provider.GetRequiredService<IAttestationRenderer>(),
                provider.GetRequiredService<IAttestationSigner>(),
                provider.GetRequiredService<TimeProvider>())
            {
                AfterRender = async cancellationToken =>
                {
                    var checklists = provider.GetRequiredService<ChecklistService>();
                    foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
                    {
                        await checklists.SetAnswerAsync(
                            projectId, "REV-1", item.Id, ChecklistOutcome.Fail, "Changed while rendering.", new Actor("ann", "Ann"), cancellationToken);
                    }
                }
            }));
        projectId = await CreateReviewAsync(app, signed: true);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: request the attestation
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: conflict that says the review changed, not a PDF, nothing recorded
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("changed", body, StringComparison.Ordinal);
        Assert.Empty(await AttestationEventsAsync(app));
    }

    /// <summary>
    ///     Test that a complete review gives a PDF download whose signature verifies against the organization key,
    ///     and that the download is recorded in the audit trail.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task AttestationEndpoint_Get_CompleteReview_ReturnsSignedPdfDownloadAndRecordsEvent()
    {
        // Arrange: an application whose key is an organization key, a complete review, a signed-in user
        using var key = TestKeys.Organization();
        var thumbprint = key.Info.Thumbprint;
        using var keys = new FakeAttestationKeyProvider(key);
        await using var app = await StartAsync(keys);
        var projectId = await CreateReviewAsync(app, signed: true);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: download the attestation
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // Assert: a download of a PDF, signed by the key, that is the review
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"attestation-{projectId}-REV-1.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        var pdf = new PdfInspector(bytes);
        Assert.True(pdf.ByteRangeCoversEverythingButContents());
        Assert.Equal(thumbprint, pdf.VerifySignature().SignerInfos[0].Certificate!.Thumbprint);
        Assert.Contains("Apollo", pdf.Text, StringComparison.Ordinal);

        // Assert: the download is in the audit trail
        var entry = Assert.Single(await AttestationEventsAsync(app));
        Assert.Equal("Cyd (cyd)", entry.ActorName);
        Assert.Equal(projectId, entry.ProjectId);
        Assert.Equal("REV-1", entry.ReviewId);
        Assert.Contains($"key {thumbprint}", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the application's own key, the self-signed one that the server creates when nothing is
    ///     configured, signs the attestation.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task AttestationEndpoint_Get_DefaultConfiguration_SignsWithTheServerGeneratedSelfSignedKey()
    {
        // Arrange: an application with the default configuration, a complete review, a signed-in user
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await CreateReviewAsync(app, signed: true);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: download the attestation
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // Assert: signed by a self-signed certificate that says it is not an organization key
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pdf = new PdfInspector(bytes);
        var certificate = pdf.VerifySignature().SignerInfos[0].Certificate!;
        Assert.Equal(certificate.Subject, certificate.Issuer);
        Assert.Contains("not an organization key", certificate.Subject, StringComparison.Ordinal);
        Assert.Contains("SELF-SIGNED KEY - NOT AN ORGANIZATION KEY", pdf.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a key that stops working after start-up answers 500 with the problem, and records nothing.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task AttestationEndpoint_Get_KeyBecomesUnavailable_Answers500AndRecordsNothing()
    {
        // Arrange: an application whose key works at start-up and then fails, and a complete review
        using var keys = new FailingAfterStartKeyProvider(TestKeys.SelfSignedRsa());
        await using var app = await StartAsync(keys);
        var projectId = await CreateReviewAsync(app, signed: true);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");
        keys.Broken = true;

        // Act: request the attestation
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert: a server error that names the problem, no document, nothing recorded
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("The key store is locked.", body, StringComparison.Ordinal);
        Assert.Empty(await AttestationEventsAsync(app));
    }

    /// <summary>
    ///     Starts the application with a key provider of the test's choosing.
    /// </summary>
    /// <param name="keys">The key provider that replaces the configured one.</param>
    /// <param name="configureServices">Further changes to the services; none when <c>null</c>.</param>
    /// <returns>The started application; the caller disposes it.</returns>
    private static async Task<WebApplication> StartAsync(IAttestationKeyProvider keys, Action<IServiceCollection>? configureServices = null)
    {
        using var context = Context.Create(["serve", "--silent"]);
        var app = ServerHost.CreateApplication(context, new ServerHostOptions
        {
            MapStaticAssets = false,
            ConfigureBuilder = builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Logging.AddProvider(new CapturingLoggerProvider());
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestDatabase.NewConnectionString()
                });
                builder.Services.AddSingleton(keys);
                configureServices?.Invoke(builder.Services);
            }
        });
        await app.StartAsync();
        return app;
    }

    /// <summary>
    ///     Creates the project and the review <c>REV-1</c> with every check answered and one slot (ann, bob), signed or not.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="signed">Whether ann signs the slot, which completes the review.</param>
    /// <returns>The project number.</returns>
    internal static async Task<int> CreateReviewAsync(WebApplication app, bool signed)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = app.Services.GetRequiredService<UserService>();
        var ids = new Dictionary<string, int>();
        foreach (var (username, name, admin) in new[] { ("ann", "Ann", true), ("bob", "Bob", false) })
        {
            ids[username] = (await users.CreateAsync(username, name, admin, TestBrowser.Password, cancellationToken)).Id;
        }

        var project = await app.Services.GetRequiredService<ProjectService>().CreateAsync("Apollo", null, cancellationToken);
        var store = app.Services.GetRequiredService<IReviewStore>();
        await store.AddMembershipAsync(ids["ann"], project.Id, cancellationToken);
        await store.AddMembershipAsync(ids["bob"], project.Id, cancellationToken);
        await app.Services.GetRequiredService<ReviewService>().CreateDraftAsync(
            project.Id, "REV-1", "First review", [new UploadedFile("a.txt", "one\ntwo\n"u8.ToArray())], cancellationToken);
        var ann = new Actor("ann", "Ann");
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await app.Services.GetRequiredService<ChecklistService>().SetAnswerAsync(
                project.Id, "REV-1", item.Id, ChecklistOutcome.Pass, null, ann, cancellationToken);
        }

        var signing = app.Services.GetRequiredService<SigningService>();
        var slot = await signing.AddSlotAsync(ann, project.Id, "REV-1", ids["ann"], ids["bob"], cancellationToken);
        if (signed)
        {
            var approval = await signing.GetApprovalAsync(project.Id, "REV-1", cancellationToken);
            await signing.SignAsync(ann, project.Id, "REV-1", slot.Id, approval!.Digest, TestBrowser.Password, cancellationToken);
        }

        return project.Id;
    }

    /// <summary>
    ///     Reads the attestation events of the audit trail.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The events, newest first.</returns>
    private static async Task<IReadOnlyList<AuditEntry>> AttestationEventsAsync(WebApplication app)
    {
        var page = await app.Services.GetRequiredService<AuditService>().ListAsync(null, null, null, null, TestContext.Current.CancellationToken);
        return page.Entries.Where(e => e.Action == AttestationService.AuditAction).ToList();
    }

    /// <summary>
    ///     A key provider whose key works until a test breaks it, as when a smart card is removed while the server runs.
    /// </summary>
    private sealed class FailingAfterStartKeyProvider : IAttestationKeyProvider, IDisposable
    {
        /// <summary>
        ///     The provider that supplies the key while it works.
        /// </summary>
        private readonly FakeAttestationKeyProvider _working;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FailingAfterStartKeyProvider"/> class.
        /// </summary>
        /// <param name="key">The key; the provider disposes it.</param>
        public FailingAfterStartKeyProvider(AttestationSigningKey key)
        {
            _working = new FakeAttestationKeyProvider(key);
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the key no longer works.
        /// </summary>
        public bool Broken { get; set; }

        /// <inheritdoc />
        public string Name => "Failing";

        /// <inheritdoc />
        public AttestationSigningKey GetSigningKey()
        {
            return Broken ? throw new AttestationKeyException("The key store is locked.") : _working.GetSigningKey();
        }

        /// <inheritdoc />
        public AttestationKeyStatus Describe()
        {
            return Broken ? new AttestationKeyStatus(Name, false, null, "The key store is locked.") : _working.Describe();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _working.Dispose();
        }
    }
}
