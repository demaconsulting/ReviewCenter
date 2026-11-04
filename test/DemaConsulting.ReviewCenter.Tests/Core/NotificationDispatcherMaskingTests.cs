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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="NotificationDispatcher"/>: who is mailed, and that a mail never holds more than the
///     inbox of the recipient may show (the masking of hidden ballots, no outcome, no detail text, no header injection).
/// </summary>
public class NotificationDispatcherMaskingTests
{
    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that a user whose mode is None is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ModeNone_NothingSent()
    {
        // Arrange: ann has a review to answer but chose no e-mail
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.None);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Store.Commits);
    }

    /// <summary>
    ///     Test that a user without a confirmed address is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_NoConfirmedAddress_NothingSent()
    {
        // Arrange: ann has a review to answer, but she is no recipient
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");

        // Act
        var result = await world.Dispatcher.RunPassAsync(Ct);

        // Assert
        Assert.Equal(new NotificationPassResult(0, 0, 0), result);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that a disabled user is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_DisabledUser_NothingSent()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every, isDisabled: true);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Store.Commits);
    }

    /// <summary>
    ///     Test that a user with a temporary password is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_MustChangePassword_NothingSent()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every, mustChangePassword: true);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Store.Commits);
    }

    /// <summary>
    ///     Test that the message goes to the confirmed address and nowhere else.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Due_MessageToConfirmedAddress()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Equal("ann@example.com", message.ToAddress);
        Assert.Equal("Test Center: there is news for you", message.Subject);
    }

    /// <summary>
    ///     Test that the inbox is read as the recipient: another user's review is not in the mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Recipient_ReadsInboxAsRecipient()
    {
        // Arrange: ann and bob each have a review of their own to answer
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r-ann", "ann");
        await world.SeatAsync("r-bob", "bob");
        world.Enroll("ann", NotificationMode.Every);
        world.Enroll("bob", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var toAnn = Assert.Single(sent, m => m.ToAddress == "ann@example.com");
        var toBob = Assert.Single(sent, m => m.ToAddress == "bob@example.com");
        Assert.Contains("r-ann", toAnn.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("r-bob", toAnn.TextBody, StringComparison.Ordinal);
        Assert.Contains("r-bob", toBob.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("r-ann", toBob.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the inbox of a recipient is read as the person and never as an API token: the "Question for you"
    ///     row, which the inbox never shows to an API-token actor, is in the mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Recipient_ActorHasNoApiToken()
    {
        // Arrange: ann holds a slot with her answers handed in, and bob asks a question that she has not written in
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.AnswerAllAsync("r1");
        await world.Inbox.AddSlotAsync("r1", "ann");
        await world.Inbox.StartThreadAsync("r1", "bob", "Why is this here?");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert: an API-token actor of ann gets no such row, so only a read as the person gives this state
        Assert.Contains(sent, m => m.ToAddress == NotificationDispatcherWorld.AddressOf("ann"));
        Assert.Contains($"r1/ToAnswer={NotificationComposer.StateQuestion}", world.NotifiedOf("ann"));
        Assert.All(world.InboxStore.ReadUserIds, id => Assert.Equal(world.IdOf("ann"), id));
    }

    /// <summary>
    ///     Test that a pass that mails a recipient writes no entry to the audit trail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Delivery_WritesNoAuditEntry()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);
        var entries = world.AuditStore.Entries.Count;

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert: a mail went out, and the audit trail did not grow
        Assert.Single(sent);
        Assert.Equal(entries, world.AuditStore.Entries.Count);
    }

    /// <summary>
    ///     Test that a recipient whose own ballot is not handed in gets no disputed item, and so learns nothing of
    ///     the answers of the others, while a recipient who handed in does.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_HiddenBallotRecipient_NoDisputedItem()
    {
        // Arrange: ann and bob handed in and disagree; cyd has not handed in
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.AddSlotAsync("r1", "ann");
        await world.Inbox.AddSlotAsync("r1", "bob");
        await world.Inbox.AddSlotAsync("r1", "cyd");
        await world.Inbox.SubmitBallotAsync("r1", "ann");
        await world.Inbox.SubmitBallotAsync("r1", "bob", true);
        await world.ChangeAsync();
        var disputed = Assert.Single(await world.Inbox.RowsAsync("ann", InboxSection.Disputed));
        world.Enroll("ann", NotificationMode.Every);
        world.Enroll("cyd", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert: cyd's mail only asks for answers; ann's mail names the dispute without its content
        var toCyd = Assert.Single(sent, m => m.ToAddress == "cyd@example.com");
        Assert.Contains("To answer", toCyd.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Answers that differ", toCyd.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("differ", toCyd.HtmlBody, StringComparison.OrdinalIgnoreCase);
        var toAnn = Assert.Single(sent, m => m.ToAddress == "ann@example.com");
        Assert.Contains("Answers that differ", toAnn.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(disputed.Detail!, toAnn.TextBody + toAnn.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", toAnn.TextBody + toAnn.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", toCyd.TextBody + toCyd.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the mail about an approved review says it was concluded, not how.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Concluded_MailHasNoOutcome()
    {
        // Arrange: ann signed first, bob completed the approval
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.ApproveAsync("r1");
        Assert.Equal(InboxService.ApprovedChip, Assert.Single(await world.Inbox.RowsAsync("ann", InboxSection.Updates)).Chip);
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        var all = message.Subject + message.TextBody + message.HtmlBody;
        Assert.Contains("was concluded", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("approved", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rejected", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Signed by", all, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the mail about a rejected review says it was concluded, the same words as for an approval,
    ///     and holds no text of the conclusion.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_Rejected_MailHasNoOutcomeOrConclusionText()
    {
        // Arrange: both slots reject, ann writes the conclusion text and signs first, bob concludes
        var world = await NotificationDispatcherWorld.CreateAsync();
        var inbox = world.Inbox;
        await inbox.CreateReviewAsync("r1");
        await inbox.AnswerAllAsync("r1");
        var first = await inbox.AddSlotAsync("r1", "ann");
        var second = await inbox.AddSlotAsync("r1", "bob");
        await inbox.FailCheckAsync("r1", "ann");
        await inbox.FailCheckAsync("r1", "bob");
        await inbox.Checklists.SetReviewConclusionTextAsync(
            inbox.ProjectId, "r1", "CONCLUSION-SENTINEL-31", InboxTestWorld.ActorOf("ann"), Ct);
        await inbox.SignAsync("r1", first.Id, "ann");
        world.Advance(TimeSpan.FromMinutes(1));
        await inbox.SignAsync("r1", second.Id, "bob");
        await world.ChangeAsync();
        Assert.Equal(InboxService.RejectedChip, Assert.Single(await inbox.RowsAsync("ann", InboxSection.Updates)).Chip);
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        var all = message.Subject + message.TextBody + message.HtmlBody;
        Assert.Contains("was concluded", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("rejected", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approved", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONCLUSION-SENTINEL-31", all, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the detail text of an inbox row, a comment and a check text never reach the mail.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_DetailText_NotInMail()
    {
        // Arrange: ann started a thread and bob replied with a sentinel; the inbox row of ann has a detail text
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.AddSlotAsync("r1", "ann");
        var thread = await world.Inbox.StartThreadAsync("r1", "ann", "THREAD-SENTINEL-11");
        await world.Inbox.ReplyAsync("r1", thread.Id, "bob", "REPLY-SENTINEL-22");
        await world.ChangeAsync();
        var row = Assert.Single(await world.Inbox.RowsAsync("ann", InboxSection.RepliesWaiting));
        Assert.False(string.IsNullOrEmpty(row.Detail));
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        var all = message.Subject + message.TextBody + message.HtmlBody;
        Assert.Contains("1 reply is waiting", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(row.Detail, all, StringComparison.Ordinal);
        Assert.DoesNotContain("THREAD-SENTINEL-11", all, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLY-SENTINEL-22", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", all, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a line break and a header in a title can neither change the subject nor the address.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_TitleWithCrLfBcc_SubjectFixedAndAddressUnchanged()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann", "x\r\nBcc: evil@example.com");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Equal("ann@example.com", message.ToAddress);
        Assert.Equal("Test Center: there is news for you", message.Subject);
        Assert.DoesNotContain("\r\nBcc:", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', message.TextBody);
    }

    /// <summary>
    ///     Test that a line break and a header in a project name can neither change the subject nor the address.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_ProjectNameWithCrLfBcc_SubjectFixedAndAddressUnchanged()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync(projectName: "x\r\nBcc: evil@example.com");
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every);

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert: the project name is in the mail, on one line, and the header holds nothing of it
        var message = Assert.Single(sent);
        Assert.Equal("ann@example.com", message.ToAddress);
        Assert.Equal("Test Center: there is news for you", message.Subject);
        Assert.Contains("Bcc: evil@example.com", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nBcc:", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', message.TextBody);
    }

    /// <summary>
    ///     Test that a line break and a header in a display name can neither change the subject nor the address.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_DisplayNameWithCrLfBcc_SubjectFixedAndAddressUnchanged()
    {
        // Arrange
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.SeatAsync("r1", "ann");
        world.Enroll("ann", NotificationMode.Every, displayName: "Ann\r\nBcc: evil@example.com");

        // Act
        var sent = await world.DeliverEveryAsync();

        // Assert
        var message = Assert.Single(sent);
        Assert.Equal("ann@example.com", message.ToAddress);
        Assert.Equal("Test Center: there is news for you", message.Subject);
        Assert.DoesNotContain("evil@example.com", message.Subject + message.ToAddress, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nBcc:", message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a failed-checks row is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_FailedChecksRow_NotMailed()
    {
        // Arrange: bob fails a check of the review of the author, who has a failed-checks row only
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.CreateReviewAsync("r1");
        await world.Inbox.FailCheckAsync("r1", "bob");
        await world.ChangeAsync();
        Assert.Single(await world.Inbox.RowsAsync("author", InboxSection.FailedChecks));
        world.Enroll("author", NotificationMode.Every);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.NotifiedOf("author"));
    }

    /// <summary>
    ///     Test that a waiting-for-others row is not mailed.
    /// </summary>
    [Fact]
    public async Task NotificationDispatcher_RunPass_WaitingForOthers_NotMailed()
    {
        // Arrange: a review without a checklist leaves ann with a waiting row only
        var world = await NotificationDispatcherWorld.CreateAsync();
        await world.Inbox.Store.AddReviewAsync(
            new Review { ProjectId = world.Inbox.ProjectId, Id = "r-none", Title = "No checklist", Created = InboxTestWorld.Start },
            Ct);
        await world.Inbox.Store.AddSlotAsync(
            new ReviewerSlot { ProjectId = world.Inbox.ProjectId, ReviewId = "r-none", PrimaryUserId = world.IdOf("ann") },
            _ => AuditTestExtensions.Draft(),
            Ct);
        await world.ChangeAsync();
        Assert.Single(await world.Inbox.RowsAsync("ann", InboxSection.WaitingForOthers));
        world.Enroll("ann", NotificationMode.Every);

        // Act
        await world.DeliverEveryAsync();

        // Assert
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.NotifiedOf("ann"));
    }
}
