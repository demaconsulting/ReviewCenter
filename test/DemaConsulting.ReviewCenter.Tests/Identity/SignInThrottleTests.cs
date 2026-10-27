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

using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests for <see cref="SignInThrottle"/> and <see cref="SignInThrottleOptions"/> with a movable clock.
/// </summary>
public class SignInThrottleTests
{
    /// <summary>
    ///     Creates a throttle with small limits: 3 per name, 5 per address, a 60 second window.
    /// </summary>
    /// <param name="clock">Receives the movable clock of the throttle.</param>
    /// <returns>The throttle.</returns>
    private static SignInThrottle Create(out MovableTimeProvider clock)
    {
        clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var options = new SignInThrottleOptions { MaxFailuresPerName = 3, MaxFailuresPerAddress = 5, WindowSeconds = 60 };
        return new SignInThrottle(Options.Create(options), clock);
    }

    /// <summary>
    ///     Creates a throttle for the tests of the table cap: 3 per name, an address limit that is never reached, a 60 second window.
    /// </summary>
    /// <param name="clock">Receives the movable clock of the throttle.</param>
    /// <returns>The throttle.</returns>
    private static SignInThrottle CreateWithHighAddressLimit(out MovableTimeProvider clock)
    {
        clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var options = new SignInThrottleOptions { MaxFailuresPerName = 3, MaxFailuresPerAddress = 1000000, WindowSeconds = 60 };
        return new SignInThrottle(Options.Create(options), clock);
    }

    /// <summary>
    ///     Makes an attempt and tells whether it was allowed.
    /// </summary>
    /// <param name="throttle">The throttle.</param>
    /// <param name="address">The client address.</param>
    /// <param name="name">The typed name.</param>
    /// <returns><c>true</c> when the attempt was allowed.</returns>
    private static bool Allowed(SignInThrottle throttle, string? address, string? name)
    {
        return throttle.TryBegin(address, name) is not null;
    }

    /// <summary>
    ///     Makes attempts with new names from one address until the table holds at least the given number of keys.
    /// </summary>
    /// <param name="throttle">The throttle; its address limit must never be reached.</param>
    /// <param name="address">The client address.</param>
    /// <param name="prefix">The start of every name, so groups do not share names.</param>
    /// <param name="keys">The number of keys to reach.</param>
    private static void FillTo(SignInThrottle throttle, string address, string prefix, int keys)
    {
        var i = 0;
        while (throttle.KeyCount() < keys)
        {
            Assert.True(Allowed(throttle, address, prefix + i));
            i++;
        }
    }

    /// <summary>
    ///     Test that attempts within the limits are all allowed.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_WithinLimits_AllowsAttempts()
    {
        // Arrange
        var throttle = Create(out _);

        // Act
        var results = Enumerable.Range(0, 3).Select(_ => Allowed(throttle, "10.0.0.1", "ann")).ToArray();

        // Assert
        Assert.All(results, Assert.True);
    }

    /// <summary>
    ///     Test that the same name from the same address is refused after the limit, whatever its case.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_SameNameTooOften_RefusesFurtherAttempts()
    {
        // Arrange
        var throttle = Create(out _);
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.True(Allowed(throttle, "10.0.0.1", "ANN"));
        Assert.True(Allowed(throttle, "10.0.0.1", "Ann"));

        // Act
        var fourth = Allowed(throttle, "10.0.0.1", "aNN");
        var otherName = Allowed(throttle, "10.0.0.1", "bob");

        // Assert: the name is limited, other names from the address still pass
        Assert.False(fourth);
        Assert.True(otherName);
    }

