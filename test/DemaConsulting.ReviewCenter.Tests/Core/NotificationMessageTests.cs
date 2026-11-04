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
///     Unit tests for <see cref="NotificationMessage"/> and <see cref="NoNotificationSender"/>.
/// </summary>
public class NotificationMessageTests
{
    /// <summary>
    ///     Test that a valid message keeps its parts and normalizes the recipient.
    /// </summary>
    [Fact]
    public void NotificationMessage_Constructor_ValidParts_KeepsThemAndNormalizesRecipient()
    {
        // Arrange and Act: build a message for a capitalized address
        var message = new NotificationMessage("Ann@Example.com", "Hello", "plain text", "<p>html</p>");

        // Assert: parts kept, recipient lower case
        Assert.Equal("ann@example.com", message.ToAddress);
        Assert.Equal("Hello", message.Subject);
        Assert.Equal("plain text", message.TextBody);
        Assert.Equal("<p>html</p>", message.HtmlBody);
    }

    /// <summary>
    ///     Test that a recipient that is not exactly one address is refused, and the value is not echoed.
    /// </summary>
    [Theory]
    [InlineData("ann@example.com\r\nBcc: eve@example.org")]
    [InlineData("ann@example.com,eve@example.org")]
    [InlineData("not an address")]
    [InlineData("")]
    public void NotificationMessage_Constructor_InvalidRecipient_ThrowsWithoutEchoingTheValue(string recipient)
    {
        // Arrange and Act: build a message with the recipient
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(recipient, "Hello", "text", "<p>html</p>"));

