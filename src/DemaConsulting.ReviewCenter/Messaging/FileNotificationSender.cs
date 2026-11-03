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
///     made of the UTC time and a random identifier and is never overwritten, so concurrent sends never collide. The
///     message is written to a temporary file in the same folder and renamed to the final <c>.eml</c> name when it is
///     complete, so the folder never holds a partial message; a failed or cancelled send leaves no file. The folder
///     is created when it does not exist.
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
                $"{DateTimeOffset.UtcNow.UtcTicks:D19}-{Guid.NewGuid():N}");
            var path = Path.Combine(directory.FullName, name + ".eml");
            var temporaryPath = Path.Combine(directory.FullName, name + ".tmp");

            // The message is written to a temporary file that is not an .eml file, so a reader of the folder never
            // sees a partial message; the finished file gets its final name in one step. CreateNew never overwrites a
            // file, and the move does not overwrite one either
            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await mime.WriteToAsync(stream, cancellationToken);
                }

                File.Move(temporaryPath, path, overwrite: false);
            }
            catch
            {
                // A failed or cancelled send leaves neither a message file nor a temporary file
                TryDelete(temporaryPath);
                throw;
            }
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

    /// <summary>
    ///     Deletes a file and ignores a failure to do so, because the original failure is the one to report.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the original failure is reported
        }
    }
}
