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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     A recording <see cref="INotificationSender"/> for tests: it keeps every message instead of sending it and can
///     be told to fail.
/// </summary>
/// <remarks>Thread-safe: one lock guards the list of messages.</remarks>
internal sealed partial class FakeNotificationSender : INotificationSender
{
    /// <summary>
    ///     Guards <see cref="_sent"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The messages handed to the sender, oldest first.
    /// </summary>
    private readonly List<NotificationMessage> _sent = [];

    /// <summary>
    ///     Gets or sets a value indicating whether the sender reports itself as configured.
    /// </summary>
    public bool Configured { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the next sends fail with a <see cref="NotificationSendException"/>.
    /// </summary>
    public bool Fail { get; set; }

    /// <summary>
    ///     Gets or sets an exception that the next sends throw instead of the normal result (for example a
    ///     cancellation or an unexpected failure).
    /// </summary>
    public Exception? FailWith { get; set; }

    /// <summary>
    ///     Gets or sets an action that runs inside every send before the message is kept, so a test can look at the
    ///     state of the system while the mail is being sent, or <c>null</c> for none.
    /// </summary>
    public Action<NotificationMessage>? OnSend { get; set; }

    /// <summary>
    ///     Gets or sets an action that runs at the very start of every send, before any failure is thrown, so a test
    ///     can look at the state of the system while a mail that will fail is being sent, or cancel the pass at that
    ///     moment; <c>null</c> for none.
    /// </summary>
    public Action<NotificationMessage>? BeforeSend { get; set; }

    /// <summary>
    ///     Gets or sets an address whose sends fail with a <see cref="NotificationSendException"/> while the other
    ///     addresses are sent to normally, or <c>null</c> for none.
    /// </summary>
    public string? FailForAddress { get; set; }

    /// <inheritdoc />
    public bool IsConfigured => Configured;

    /// <summary>
    ///     Gets a snapshot of the messages sent so far, oldest first.
    /// </summary>
    public IReadOnlyList<NotificationMessage> Sent
    {
        get
        {
            lock (_lock)
            {
                return _sent.ToList();
            }
        }
    }

    /// <inheritdoc />
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        BeforeSend?.Invoke(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWith is not null)
        {
            throw FailWith;
        }

        if (Fail || (FailForAddress is not null && message.ToAddress == FailForAddress))
        {
            throw new NotificationSendException("The fake sender was told to fail.");
        }

        lock (_lock)
        {
            OnSend?.Invoke(message);
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Reads the confirmation code from the most recent message.
    /// </summary>
    /// <returns>The six digits of the code.</returns>
    public string LastCode()
    {
        var last = Sent[^1];
        var match = CodePattern().Match(last.TextBody);
        Assert.True(match.Success, "The message holds no confirmation code.");
        return match.Groups[1].Value;
    }

    /// <summary>
    ///     Gets the pattern of the code in the confirmation mail.
    /// </summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"confirmation code is (\d{6})\.")]
    private static partial Regex CodePattern();
}
