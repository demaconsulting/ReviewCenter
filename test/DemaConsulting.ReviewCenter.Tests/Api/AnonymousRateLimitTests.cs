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

using DemaConsulting.ReviewCenter.Api;
using DemaConsulting.ReviewCenter.Tests.Core;

namespace DemaConsulting.ReviewCenter.Tests.Api;

/// <summary>
///     Unit tests of the memory bound and the thread safety of <see cref="AnonymousRateLimit"/>, using a clock that
///     the test moves.
/// </summary>
public class AnonymousRateLimitTests
{
    /// <summary>
    ///     The instant at which each test starts.
    /// </summary>
    private static readonly DateTimeOffset _start = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);

    /// <summary>
    ///     One millisecond, the step between the creation of two addresses, so that their order is defined.
    /// </summary>
    private static readonly TimeSpan _tick = TimeSpan.FromMilliseconds(1);

    /// <summary>
    ///     Test that a flood of distinct addresses inside one window never makes the table larger than its capacity,
    ///     and that no new address is refused.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_ManyDistinctAddresses_NeverExceedsCapacity()
    {
        // Arrange
        var clock = new MovableTimeProvider(_start);
        var limiter = new AnonymousRateLimit(clock);

        // Act, Assert: three times the capacity of addresses, each asking once; all are granted
        for (var i = 0; i < 3 * AnonymousRateLimit.Capacity; i++)
        {
            clock.Advance(_tick);
            Assert.True(limiter.TryAcquire($"10.0.{i}", out _));
            if (i % 512 == 0)
            {
                Assert.True(limiter.AddressCount() <= AnonymousRateLimit.Capacity);
            }
        }

        Assert.Equal(AnonymousRateLimit.Capacity, limiter.AddressCount());
    }

    /// <summary>
    ///     Test that a full table removes the windows that have ended before it drops a live one: a refused address
    ///     stays refused when a new address arrives.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_FullTable_RemovesExpiredBeforeEvictingLive()
    {
        // Arrange: half of the table is filled, 40 seconds later the other half; one live address is used up
        var half = AnonymousRateLimit.Capacity / 2;
        var clock = new MovableTimeProvider(_start);
        var limiter = new AnonymousRateLimit(clock);
        for (var i = 0; i < half; i++)
        {
            Assert.True(limiter.TryAcquire($"first-{i}", out _));
        }

        clock.Advance(TimeSpan.FromSeconds(40));
        for (var i = 0; i < half; i++)
        {
            Assert.True(limiter.TryAcquire($"second-{i}", out _));
        }

        for (var i = 1; i < AnonymousRateLimit.Limit; i++)
        {
            Assert.True(limiter.TryAcquire("second-0", out _));
        }

        Assert.False(limiter.TryAcquire("second-0", out _));
        Assert.Equal(AnonymousRateLimit.Capacity, limiter.AddressCount());

        // Act: the first half has ended, the second is live; a new address arrives at the full table
        clock.Advance(TimeSpan.FromSeconds(30));
        var granted = limiter.TryAcquire("new", out _);

        // Assert: only the ended windows were removed, and the live, used-up address is still refused
        Assert.True(granted);
        Assert.Equal(half + 1, limiter.AddressCount());
        Assert.False(limiter.TryAcquire("second-0", out _));
    }

    /// <summary>
    ///     Test that a table full of live windows drops the oldest window first, never refuses the new address, and
    ///     stays at its capacity. This is the documented loss of a budget under a flood of addresses.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_FullOfLiveWindows_EvictsOldestFirst()
    {
        // Arrange: a full table of live windows, created one tick apart; the two oldest addresses are used up
        var clock = new MovableTimeProvider(_start);
        var limiter = new AnonymousRateLimit(clock);
        for (var i = 0; i < AnonymousRateLimit.Capacity; i++)
        {
            clock.Advance(_tick);
            Assert.True(limiter.TryAcquire($"a{i}", out _));
        }

        for (var i = 0; i < AnonymousRateLimit.Limit; i++)
        {
            _ = limiter.TryAcquire("a0", out _);
            _ = limiter.TryAcquire("a1", out _);
        }

        Assert.False(limiter.TryAcquire("a0", out _));

        // Act: a new address arrives at the full table
        clock.Advance(_tick);
        var granted = limiter.TryAcquire("new", out _);
        var countAfterNew = limiter.AddressCount();
        var secondOldest = limiter.TryAcquire("a1", out _);
        var oldest = limiter.TryAcquire("a0", out _);

        // Assert: the new address is granted, the table did not grow, the oldest window was dropped (so its address
        // starts again) while the next oldest was still present
        Assert.True(granted);
        Assert.Equal(AnonymousRateLimit.Capacity, countAfterNew);
        Assert.False(secondOldest);
        Assert.True(oldest);
        Assert.Equal(AnonymousRateLimit.Capacity, limiter.AddressCount());
    }

    /// <summary>
    ///     Test the regression of the pruning race: a window that was renewed is a live window, and making room in
    ///     the table never removes it, so an address that is over its limit stays refused.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_RenewedWindow_IsNotRemovedByPruning()
    {
        // Arrange: a full table, one address of which is then used in a new window up to over its limit
        var clock = new MovableTimeProvider(_start);
        var limiter = new AnonymousRateLimit(clock);
        Assert.True(limiter.TryAcquire("renewed", out _));
        for (var i = 0; i < AnonymousRateLimit.Capacity - 1; i++)
        {
            Assert.True(limiter.TryAcquire($"other-{i}", out _));
        }

        clock.Advance(AnonymousRateLimit.Window + TimeSpan.FromSeconds(1));
        for (var i = 0; i < AnonymousRateLimit.Limit; i++)
        {
            Assert.True(limiter.TryAcquire("renewed", out _));
        }

        Assert.False(limiter.TryAcquire("renewed", out _));

        // Act: new addresses arrive; the ended windows of the others are pruned to make room
        Assert.True(limiter.TryAcquire("new-1", out _));
        Assert.True(limiter.TryAcquire("new-2", out _));
        var stillRefused = limiter.TryAcquire("renewed", out var retryAfter);

        // Assert: the renewed window survived and the ended ones are gone
        Assert.False(stillRefused);
        Assert.True(retryAfter > TimeSpan.Zero && retryAfter <= AnonymousRateLimit.Window);
        Assert.Equal(3, limiter.AddressCount());
    }

    /// <summary>
    ///     Test that a clock that steps backwards keeps the table within its capacity without an error, and that the
    ///     windows that have ended are still found when they sit behind a window that has not.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_ClockStepsBack_StillBoundedAndConsistent()
    {
        // Arrange: a window that starts later than the windows created after the clock stepped back by ten minutes
        var clock = new MovableTimeProvider(_start);
        var limiter = new AnonymousRateLimit(clock);
        Assert.True(limiter.TryAcquire("head", out _));
        clock.Advance(-TimeSpan.FromMinutes(10));
        for (var i = 0; i < AnonymousRateLimit.Capacity - 1; i++)
        {
            Assert.True(limiter.TryAcquire($"behind-{i}", out _));
        }

        Assert.Equal(AnonymousRateLimit.Capacity, limiter.AddressCount());

        // Act: the later windows have ended, the first has not (the clock is still before its start)
        clock.Advance(AnonymousRateLimit.Window + TimeSpan.FromSeconds(1));
        var granted = limiter.TryAcquire("new", out var retryAfter);

        // Assert: the full scan removed every ended window behind the live head; the answer is not negative
        Assert.True(granted);
        Assert.Equal(2, limiter.AddressCount());
        Assert.True(retryAfter > TimeSpan.Zero);

        // Act, Assert: a flood of addresses after a further step back is still bounded
        clock.Advance(-TimeSpan.FromMinutes(5));
        for (var i = 0; i < 2 * AnonymousRateLimit.Capacity; i++)
        {
            clock.Advance(_tick);
            Assert.True(limiter.TryAcquire($"flood-{i}", out _));
        }

        Assert.Equal(AnonymousRateLimit.Capacity, limiter.AddressCount());
    }

    /// <summary>
    ///     Test that parallel requests of one address are granted exactly up to the limit, no more and no fewer.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_Concurrent_GrantsExactlyTheLimitPerAddress()
    {
        // Arrange: a clock that does not move, and many parallel requests of one address
        var limiter = new AnonymousRateLimit(new MovableTimeProvider(_start));
        var granted = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = TestContext.Current.CancellationToken
        };

        // Act
        Parallel.For(0, 8 * 200, options, call =>
        {
            if (limiter.TryAcquire("hot", out _))
            {
                _ = Interlocked.Increment(ref granted);
            }
        });

        // Assert: exactly the limit was granted
        Assert.Equal(AnonymousRateLimit.Limit, granted);
        Assert.Equal(1, limiter.AddressCount());
    }

    /// <summary>
    ///     Test that parallel requests of many distinct addresses next to a few hot addresses keep the table within its
    ///     capacity, and that while the table is not full of live windows no hot address gets more than its limit.
    /// </summary>
    [Fact]
    public void AnonymousRateLimit_TryAcquire_ConcurrentDistinctAddresses_StaysWithinCapacity()
    {
        // Arrange: a clock that does not move
        const int hotCount = 4;
        var limiter = new AnonymousRateLimit(new MovableTimeProvider(_start));
        var granted = new int[hotCount];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 8,
            CancellationToken = TestContext.Current.CancellationToken
        };

        // Act: churn below the capacity next to the hot addresses
        Parallel.For(0, AnonymousRateLimit.Capacity / 2, options, i =>
        {
            Assert.True(limiter.TryAcquire($"churn-{i}", out _));
            var hot = i % hotCount;
            if (limiter.TryAcquire($"hot-{hot}", out _))
            {
                _ = Interlocked.Increment(ref granted[hot]);
            }
        });

        // Assert: every hot address was granted exactly its limit (nothing live was dropped)
        Assert.All(granted, count => Assert.Equal(AnonymousRateLimit.Limit, count));
        Assert.Equal((AnonymousRateLimit.Capacity / 2) + hotCount, limiter.AddressCount());

        // Act: a flood of three times the capacity, in parallel
        Parallel.For(0, 3 * AnonymousRateLimit.Capacity, options, i =>
        {
            Assert.True(limiter.TryAcquire($"flood-{i}", out _));
        });

        // Assert: only the cap is checked, because a full table of live windows may drop any of them
        Assert.Equal(AnonymousRateLimit.Capacity, limiter.AddressCount());
    }
}
