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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Sends e-mail on behalf of the server.
/// </summary>
/// <remarks>
///     The interface is the only thing the domain services know about mail, so a service can be tested with a
///     recording fake and the real transports (an SMTP server, a folder of files) stay in the host. An
///     implementation delivers exactly the message it is given, never reads or changes the recipient, and never
///     puts a secret (the password of the mail server) in an exception. Implementations are thread-safe.
/// </remarks>
public interface INotificationSender
{
    /// <summary>
    ///     Gets a value indicating whether a transport is configured, that is whether <see cref="SendAsync"/> can
    ///     deliver anything.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    ///     Sends one message.
    /// </summary>
    /// <param name="message">The message. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the transport has accepted the message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
    /// <exception cref="NotificationSendException">
    ///     Thrown when the message could not be handed to the transport, or when no transport is configured; the
    ///     message never holds a password.
    /// </exception>
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
