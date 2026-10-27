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
    ///     Test that attempts within the limits are all allowed.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_WithinLimits_AllowsAttempts()
    {
        // Arrange
        var throttle = Create(out _);

        // Act
        var results = Enumerable.Range(0, 3).Select(_ => throttle.TryBegin("10.0.0.1", "ann")).ToArray();

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
        Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        Assert.True(throttle.TryBegin("10.0.0.1", "ANN"));
        Assert.True(throttle.TryBegin("10.0.0.1", "Ann"));

        // Act
        var fourth = throttle.TryBegin("10.0.0.1", "aNN");
        var otherName = throttle.TryBegin("10.0.0.1", "bob");

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
            Assert.True(throttle.TryBegin("10.0.0.1", "user" + i));
        }

        // Act
        var sixth = throttle.TryBegin("10.0.0.1", "user-new");

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
            Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        }

        // Act
        var sameAddress = throttle.TryBegin("10.0.0.1", "ann");
        var otherAddress = throttle.TryBegin("10.0.0.2", "ann");

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
            Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        }

        Assert.False(throttle.TryBegin("10.0.0.1", "ann"));

        // Act
        clock.Advance(TimeSpan.FromSeconds(60));
        var afterWindow = throttle.TryBegin("10.0.0.1", "ann");

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
        Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        Assert.True(throttle.TryBegin("10.0.0.1", "bob"));
        Assert.True(throttle.TryBegin("10.0.0.1", "bob"));
        Assert.True(throttle.TryBegin("10.0.0.1", "bob"));
        Assert.False(throttle.TryBegin("10.0.0.1", "carol"));

        // Act: ann signs in
        throttle.Succeeded("10.0.0.1", "ANN");
        var carol = throttle.TryBegin("10.0.0.1", "carol");
        var annAgain = throttle.TryBegin("10.0.0.1", "ann");

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
            Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        }

        // Act: many refused attempts for ann
        for (var i = 0; i < 10; i++)
        {
            Assert.False(throttle.TryBegin("10.0.0.1", "ann"));
        }

        // Assert: two address slots are still free
        Assert.True(throttle.TryBegin("10.0.0.1", "bob"));
        Assert.True(throttle.TryBegin("10.0.0.1", "carol"));
        Assert.False(throttle.TryBegin("10.0.0.1", "dave"));
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
        var results = Enumerable.Range(0, 4).Select(_ => throttle.TryBegin(null, null)).ToArray();

        // Assert: the unknown address with an empty name is limited like any other key
        Assert.Equal([true, true, true, false], results);
        Assert.True(throttle.TryBegin("", "ann"));
    }

    /// <summary>
    ///     Test that a flood of distinct keys cannot grow the table past its cap.
    /// </summary>
    [Fact]
    public void SignInThrottle_TryBegin_ManyDistinctKeys_KeepsTableBounded()
    {
        // Arrange: limits high enough that no address is ever refused
        var clock = new MovableTimeProvider(DateTimeOffset.UnixEpoch);
        var options = new SignInThrottleOptions { MaxFailuresPerName = 5, MaxFailuresPerAddress = 100000, WindowSeconds = 60 };
        var throttle = new SignInThrottle(Options.Create(options), clock);

        // Act
        for (var i = 0; i < SignInThrottle.MaxKeys; i++)
        {
            Assert.True(throttle.TryBegin("10.0.0.1", "name" + i));
        }

        // Assert
        Assert.True(throttle.KeyCount() <= SignInThrottle.MaxKeys);
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
            Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
        }

        Assert.False(throttle.TryBegin("10.0.0.1", "ann"));

        // Act
        throttle.Reset();

        // Assert
        Assert.True(throttle.TryBegin("10.0.0.1", "ann"));
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
