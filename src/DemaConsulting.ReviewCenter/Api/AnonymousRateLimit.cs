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

namespace DemaConsulting.ReviewCenter.Api;

/// <summary>
///     Limits how often one client address may use the two routes that a project can open to anonymous callers.
/// </summary>
/// <remarks>
///     An anonymous download of an attestation PDF costs a signature, so an address that asks too often is answered
///     <c>429</c> with <c>Retry-After</c>. The limit is a fixed window per address; only requests without a signed-in
///     user are counted (a token holder is counted by the token throttle instead). The table holds at most
///     <see cref="Capacity"/> addresses, so a flood of addresses cannot grow the memory without limit. When a new
///     address needs room, windows that have ended are removed first and, if that is not enough, only as many of the
///     oldest windows as are needed; the table is never cleared as a whole. A window is only ever removed by pruning
///     once it has ended, under the same lock that renews it, so a renewed window is never lost. A distributed flood
///     (more than <see cref="Capacity"/> addresses within one window, for example from one IPv6 /64 or with a forged
///     forwarded header) can still push live windows out, so those clients get a fresh budget: the limiter then
///     degrades toward failing open for them but never refuses anybody. A clock that steps backwards keeps a window
///     alive until time catches up. The counters are in memory per process, so they start empty after a restart. All
///     state is behind one lock, so the class is thread-safe.
/// </remarks>
internal sealed class AnonymousRateLimit
{
    /// <summary>
    ///     The most requests that one address may make in one window.
    /// </summary>
    public const int Limit = 30;

    /// <summary>
    ///     The most addresses kept in the table.
    /// </summary>
    internal const int Capacity = 4096;

    /// <summary>
    ///     The length of the window.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The address that is used when the client address is not known.
    /// </summary>
    private const string UnknownAddress = "unknown";

    /// <summary>
    ///     The time source.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Guards <see cref="_windows"/> and <see cref="_order"/>, and the windows in them.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The window of each address.
    /// </summary>
    private readonly Dictionary<string, AddressWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>
    ///     The addresses in the order their windows were created, oldest first; always the same addresses as <see cref="_windows"/>.
    /// </summary>
    private readonly LinkedList<string> _order = new();

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
        var key = address ?? UnknownAddress;
        lock (_lock)
        {
            var now = _clock.GetUtcNow();
            if (!_windows.TryGetValue(key, out var window) || now - window.Start >= Window)
            {
                // A new address, or a window that has ended: start a fresh window at the end of the order
                if (window is not null)
                {
                    RemoveWindow(key, window);
                }

                if (_windows.Count >= Capacity)
                {
                    MakeRoom(now);
                }

                window = new AddressWindow { Start = now };
                window.Node = _order.AddLast(key);
                _windows[key] = window;
            }

            window.Count++;
            retryAfter = window.Start + Window - now;
            return window.Count <= Limit;
        }
    }

    /// <summary>
    ///     Gets the number of addresses held; for tests of the cap.
    /// </summary>
    /// <returns>The number of addresses in the table.</returns>
    internal int AddressCount()
    {
        lock (_lock)
        {
            return _windows.Count;
        }
    }

    /// <summary>
    ///     Makes room for one more address: removes the windows that have ended, and if the table is still full, the
    ///     oldest windows by creation order. The address being added is not in the table, so it is never removed.
    /// </summary>
    /// <param name="now">The current time.</param>
    private void MakeRoom(DateTimeOffset now)
    {
        PruneExpired(now);
        if (_windows.Count >= Capacity)
        {
            // Look at every window, because a clock that stepped backwards breaks the order of the start times
            PruneAllExpired(now);
        }

        while (_windows.Count >= Capacity && _order.First is { } oldest)
        {
            RemoveWindow(oldest.Value, _windows[oldest.Value]);
        }
    }

    /// <summary>
    ///     Removes the windows that have ended from the head of the order. This is cheap and is enough while the clock
    ///     only moves forward; it can stop early after the clock has stepped backwards, and
    ///     <see cref="PruneAllExpired"/> finishes the work when the table is full.
    /// </summary>
    /// <param name="now">The current time.</param>
    private void PruneExpired(DateTimeOffset now)
    {
        while (_order.First is { } first)
        {
            var window = _windows[first.Value];
            if (now - window.Start < Window)
            {
                return;
            }

            RemoveWindow(first.Value, window);
        }
    }

    /// <summary>
    ///     Removes every window that has ended, wherever it is in the order. It scans the whole table (at most
    ///     <see cref="Capacity"/> windows), so it is used only when the table is full.
    /// </summary>
    /// <param name="now">The current time.</param>
    private void PruneAllExpired(DateTimeOffset now)
    {
        var node = _order.First;
        while (node is not null)
        {
            var next = node.Next;
            var window = _windows[node.Value];
            if (now - window.Start >= Window)
            {
                RemoveWindow(node.Value, window);
            }

            node = next;
        }
    }

    /// <summary>
    ///     Removes a window from the table and from the order.
    /// </summary>
    /// <param name="key">The address.</param>
    /// <param name="window">The window of the address.</param>
    private void RemoveWindow(string key, AddressWindow window)
    {
        _windows.Remove(key);
        _order.Remove(window.Node);
    }

    /// <summary>
    ///     The window of one address.
    /// </summary>
    private sealed class AddressWindow
    {
        /// <summary>
        ///     Gets or sets the start of the window.
        /// </summary>
        public DateTimeOffset Start { get; set; }

        /// <summary>
        ///     Gets or sets the number of requests in the window.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        ///     Gets or sets the place of the address in the creation order.
        /// </summary>
        public LinkedListNode<string> Node { get; set; } = null!;
    }
}
