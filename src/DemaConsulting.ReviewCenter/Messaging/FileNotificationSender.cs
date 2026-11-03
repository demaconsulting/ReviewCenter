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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Messaging;

/// <summary>
///     Delivers e-mail by writing one <c>.eml</c> file per message into a folder, for demonstrations, development
///     and tests.
/// </summary>
/// <remarks>
///     <para>
///     The file holds the same MIME message that <see cref="SmtpNotificationSender"/> would send (built by
///     <see cref="SmtpNotificationSender.BuildMessage"/>), so a mail client can open it. Every file has a new name
///     made of the UTC time and a random identifier and is created exclusively, so an existing file is never
///     overwritten and concurrent sends never collide. The folder is created when it does not exist.
///     </para>
///     <para>
///     The class holds no mutable state and is thread-safe. A failure to write surfaces as a
///     <see cref="NotificationSendException"/> whose message holds no path.
///     </para>
/// </remarks>
/// <param name="options">The settings. Must not be null.</param>
/// <param name="logger">The logger. Must not be null.</param>
internal sealed class FileNotificationSender(NotificationOptions options, ILogger<FileNotificationSender> logger)
    : INotificationSender
{
    /// <summary>
    ///     The settings; checked for null once.
    /// </summary>
    private readonly NotificationOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    /// <exception cref="NotificationSendException">Thrown when the file could not be written.</exception>
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var mime = SmtpNotificationSender.BuildMessage(_options, message);
            var directory = Directory.CreateDirectory(_options.File.Directory.Trim());
            var name = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow.UtcTicks:D19}-{Guid.NewGuid():N}.eml");
            var path = Path.Combine(directory.FullName, name);

            // CreateNew never overwrites a file
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await mime.WriteToAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or NotSupportedException or ArgumentException)
        {
            // The operator log gets only the exception type, because exception messages can repeat the path; the
            // caller gets a message without a path. The exception is deliberately not passed to the logger (S6667),
            // because the logger would write its message.
#pragma warning disable S6667
            logger.LogWarning("Writing an e-mail file failed ({ExceptionType}).", ex.GetType().Name);
#pragma warning restore S6667
            throw new NotificationSendException("The e-mail file could not be written.", ex);
        }
    }
}
