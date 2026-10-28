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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="ReauthProofStore"/> with a clock the tests can move.
/// </summary>
public class ReauthProofStoreTests
{
    /// <summary>
    ///     The instant at which each test's clock starts.
    /// </summary>
    private static readonly DateTimeOffset _start = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Test that a proof can be seen without being spent, and is spent exactly once.
    /// </summary>
    [Fact]
    public void ReauthProofStore_TryConsume_SucceedsExactlyOnce()
    {
        // Arrange
        var store = new ReauthProofStore(new MovableTimeProvider(_start));
        store.Issue(1, 2, "rev-1", 3);

        // Act
        var seen = store.Has(1, 2, "rev-1", 3);
        var first = store.TryConsume(1, 2, "rev-1", 3);
        var second = store.TryConsume(1, 2, "rev-1", 3);

        // Assert
        Assert.True(seen);
        Assert.True(first);
        Assert.False(second);
        Assert.False(store.Has(1, 2, "rev-1", 3));
    }

    /// <summary>
    ///     Test that a proof is bound to the user, project, review and slot it was issued for, and that a refusal for
    ///     another binding does not spend it.
    /// </summary>
    /// <param name="userId">The user that tries to use the proof.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="reviewId">The review.</param>
    /// <param name="slotId">The slot.</param>
    [Theory]
    [InlineData(9, 2, "rev-1", 3)]
    [InlineData(1, 9, "rev-1", 3)]
    [InlineData(1, 2, "rev-9", 3)]
    [InlineData(1, 2, "rev-1", 9)]
    public void ReauthProofStore_OtherBinding_IsRefusedAndKeepsTheProof(int userId, int projectId, string reviewId, int slotId)
    {
        // Arrange
        var store = new ReauthProofStore(new MovableTimeProvider(_start));
        store.Issue(1, 2, "rev-1", 3);

        // Act
        var other = store.TryConsume(userId, projectId, reviewId, slotId);

        // Assert
        Assert.False(other);
        Assert.False(store.Has(userId, projectId, reviewId, slotId));
        Assert.True(store.TryConsume(1, 2, "rev-1", 3));
    }

    /// <summary>
    ///     Test that a proof is live just before its lifetime ends and expired at the end of it.
    /// </summary>
    [Fact]
    public void ReauthProofStore_Expiry_EndsTheProofAtTheLifetime()
    {
        // Arrange
        var clock = new MovableTimeProvider(_start);
        var store = new ReauthProofStore(clock);
        store.Issue(1, 2, "rev-1", 3);
        store.Issue(1, 2, "rev-1", 4);

        // Act
        clock.Advance(ReauthProofStore.Lifetime - TimeSpan.FromSeconds(1));
        var justBefore = store.Has(1, 2, "rev-1", 3);
        clock.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.True(justBefore);
        Assert.False(store.Has(1, 2, "rev-1", 3));
        Assert.False(store.TryConsume(1, 2, "rev-1", 3));
        Assert.False(store.TryConsume(1, 2, "rev-1", 4));
    }

    /// <summary>
    ///     Test that issuing again restarts the lifetime of a proof.
    /// </summary>
    [Fact]
    public void ReauthProofStore_IssueAgain_RestartsTheLifetime()
    {
        // Arrange
        var clock = new MovableTimeProvider(_start);
        var store = new ReauthProofStore(clock);
        store.Issue(1, 2, "rev-1", 3);
        clock.Advance(ReauthProofStore.Lifetime - TimeSpan.FromSeconds(1));

        // Act
        store.Issue(1, 2, "rev-1", 3);
        clock.Advance(TimeSpan.FromSeconds(2));

        // Assert
        Assert.True(store.TryConsume(1, 2, "rev-1", 3));
    }

    /// <summary>
    ///     Test that the store never holds more than its maximum: expired proofs are dropped first, then the oldest.
    /// </summary>
    [Fact]
    public void ReauthProofStore_Full_DropsExpiredThenOldest()
    {
        // Arrange: a full store whose first proof is the oldest
        var clock = new MovableTimeProvider(_start);
        var store = new ReauthProofStore(clock);
        for (var slot = 0; slot < ReauthProofStore.MaxEntries; slot++)
        {
            store.Issue(1, 1, "rev-1", slot);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        // Act: one more proof pushes the oldest out
        store.Issue(1, 1, "rev-1", ReauthProofStore.MaxEntries);

        // Assert
        Assert.False(store.Has(1, 1, "rev-1", 0));
        Assert.True(store.Has(1, 1, "rev-1", 1));
        Assert.True(store.Has(1, 1, "rev-1", ReauthProofStore.MaxEntries));

        // Act: after the lifetime every proof is expired and is dropped for a new one
        clock.Advance(ReauthProofStore.Lifetime);
        store.Issue(2, 1, "rev-1", 0);

        // Assert
        Assert.True(store.Has(2, 1, "rev-1", 0));
        Assert.False(store.Has(1, 1, "rev-1", 1));
    }

    /// <summary>
    ///     Test that a missing review identifier and a missing clock are refused.
    /// </summary>
    [Fact]
    public void ReauthProofStore_NullArguments_Throw()
    {
        // Arrange
        var store = new ReauthProofStore(new MovableTimeProvider(_start));

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ReauthProofStore(null!));
        Assert.Throws<ArgumentNullException>(() => store.Issue(1, 1, null!, 1));
        Assert.Throws<ArgumentNullException>(() => store.Has(1, 1, null!, 1));
        Assert.Throws<ArgumentNullException>(() => store.TryConsume(1, 1, null!, 1));
    }
}
