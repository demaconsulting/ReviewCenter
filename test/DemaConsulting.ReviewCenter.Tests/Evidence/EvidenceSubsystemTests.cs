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

using System.Text.RegularExpressions;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Subsystem tests for the Evidence subsystem: the pieces (key provider, renderer, signer, service and endpoint)
///     working together in the real application, from a completed review to a signed PDF a person downloads.
/// </summary>
public class EvidenceSubsystemTests
{
    /// <summary>
    ///     The review identifier of the review that the shared arrangement creates.
    /// </summary>
    private const string ReviewId = "REV-1";

    /// <summary>
    ///     The text of the finding that the arrangements with a finding include in the report.
    /// </summary>
    private const string FindingText = "Please double check the loop bound";

    /// <summary>
    ///     The names of the people of the arrangement, as whole words in any case.
    /// </summary>
    private static readonly Regex _people = new(@"\b(ann|bob|zed|cyd)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(10));

    /// <summary>
    ///     The attestation of the approved review of the shared arrangement, downloaded once for all tests of the class
    ///     that only read it: starting the application and downloading the document is the same every time.
    /// </summary>
    private static readonly Lazy<Task<PdfInspector>> ApprovedPdf = new(async () =>
    {
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        return await DownloadPdfAsync(app, projectId, CancellationToken.None);
    });

    /// <summary>
    ///     Test the complete path through the real application: a review is completed, the review page offers the
    ///     attestation, the download is a PDF signed by the server's self-signed key that holds the review, and the
    ///     download is in the audit trail.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_IsOfferedAndDownloadedAsSignedPdf()
    {
        // Arrange: the real application with its default (self-signed) key, a complete review, a signed-in user
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: open the review page, then follow the offered link
        var page = await browser.GetPageAsync(ReviewLinks.ForReview(projectId, "REV-1"));
        var button = page.QuerySelector("[data-testid='attestation-download-button']");
        var response = await browser.Client.GetAsync(button?.GetAttribute("href"), TestContext.Current.CancellationToken);
        var pdf = new PdfInspector(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        // Assert: the page offered the download, the file is the signed attestation of the review
        Assert.NotNull(button);
        Assert.Equal(ReviewLinks.ForAttestation(projectId, "REV-1"), button.GetAttribute("href"));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(pdf.VerifySignature());
        Assert.True(pdf.ByteRangeCoversEverythingButContents());
        Assert.Contains("Apollo", pdf.Text, StringComparison.Ordinal);
        Assert.Contains("a.txt", pdf.Text, StringComparison.Ordinal);
        Assert.Contains("SELF-SIGNED KEY - NOT AN ORGANIZATION KEY", pdf.Text, StringComparison.Ordinal);

        // Assert: the download is in the audit trail
        var trail = await app.Services.GetRequiredService<AuditService>().ListAsync(null, null, null, null, TestContext.Current.CancellationToken);
        Assert.Single(trail.Entries, e => e.Action == AttestationService.AuditAction);
    }

    /// <summary>
    ///     Test that a review that is not complete is not offered and cannot be downloaded through the real application.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_ReviewNotComplete_IsNotOfferedAndRefused()
    {
        // Arrange: the real application, a review that nobody has signed, a signed-in user
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");

        // Act: open the review page and ask for the download anyway
        var page = await browser.GetPageAsync(ReviewLinks.ForReview(projectId, "REV-1"));
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, "REV-1"), TestContext.Current.CancellationToken);

        // Assert: no button, and a conflict
        Assert.Null(page.QuerySelector("[data-testid='attestation-download-button']"));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states the review, its content fingerprint, and its
    ///     files.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesReviewFingerprintAndFiles()
    {
        // Arrange: the fingerprint of the one file of the review, computed independently of the review
        var fingerprint = Fingerprint.Compute([System.Text.Encoding.UTF8.GetBytes("one\ntwo\n")]);

        // Act: the downloaded document of the approved review
        var lines = (await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken)).TextLines;

        // Assert: the review, its content fingerprint, and the list of its files
        Assert.Contains("Review", lines);
        Assert.Contains(ReviewId, lines);
        Assert.Contains("Apollo (#1)", lines);
        Assert.Contains("Content fingerprint", lines);
        Assert.Contains(fingerprint, lines);
        Assert.Contains("Files (1)", lines);
        Assert.Contains("a.txt", lines);
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states the checklist: the consolidated answers and
    ///     who answered each agreed check.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesChecklistWithWhoAnswered()
    {
        // Arrange: the first check of the default checklist
        var first = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();

        // Act: the downloaded document of the approved review
        var lines = (await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken)).TextLines;

        // Assert: the checklist section, the answer of the check, and who answered it and when
        Assert.Contains("Checklist", lines);
        Assert.Contains(lines, l => l.StartsWith(first.Id, StringComparison.Ordinal) && l.EndsWith("Pass", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Answered by Ann, ", StringComparison.Ordinal) && l.EndsWith(" UTC", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states its conclusion.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesConclusionApproved()
    {
        // Act: the downloaded document of the approved review
        var lines = (await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken)).TextLines;

        // Assert: the conclusion section with the outcome, and no sign of a rejection
        Assert.Contains("Conclusion", lines);
        Assert.Contains("Conclusion: Approved", lines);
        Assert.DoesNotContain("Review attestation - REJECTED", lines);
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review has a ballots appendix with the final ballot of the
    ///     slot and its fingerprint.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesBallotsAppendixWithFingerprint()
    {
        // Act: the downloaded document of the approved review
        var lines = (await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken)).TextLines;

        // Assert: the appendix, the ballot of slot 1 and the fingerprint with the version of its algorithm
        Assert.Contains("Ballots appendix", lines);
        Assert.Contains("Slot 1: Ann (ann)", lines);
        Assert.Contains("Ballot submitted", lines);
        Assert.Contains("Ballot fingerprint", lines);
        Assert.Contains($"({RecordDigest.BallotsHeader})", lines);
        Assert.Contains(lines, l => l.Length == 64 && l.All(Uri.IsHexDigit));
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states the sign-off of the reviewer: who signed,
    ///     when, with which meaning and for which record digest.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesReviewerSignOffs()
    {
        // Act: the downloaded document of the approved review
        var lines = (await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken)).TextLines;

        // Assert: the section, the slot, who signed and when, what the signature means, and the digest it covers
        Assert.Contains("Reviewer sign-offs", lines);
        Assert.Contains("Primary reviewer", lines);
        Assert.Contains("Signed by", lines);
        Assert.Contains(lines, l => l.StartsWith("Ann (", StringComparison.Ordinal));
        Assert.Contains("Signed at", lines);
        Assert.Contains("Meaning", lines);
        Assert.Contains("Record digest", lines);
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states the head of the audit trail.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesAuditTrailHead()
    {
        // Act: the downloaded document of the approved review
        var lines = (await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken)).TextLines.ToList();

        // Assert: the audit section, and a head hash of 64 hexadecimal characters after its label
        Assert.Contains("Audit trail", lines);
        var label = lines.IndexOf("Chain head hash");
        Assert.True(label >= 0, "The document has no 'Chain head hash' row.");
        Assert.Matches("^[0-9a-f]{64}$", lines[label + 1]);
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states the key that signed it, and that the
    ///     thumbprint it states is the one of the certificate in the signature.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_CompletedReview_StatesTheSigningKey()
    {
        // Act: the downloaded document of the approved review and the certificate of its signature
        var pdf = await ApprovedPdf.Value.WaitAsync(TestContext.Current.CancellationToken);
        var lines = pdf.TextLines.ToList();
        var certificate = pdf.VerifySignature().SignerInfos[0].Certificate!;

        // Assert: the section with the key rows, and the thumbprint of the key that made the signature
        Assert.Contains("Digital signature", lines);
        Assert.Contains("Certificate subject", lines);
        Assert.Contains("Key algorithm", lines);
        var label = lines.IndexOf("Thumbprint (SHA-1)");
        Assert.True(label >= 0, "The document has no 'Thumbprint (SHA-1)' row.");
        Assert.Equal(certificate.Thumbprint, lines[label + 1], ignoreCase: true);
    }

    /// <summary>
    ///     Test that the downloaded document of a rejected review is marked as a rejection on the first page: the
    ///     title, and the banner directly under it before the Review section.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_RejectedReview_MarksRejectionOnFirstPage()
    {
        // Arrange: a review whose only reviewer answered every check Fail, with a conclusion text, signed
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var checklists = app.Services.GetRequiredService<ChecklistService>();
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            await checklists.SetAnswerAsync(
                projectId, ReviewId, item.Id, ChecklistOutcome.Fail, "Wrong.", EvidenceTestSupport.Ann, TestContext.Current.CancellationToken);
        }

        await checklists.SetReviewConclusionTextAsync(
            projectId, ReviewId, "Not ready.", EvidenceTestSupport.Ann, TestContext.Current.CancellationToken);
        await SignAsync(app, projectId);

        // Act: download the document
        var lines = (await DownloadPdfAsync(app, projectId, TestContext.Current.CancellationToken)).TextLines.ToList();

        // Assert: the title, the banner on the line after it, and the Review section after the banner, so all of it is
        // at the head of the first page; the conclusion says rejected
        var title = lines.IndexOf("Review attestation - REJECTED");
        var banner = lines.FindIndex(l => l.Length > 30 && AttestationRenderer.RejectedBanner.StartsWith(l, StringComparison.Ordinal));
        var review = lines.IndexOf("Review");
        Assert.True(title >= 0, "The document has no rejected title.");
        Assert.Equal(title + 1, banner);
        Assert.True(review > banner, "The Review section does not follow the banner.");
        Assert.Contains("Conclusion: Rejected", lines);
    }

    /// <summary>
    ///     Test that the downloaded document of a review that was reopened after an approval and approved again holds
    ///     the history of the reopening: when, by whom, why, and the digest that was replaced.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_ReopenedAndApprovedAgain_StatesReopenHistory()
    {
        // Arrange: an approved review that an administrator other than the signer reopened and that was signed again
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: true);
        await app.Services.GetRequiredService<UserService>().CreateAsync("dee", "Dee", true, TestBrowser.Password, TestContext.Current.CancellationToken);
        var signing = app.Services.GetRequiredService<SigningService>();
        var previous = (await signing.GetApprovalAsync(projectId, ReviewId, TestContext.Current.CancellationToken))!.Digest;
        await signing.ReopenReviewAsync(new Actor("dee", "Dee"), projectId, ReviewId, "Missed a case.", TestContext.Current.CancellationToken);
        await SignAsync(app, projectId);

        // Act: download the document
        var lines = (await DownloadPdfAsync(app, projectId, TestContext.Current.CancellationToken)).TextLines.ToList();

        // Assert: the section with who reopened, why, and the previous digest
        Assert.Contains("Reopen history", lines);
        Assert.Contains("Reopened by", lines);
        Assert.Contains("Dee (dee)", lines);
        Assert.Contains("Missed a case.", lines);
        Assert.Contains("Previous digest", lines);
        Assert.Contains(previous, string.Concat(lines.Where(l => l.All(Uri.IsHexDigit))), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the downloaded document of a review leaves out the reviewer names, the checklist wording and the
    ///     findings when the evidence settings of the project say so, and says what it left out.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_SettingsLeaveOut_NamesWordingAndFindings_AreLeftOutOfTheDocument()
    {
        // Arrange: a review with an included, resolved finding, signed, in a project that leaves out all three
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await ArrangeReviewWithFindingAsync(app);
        var changed = await app.Services.GetRequiredService<EvidenceSettingsService>().SetAsync(
            EvidenceTestSupport.Ann,
            projectId,
            EvidenceSettings.Default,
            EvidenceSettings.Default with { IncludeReviewerNames = false, IncludeChecklistWording = false, IncludeThreads = false },
            TestContext.Current.CancellationToken);
        var firstItem = DefaultChecklist.Current.Sections.SelectMany(section => section.Items).First();

        // Act: download the document
        var lines = (await DownloadPdfAsync(app, projectId, TestContext.Current.CancellationToken)).TextLines.ToList();
        var text = string.Join("\n", lines);

        // Assert: the setting was saved, and the document names nobody
        Assert.True(changed);
        Assert.DoesNotMatch(_people, text);

        // Assert: the wording of the checks is gone while the identifier and the outcome stay
        Assert.DoesNotContain(firstItem.Text[..30], text, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.StartsWith(firstItem.Id, StringComparison.Ordinal) && l.EndsWith("Pass", StringComparison.Ordinal));

        // Assert: the finding is gone and the document says so
        Assert.DoesNotContain(FindingText, text, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("Not included by project setting", StringComparison.Ordinal));

        // Assert: the document says what it left out
        var leftOut = Assert.Single(lines, l => l.StartsWith("Left out of this document:", StringComparison.Ordinal));
        Assert.Contains("Reviewer names", leftOut, StringComparison.Ordinal);
        Assert.Contains("Checklist wording", leftOut, StringComparison.Ordinal);
        Assert.Contains("Review discussion threads", leftOut, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the downloaded document of an approved review states the findings and the comments that were
    ///     included: the section heading, the comment text and the status of the finding.
    /// </summary>
    [Fact(Timeout = 120000)]
    public async Task EvidenceSubsystem_IncludedFinding_IsStatedInFindingsSection()
    {
        // Arrange: a review with a finding that was included in the report and resolved, signed
        await using var app = await TestWebApp.StartAsync(new CapturingLoggerProvider(), "serve", "--silent");
        var projectId = await ArrangeReviewWithFindingAsync(app);

        // Act: download the document
        var lines = (await DownloadPdfAsync(app, projectId, TestContext.Current.CancellationToken)).TextLines.ToList();

        // Assert: the heading, the finding with its text and its status
        Assert.Contains("Findings and comments in this report", lines);
        Assert.Contains(lines, l => l.StartsWith("Finding by Ann, ", StringComparison.Ordinal));
        Assert.Contains(FindingText, lines);
        Assert.Contains("Status: Resolved", lines);
    }

    /// <summary>
    ///     Downloads the attestation of the review as a signed-in user.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="projectId">The project number.</param>
    /// <param name="cancellationToken">The cancellation token of the test.</param>
    /// <returns>The inspector over the downloaded document.</returns>
    private static async Task<PdfInspector> DownloadPdfAsync(WebApplication app, int projectId, CancellationToken cancellationToken)
    {
        using var browser = await TestBrowser.SignedInAsync(app, "cyd");
        var response = await browser.Client.GetAsync(ReviewLinks.ForAttestation(projectId, ReviewId), cancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return new PdfInspector(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    /// <summary>
    ///     Signs the only slot of the review as ann with the current record.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="projectId">The project number.</param>
    /// <returns>A task that completes when the slot is signed.</returns>
    private static async Task SignAsync(WebApplication app, int projectId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var slot = (await app.Services.GetRequiredService<IReviewStore>().ListSlotsAsync(projectId, ReviewId, cancellationToken))[0];
        await EvidenceTestSupport.SignAsync(app.Services.GetRequiredService<SigningService>(), projectId, ReviewId, slot.Id, EvidenceTestSupport.Ann, TestBrowser.Password);
    }

    /// <summary>
    ///     Arranges the review with a finding that is included in the report and resolved, and signs it.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The project number.</returns>
    private static async Task<int> ArrangeReviewWithFindingAsync(WebApplication app)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectId = await AttestationEndpointTests.CreateReviewAsync(app, signed: false);
        var threads = app.Services.GetRequiredService<ThreadService>();
        var thread = await threads.StartThreadAsync(
            projectId,
            ReviewId,
            new ThreadAnchor("a.txt", 1, 1),
            EvidenceTestSupport.Ann,
            FindingText,
            cancellationToken,
            includeInReport: true);
        await threads.ResolveAsync(projectId, ReviewId, thread.Id, EvidenceTestSupport.Ann, cancellationToken, resolutionNote: "Bound checked");
        await SignAsync(app, projectId);
        return projectId;
    }
}
