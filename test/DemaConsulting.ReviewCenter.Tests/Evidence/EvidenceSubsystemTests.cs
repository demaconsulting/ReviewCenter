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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Evidence;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Subsystem tests for the Evidence subsystem: the pieces (key provider, renderer, signer, service and endpoint)
///     working together in the real application, from a completed review to a signed PDF a person downloads.
/// </summary>
public class EvidenceSubsystemTests
{
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
}
