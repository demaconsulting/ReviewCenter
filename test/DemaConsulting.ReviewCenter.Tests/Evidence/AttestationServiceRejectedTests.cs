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
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Evidence;

/// <summary>
///     Unit tests for <see cref="AttestationService"/> with a concluded review: the attestation of a rejection, the
///     findings and comments that reach the report, the ballots appendix and what is never printed.
/// </summary>
public class AttestationServiceRejectedTests
{
    /// <summary>
    ///     The text of the finding that is included in the report.
    /// </summary>
    private const string FindingText = "Boundary check missing";

    /// <summary>
    ///     The note of the resolution of the finding, included in the report.
    /// </summary>
    private const string ResolutionText = "Boundary check added";

    /// <summary>
    ///     The reply to the finding that was not included in the report.
    /// </summary>
    private const string HiddenReplyText = "Internal chatter about lunch";

    /// <summary>
    ///     The text of the chat thread, which is never part of the report.
    /// </summary>
    private const string ChatText = "Question about the build server";

    /// <summary>
    ///     The starting comment of the Question thread, which is not included in the report.
    /// </summary>
    private const string QuestionText = "Why is the page size four kilobytes";

    /// <summary>
    ///     The reply to the Question that is included in the report.
    /// </summary>
    private const string IncludedReplyText = "Because the page size is 4096.";

    /// <summary>
    ///     The reply to the Question that is not included in the report.
    /// </summary>
    private const string HiddenQuestionReplyText = "Thanks, I will note that privately";

    /// <summary>
    ///     Test that the attestation of a rejection has the conclusion with its reasons, the included finding with
    ///     its resolution, the ballots appendix with the final ballot of the slot, and the ballot fingerprint; and that
    ///     neither the chat thread nor a reply that was not included is printed.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RejectedWithFindings_PrintsConclusionFindingsAndAppendixOnly()
    {
        // Arrange: a review with an included finding (resolved with a note), a hidden reply and a chat thread, rejected
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await ResolveThreadsAsync(fixture);
        await fixture.RejectAsync("Not ready: the boundary handling is wrong.");
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, cancellationToken);

        // Assert: the banner, the conclusion with its reasons, the finding with its resolution
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var pdf = new PdfInspector(result.Content!);
        var lines = pdf.TextLines;
        Assert.Contains("Review attestation - REJECTED", lines);
        Assert.Contains("Conclusion: Rejected", lines);
        Assert.Contains("Reasons for rejection", lines);
        Assert.Contains("Not ready: the boundary handling is wrong.", lines);
        Assert.Contains(FindingText, lines);
        Assert.Contains(lines, l => l.StartsWith("Finding by Ann, 2025-03-04", StringComparison.Ordinal));
        Assert.Contains(ResolutionText, lines);
        Assert.Contains("Status: Resolved", lines);

        // Assert: the chat thread and the reply that was not included are not printed anywhere
        Assert.DoesNotContain(lines, l => l.Contains(ChatText, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(HiddenReplyText, StringComparison.Ordinal));

        // Assert: the ballots appendix names the slot and the answers with their justification
        Assert.Contains("Ballots appendix", lines);
        Assert.Contains("Slot 1: Ann (ann)", lines);
        Assert.Contains(lines, l => l.StartsWith("Justification: Changed after the signature.", StringComparison.Ordinal));
        Assert.Contains("Ballot fingerprint", lines);
        Assert.Contains("(ReviewCenter ballots v1)", lines);
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that the ballot fingerprint in the attestation is the fingerprint of the stored record, so a reader can
    ///     recompute it from the ballots of the review.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_Rejected_BallotFingerprintMatchesRecord()
    {
        // Arrange: a rejected review
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await fixture.RejectAsync("Not ready.");
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);
        var snapshot = await fixture.Store.GetSnapshotAsync(fixture.ProjectId, AttestationFixture.ReviewId, cancellationToken);
        var expected = RecordDigest.ComputeBallotFingerprint(snapshot!.Record);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, cancellationToken);

