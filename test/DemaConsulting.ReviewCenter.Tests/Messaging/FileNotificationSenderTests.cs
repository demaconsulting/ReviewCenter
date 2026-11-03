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
using DemaConsulting.ReviewCenter.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     Unit tests for <see cref="FileNotificationSender"/>: one parseable <c>.eml</c> file per message in a
///     temporary folder.
/// </summary>
public sealed class FileNotificationSenderTests : IDisposable
{
    /// <summary>
    ///     The folder the sender writes into; deleted with the test.
    /// </summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rc-file-sender-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    /// <summary>
    ///     Test that the sender reports that it is configured.
    /// </summary>
    [Fact]
    public void FileNotificationSender_IsConfigured_IsTrue()
    {
        // Arrange and Act: a sender
        var sender = new FileNotificationSender(Options(), NullLogger<FileNotificationSender>.Instance);

        // Assert: configured
        Assert.True(sender.IsConfigured);
    }

    /// <summary>
    ///     Test that a message becomes one <c>.eml</c> file that holds the headers and both parts, and that the
    ///     folder is created.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_WritesParseableEmlFile()
    {
        // Arrange: a sender for a folder that does not exist yet
        var sender = new FileNotificationSender(Options(), NullLogger<FileNotificationSender>.Instance);
        var message = new NotificationMessage("ann@example.com", "Hello Ann", "plain body", "<p>html body</p>");

        // Act: send
        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: one file that parses back to the same content
        var file = Assert.Single(Directory.GetFiles(_folder, "*.eml"));
        var parsed = await MimeMessage.LoadAsync(file, TestContext.Current.CancellationToken);
        Assert.Equal("Hello Ann", parsed.Subject);
        Assert.Equal("ann@example.com", parsed.To.Mailboxes.Single().Address);
        Assert.Equal("reviews@example.com", parsed.From.Mailboxes.Single().Address);
        Assert.Contains("plain body", parsed.TextBody, StringComparison.Ordinal);
        Assert.Contains("<p>html body</p>", parsed.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that several messages never overwrite each other.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_SeveralMessages_WritesSeparateFiles()
    {
        // Arrange: a sender
        var sender = new FileNotificationSender(Options(), NullLogger<FileNotificationSender>.Instance);
        var message = new NotificationMessage("ann@example.com", "Same", "text", "<p>text</p>");

        // Act: send the same message concurrently
        await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => sender.SendAsync(message, TestContext.Current.CancellationToken)));

        // Assert: five files with different names
        Assert.Equal(5, Directory.GetFiles(_folder, "*.eml").Select(Path.GetFileName).Distinct().Count());
    }

    /// <summary>
    ///     Test that a folder that can not be created fails the send without the path in the message.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_FolderIsAFile_ThrowsWithoutPath()
    {
        // Arrange: a regular file where the folder should be
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        await File.WriteAllTextAsync(blocker, "x", TestContext.Current.CancellationToken);
        var options = Options();
        options.File.Directory = blocker;
        var sender = new FileNotificationSender(options, NullLogger<FileNotificationSender>.Instance);

        // Act: send
        var exception = await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(new NotificationMessage("ann@example.com", "S", "t", "<p>t</p>"), TestContext.Current.CancellationToken));

        // Assert: the message does not show the path
        Assert.DoesNotContain("blocker", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a send leaves exactly one <c>.eml</c> file and no temporary file in the folder.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_Success_LeavesOnlyTheEmlFile()
    {
        // Arrange: a sender
        var sender = new FileNotificationSender(Options(), NullLogger<FileNotificationSender>.Instance);

        // Act: send
        await sender.SendAsync(
            new NotificationMessage("ann@example.com", "S", "t", "<p>t</p>"),
            TestContext.Current.CancellationToken);

        // Assert: the folder holds exactly the one message file
        var file = Assert.Single(Directory.GetFileSystemEntries(_folder));
        Assert.EndsWith(".eml", file, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a cancelled send leaves neither a message file nor a temporary file.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_Cancelled_LeavesNoFile()
    {
        // Arrange: a sender and a token that is already cancelled
        var sender = new FileNotificationSender(Options(), NullLogger<FileNotificationSender>.Instance);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        // Act: send
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(
            new NotificationMessage("ann@example.com", "S", "t", "<p>t</p>"), source.Token));

        // Assert: the folder is empty
        Assert.Empty(Directory.GetFileSystemEntries(_folder));
    }

    /// <summary>
    ///     Test that a failed send leaves neither a message file nor a temporary file in the folder that holds the
    ///     blocking entry.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_Failure_LeavesNoMessageOrTemporaryFile()
    {
        // Arrange: a regular file where the folder should be
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        await File.WriteAllTextAsync(blocker, "x", TestContext.Current.CancellationToken);
        var options = Options();
        options.File.Directory = blocker;
        var sender = new FileNotificationSender(options, NullLogger<FileNotificationSender>.Instance);

        // Act: send
        await Assert.ThrowsAsync<NotificationSendException>(
            () => sender.SendAsync(new NotificationMessage("ann@example.com", "S", "t", "<p>t</p>"), TestContext.Current.CancellationToken));

        // Assert: only the blocking file is left
        Assert.Equal(blocker, Assert.Single(Directory.GetFileSystemEntries(_folder)));
    }

    /// <summary>
    ///     Test that a missing message is a programming error.
    /// </summary>
    [Fact]
    public async Task FileNotificationSender_Send_NullMessage_ThrowsArgumentNull()
    {
        // Arrange: a sender
        var sender = new FileNotificationSender(Options(), NullLogger<FileNotificationSender>.Instance);

        // Act and Assert: refused
        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Creates settings for the test folder.
    /// </summary>
    /// <returns>The settings.</returns>
    private NotificationOptions Options()
    {
        return new NotificationOptions
        {
            Enabled = true,
            Sender = NotificationSenderKind.File,
            FromAddress = "reviews@example.com",
            FromName = "Review Center",
            BaseUrl = "https://reviews.example.com",
            File = new FileNotificationOptions { Directory = _folder }
        };
    }
}
