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
using DemaConsulting.ReviewCenter.Tests.Web;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     Unit tests for <see cref="NotificationWorker"/>: the loop runs a pass, waits the poll interval on the
///     injected clock, never ends because of a failing pass, stops with the host and logs no address.
/// </summary>
public sealed class NotificationWorkerTests
{
    /// <summary>
    ///     The poll interval of the tests.
    /// </summary>
    private static readonly TimeSpan _poll = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a test waits for the loop before it gives up; only reached when a test fails.
    /// </summary>
    private static readonly TimeSpan _giveUp = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The cancellation token of the running test.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    ///     Test that a pass runs when the worker starts.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_Started_RunsPass()
    {
        // Arrange
        var world = new World();

        // Act
        await world.StartAsync();

        // Assert
        Assert.Equal(1, world.Passes);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that the next pass runs once the poll interval has elapsed.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_IntervalElapsed_RunsNextPass()
    {
        // Arrange
        var world = new World();
        await world.StartAsync();

        // Act
        await world.Clock.AdvanceAsync(_poll, expectPass: true);

        // Assert
        Assert.Equal(2, world.Passes);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that no second pass runs before the poll interval has elapsed.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_BeforeInterval_NoSecondPass()
    {
        // Arrange
        var world = new World();
        await world.StartAsync();

        // Act
        await world.Clock.AdvanceAsync(_poll - TimeSpan.FromSeconds(1), expectPass: false);

        // Assert
        Assert.Equal(1, world.Passes);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that the worker completes when the host stops.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_StopAsync_Running_Completes()
    {
        // Arrange
        var world = new World();
        await world.StartAsync();

        // Act
        await world.Worker.StopAsync(Ct).WaitAsync(_giveUp, Ct);

        // Assert
        Assert.NotNull(world.Worker.ExecuteTask);
        Assert.True(world.Worker.ExecuteTask.IsCompletedSuccessfully);
    }

    /// <summary>
    ///     Test that a worker that waits for the next pass stops at once, without the clock moving.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_StopAsync_Waiting_ReturnsPromptly()
    {
        // Arrange: the worker waits for the poll interval and the clock never moves
        var world = new World();
        await world.StartAsync();

        // Act
        await world.Worker.StopAsync(Ct).WaitAsync(_giveUp, Ct);

        // Assert
        Assert.Equal(1, world.Passes);
        Assert.True(world.Worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    /// <summary>
    ///     Test that a pass that is running when the host stops is canceled.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_StopAsync_PassRunning_CancelsPass()
    {
        // Arrange: a pass that only ends when it is canceled
        var canceled = false;
        var world = new World
        {
            Behavior = async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    throw;
                }

                return new NotificationPassResult(0, 0, 0);
            }
        };
        await world.Worker.StartAsync(Ct);
        await world.WaitForPassesAsync(1);

        // Act
        await world.Worker.StopAsync(Ct).WaitAsync(_giveUp, Ct);

        // Assert
        Assert.True(canceled);
        Assert.True(world.Worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    /// <summary>
    ///     Test that a pass that is canceled because the host stops is not logged as a failure.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_StopAsync_PassRunning_NothingLogged()
    {
        // Arrange: a pass that only ends when it is canceled
        var world = new World
        {
            Behavior = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new NotificationPassResult(0, 0, 0);
            }
        };
        await world.Worker.StartAsync(Ct);
        await world.WaitForPassesAsync(1);

        // Act
        await world.Worker.StopAsync(Ct).WaitAsync(_giveUp, Ct);

        // Assert
        Assert.Empty(world.Logger.Entries);
    }

    /// <summary>
    ///     Test that the loop goes on with the next pass after a pass threw.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassThrows_RunsNextPass()
    {
        // Arrange: the first pass fails, the next does not
        var world = new World
        {
            Behavior = (number, _) => number == 1
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(new NotificationPassResult(0, 0, 0))
        };
        await world.StartAsync();

        // Act
        await world.Clock.AdvanceAsync(_poll, expectPass: true);

        // Assert
        Assert.Equal(2, world.Passes);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that a pass that throws does not fault the worker, so the host keeps running.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassThrows_TaskNotFaulted()
    {
        // Arrange
        var world = new World { Behavior = (_, _) => throw new InvalidOperationException("boom") };

        // Act
        await world.StartAsync();

        // Assert: still running, and it completes without a fault when stopped
        Assert.False(world.Worker.ExecuteTask!.IsCompleted);
        await world.StopAsync();
        Assert.True(world.Worker.ExecuteTask.IsCompletedSuccessfully);
    }

    /// <summary>
    ///     Test that the log of a failed pass names the type of the exception.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassThrows_LogHasType()
    {
        // Arrange
        var world = new World
        {
            Behavior = (_, _) => throw new InvalidOperationException("could not reach the mail server")
        };

        // Act
        await world.StartAsync();

        // Assert
        var entry = Assert.Single(world.Logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(nameof(InvalidOperationException), entry.Text, StringComparison.Ordinal);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that the log of a failed pass holds neither the message of the exception nor the exception itself.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassThrows_LogHasNoMessage()
    {
        // Arrange: an exception whose message holds a sentence
        var world = new World
        {
            Behavior = (_, _) => throw new InvalidOperationException("could not reach the mail server")
        };

        // Act
        await world.StartAsync();

        // Assert
        var entry = Assert.Single(world.Logger.Entries);
        Assert.DoesNotContain("could not reach", entry.Text, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that no address reaches the log of a pass that fails with an exception whose messages hold an address,
    ///     whatever the logger is handed: the message, the state or the exception.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassThrowsWithAddress_NoAddressAnywhereInLog()
    {
        // Arrange: the address is in the message and in an inner exception
        var world = new World
        {
            Behavior = (_, _) => throw new InvalidOperationException(
                "could not send to ann@example.com",
                new IOException("550 ann@example.com refused"))
        };

        // Act
        await world.StartAsync();

        // Assert
        Assert.NotEmpty(world.Logger.Everything);
        Assert.All(world.Logger.Everything, text => Assert.DoesNotContain("ann@example.com", text, StringComparison.Ordinal));
        Assert.All(world.Logger.Everything, text => Assert.DoesNotContain("@", text, StringComparison.Ordinal));
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that nothing that looks like an address is logged for a pass that sent and failed mails.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassResultWithAddress_NotLogged()
    {
        // Arrange: a pass that reports counts while its mails went to addresses
        var world = new World { Behavior = (_, _) => Task.FromResult(new NotificationPassResult(2, 1, 0)) };

        // Act
        await world.StartAsync();

        // Assert
        Assert.NotEmpty(world.Logger.Everything);
        Assert.All(world.Logger.Everything, text => Assert.DoesNotContain("@", text, StringComparison.Ordinal));
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that the counts of a pass that sent or failed mails are logged.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_PassSent_LogsCounts()
    {
        // Arrange
        var world = new World { Behavior = (_, _) => Task.FromResult(new NotificationPassResult(2, 1, 3)) };

        // Act
        await world.StartAsync();

        // Assert
        var entry = Assert.Single(world.Logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("2 sent, 1 failed, 3 deferred", entry.Text, StringComparison.Ordinal);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that a pass that sent and failed nothing writes no log entry.
    /// </summary>
    [Fact]
    public async Task NotificationWorker_ExecuteAsync_IdlePass_NothingLogged()
    {
        // Arrange
        var world = new World();

        // Act
        await world.StartAsync();

        // Assert
        Assert.Empty(world.Logger.Entries);
        await world.StopAsync();
    }

    /// <summary>
    ///     Test that the constructor refuses a missing argument.
    /// </summary>
    [Fact]
    public void NotificationWorker_Constructor_NullArgument_Throws()
    {
        // Arrange
        var logger = new CapturingLogger();
        var clock = new StepClock();
        Func<CancellationToken, Task<NotificationPassResult>> pass = _ => Task.FromResult(new NotificationPassResult(0, 0, 0));

        // Act and Assert
        Assert.Throws<ArgumentNullException>(() => new NotificationWorker(null!, _poll, clock, logger));
        Assert.Throws<ArgumentNullException>(() => new NotificationWorker(pass, _poll, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new NotificationWorker(pass, _poll, clock, null!));
    }

    /// <summary>
    ///     Test that the constructor refuses a poll interval that is not positive.
    /// </summary>
    [Fact]
    public void NotificationWorker_Constructor_NonPositivePoll_Throws()
    {
        // Arrange
        var logger = new CapturingLogger();
        var clock = new StepClock();

        // Act and Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationWorker(
            _ => Task.FromResult(new NotificationPassResult(0, 0, 0)),
            TimeSpan.Zero,
            clock,
            logger));
    }

    /// <summary>
    ///     A worker with a counting pass, a manual clock and a capturing log.
    /// </summary>
    private sealed class World
    {
        /// <summary>
        ///     The number of the passes that started.
        /// </summary>
        private int _passes;

        /// <summary>
        ///     The worker, created on first use so that a test can set <see cref="Behavior"/> first.
        /// </summary>
        private NotificationWorker? _worker;

        /// <summary>
        ///     Gets the manual clock the worker waits on.
        /// </summary>
        public StepClock Clock { get; } = new();

        /// <summary>
        ///     Gets the log of the worker.
        /// </summary>
        public CapturingLogger Logger { get; } = new();

        /// <summary>
        ///     Gets the number of the passes that started.
        /// </summary>
        public int Passes => Volatile.Read(ref _passes);

        /// <summary>
        ///     Gets the behavior of a pass, given the number of the pass (from one) and the token; <c>null</c> for an
        ///     idle pass.
        /// </summary>
        public Func<int, CancellationToken, Task<NotificationPassResult>>? Behavior { get; init; }

        /// <summary>
        ///     Gets the worker.
        /// </summary>
        public NotificationWorker Worker => _worker ??= new NotificationWorker(RunAsync, _poll, Clock, Logger);

        /// <summary>
        ///     Starts the worker and waits until the first pass is over and the worker waits for the next one.
        /// </summary>
        /// <returns>A task that completes when the worker waits.</returns>
        public async Task StartAsync()
        {
            await Worker.StartAsync(Ct);
            await Clock.WaitForTimersAsync(1);
        }

        /// <summary>
        ///     Waits, without sleeping, until the given number of passes has started.
        /// </summary>
        /// <param name="count">The number of passes.</param>
        /// <returns>A task that completes when the passes have started.</returns>
        public async Task WaitForPassesAsync(int count)
        {
            for (var spin = 0; spin < 1_000_000 && Volatile.Read(ref _passes) < count; spin++)
            {
                await Task.Yield();
            }

            Assert.True(Passes >= count, "The worker did not run the pass.");
        }

        /// <summary>
        ///     Stops the worker.
        /// </summary>
        /// <returns>A task that completes when the worker has stopped.</returns>
        public async Task StopAsync()
        {
            await Worker.StopAsync(Ct).WaitAsync(_giveUp, Ct);
        }

        /// <summary>
        ///     Runs one pass.
        /// </summary>
        /// <param name="token">Cancels the pass.</param>
        /// <returns>The result of the pass.</returns>
        private async Task<NotificationPassResult> RunAsync(CancellationToken token)
        {
            var number = Interlocked.Increment(ref _passes);
            return Behavior is null
                ? new NotificationPassResult(0, 0, 0)
                : await Behavior(number, token);
        }
    }

    /// <summary>
    ///     A logger that keeps every entry for inspection.
    /// </summary>
    private sealed class CapturingLogger : ILogger<NotificationWorker>
    {
        /// <summary>
        ///     Gets the entries written so far.
        /// </summary>
        public List<(LogLevel Level, string Text, Exception? Exception)> Entries { get; } = [];

        /// <summary>
        ///     Gets everything that was handed to the logger, as text: the formatted message, the structured state
        ///     and the full text of the exception, if any.
        /// </summary>
        public List<string> Everything { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
            Everything.Add(formatter(state, exception) + "|" + state + "|" + exception);
        }
    }
}
