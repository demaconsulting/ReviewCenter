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

using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Messaging;

/// <summary>
///     A <see cref="TimeProvider"/> that only moves when a test moves it and that counts the waits a background
///     loop started, so a test knows when the loop has finished a pass and waits again.
/// </summary>
/// <remarks>
///     Only one-shot timers are supported, which is all that
///     <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> needs. A timer fires on the thread that
///     calls <see cref="AdvanceAsync"/>. The clock starts at <see cref="InboxTestWorld.Start"/>.
/// </remarks>
internal sealed class StepClock : TimeProvider
{
    /// <summary>
    ///     The upper bound of the yields a test spends on waiting for the loop, so that a failing test ends.
    /// </summary>
    private const int MaxYields = 1_000_000;

    /// <summary>
    ///     The timers that have not fired or been disposed; also guards the clock.
    /// </summary>
    private readonly List<StepTimer> _timers = [];

    /// <summary>
    ///     The current instant.
    /// </summary>
    private DateTimeOffset _now = InboxTestWorld.Start;

    /// <summary>
    ///     The number of the timers created so far.
    /// </summary>
    private int _created;

    /// <summary>
    ///     Gets the number of the timers created so far.
    /// </summary>
    public int TimersCreated => Volatile.Read(ref _created);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_timers)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new StepTimer(this, callback, state);
        lock (_timers)
        {
            timer.Change(dueTime, period);
            _timers.Add(timer);
            _created++;
        }

        return timer;
    }

    /// <summary>
    ///     Waits, without sleeping, until the given number of timers has been created.
    /// </summary>
    /// <param name="count">The number of timers.</param>
    /// <returns>A task that completes when the timers exist.</returns>
    public async Task WaitForTimersAsync(int count)
    {
        for (var spin = 0; spin < MaxYields && TimersCreated < count; spin++)
        {
            await Task.Yield();
        }

        Assert.True(TimersCreated >= count, "The loop did not wait again.");
    }

    /// <summary>
    ///     Moves the clock, fires the timers that are due and, when a pass is expected, waits until the loop has
    ///     finished that pass and waits again.
    /// </summary>
    /// <param name="span">How far to move the clock.</param>
    /// <param name="expectPass">Whether a pass is expected to run.</param>
    /// <returns>A task that completes when the pass is over.</returns>
    public async Task AdvanceAsync(TimeSpan span, bool expectPass)
    {
        int before;
        List<StepTimer> due;
        lock (_timers)
        {
            before = _created;
            _now += span;
            due = [.. _timers.Where(timer => timer.DueTime is { } at && at <= _now)];
            foreach (var timer in due)
            {
                _timers.Remove(timer);
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }

        if (expectPass)
        {
            await WaitForTimersAsync(before + 1);
        }
    }

    /// <summary>
    ///     A one-shot timer of the clock.
    /// </summary>
    /// <param name="owner">The clock.</param>
    /// <param name="callback">The method to call when the timer fires.</param>
    /// <param name="state">The state passed to the callback.</param>
    private sealed class StepTimer(StepClock owner, TimerCallback callback, object? state) : ITimer
    {
        /// <summary>
        ///     Gets the instant the timer fires at, or <c>null</c> when it does not run.
        /// </summary>
        public DateTimeOffset? DueTime { get; private set; }

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        /// <summary>
        ///     Calls the callback.
        /// </summary>
        public void Fire()
        {
            DueTime = null;
            callback(state);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            DueTime = null;
            lock (owner._timers)
            {
                owner._timers.Remove(this);
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
