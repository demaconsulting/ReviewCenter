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

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for the report sections of <see cref="AttestationRenderer"/>: the rejected banner, the conclusion,
///     the findings and the ballots appendix.
/// </summary>
public class AttestationRendererReportTests
{
    /// <summary>
    ///     The fingerprint of the ballots of the sample: 64 hexadecimal characters.
    /// </summary>
    private const string Fingerprint = "9999888877776666555544443333222211110000ffffeeeeddddccccbbbbaaaa";

    /// <summary>
    ///     Creates the sample ballots appendix: slot 1 answered and slot 2 marked a section not applicable.
    /// </summary>
    /// <returns>The appendix.</returns>
    private static List<AttestationBallot> Appendix()
    {
        var at = new DateTimeOffset(2025, 3, 4, 5, 2, 3, TimeSpan.Zero);
        return
        [
            new AttestationBallot(
                1,
                "Ann (ann)",
                at,
                [
                    new AttestationBallotSection(
                        "correctness",
                        "Correctness",
                        true,
                        null,
                        [
                            new AttestationBallotAnswer("C-1", "Does it compile?", "Pass", null, "Ann", at),
                            new AttestationBallotAnswer("C-2", "Are errors handled?", "Fail", "Missing catch", "Ann", at),
                            new AttestationBallotAnswer("C-3", "Is it thread safe?", "Not answered", null, null, null)
                        ])
                ]),
            new AttestationBallot(
                2,
                "Cyd (cyd)",
                null,
                [new AttestationBallotSection("security", "Security", false, "No external input", [])])
        ];
    }

    /// <summary>
    ///     Creates a rejected sample document with the ballots appendix and the conclusion.
    /// </summary>
    /// <param name="content">The evidence settings; the defaults when <c>null</c>.</param>
    /// <returns>The document.</returns>
    private static AttestationDocument Rejected(EvidenceSettings? content = null)
    {
        return AttestationRendererTests.Sample(content: content) with
        {
            Outcome = ReviewOutcome.Rejected,
            ConclusionText = "Not ready: errors are not handled.",
            BallotAppendix = Appendix(),
            BallotFingerprint = Fingerprint
        };
    }

    /// <summary>
    ///     Renders a document and returns the inspector over it.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The inspector.</returns>
    private static PdfInspector Render(AttestationDocument document)
    {
        return new PdfInspector(new AttestationRenderer().Render(document));
    }

