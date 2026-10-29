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
///     Tests of <see cref="TotpChallengeStore"/>, the short-lived single-use challenges of the second sign-in step.
/// </summary>
public class TotpChallengeStoreTests
{
    /// <summary>
    ///     The clock the tests move.
    /// </summary>
    private readonly MovableTimeProvider _clock = new(TotpTestWorld.Start);

    /// <summary>
    ///     Test that an issued token is long, URL-safe and different each time, and that the store keeps only a hash.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_Issue_ReturnsRandomUrlSafeTokens()
    {
        // Arrange: a store
        var store = new TotpChallengeStore(_clock);

        // Act: issue two
        var first = store.Issue("ann", "10.0.0.1", "/");
        var second = store.Issue("ann", "10.0.0.1", "/");

        // Assert: 256 bits as URL-safe text, different, and the challenge does not hold the token
        Assert.Equal(43, first.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", first);
        Assert.NotEqual(first, second);
        var challenge = store.Peek(first, "10.0.0.1");
        Assert.NotNull(challenge);
        Assert.DoesNotContain(first, challenge.TokenHash, StringComparison.Ordinal);
        Assert.Equal("ann", challenge.Username);
        Assert.Equal("/", challenge.ReturnUrl);
    }

    /// <summary>
    ///     Test that a challenge can be taken exactly once.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_Take_Twice_GivesTheChallengeOnlyOnce()
    {
        // Arrange: one challenge
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", "10.0.0.1", "/x");

        // Act: take it twice
        var first = store.Take(token, "10.0.0.1");
        var second = store.Take(token, "10.0.0.1");

        // Assert: only the first gets it, and it is gone
        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Null(store.Peek(token, "10.0.0.1"));
    }

    /// <summary>
    ///     Test that parallel takers get the challenge exactly once.
    /// </summary>
    [Fact]
    public async Task TotpChallengeStore_Take_Concurrently_GivesTheChallengeToExactlyOne()
    {
        // Arrange: one challenge and 32 takers released together
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", "10.0.0.1", "/");
        using var start = new ManualResetEventSlim();

        // Act: take in parallel
        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(
            () =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                return store.Take(token, "10.0.0.1") is not null;
            },
            TestContext.Current.CancellationToken)).ToList();
        start.Set();
        var results = await Task.WhenAll(tasks);

