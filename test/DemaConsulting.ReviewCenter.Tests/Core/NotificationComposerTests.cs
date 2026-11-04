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
using DemaConsulting.ReviewCenter.Web;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="NotificationComposer"/>: the classification of inbox rows, the subject, the items,
///     the links, the encoding of user strings, the limits and the footer.
/// </summary>
public class NotificationComposerTests
{
    /// <summary>
    ///     The address links start with.
    /// </summary>
    private const string BaseUrl = "https://reviews.example.com";

    /// <summary>
    ///     Test that the subject of a message with new items is a fixed text.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Every_SubjectIsFixedText()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.Equal("Test Center: there is news for you", mail.Subject);
    }

    /// <summary>
    ///     Test that the subject of a daily summary is a fixed text.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Daily_SubjectIsFixedText()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Daily);

        // Assert
        Assert.Equal("Test Center: your daily summary", mail.Subject);
    }

    /// <summary>
    ///     Test that no project name, title or review id reaches the subject.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_AnyItems_SubjectHasNoItemText()
    {
        // Arrange
        var composer = new NotificationComposer(null, BaseUrl, true);
        var items = new[]
        {
            Candidate(InboxSection.ToSign, "secret-review", NotificationComposer.StateReady, "Secret Title", "Secret Project")
        };

        // Act
        var every = composer.Compose(items, 0, NotificationMode.Every);
        var daily = composer.Compose(items, 0, NotificationMode.Daily);

        // Assert
        foreach (var subject in new[] { every.Subject, daily.Subject })
        {
            Assert.DoesNotContain("Secret", subject, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-review", subject, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that control characters of the instance name become spaces, so the subject stays on one line.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_InstanceNameWithLineBreak_SubjectSingleLine()
    {
        // Arrange
        var composer = new NotificationComposer("Evil\r\nBcc: x@example.com", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\n', mail.Subject);
        Assert.StartsWith("Evil", mail.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that "Not started" and "Draft" rows of the section to answer are the same state.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_NotStartedAndDraft_SameState()
    {
        // Arrange
        var notStarted = Row(InboxSection.ToAnswer, InboxService.NotStartedChip);
        var draft = Row(InboxSection.ToAnswer, InboxService.DraftChip);

        // Act
        var first = NotificationComposer.Classify(notStarted);
        var second = NotificationComposer.Classify(draft);

        // Assert
        Assert.Equal(NotificationComposer.StateToAnswer, first?.State);
        Assert.Equal(NotificationComposer.StateToAnswer, second?.State);
    }

    /// <summary>
    ///     Test that a question for the user is its own state.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_QuestionChip_QuestionState()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.ToAnswer, InboxService.QuestionChip));

        // Assert
        Assert.Equal(NotificationComposer.StateQuestion, candidate?.State);
    }

    /// <summary>
    ///     Test that a disputed row is classified without its count.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_Disputed_StateHasNoCount()
    {
        // Arrange
        var two = Row(InboxSection.Disputed, "2 disputed");
        var five = Row(InboxSection.Disputed, "5 disputed");

        // Act
        var first = NotificationComposer.Classify(two);
        var second = NotificationComposer.Classify(five);

        // Assert
        Assert.Equal(NotificationComposer.StateDisputed, first?.State);
        Assert.Equal(first?.State, second?.State);
    }

    /// <summary>
    ///     Test that a "Not signed" row is the state ready to sign.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_NotSigned_ReadyState()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.ToSign, InboxService.NotSignedChip));

        // Assert
        Assert.Equal(NotificationComposer.StateReady, candidate?.State);
    }

    /// <summary>
    ///     Test that a "sign again" row is its own state.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_SignAgain_SignAgainState()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.ToSign, InboxService.SignAgainChip));

        // Assert
        Assert.Equal(NotificationComposer.StateSignAgain, candidate?.State);
    }

    /// <summary>
    ///     Test that a lost signature is its own state.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_SignatureLost_LostState()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.SignatureLost, InboxService.SignatureLostChip));

        // Assert
        Assert.Equal(NotificationComposer.StateLost, candidate?.State);
    }

    /// <summary>
    ///     Test that the count of waiting replies is part of the state.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_RepliesWaiting_StateHasCount()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.RepliesWaiting, "3 waiting"));

        // Assert
        Assert.Equal("replies:3", candidate?.State);
    }

    /// <summary>
    ///     Test that an approved update is classified as concluded.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_Approved_Concluded()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.Updates, InboxService.ApprovedChip));

        // Assert
        Assert.Equal(NotificationComposer.StateConcluded, candidate?.State);
    }

    /// <summary>
    ///     Test that a rejected update is classified as concluded, the same as an approved one.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_Rejected_Concluded()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.Updates, InboxService.RejectedChip));

        // Assert
        Assert.Equal(NotificationComposer.StateConcluded, candidate?.State);
    }

    /// <summary>
    ///     Test that a reopened update is its own state.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_Reopened_Reopened()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.Updates, InboxService.ReopenedChip));

        // Assert
        Assert.Equal(NotificationComposer.StateReopened, candidate?.State);
    }

    /// <summary>
    ///     Test that a failed-checks row is not mailed.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_FailedChecks_ReturnsNull()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.FailedChecks, "1 failed"));

        // Assert
        Assert.Null(candidate);
    }

    /// <summary>
    ///     Test that a waiting-for-others row is not mailed.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_WaitingForOthers_ReturnsNull()
    {
        // Arrange, Act
        var candidate = NotificationComposer.Classify(Row(InboxSection.WaitingForOthers, "Waiting"));

        // Assert
        Assert.Null(candidate);
    }

    /// <summary>
    ///     Test that a missing row is a programming error.
    /// </summary>
    [Fact]
    public void NotificationComposer_Classify_NullRow_ThrowsArgumentNull()
    {
        // Arrange, Act and Assert
        Assert.Throws<ArgumentNullException>(() => NotificationComposer.Classify(null!));
    }

    /// <summary>
    ///     Test that the detail sentence of an inbox row never reaches the message.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_DetailSentinel_NotInTextOrHtml()
    {
        // Arrange: a row whose detail holds a sentinel
        var row = Row(InboxSection.ToSign, InboxService.NotSignedChip) with { Detail = "DETAIL-SENTINEL-77" };
        var candidate = NotificationComposer.Classify(row)!;
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([candidate], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain("DETAIL-SENTINEL-77", mail.Subject + mail.Text + mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the words Approved and Rejected appear nowhere in a message about updates.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Updates_NoOutcomeWord()
    {
        // Arrange: an approved and a rejected review
        var approved = NotificationComposer.Classify(Row(InboxSection.Updates, InboxService.ApprovedChip, "r1"))!;
        var rejected = NotificationComposer.Classify(Row(InboxSection.Updates, InboxService.RejectedChip, "r2"))!;
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([approved, rejected], 0, NotificationMode.Daily);

        // Assert
        var all = mail.Subject + mail.Text + mail.Html;
        Assert.DoesNotContain("approved", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rejected", all, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a concluded review is worded as "was concluded".
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Concluded_SaysWasConcluded()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.Updates, "r1", NotificationComposer.StateConcluded)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("was concluded", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a reopened review is worded as "was reopened".
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Reopened_SaysWasReopened()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.Updates, "r1", NotificationComposer.StateReopened)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("was reopened", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a disputed item shows no number.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Disputed_NoCountInBody()
    {
        // Arrange: a row whose chip carries the count 7
        var candidate = NotificationComposer.Classify(Row(InboxSection.Disputed, "7 disputed", "r-dispute"))!;
        var composer = new NotificationComposer("Test Center", BaseUrl, false);

        // Act
        var mail = composer.Compose([candidate], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain("7", mail.Text.Replace("r-dispute", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("disputed", mail.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the number of waiting replies is shown.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Replies_ShowsCount()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.RepliesWaiting, "r1", "replies:4")], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("4 replies are waiting", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the project name of each item is shown.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Item_ShowsProjectName()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer, "T", "Apollo")], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("Apollo / r1", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Apollo / r1", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the title is shown when titles are included.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_IncludeTitlesTrue_ShowsTitle()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer, "Fix the parser")], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("Fix the parser", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the title is left out when titles are not included.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_IncludeTitlesFalse_OmitsTitle()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, false);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer, "Fix the parser")], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain("Fix the parser", mail.Text + mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the review id is shown when titles are not included.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_IncludeTitlesFalse_ShowsReviewId()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, false);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "rev-42", NotificationComposer.StateToAnswer, "Fix the parser")], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("rev-42", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the link of an item is the path the review page uses.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Item_LinkMatchesReviewLinks()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r 1/é", NotificationComposer.StateToAnswer, "T", "Apollo", 12)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains(BaseUrl + ReviewLinks.ForReview(12, "r 1/é"), mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every link starts with the configured address.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Item_LinkStartsWithBaseUrl()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("  " + BaseUrl + "/projects/1/reviews/r1", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a base address that ends with a slash does not double it.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_BaseUrlTrailingSlash_SingleSlash()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl + "/", true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains(BaseUrl + "/projects/1/reviews/r1", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("com//", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that special characters of a review id are escaped in the link.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_ReviewIdWithSpecialChars_LinkEscaped()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "a b?c#d", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("/reviews/a%20b%3Fc%23d", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a link carries no query string and no token.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Item_LinkHasNoQuery()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert: no question mark in the whole text, because every link and sentence has none
        Assert.DoesNotContain('?', mail.Text);
        Assert.DoesNotContain("token", mail.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a title with a line break stays on one line in the text part.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_TitleWithLineBreak_TextPartSingleLine()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer, "one\r\nBcc: evil@example.com")], 0, NotificationMode.Every);

        // Assert: the title and what follows it are on the line of the item
        var line = mail.Text.Split('\n').Single(l => l.Contains("Bcc:", StringComparison.Ordinal));
        Assert.StartsWith("- ", line, StringComparison.Ordinal);
        Assert.Contains("one  Bcc: evil@example.com", line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the line separator and the paragraph separator of Unicode in a title become spaces in both parts.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_TitleWithUnicodeLineSeparators_BecomeSpaces()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer, "one\u2028two\u2029three")], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain('\u2028', mail.Text);
        Assert.DoesNotContain('\u2029', mail.Text);
        Assert.DoesNotContain('\u2028', mail.Html);
        Assert.DoesNotContain('\u2029', mail.Html);
        Assert.Contains("one two three", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that markup in a title is encoded in the HTML part.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_TitleWithMarkup_HtmlEncoded()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer, "<script>alert(1)</script> & \"x\"", "<b>P</b>")], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain("<script>", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>P", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;x&quot;", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a quote in a review id cannot leave the href attribute.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_ReviewIdWithQuote_HrefEncoded()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "x\"onmouseover=\"y", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain("\"onmouseover", mail.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"" + BaseUrl + "/projects/1/reviews/x%22onmouseover%3D%22y\"", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that items are grouped by section in inbox order.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_MixedSections_GroupedInOrder()
    {
        // Arrange: handed over in the wrong order
        var composer = new NotificationComposer("Test Center", BaseUrl, true);
        var items = new[]
        {
            Candidate(InboxSection.Updates, "r-update", NotificationComposer.StateConcluded),
            Candidate(InboxSection.ToSign, "r-sign", NotificationComposer.StateReady),
            Candidate(InboxSection.ToAnswer, "r-answer", NotificationComposer.StateToAnswer)
        };

        // Act
        var mail = composer.Compose(items, 0, NotificationMode.Every);

        // Assert
        var answer = mail.Text.IndexOf("To answer", StringComparison.Ordinal);
        var sign = mail.Text.IndexOf("To sign", StringComparison.Ordinal);
        var updates = mail.Text.IndexOf("Updates", StringComparison.Ordinal);
        Assert.InRange(answer, 0, sign - 1);
        Assert.InRange(sign, answer + 1, updates - 1);
        Assert.True(mail.Text.IndexOf("r-answer", StringComparison.Ordinal) < mail.Text.IndexOf("r-sign", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a daily summary lists the new items and then the count of the outstanding ones.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Daily_NewItemsThenOutstanding()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 3, NotificationMode.Daily);

        // Assert
        var item = mail.Text.IndexOf("r1", StringComparison.Ordinal);
        var outstanding = mail.Text.IndexOf("3 more items you were told about earlier are still waiting for you.", StringComparison.Ordinal);
        Assert.True(item >= 0 && outstanding > item);
    }

    /// <summary>
    ///     Test that outstanding items are only a number, not a list.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Daily_OutstandingNotListed()
    {
        // Arrange: only the new item is handed over; the outstanding ones are a number
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r-new", NotificationComposer.StateToAnswer)], 1, NotificationMode.Daily);

        // Assert
        Assert.Contains("1 more item you were told about earlier is still waiting for you.", mail.Text, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(mail.Text, "/projects/"));
    }

    /// <summary>
    ///     Test that a message of the mode Every has no line about outstanding items.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Every_NoOutstandingLine()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 5, NotificationMode.Every);

        // Assert
        Assert.DoesNotContain("told about earlier", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that at most 50 items are listed and the rest is counted.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_60Items_50ListedAndMore()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);
        var items = Enumerable.Range(1, 60)
            .Select(i => Candidate(InboxSection.ToAnswer, $"r{i:00}", NotificationComposer.StateToAnswer))
            .ToList();

        // Act
        var mail = composer.Compose(items, 0, NotificationMode.Every);

        // Assert
        Assert.Equal(NotificationComposer.MaxListedItems, CountOccurrences(mail.Text, "/projects/"));
        Assert.Contains("... and 10 more.", mail.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the mail states how many items it did not list, in both parts.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_60Items_StatesHowManyNotListed()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);
        var items = Enumerable.Range(1, 60)
            .Select(i => Candidate(InboxSection.ToAnswer, $"r{i:00}", NotificationComposer.StateToAnswer))
            .ToList();

        // Act
        var mail = composer.Compose(items, 0, NotificationMode.Every);

        // Assert
        Assert.Contains("... and 10 more.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("... and 10 more.", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the footer says why the mail was sent.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Footer_SaysWhyMailWasSent()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("You get this message because you chose to be notified by e-mail in Test Center.", mail.Text, StringComparison.Ordinal);
        Assert.Contains("You get this message because you chose to be notified by e-mail in Test Center.", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the footer says why the mail was sent and links to the Account page.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_Footer_LinksToAccount()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act
        var mail = composer.Compose([Candidate(InboxSection.ToAnswer, "r1", NotificationComposer.StateToAnswer)], 0, NotificationMode.Every);

        // Assert
        Assert.Contains("You get this message because you chose to be notified", mail.Text, StringComparison.Ordinal);
        Assert.EndsWith(BaseUrl + "/account\n", mail.Text, StringComparison.Ordinal);
        Assert.Contains("href=\"" + BaseUrl + "/account\"", mail.Html, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that even the largest message is accepted by <see cref="NotificationMessage"/>.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_MaxItemsMaxLengthTitles_MessageAccepted()
    {
        // Arrange: 200 items with the longest project names, review ids and titles, full of characters that expand when encoded
        var composer = new NotificationComposer(new string('N', 200), BaseUrl, true);
        var items = Enumerable.Range(1, 200)
            .Select(i => Candidate(
                InboxSection.ToAnswer,
                new string('&', ReviewLimits.MaxReviewIdLength),
                NotificationComposer.StateToAnswer,
                new string('<', ReviewLimits.MaxReviewTitleLength),
                new string('"', ReviewLimits.MaxProjectNameLength),
                i))
            .ToList();

        // Act
        var mail = composer.Compose(items, 99, NotificationMode.Daily);
        var message = new NotificationMessage("ann@example.com", mail.Subject, mail.Text, mail.Html);

        // Assert
        Assert.True(message.TextBody.Length <= NotificationMessage.MaxBodyLength);
        Assert.True(message.HtmlBody.Length <= NotificationMessage.MaxBodyLength);
    }

    /// <summary>
    ///     Test that an item with a stored state is new only when the state differs, and that replies are new only
    ///     when their number rises.
    /// </summary>
    [Fact]
    public void NotificationComposer_IsNew_StoredAndCurrentState_FollowsRules()
    {
        // Arrange, Act and Assert
        Assert.True(NotificationComposer.IsNew(null, "to-answer"));
        Assert.False(NotificationComposer.IsNew("to-answer", "to-answer"));
        Assert.True(NotificationComposer.IsNew("to-answer", "question"));
        Assert.True(NotificationComposer.IsNew("replies:1", "replies:2"));
        Assert.False(NotificationComposer.IsNew("replies:2", "replies:1"));
        Assert.False(NotificationComposer.IsNew("replies:2", "replies:2"));
    }

    /// <summary>
    ///     Test that composing without an item is refused.
    /// </summary>
    [Fact]
    public void NotificationComposer_Compose_NoItems_ThrowsArgument()
    {
        // Arrange
        var composer = new NotificationComposer("Test Center", BaseUrl, true);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => composer.Compose([], 0, NotificationMode.Every));
    }

    /// <summary>
    ///     Test that an empty base address is refused.
    /// </summary>
    [Fact]
    public void NotificationComposer_Constructor_EmptyBaseUrl_Throws()
    {
        // Arrange, Act and Assert
        Assert.Throws<ArgumentException>(() => new NotificationComposer("Test Center", " ", true));
    }

    /// <summary>
    ///     Makes a candidate.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <param name="state">The state token.</param>
    /// <param name="title">The title.</param>
    /// <param name="project">The project name.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The candidate.</returns>
    private static NotificationCandidate Candidate(
        InboxSection section,
        string reviewId,
        string state,
        string title = "A title",
        string project = "Apollo",
        int projectId = 1)
    {
        return new NotificationCandidate(section, projectId, project, reviewId, title, state);
    }

    /// <summary>
    ///     Makes an inbox row.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <param name="chip">The chip.</param>
    /// <param name="reviewId">The review identifier.</param>
    /// <returns>The row.</returns>
    private static InboxItem Row(InboxSection section, string chip, string reviewId = "r1")
    {
        return new InboxItem(section, 1, "Apollo", reviewId, "A title", chip, false, null, false, null);
    }

    /// <summary>
    ///     Counts the occurrences of a text.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <param name="part">The part to count.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountOccurrences(string text, string part)
    {
        var count = 0;
        var index = text.IndexOf(part, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(part, index + part.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