    /// <summary>
    ///     Test that a rejected document says so in its title and banner, and that an approved one has neither.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Rejected_ShowsTitleAndBannerOnlyForRejection()
    {
        // Arrange: the same sample rejected and approved
        var rejected = Rejected();
        var approved = AttestationRendererTests.Sample();

        // Act
        var rejectedPdf = Render(rejected);
        var approvedPdf = Render(approved);

        // Assert: the banner may wrap, so it is compared on the joined text
        var rejectedText = string.Join(" ", rejectedPdf.TextLines);
        Assert.Contains("Review attestation - REJECTED", rejectedPdf.TextLines);
        Assert.Contains(AttestationRenderer.RejectedBanner, rejectedText, StringComparison.Ordinal);
        Assert.Contains("concluded as rejected and is signed", rejectedText, StringComparison.Ordinal);
        Assert.Contains("(REJECTED)", rejectedPdf.Info("Title"), StringComparison.Ordinal);
        Assert.DoesNotContain("Review attestation - REJECTED", approvedPdf.TextLines);
        Assert.DoesNotContain("REJECTED", string.Join(" ", approvedPdf.TextLines), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the banner of a rejected document comes directly under the title, before the review section.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Rejected_PrintsBannerBetweenTitleAndReviewSection()
    {
        // Arrange: a rejected document
        var rejected = Rejected();

        // Act: render
        var lines = Render(rejected).TextLines.ToList();

        // Assert: the banner starts on the line after the title, and the review section follows it; the banner is
        // wrapped, so its first line is the start of it
        var title = lines.IndexOf("Review attestation - REJECTED");
        var banner = lines.FindIndex(
            l => l.Length > 30 && AttestationRenderer.RejectedBanner.StartsWith(l, StringComparison.Ordinal));
        var review = lines.IndexOf("Review");
        Assert.True(title >= 0);
        Assert.Equal(title + 1, banner);
        Assert.True(review > banner);
    }

    /// <summary>
    ///     Test that the conclusion prints the outcome and its text: the reasons for a rejection, the conclusion of
    ///     an approval, and no text for a blank one.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_Conclusion_PrintsOutcomeAndText()
    {
        // Arrange: a rejection with reasons, an approval with a conclusion, and an approval without one
        var rejected = Rejected();
        var approved = AttestationRendererTests.Sample() with { ConclusionText = "Looks good." };
        var blank = AttestationRendererTests.Sample() with { ConclusionText = "  " };

        // Act
        var rejectedLines = Render(rejected).TextLines;
        var approvedLines = Render(approved).TextLines;
        var blankLines = Render(blank).TextLines;

        // Assert
        Assert.Contains("Conclusion: Rejected", rejectedLines);
        Assert.Contains("Reasons for rejection", rejectedLines);
        Assert.Contains("Not ready: errors are not handled.", rejectedLines);
        Assert.Contains("Conclusion: Approved", approvedLines);
        Assert.Contains("Looks good.", approvedLines);
        Assert.DoesNotContain("Reasons for rejection", approvedLines);
        Assert.Contains("Conclusion: Approved", blankLines);
        Assert.DoesNotContain(blankLines, l => l.Contains("Looks good.", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the ballots appendix names each slot with its reviewer and submission time, each answer with its
    ///     outcome, justification and answerer, the sections that do not apply, and the fingerprint with its version.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_BallotAppendix_ShowsSlotsAnswersAndFingerprint()
    {
        // Arrange
        var document = Rejected();

        // Act
        var lines = Render(document).TextLines;

        // Assert
        Assert.Contains("Ballots appendix", lines);
        Assert.Contains("Slot 1: Ann (ann)", lines);
        Assert.Contains("2025-03-04 05:02:03 UTC", lines);
        Assert.Contains("C-1 Pass", lines);
        Assert.Contains("C-2 Fail", lines);
        Assert.Contains("Are errors handled?", lines);
        Assert.Contains("Justification: Missing catch", lines);
        Assert.Contains("C-3 Not answered", lines);
        Assert.Contains("Answered by Ann, 2025-03-04 05:02:03 UTC", lines);
        Assert.Contains("Slot 2: Cyd (cyd)", lines);
        Assert.Contains("Not applicable: No external input", lines);
        Assert.Contains(Fingerprint, string.Concat(lines.Where(l => l.All(Uri.IsHexDigit))));
        Assert.Contains("(ReviewCenter ballots v1)", lines);
    }

    /// <summary>
    ///     Test that a document without a ballot appendix, as when the review has no checklist, has no appendix section
    ///     and no fingerprint.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_NoBallotAppendix_OmitsTheSection()
    {
        // Arrange: the sample, which has no appendix
        var document = AttestationRendererTests.Sample();

        // Act
        var lines = Render(document).TextLines;

        // Assert
        Assert.DoesNotContain("Ballots appendix", lines);
        Assert.DoesNotContain("Ballot fingerprint", lines);
    }

    /// <summary>
    ///     Test that with the checklist wording off, the appendix prints identifiers and outcomes only.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_BallotAppendixWordingOff_PrintsIdentifiersOnly()
    {
        // Arrange
        var document = Rejected(EvidenceSettings.Default with { IncludeChecklistWording = false });

        // Act
        var lines = Render(document).TextLines;

        // Assert
        Assert.Contains("C-2 Fail", lines);
        Assert.Contains("Justification: Missing catch", lines);
        Assert.DoesNotContain("Are errors handled?", lines);
        Assert.DoesNotContain("Correctness", lines);
        Assert.DoesNotContain("Security", lines);
    }

    /// <summary>
    ///     Test that a document with no included finding says so, instead of leaving the section empty.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_NoFindings_SaysNoneWereIncluded()
    {
        // Arrange
        var document = AttestationRendererTests.Sample() with { Findings = [] };

        // Act
        var lines = Render(document).TextLines;

        // Assert
        Assert.Contains("Findings and comments in this report", lines);
        Assert.Contains("No finding or comment was included in the report.", lines);
    }

    /// <summary>
    ///     Test that a long comment of a finding is printed in full and in order, with no cut.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_LongFindingComment_IsPrintedInFull()
    {
        // Arrange: a finding with a comment of 300 numbered words
        var words = Enumerable.Range(0, 300).Select(i => $"word{i:D4}").ToList();
        var at = new DateTimeOffset(2025, 3, 4, 5, 1, 0, TimeSpan.Zero);
        var finding = new AttestationFinding(
            "src/a.txt, line 1",
            [new AttestationFindingComment("Finding", "Bob", at, string.Join(' ', words))],
            true);
        var document = AttestationRendererTests.Sample() with { Findings = [finding] };

        // Act
        var text = string.Join(' ', Render(document).TextLines);

        // Assert: every word is there, in order
        var position = 0;
        foreach (var word in words)
        {
            var found = text.IndexOf(word, position, StringComparison.Ordinal);
            Assert.True(found >= position, $"{word} is missing or out of order");
            position = found + word.Length;
        }
    }

    /// <summary>
    ///     Test that each thread of the report shows its status: <c>Status: Resolved</c> for a resolved thread and
    ///     <c>Status: Open</c> for one that is not, each right after its own location.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_FindingStatus_PrintsResolvedAndOpen()
    {
        // Arrange: a resolved finding and an open one
        var at = new DateTimeOffset(2025, 3, 4, 5, 1, 0, TimeSpan.Zero);
        var resolved = new AttestationFinding(
            "src/a.txt, line 1", [new AttestationFindingComment("Finding", "Bob", at, "Resolved one")], true);
        var open = new AttestationFinding(
            "src/b.txt, line 2", [new AttestationFindingComment("Finding", "Bob", at, "Open one")], false);
        var document = AttestationRendererTests.Sample() with { Findings = [resolved, open] };

        // Act
        var lines = Render(document).TextLines.ToList();

        // Assert: one status line of each kind, each after its own location line and before the next one
        Assert.Equal(1, lines.Count(l => l == "Status: Resolved"));
        Assert.Equal(1, lines.Count(l => l == "Status: Open"));
        var firstLocation = lines.IndexOf("src/a.txt, line 1");
        var secondLocation = lines.IndexOf("src/b.txt, line 2");
        Assert.True(firstLocation >= 0 && secondLocation > firstLocation);
        Assert.Equal(firstLocation + 1, lines.IndexOf("Status: Resolved"));
        Assert.Equal(secondLocation + 1, lines.IndexOf("Status: Open"));
    }

    /// <summary>
    ///     Test that a thread whose starting comment was not included is headed as a discussion and is not presented
    ///     as a finding, while its included comment is printed.
    /// </summary>
    [Fact]
    public void AttestationRenderer_Render_DiscussionThread_IsNotPresentedAsFinding()
    {
        // Arrange: a thread whose only included comment is a reply
        var at = new DateTimeOffset(2025, 3, 4, 5, 1, 0, TimeSpan.Zero);
        var discussion = new AttestationFinding(
            "src/a.txt, line 7", [new AttestationFindingComment("Reply", "Bob", at, "The page size is 4096.")], true);
        var document = AttestationRendererTests.Sample() with { Findings = [discussion] };

        // Act
        var lines = Render(document).TextLines.ToList();

        // Assert: the discussion heading, the status note, the reply and its body, and no finding label
        Assert.True(discussion.IsDiscussion);
        Assert.Contains("Discussion on src/a.txt, line 7", lines);
        Assert.Contains(lines, l => l.StartsWith("Discussion thread, not a finding", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Reply by Bob, 2025-03-04", StringComparison.Ordinal));
        Assert.Contains("The page size is 4096.", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Finding by", StringComparison.Ordinal));
        Assert.DoesNotContain("src/a.txt, line 7", lines);
    }
}