        // Assert: the 64 hexadecimal characters appear on their own line
        var lines = new PdfInspector(result.Content!).TextLines;
        Assert.Equal(64, expected.Length);
        Assert.Contains(expected, string.Concat(lines.Where(l => l.All(Uri.IsHexDigit))));
    }

    /// <summary>
    ///     Test that the attestation of an approval carries the conclusion and the ballots appendix too, and is not
    ///     marked as rejected.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_Approved_PrintsConclusionAndBallotsAppendixWithoutRejectedMark()
    {
        // Arrange: an approved review
        var fixture = await AttestationFixture.CreateAsync(signed: true);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Ann, TestContext.Current.CancellationToken);

        // Assert
        var lines = new PdfInspector(result.Content!).TextLines;
        Assert.Contains("Review attestation", lines);
        Assert.DoesNotContain("Review attestation - REJECTED", lines);
        Assert.Contains("Conclusion: Approved", lines);
        Assert.Contains("Ballots appendix", lines);
        Assert.Contains("Slot 1: Ann (ann)", lines);
        Assert.DoesNotContain("Reasons for rejection", lines);
    }

    /// <summary>
    ///     Test that a review that is not concluded gives neither a document nor any detail of a ballot, even when
    ///     ballots exist: the answer names no answer, no justification and no outcome.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_OpenReviewWithBallots_RevealsNoBallotDetail()
    {
        // Arrange: a ballot that was handed in with failing answers, but no signature and no conclusion
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await fixture.AnswerAllAsync(ChecklistOutcome.Fail);
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.NotApproved, result.Status);
        Assert.Null(result.Content);
        Assert.DoesNotContain("Changed after the signature", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, keys.Requests);
    }

    /// <summary>
    ///     Test that with the reviewer names off, the ballots appendix and the findings name people by label only.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RejectedNamesOff_AppendixAndFindingsUseLabels()
    {
        // Arrange: a rejected review with a finding, in a project that leaves the reviewer names out
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await ResolveThreadsAsync(fixture);
        await fixture.RejectAsync("Not ready.");
        await SetEvidenceAsync(fixture, EvidenceSettings.Default with { IncludeReviewerNames = false });
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, cancellationToken);

        // Assert: no name anywhere, the labels in the appendix and the finding
        var lines = new PdfInspector(result.Content!).TextLines;
        Assert.DoesNotMatch(@"\b(Ann|ann|Bob|bob|Cyd|cyd)\b", string.Join("\n", lines));
        Assert.Contains("Slot 1: Reviewer 1 (primary)", lines);
        Assert.Contains(lines, l => l.StartsWith("Finding by Reviewer 1 (primary),", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that with the checklist wording off, the ballots appendix prints identifiers and outcomes but neither
    ///     the question text nor the section title.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RejectedWordingOff_AppendixPrintsIdentifiersOnly()
    {
        // Arrange: a rejected review in a project that leaves the checklist wording out
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await fixture.RejectAsync("Not ready.");
        await SetEvidenceAsync(fixture, EvidenceSettings.Default with { IncludeChecklistWording = false });
        var section = DefaultChecklist.Current.Sections[0];
        var item = section.Items[0];
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, cancellationToken);

        // Assert
        var lines = new PdfInspector(result.Content!).TextLines;
        Assert.Contains($"{item.Id} Fail", lines);
        Assert.DoesNotContain(lines, l => l.Contains(item.Text, StringComparison.Ordinal));
        Assert.DoesNotContain(section.Title, lines);
    }

    /// <summary>
    ///     Test that with the discussion off, no finding is printed, and the attestation says that it left them out.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RejectedThreadsOff_PrintsNoFindings()
    {
        // Arrange: a rejected review with a finding, in a project that leaves the discussion out
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await ResolveThreadsAsync(fixture);
        await fixture.RejectAsync("Not ready.");
        await SetEvidenceAsync(fixture, EvidenceSettings.Default with { IncludeThreads = false });
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, cancellationToken);

        // Assert
        var lines = new PdfInspector(result.Content!).TextLines;
        Assert.DoesNotContain(lines, l => l.Contains(FindingText, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Not included by project setting", StringComparison.Ordinal));
        Assert.Contains("Ballots appendix", lines);
    }

    /// <summary>
    ///     Test that a caller without signing in gets the attestation of an approved review but the answer of a
    ///     review that does not exist for a rejected one.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_AnonymousApprovedAndRejected_OnlyApprovedIsServed()
    {
        // Arrange: an approved review and, in another project, a rejected one; both allow anonymous attestations
        var cancellationToken = TestContext.Current.CancellationToken;
        var approved = await AttestationFixture.CreateAsync(signed: true);
        var rejected = await AttestationFixture.CreateAsync(signed: false);
        await rejected.RejectAsync("Not ready.");
        await SetEvidenceAsync(approved, EvidenceSettings.Default with { AnonymousAttestations = true });
        await SetEvidenceAsync(rejected, EvidenceSettings.Default with { AnonymousAttestations = true });
        using var approvedKeys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        using var rejectedKeys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());

        // Act
        var approvedResult = await approved.CreateService(approvedKeys)
            .GenerateAsync(approved.ProjectId, AttestationFixture.ReviewId, null, cancellationToken);
        var rejectedResult = await rejected.CreateService(rejectedKeys)
            .GenerateAsync(rejected.ProjectId, AttestationFixture.ReviewId, null, cancellationToken);

        // Assert
        Assert.Equal(AttestationStatus.Ok, approvedResult.Status);
        Assert.Equal(AttestationStatus.NotFound, rejectedResult.Status);
        Assert.Null(rejectedResult.Content);
        Assert.Equal(0, rejectedKeys.Requests);
    }

    /// <summary>
    ///     Test that a Question thread whose reply was included in the report is printed as a discussion thread with
    ///     only the included reply: neither the starting comment nor the reply that was not included is printed, and
    ///     nothing is presented as a finding.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_QuestionThreadWithIncludedReply_PrintsDiscussionWithOnlyIncludedComments()
    {
        // Arrange: a Question with one included and one hidden reply, resolved, then approved
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await AddQuestionThreadsAsync(fixture);
        await fixture.SignAsync();
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, cancellationToken);

        // Assert: a discussion heading, the included reply, and neither the starter nor the hidden reply
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var pdf = new PdfInspector(result.Content!);
        var lines = pdf.TextLines;
        Assert.Contains("Discussion on a.txt, line 1", lines);
        Assert.Contains(lines, l => l.StartsWith("Reply by Bob,", StringComparison.Ordinal));
        Assert.Contains(IncludedReplyText, lines);
        Assert.DoesNotContain(lines, l => l.Contains(QuestionText, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(HiddenQuestionReplyText, StringComparison.Ordinal));

        // Assert: nothing is printed as a finding, not even under the location of the thread
        Assert.DoesNotContain(lines, l => l.StartsWith("Finding by", StringComparison.Ordinal));
        Assert.DoesNotContain("a.txt, line 1", lines);
        Assert.NotNull(pdf.VerifySignature());
    }

    /// <summary>
    ///     Test that the attestation prints exactly the comments that the record digest binds: every comment of a
    ///     thread with at least one included comment that is itself included, and no other, for a record that mixes
    ///     findings, questions with and without an included reply, a chat-only thread and a finding about a check.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_MixedThreads_PrintsExactlyTheCommentsTheDigestCovers()
    {
        // Arrange: a mixed set of resolved threads, then approved
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        var threads = TestServices.Threads(fixture.Store, fixture.Clock);
        var project = fixture.ProjectId;
        var review = AttestationFixture.ReviewId;
        var finding = await threads.StartThreadAsync(
            project, review, new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, "PARITY finding starter", cancellationToken, includeInReport: true);
        await threads.ReplyAsync(
            project, review, finding.Id, TestActors.Bob, "PARITY finding included reply", cancellationToken, includeInReport: true);
        await threads.ReplyAsync(
            project, review, finding.Id, TestActors.Ann, "PARITY finding hidden reply", cancellationToken, includeInReport: false);
        await threads.ResolveAsync(project, review, finding.Id, TestActors.Ann, cancellationToken, resolutionNote: "PARITY finding resolution");
        var questionOne = await threads.StartThreadAsync(
            project, review, new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, "PARITY question one starter", cancellationToken);
        await threads.ReplyAsync(
            project, review, questionOne.Id, TestActors.Bob, "PARITY question one included reply", cancellationToken, includeInReport: true);
        await threads.ResolveAsync(project, review, questionOne.Id, TestActors.Ann, cancellationToken);
        var questionTwo = await threads.StartThreadAsync(
            project, review, new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, "PARITY question two starter", cancellationToken);
        await threads.ReplyAsync(
            project, review, questionTwo.Id, TestActors.Bob, "PARITY question two hidden reply", cancellationToken, includeInReport: false);
        await threads.ResolveAsync(project, review, questionTwo.Id, TestActors.Ann, cancellationToken);
        var chat = await threads.StartThreadAsync(
            project, review, new ThreadAnchor("a.txt", null, null, ThreadSide.Head), TestActors.Bob, "PARITY chat only", cancellationToken);
        await threads.ResolveAsync(project, review, chat.Id, TestActors.Bob, cancellationToken);
        var check = await threads.StartThreadAsync(
            project, review, ThreadAnchor.ForCheck(DefaultChecklist.Current.Sections[0].Items[0].Id), TestActors.Ann, "PARITY check finding", cancellationToken, includeInReport: true);
        await threads.ResolveAsync(project, review, check.Id, TestActors.Ann, cancellationToken, resolutionNote: "PARITY check resolution");
        await fixture.SignAsync();
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);
        var snapshot = await fixture.Store.GetSnapshotAsync(project, review, cancellationToken);
        var allThreads = snapshot!.Record.Threads;

        // The same predicate as RecordDigest.Canonicalize: a thread with at least one included comment is in the
        // report, and within it only the included comments are
        var reported = allThreads.Where(t => t.Comments.Any(c => c.IncludeInReport)).ToList();
        var expected = reported.SelectMany(t => t.Comments).Where(c => c.IncludeInReport).Select(c => c.Text).ToList();
        var excluded = allThreads.SelectMany(t => t.Comments).Where(c => !c.IncludeInReport).Select(c => c.Text).ToList();
        Assert.Equal(6, expected.Count);
        Assert.Equal(3, reported.Count);

        // Act
        var result = await service.GenerateAsync(project, review, TestActors.Bob, cancellationToken);

        // Assert: every included text is printed and no other text is
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var lines = new PdfInspector(result.Content!).TextLines;
        var text = string.Join("\n", lines);
        foreach (var included in expected)
        {
            Assert.Contains(included, text, StringComparison.Ordinal);
        }

        foreach (var hidden in excluded)
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }

        // Assert: one comment label per included comment and one status line per reported thread
        Assert.Equal(expected.Count, lines.Count(l => Regex.IsMatch(l, "^(Finding|Reply|Resolution) by ")));
        Assert.Equal(reported.Count, lines.Count(l => Regex.IsMatch(l, "Status: (Resolved|Open)$")));
    }

    /// <summary>
    ///     Test that the ballots appendix of a rejection with two slots prints both ballots, each with its own
    ///     justification, and that the ballot fingerprint is the one of the record.
    /// </summary>
    [Fact]
    public async Task AttestationService_GenerateAsync_RejectedTwoSlots_AppendixPrintsBothBallotsWithTheirOwnJustifications()
    {
        // Arrange: two slots whose ballots agree on Fail but word the justification differently, both signed
        var cancellationToken = TestContext.Current.CancellationToken;
        var fixture = await AttestationFixture.CreateAsync(signed: false);
        await fixture.AddSecondSlotWithBallotAsync(ChecklistOutcome.Fail, "Cyd words it differently.");
        await fixture.RejectAsync("Not ready: both reviewers failed it.");
        await fixture.SignSecondSlotAsync();
        using var keys = new FakeAttestationKeyProvider(TestKeys.SelfSignedRsa());
        var service = fixture.CreateService(keys);
        var snapshot = await fixture.Store.GetSnapshotAsync(fixture.ProjectId, AttestationFixture.ReviewId, cancellationToken);
        var expectedFingerprint = RecordDigest.ComputeBallotFingerprint(snapshot!.Record);

        // Act
        var result = await service.GenerateAsync(fixture.ProjectId, AttestationFixture.ReviewId, TestActors.Bob, cancellationToken);

        // Assert: the appendix names both slots in order, with their own justifications
        Assert.Equal(AttestationStatus.Ok, result.Status);
        var lines = new PdfInspector(result.Content!).TextLines.ToList();
        var appendix = lines.IndexOf("Ballots appendix");
        Assert.True(appendix >= 0);
        var after = lines.Skip(appendix).ToList();
        var slotLines = after.Where(l => Regex.IsMatch(l, @"^Slot \d+: ")).ToList();
        Assert.Equal(["Slot 1: Ann (ann)", "Slot 2: Cyd (cyd)"], slotLines);
        Assert.True(after.IndexOf("Slot 1: Ann (ann)") < after.IndexOf("Slot 2: Cyd (cyd)"));
        Assert.Contains(after, l => l.StartsWith("Justification: Changed after the signature.", StringComparison.Ordinal));
        Assert.Contains(after, l => l.StartsWith("Justification: Cyd words it differently.", StringComparison.Ordinal));

        // Assert: the fingerprint printed is the one of the stored record
        Assert.Contains(expectedFingerprint, string.Concat(lines.Where(l => l.All(Uri.IsHexDigit))));
    }

    /// <summary>
    ///     Starts a Question (its starting comment is not included) with a reply by Bob that is included in the report
    ///     and a reply by Ann that is not, on <c>a.txt</c> line 1, and resolves it.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>A task that completes when the thread is resolved.</returns>
    private static async Task AddQuestionThreadsAsync(AttestationFixture fixture)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var threads = TestServices.Threads(fixture.Store, fixture.Clock);
        var question = await threads.StartThreadAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, QuestionText, cancellationToken);
        await threads.ReplyAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, question.Id, TestActors.Bob, IncludedReplyText, cancellationToken, includeInReport: true);
        await threads.ReplyAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, question.Id, TestActors.Ann, HiddenQuestionReplyText, cancellationToken, includeInReport: false);
        await threads.ResolveAsync(fixture.ProjectId, AttestationFixture.ReviewId, question.Id, TestActors.Ann, cancellationToken);
    }

    /// <summary>
    ///     Starts the threads of the scenario and resolves them: an included finding with a resolution note and a hidden
    ///     reply, and a chat thread.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>A task that completes when every thread is resolved.</returns>
    private static async Task ResolveThreadsAsync(AttestationFixture fixture)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var threads = TestServices.Threads(fixture.Store, fixture.Clock);
        var finding = await threads.StartThreadAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, new ThreadAnchor("a.txt", 1, 1), TestActors.Ann, FindingText, cancellationToken, includeInReport: true);
        await threads.ReplyAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, finding.Id, TestActors.Bob, HiddenReplyText, cancellationToken, includeInReport: false);
        await threads.ResolveAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, finding.Id, TestActors.Ann, cancellationToken, resolutionNote: ResolutionText);
        var chat = await threads.StartThreadAsync(
            fixture.ProjectId, AttestationFixture.ReviewId, new ThreadAnchor("a.txt", null, null, ThreadSide.Head), TestActors.Bob, ChatText, cancellationToken);
        await threads.ResolveAsync(fixture.ProjectId, AttestationFixture.ReviewId, chat.Id, TestActors.Bob, cancellationToken);
    }

    /// <summary>
    ///     Changes the evidence settings of the project of the fixture.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="settings">The settings to store.</param>
    /// <returns>A task that completes when the settings are stored.</returns>
    private static async Task SetEvidenceAsync(AttestationFixture fixture, EvidenceSettings settings)
    {
        Assert.True(await fixture.Store.UpdateEvidenceSettingsAsync(
            fixture.ProjectId,
            EvidenceSettings.Default,
            settings,
            AuditDraft.By(TestActors.Ann, EvidenceSettingsService.ChangedAction, "x", fixture.ProjectId),
            TestContext.Current.CancellationToken));
    }
}