        // Assert: exactly one succeeded
        Assert.Equal(1, results.Count(r => r));
    }

    /// <summary>
    ///     Test that a challenge issued to one client address cannot be used or removed from another.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_PeekAndTake_ForeignAddress_AreRefusedAndLeaveTheChallenge()
    {
        // Arrange: a challenge for one address
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", "10.0.0.1", "/");

        // Act: another address asks
        var peeked = store.Peek(token, "10.0.0.2");
        var taken = store.Take(token, "10.0.0.2");

        // Assert: refused, and the owner still has it
        Assert.Null(peeked);
        Assert.Null(taken);
        Assert.NotNull(store.Peek(token, "10.0.0.1"));
    }

    /// <summary>
    ///     Test that an unknown address is treated as one address and not as a wildcard.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_NullAddress_MatchesOnlyNullAddress()
    {
        // Arrange: a challenge issued without an address
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", null, "/");

        // Act and Assert: only a request without an address fits
        Assert.NotNull(store.Peek(token, null));
        Assert.Null(store.Peek(token, "10.0.0.1"));
    }

    /// <summary>
    ///     Test that missing and unknown tokens give nothing.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_UnknownOrMissingToken_GivesNothing()
    {
        // Arrange: a store with one challenge
        var store = new TotpChallengeStore(_clock);
        store.Issue("ann", "10.0.0.1", "/");

        // Act and Assert: nothing for null, empty and a made-up token
        Assert.Null(store.Peek(null, "10.0.0.1"));
        Assert.Null(store.Peek(string.Empty, "10.0.0.1"));
        Assert.Null(store.Take(null, "10.0.0.1"));
        Assert.Null(store.Take("made-up", "10.0.0.1"));
    }

    /// <summary>
    ///     Test that a challenge ends after its lifetime, and that ending removes it.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_AfterTheLifetime_TheChallengeIsGone()
    {
        // Arrange: a challenge, then time passes just beyond the lifetime
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", "10.0.0.1", "/");
        _clock.Advance(TotpChallengeStore.Lifetime - TimeSpan.FromSeconds(1));
        var stillThere = store.Peek(token, "10.0.0.1");
        _clock.Advance(TimeSpan.FromSeconds(1));

        // Act: look it up and take it
        var peeked = store.Peek(token, "10.0.0.1");
        var taken = store.Take(token, "10.0.0.1");

        // Assert: valid one second before the end, gone at the end, and removed
        Assert.NotNull(stillThere);
        Assert.Null(peeked);
        Assert.Null(taken);
        Assert.Equal(0, store.Count());
    }

    /// <summary>
    ///     Test that a returned challenge counts failures and ends after the maximum.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_Return_CountsFailuresAndEndsAtTheMaximum()
    {
        // Arrange: a challenge
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", "10.0.0.1", "/");

        // Act: take and return it until the limit
        var kept = 0;
        var ended = false;
        for (var i = 0; i < TotpChallengeStore.MaxFailures + 2 && !ended; i++)
        {
            var challenge = store.Take(token, "10.0.0.1");
            Assert.NotNull(challenge);
            if (store.Return(challenge))
            {
                kept++;
            }
            else
            {
                ended = true;
            }
        }

        // Assert: kept for the first four failures, ended on the fifth, and gone
        Assert.Equal(TotpChallengeStore.MaxFailures - 1, kept);
        Assert.True(ended);
        Assert.Null(store.Peek(token, "10.0.0.1"));
    }

    /// <summary>
    ///     Test that invalidating a user drops that user's challenges, without regard to case, and no others.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_InvalidateUser_DropsOnlyThatUsersChallenges()
    {
        // Arrange: two challenges of ann (one in other case) and one of bob
        var store = new TotpChallengeStore(_clock);
        var a1 = store.Issue("ann", "10.0.0.1", "/");
        var a2 = store.Issue("Ann", "10.0.0.1", "/");
        var b1 = store.Issue("bob", "10.0.0.1", "/");

        // Act: invalidate ann
        store.InvalidateUser("ANN");

        // Assert: ann's are gone, bob's stays
        Assert.Null(store.Peek(a1, "10.0.0.1"));
        Assert.Null(store.Peek(a2, "10.0.0.1"));
        Assert.NotNull(store.Peek(b1, "10.0.0.1"));
    }

    /// <summary>
    ///     Test that a challenge given back after its end is not kept.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_Return_AfterTheLifetime_EndsTheChallenge()
    {
        // Arrange: a challenge taken just before its end
        var store = new TotpChallengeStore(_clock);
        var token = store.Issue("ann", "10.0.0.1", "/");
        var challenge = store.Take(token, "10.0.0.1");
        _clock.Advance(TotpChallengeStore.Lifetime);

        // Act: give it back after the end
        var kept = store.Return(challenge!);

        // Assert: not kept
        Assert.False(kept);
        Assert.Equal(0, store.Count());
    }

    /// <summary>
    ///     Test that the number of challenges is capped, dropping ended ones first and then the oldest.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_Issue_BeyondTheCap_StaysWithinTheCapAndDropsTheOldest()
    {
        // Arrange: a full store, filled with challenges issued a millisecond apart
        var store = new TotpChallengeStore(_clock);
        var first = store.Issue("first", "10.0.0.1", "/");
        for (var i = 1; i < TotpChallengeStore.MaxEntries; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(1));
            store.Issue("user" + i, "10.0.0.1", "/");
        }

        Assert.Equal(TotpChallengeStore.MaxEntries, store.Count());

        // Act: issue one more
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var extra = store.Issue("extra", "10.0.0.1", "/");

        // Assert: the cap holds, the newest is there, and the oldest was dropped
        Assert.True(store.Count() <= TotpChallengeStore.MaxEntries);
        Assert.NotNull(store.Peek(extra, "10.0.0.1"));
        Assert.Null(store.Peek(first, "10.0.0.1"));
    }

    /// <summary>
    ///     Test that null arguments are refused.
    /// </summary>
    [Fact]
    public void TotpChallengeStore_NullArguments_Throw()
    {
        // Arrange: a store
        var store = new TotpChallengeStore(_clock);

        // Act and Assert: each misuse is refused
        Assert.Throws<ArgumentNullException>(() => new TotpChallengeStore(null!));
        Assert.Throws<ArgumentNullException>(() => store.Issue(null!, "a", "/"));
        Assert.Throws<ArgumentNullException>(() => store.Issue("a", "a", null!));
        Assert.Throws<ArgumentNullException>(() => store.Return(null!));
        Assert.Throws<ArgumentNullException>(() => store.InvalidateUser(null!));
    }
}
