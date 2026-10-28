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

using System.Collections.Concurrent;

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     Limits how often one client address may use the two routes that a project can open to anonymous callers.
/// </summary>
/// <remarks>
///     An anonymous download of an attestation PDF costs a signature, so an address that asks too often is answered
///     <c>429</c> with <c>Retry-After</c>. The limit is a fixed window per address; only requests without a signed-in
///     user are counted (a token holder is counted by the token throttle instead). The counters are in memory, so
///     they start empty after a restart, and the table is pruned when it grows. The class is safe for concurrent use.
/// </remarks>
internal sealed class AnonymousRateLimit
{
    /// <summary>
    ///     The most requests that one address may make in one window.
    /// </summary>
    public const int Limit = 30;

    /// <summary>
    ///     The length of the window.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The number of addresses after which expired windows are removed.
    /// </summary>
    private const int PruneThreshold = 4096;

    /// <summary>
    ///     The address that is used when the client address is not known.
    /// </summary>
    private const string UnknownAddress = "unknown";

    /// <summary>
    ///     The time source.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     The window start and the count of each address.
    /// </summary>
    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="AnonymousRateLimit"/> class.
    /// </summary>
    /// <param name="clock">The time source. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clock"/> is null.</exception>
    public AnonymousRateLimit(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>
    ///     Counts a request and says whether it is within the limit.
    /// </summary>
    /// <param name="address">The client address; <c>null</c> when it is not known.</param>
    /// <param name="retryAfter">When the request is refused, how long until the window ends.</param>
    /// <returns><c>true</c> when the request may proceed; <c>false</c> when the address is over its limit.</returns>
    public bool TryAcquire(string? address, out TimeSpan retryAfter)
    {
        var now = _clock.GetUtcNow();
        if (_counters.Count > PruneThreshold)
        {
            Prune(now);
        }

        var counter = _counters.GetOrAdd(address ?? UnknownAddress, _ => new Counter());
        lock (counter)
        {
            if (now - counter.Start >= Window)
            {
                counter.Start = now;
                counter.Count = 0;
            }

            counter.Count++;
            retryAfter = counter.Start + Window - now;
            return counter.Count <= Limit;
        }
    }

    /// <summary>
    ///     Removes the addresses whose window has ended.
    /// </summary>
    /// <param name="now">The current time.</param>
    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _counters)
        {
            if (now - pair.Value.Start >= Window)
            {
                _ = _counters.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>
    ///     The window of one address.
    /// </summary>
    private sealed class Counter
    {
        /// <summary>
        ///     Gets or sets the start of the window.
        /// </summary>
        public DateTimeOffset Start { get; set; } = DateTimeOffset.MinValue;

        /// <summary>
        ///     Gets or sets the number of requests in the window.
        /// </summary>
        public int Count { get; set; }
    }
}