    /// <summary>
    ///     Test that many names from one address are refused at the address limit.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_ManyNamesFromOneAddress_RefusesAtAddressLimit()
    {
        // Arrange
        var throttle = Create(out _);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "user" + i));
        }

        // Act
        var sixth = Allowed(throttle, "10.0.0.1", "user-new");

        // Assert
        Assert.False(sixth);
    }

    /// <summary>
    ///     Test that another address is counted separately.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_OtherAddress_IsCountedSeparately()
    {
        // Arrange
        var throttle = Create(out _);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        }

        // Act
        var sameAddress = Allowed(throttle, "10.0.0.1", "ann");
        var otherAddress = Allowed(throttle, "10.0.0.2", "ann");

        // Assert
        Assert.False(sameAddress);
        Assert.True(otherAddress);
    }

    /// <summary>
    ///     Test that attempts are allowed again once the window has passed.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_AfterWindow_AllowsAgain()
    {
        // Arrange
        var throttle = Create(out var clock);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        }

        Assert.False(Allowed(throttle, "10.0.0.1", "ann"));

        // Act
        clock.Advance(TimeSpan.FromSeconds(60));
        var afterWindow = Allowed(throttle, "10.0.0.1", "ann");

        // Assert
        Assert.True(afterWindow);
    }

    /// <summary>
    ///     Test that a success clears the name counter and gives one slot back to the address counter.
    /// </summary>
    [Fact]
    public void SignInThrottle_Succeeded_ClearsNameCounterAndReturnsAddressSlot()
    {
        // Arrange: 2 attempts for ann and 3 for bob use 5 address slots
        var throttle = Create(out _);
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        var reservation = throttle.TryBegin("10.0.0.1", "ANN");
        Assert.NotNull(reservation);
        Assert.True(Allowed(throttle, "10.0.0.1", "bob"));
        Assert.True(Allowed(throttle, "10.0.0.1", "bob"));
        Assert.True(Allowed(throttle, "10.0.0.1", "bob"));
        Assert.False(Allowed(throttle, "10.0.0.1", "carol"));

        // Act: ann signs in
        throttle.Succeeded(reservation);
        var carol = Allowed(throttle, "10.0.0.1", "carol");
        var annAgain = Allowed(throttle, "10.0.0.1", "ann");

        // Assert: one address slot came back (used by carol); ann's own counter is clear, but the address is full again
        Assert.True(carol);
        Assert.False(annAgain);
    }

    /// <summary>
    ///     Test that a refused attempt uses up no slot.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_RefusedAttempt_DoesNotConsumeSlots()
    {
        // Arrange: ann is at her limit; the address has 3 of 5 used
        var throttle = Create(out _);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        }

        // Act: many refused attempts for ann
        for (var i = 0; i < 10; i++)
        {
            Assert.False(Allowed(throttle, "10.0.0.1", "ann"));
        }

        // Assert: two address slots are still free
        Assert.True(Allowed(throttle, "10.0.0.1", "bob"));
        Assert.True(Allowed(throttle, "10.0.0.1", "carol"));
        Assert.False(Allowed(throttle, "10.0.0.1", "dave"));
    }

    /// <summary>
    ///     Test that a missing address or name is handled and counted like any other.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_NullAddressOrName_IsHandled()
    {
        // Arrange
        var throttle = Create(out _);

        // Act
        var results = Enumerable.Range(0, 4).Select(_ => Allowed(throttle, null, null)).ToArray();

        // Assert: the unknown address with an empty name is limited like any other key
        Assert.Equal([true, true, true, false], results);
        Assert.True(Allowed(throttle, "", "ann"));
    }

    /// <summary>
    ///     Test that a flood of distinct keys cannot grow the table past its cap.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_ManyDistinctKeys_KeepsTableBounded()
    {
        // Arrange: limits high enough that no address is ever refused
        var throttle = CreateWithHighAddressLimit(out _);

        // Act
        for (var i = 0; i < SignInThrottle.MaxKeys; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "name" + i));
        }

        // Assert
        Assert.True(throttle.KeyCount() <= SignInThrottle.MaxKeys);
    }

    /// <summary>
    ///     Test that a full table evicts only its oldest counters, keeps the recent ones and is never cleared.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_TableFull_EvictsOldestCountersAndKeepsRecentOnes()
    {
        // Arrange: the oldest name is at its limit, a later name has used 2 of 3, and the table is exactly full
        var throttle = CreateWithHighAddressLimit(out _);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "first"));
        }

        Assert.False(Allowed(throttle, "10.0.0.1", "first"));
        FillTo(throttle, "10.0.0.1", "filler", SignInThrottle.MaxKeys - 1);
        Assert.True(Allowed(throttle, "10.0.0.1", "victim"));
        Assert.True(Allowed(throttle, "10.0.0.1", "victim"));
        Assert.Equal(SignInThrottle.MaxKeys, throttle.KeyCount());

        // Act: 50 more new names force 50 evictions
        for (var i = 0; i < 50; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "new" + i));
        }

        // Assert: the table stayed full (not cleared), the oldest counter is gone, the recent one is intact
        Assert.Equal(SignInThrottle.MaxKeys, throttle.KeyCount());
        Assert.True(Allowed(throttle, "10.0.0.1", "first"));
        Assert.True(Allowed(throttle, "10.0.0.1", "victim"));
        Assert.False(Allowed(throttle, "10.0.0.1", "victim"));
    }

    /// <summary>
    ///     Test that an attempt that needs only one new key evicts nothing when exactly one slot is free.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_OneKeyMissing_DoesNotEvictForTheExistingAddressKey()
    {
        // Arrange: the table holds one key less than its cap, the first name is at its limit
        var throttle = CreateWithHighAddressLimit(out _);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "first"));
        }

        FillTo(throttle, "10.0.0.1", "filler", SignInThrottle.MaxKeys - 1);

        // Act: a new name from the same address needs one slot only, because the address key exists
        var allowed = Allowed(throttle, "10.0.0.1", "extra");

        // Assert: nothing was evicted, so the first name is still refused
        Assert.True(allowed);
        Assert.Equal(SignInThrottle.MaxKeys, throttle.KeyCount());
        Assert.False(Allowed(throttle, "10.0.0.1", "first"));
    }

    /// <summary>
    ///     Test that expired counters are removed before live counters are evicted.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_TableFullOfExpiredKeys_PrunesExpiredBeforeEvictingLiveOnes()
    {
        // Arrange: an old group of keys, and a newer group that fills the table and holds a name at its limit
        var throttle = CreateWithHighAddressLimit(out var clock);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "old-start"));
        }

        FillTo(throttle, "10.0.0.1", "old", 4000);
        clock.Advance(TimeSpan.FromSeconds(30));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.2", "mid-start"));
        }

        FillTo(throttle, "10.0.0.2", "mid", SignInThrottle.MaxKeys - 1);

        // Act: the old group has passed its window; a new attempt arrives
        clock.Advance(TimeSpan.FromSeconds(30));
        var allowed = Allowed(throttle, "10.0.0.3", "fresh");

        // Assert: the old group is gone, the newer group is intact
        Assert.True(allowed);
        Assert.True(throttle.KeyCount() < SignInThrottle.MaxKeys - 3000);
        Assert.False(Allowed(throttle, "10.0.0.2", "mid-start"));
        Assert.True(Allowed(throttle, "10.0.0.1", "old-start"));
    }

    /// <summary>
    ///     Test that a full table removes expired counters wherever they are in the creation order, so a clock that
    ///     stepped backwards does not cause a live counter to be evicted before expired ones.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_ClockSteppedBackwards_PrunesExpiredBeforeEvictingLiveOnes()
    {
        // Arrange: a live name at its limit is created at +100 s; the clock then steps back to 0 and fills the table
        var throttle = CreateWithHighAddressLimit(out var clock);
        clock.Advance(TimeSpan.FromSeconds(100));
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "live"));
        }

        clock.Advance(TimeSpan.FromSeconds(-100));
        FillTo(throttle, "10.0.0.2", "expiring", SignInThrottle.MaxKeys);
        Assert.Equal(SignInThrottle.MaxKeys, throttle.KeyCount());

        // Act: at +61 s the filler windows (started at 0) have passed, the live one (started at +100) has not
        clock.Advance(TimeSpan.FromSeconds(61));
        var allowed = Allowed(throttle, "10.0.0.3", "fresh");

        // Assert: the expired fillers are gone and the live counter was not evicted, so its name is still refused
        Assert.True(allowed);
        Assert.True(throttle.KeyCount() < SignInThrottle.MaxKeys - 1000);
        Assert.False(Allowed(throttle, "10.0.0.1", "live"));
    }

    /// <summary>
    ///     Test that a success from before a window rollover does not clear the new name counter.
    /// </summary>
    [Fact]
    public void SignInThrottle_Succeeded_AfterWindowRolledOver_DoesNotClearNewNameCounter()
    {
        // Arrange: a slow sign-in starts, the window passes, and a new attempt starts a new window
        var throttle = Create(out var clock);
        var slow = throttle.TryBegin("10.0.0.1", "ann");
        Assert.NotNull(slow);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));

        // Act: the slow sign-in succeeds
        throttle.Succeeded(slow);

        // Assert: the new window still counts one attempt, so only two more are allowed
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.False(Allowed(throttle, "10.0.0.1", "ann"));
    }

    /// <summary>
    ///     Test that a success from before a window rollover does not give a slot back to the new address counter.
    /// </summary>
    [Fact]
    public void SignInThrottle_Succeeded_AfterWindowRolledOver_DoesNotRefundNewAddressCounter()
    {
        // Arrange: a slow sign-in starts, the window passes, and four attempts start a new address window
        var throttle = Create(out var clock);
        var slow = throttle.TryBegin("10.0.0.1", "ann");
        Assert.NotNull(slow);
        clock.Advance(TimeSpan.FromSeconds(60));
        for (var i = 0; i < 4; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "user" + i));
        }

        // Act: the slow sign-in succeeds
        throttle.Succeeded(slow);

        // Assert: the address window still counts four attempts, so one more is allowed and then it is full
        Assert.True(Allowed(throttle, "10.0.0.1", "user4"));
        Assert.False(Allowed(throttle, "10.0.0.1", "user5"));
    }

    /// <summary>
    ///     Test that a success after the counters were evicted and started again leaves the new counters alone.
    /// </summary>
    [Fact]
    public void SignInThrottle_Succeeded_AfterEviction_DoesNotTouchRecreatedWindows()
    {
        // Arrange: a sign-in starts, a flood from another address evicts its counters, and the name is counted again twice
        var throttle = CreateWithHighAddressLimit(out _);
        var slow = throttle.TryBegin("10.0.0.1", "ann");
        Assert.NotNull(slow);
        FillTo(throttle, "10.0.0.9", "flood", SignInThrottle.MaxKeys);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.9", "extra" + i));
        }

        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));

        // Act: the earlier sign-in succeeds
        throttle.Succeeded(slow);

        // Assert: the recreated name counter was not cleared, so the third attempt is allowed and the fourth is not
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.False(Allowed(throttle, "10.0.0.1", "ann"));
    }

    /// <summary>
    ///     Test that one reservation gives its slot back only once.
    /// </summary>
    [Fact]
    public void SignInThrottle_Succeeded_CalledTwiceWithOneReservation_RefundsOnce()
    {
        // Arrange: four other names and one attempt for ann fill the address (5 of 5)
        var throttle = Create(out _);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "user" + i));
        }

        var reservation = throttle.TryBegin("10.0.0.1", "ann");
        Assert.NotNull(reservation);
        throttle.Succeeded(reservation);

        // Act
        throttle.Succeeded(reservation);

        // Assert: only one slot came back, so one more attempt is allowed and then the address is full again
        Assert.True(Allowed(throttle, "10.0.0.1", "user4"));
        Assert.False(Allowed(throttle, "10.0.0.1", "user5"));
    }

    /// <summary>
    ///     Test that a missing reservation is rejected.
    /// </summary>
    [Fact]
    public void SignInThrottle_Succeeded_NullReservation_Throws()
    {
        // Arrange
        var throttle = Create(out _);

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => throttle.Succeeded(null!));
    }

    /// <summary>
    ///     Test that a reservation from before a reset cannot refund counters that were started after the reset.
    /// </summary>
    [Fact]
    public void SignInThrottle_Reset_DoesNotReuseGenerations()
    {
        // Arrange: a reservation, a reset, and two new attempts for the same name
        var throttle = Create(out _);
        var old = throttle.TryBegin("10.0.0.1", "ann");
        Assert.NotNull(old);
        throttle.Reset();
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));

        // Act
        throttle.Succeeded(old);

        // Assert: the name counter still holds two attempts, so one more is allowed and then it is full
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.False(Allowed(throttle, "10.0.0.1", "ann"));
    }

    /// <summary>
    ///     Test that reset forgets every counter.
    /// </summary>
    [Fact]
    public void SignInThrottle_Reset_ClearsAllCounters()
    {
        // Arrange
        var throttle = Create(out _);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        }

        Assert.False(Allowed(throttle, "10.0.0.1", "ann"));

        // Act
        throttle.Reset();

        // Assert
        Assert.True(Allowed(throttle, "10.0.0.1", "ann"));
        Assert.Equal(2, throttle.KeyCount());
    }

    /// <summary>
    ///     Test that the settings default to 5 attempts per name, 20 per address and a window of 5 minutes.
    /// </summary>
    [Fact]
    public void SignInThrottleOptions_Defaults_AreFiveTwentyAndFiveMinutes()
    {
        // Arrange / Act
        var options = new SignInThrottleOptions();

        // Assert
        Assert.Equal("Identity:Throttle", SignInThrottleOptions.SectionName);
        Assert.Equal(5, options.MaxFailuresPerName);
        Assert.Equal(20, options.MaxFailuresPerAddress);
        Assert.Equal(300, options.WindowSeconds);
    }
}
