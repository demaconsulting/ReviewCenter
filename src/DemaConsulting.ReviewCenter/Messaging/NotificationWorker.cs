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

namespace DemaConsulting.ReviewCenter.Messaging;

/// <summary>
///     The background service that runs the notification dispatcher on a timer for as long as the server runs.
/// </summary>
/// <remarks>
///     The service runs one pass, waits the poll interval on the injected clock, and repeats until the host stops.
///     A failure of a pass never ends the loop: it is logged by its type only and the next pass goes on, so a
///     mail-server outage or a database fault can not take the server down. No address, subject or message text is
///     ever logged. The service holds no state besides the loop and is not meant to be shared.
/// </remarks>
internal sealed partial class NotificationWorker : BackgroundService
{
    /// <summary>
    ///     The runs of one pass.
    /// </summary>
    private readonly Func<CancellationToken, Task<NotificationPassResult>> _pass;

    /// <summary>
    ///     The time between two passes.
    /// </summary>
    private readonly TimeSpan _poll;

    /// <summary>
    ///     The clock the waits are measured on.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The log of the worker.
    /// </summary>
    private readonly ILogger<NotificationWorker> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationWorker"/> class.
    /// </summary>
    /// <param name="pass">Runs one pass of the dispatcher. Must not be null.</param>
    /// <param name="poll">The time between two passes; positive.</param>
    /// <param name="clock">The clock the waits are measured on. Must not be null.</param>
    /// <param name="logger">The log of the worker. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="poll"/> is not positive.</exception>
    public NotificationWorker(
        Func<CancellationToken, Task<NotificationPassResult>> pass,
        TimeSpan poll,
        TimeProvider clock,
        ILogger<NotificationWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(poll, TimeSpan.Zero);
        _pass = pass;
        _poll = poll;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_poll, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // The host is stopping
                return;
            }
        }
    }

    /// <summary>
    ///     Runs one pass and logs what happened; never throws.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the pass is over.</returns>
    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pass(cancellationToken);
            if (result.Sent > 0 || result.Failed > 0)
            {
                LogPass(result.Sent, result.Failed, result.Deferred);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping
        }
        catch (Exception ex)
        {
            // Only the type is logged: the message of a mail or database failure can hold an address
            LogPassFailed(ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Logs the counts of a pass that sent or failed to send mail.
    /// </summary>
    /// <param name="sent">The mails sent.</param>
    /// <param name="failed">The mails that failed.</param>
    /// <param name="deferred">The mails put off to a later pass.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Notification pass: {Sent} sent, {Failed} failed, {Deferred} deferred.")]
    private partial void LogPass(int sent, int failed, int deferred);

    /// <summary>
    ///     Logs that a pass failed unexpectedly.
    /// </summary>
    /// <param name="errorType">The name of the type of the exception.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "A notification pass failed ({ErrorType}); the next pass will try again.")]
    private partial void LogPassFailed(string errorType);
}