        // Assert: the parameter is named and the value is not repeated
        Assert.Equal("toAddress", exception.ParamName);
        if (recipient.Length > 0)
        {
            Assert.DoesNotContain(recipient, exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a subject with a line break or another control character is refused (header injection).
    /// </summary>
    [Theory]
    [InlineData("Hello\r\nBcc: eve@example.org")]
    [InlineData("Hello\nWorld")]
    [InlineData("Hello\rWorld")]
    [InlineData("Hello\tWorld")]
    [InlineData("Hello\0World")]
    public void NotificationMessage_Constructor_ControlCharacterInSubject_ThrowsWithoutEchoingTheValue(string subject)
    {
        // Arrange and Act: build a message with the subject
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage("ann@example.com", subject, "text", "<p>html</p>"));

        // Assert: the parameter is named and the value is not repeated
        Assert.Equal("subject", exception.ParamName);
        Assert.DoesNotContain("Bcc", exception.Message, StringComparison.Ordinal);
        foreach (var part in subject.Split(['\r', '\n', '\t', '\0'], StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.DoesNotContain(part, exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that an empty subject and a subject of white space only are refused, naming the subject parameter.
    /// </summary>
    /// <param name="subject">The refused subject.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void NotificationMessage_Constructor_EmptyOrBlankSubject_Throws(string subject)
    {
        // Arrange and Act: build a message with the subject
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage("ann@example.com", subject, "text", "<p>html</p>"));

        // Assert: the subject is named
        Assert.Equal("subject", exception.ParamName);
    }

    /// <summary>
    ///     Test that a subject of white space only is refused, naming the subject parameter.
    /// </summary>
    /// <param name="subject">The refused subject.</param>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void NotificationMessage_Constructor_BlankSubject_Throws(string subject)
    {
        // Arrange and Act: build a message with the blank subject
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage("ann@example.com", subject, "text", "<p>html</p>"));
        var padded = new NotificationMessage("ann@example.com", "a" + subject + "b", "text", "<p>html</p>");

        // Assert: the subject is named, and the same white space between letters is not blank
        Assert.Equal("subject", exception.ParamName);
        Assert.Equal("a" + subject + "b", padded.Subject);
    }

    /// <summary>
    ///     Test that an empty or too long subject is refused and the longest allowed subject is accepted.
    /// </summary>
    [Fact]
    public void NotificationMessage_Constructor_SubjectLength_RefusesEmptyAndTooLong()
    {
        // Arrange, Act and Assert: empty and over the limit fail; the limit itself passes
        Assert.Throws<ArgumentException>(() => new NotificationMessage("ann@example.com", " ", "t", "<p>h</p>"));
        Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", new string('s', NotificationMessage.MaxSubjectLength + 1), "t", "<p>h</p>"));
        var message = new NotificationMessage(
            "ann@example.com", new string('s', NotificationMessage.MaxSubjectLength), "t", "<p>h</p>");
        Assert.Equal(NotificationMessage.MaxSubjectLength, message.Subject.Length);
    }

    /// <summary>
    ///     Test that an empty body, a body with a NUL character and a too long body are refused.
    /// </summary>
    [Fact]
    public void NotificationMessage_Constructor_InvalidBody_Throws()
    {
        // Arrange, Act and Assert: each bad body names its parameter
        var empty = Assert.Throws<ArgumentException>(
            () => new NotificationMessage("ann@example.com", "Hello", "  ", "<p>h</p>"));
        Assert.Equal("textBody", empty.ParamName);
        var nul = Assert.Throws<ArgumentException>(
            () => new NotificationMessage("ann@example.com", "Hello", "text", "<p>h\0</p>"));
        Assert.Equal("htmlBody", nul.ParamName);
        var tooLong = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", "Hello", new string('x', NotificationMessage.MaxBodyLength + 1), "<p>h</p>"));
        Assert.Equal("textBody", tooLong.ParamName);
        var blankHtml = Assert.Throws<ArgumentException>(
            () => new NotificationMessage("ann@example.com", "Hello", "text", "  "));
        Assert.Equal("htmlBody", blankHtml.ParamName);
        var tooLongHtml = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", "Hello", "text", new string('x', NotificationMessage.MaxBodyLength + 1)));
        Assert.Equal("htmlBody", tooLongHtml.ParamName);
    }

    /// <summary>
    ///     Test that a text body or an HTML body of white space only is refused, naming that body parameter.
    /// </summary>
    /// <param name="htmlBody">Whether the blank part is the HTML body; otherwise it is the text body.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotificationMessage_Constructor_BlankBody_Throws(bool htmlBody)
    {
        // Arrange and Act: one body part of white space
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", "Hello", htmlBody ? "text" : "  ", htmlBody ? "  " : "<p>h</p>"));

        // Assert: the blank part is named
        Assert.Equal(htmlBody ? "htmlBody" : "textBody", exception.ParamName);
    }

    /// <summary>
    ///     Test that a text body or an HTML body of more than the limit is refused, and a body of exactly the limit
    ///     is accepted.
    /// </summary>
    /// <param name="htmlBody">Whether the long part is the HTML body; otherwise it is the text body.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotificationMessage_Constructor_TooLongBody_Throws(bool htmlBody)
    {
        // Arrange: a body one character over the limit and a body at the limit
        var tooLong = new string('x', NotificationMessage.MaxBodyLength + 1);
        var atLimit = new string('x', NotificationMessage.MaxBodyLength);

        // Act
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", "Hello", htmlBody ? "text" : tooLong, htmlBody ? tooLong : "<p>h</p>"));
        var accepted = new NotificationMessage(
            "ann@example.com", "Hello", htmlBody ? "text" : atLimit, htmlBody ? atLimit : "<p>h</p>");

        // Assert: the long part is named, and the limit itself passes
        Assert.Equal(htmlBody ? "htmlBody" : "textBody", exception.ParamName);
        Assert.Equal(NotificationMessage.MaxBodyLength, (htmlBody ? accepted.HtmlBody : accepted.TextBody).Length);
    }

    /// <summary>
    ///     Test that a NUL character in the plain text body or in the HTML body is refused.
    /// </summary>
    /// <param name="htmlBody">Whether the NUL character is in the HTML body; otherwise in the text body.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotificationMessage_Constructor_NulInBody_Throws(bool htmlBody)
    {
        // Arrange and Act: one body part with a NUL character
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", "Hello", htmlBody ? "text" : "te\0xt", htmlBody ? "<p>h\0</p>" : "<p>h</p>"));

        // Assert: the part with the NUL character is named
        Assert.Equal(htmlBody ? "htmlBody" : "textBody", exception.ParamName);
    }

    /// <summary>
    ///     Test that an empty text body or an empty HTML body is refused.
    /// </summary>
    /// <param name="htmlBody">Whether the empty part is the HTML body; otherwise it is the text body.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotificationMessage_Constructor_EmptyBody_Throws(bool htmlBody)
    {
        // Arrange and Act: one empty body part
        var exception = Assert.Throws<ArgumentException>(
            () => new NotificationMessage(
                "ann@example.com", "Hello", htmlBody ? "text" : string.Empty, htmlBody ? string.Empty : "<p>h</p>"));

        // Assert: the empty part is named
        Assert.Equal(htmlBody ? "htmlBody" : "textBody", exception.ParamName);
    }

    /// <summary>
    ///     Test that a body may hold line breaks.
    /// </summary>
    [Fact]
    public void NotificationMessage_Constructor_BodyWithLineBreaks_Succeeds()
    {
        // Arrange and Act: a multi-line body
        var message = new NotificationMessage("ann@example.com", "Hello", "line 1\r\nline 2\n", "<p>a</p>\n<p>b</p>");

        // Assert: both bodies kept exactly as given
        Assert.Equal("line 1\r\nline 2\n", message.TextBody);
        Assert.Equal("<p>a</p>\n<p>b</p>", message.HtmlBody);
    }

    /// <summary>
    ///     Test that the no-op sender reports that it is not configured and refuses to send.
    /// </summary>
    [Fact]
    public async Task NoNotificationSender_SendAsync_ThrowsSendException()
    {
        // Arrange: the sender used while nothing is configured
        var sender = new NoNotificationSender();
        var message = new NotificationMessage("ann@example.com", "Hello", "text", "<p>h</p>");

        // Act and Assert: it is not configured and sending fails with the dedicated exception
        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(message, TestContext.Current.CancellationToken));
    }
}
