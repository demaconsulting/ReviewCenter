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

using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     Limits how often one client address may try to sign in, in memory.
/// </summary>
/// <remarks>
///     Two fixed-window counters are kept: one per client address and lower-cased user name, and one per client
///     address for all names together. <see cref="TryBegin"/> reserves a slot in both before the password is
///     checked (<see cref="TryBeginAddressOnly"/> reserves in the address counter alone), so a burst of parallel requests cannot all pass, and returns a <see cref="Reservation"/>;
///     <see cref="Succeeded"/> gives the slots back, but only to the windows the reservation was counted in (a
///     window that has since been removed or started again is left alone, and a reservation is given back once).
///     Every attempt is counted whether or not the user name exists, so the state never depends on the account.
///     The table holds at most <see cref="MaxKeys"/> counters, so an attacker cannot grow the memory without
///     limit. When a new counter needs room, expired counters are removed first and, if that is not enough, only
///     as many of the oldest counters as are needed; the table is never cleared as a whole. A distributed flood
///     (one address creates at most <c>MaxFailuresPerAddress</c> name counters per window, so it takes a few
///     hundred addresses, for example from one IPv6 /64 or with a forged forwarded header) can still push older
///     counters, its own included, out within one window: the throttle then degrades toward failing open for
///     those clients but never resets every counter at once and never refuses anybody. The account lockout is
///     kept in the database and still applies. A clock that steps backwards keeps a window alive until time
///     catches up. The state is per process and lost on restart. All state is behind one lock, so the class is
///     thread-safe.
/// </remarks>
internal sealed class SignInThrottle
{
    /// <summary>
    ///     The most keys kept in the table.
    /// </summary>
    public const int MaxKeys = 10000;

    /// <summary>
    ///     The longest key part taken from a typed name, so a long name cannot make long keys.
    /// </summary>
    private const int MaxNameKeyLength = 64;

    /// <summary>
    ///     The address text used when the client address is not known.
    /// </summary>
    private const string UnknownAddress = "unknown";

    /// <summary>
    ///     The settings.
    /// </summary>
    private readonly SignInThrottleOptions _options;

    /// <summary>
    ///     The clock; injected so tests control time.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Guards <see cref="_windows"/>, <see cref="_order"/> and <see cref="_nextGeneration"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The counter of each key.
    /// </summary>
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);

    /// <summary>
    ///     The keys in the order their windows were created, oldest first; always the same keys as <see cref="_windows"/>.
    /// </summary>
    private readonly LinkedList<string> _order = new();

    /// <summary>
    ///     The generation given to the next window; never reset, so a stale reservation cannot match a new window.
    /// </summary>
    private long _nextGeneration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SignInThrottle"/> class.
    /// </summary>
    /// <param name="options">The settings, already validated. Must not be null.</param>
    /// <param name="timeProvider">The clock. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public SignInThrottle(IOptions<SignInThrottleOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Counts a sign-in attempt, unless the client has used up its attempts.
    /// </summary>
    /// <param name="clientAddress">The client address as text; <c>null</c> is counted as one unknown address.</param>
    /// <param name="username">The typed user name; compared without regard to case; <c>null</c> is treated as empty.</param>
    /// <returns>
    ///     The reservation to give to <see cref="Succeeded"/> when the attempt may go on (it has been counted);
    ///     <c>null</c> when either limit is reached (nothing is counted then).
    /// </returns>
    public Reservation? TryBegin(string? clientAddress, string? username)
    {
        return Begin(clientAddress, NameKey(clientAddress, username));
    }

    /// <summary>
    ///     Counts an attempt against the quota of the client address only, with no name counter, unless the address
    ///     has used up its attempts.
    /// </summary>
    /// <param name="clientAddress">The client address as text; <c>null</c> is counted as one unknown address.</param>
    /// <returns>
    ///     The reservation to give to <see cref="Succeeded"/> when the attempt may go on (it has been counted);
    ///     <c>null</c> when the address limit is reached (nothing is counted then).
    /// </returns>
    /// <remarks>
    ///     For a request that carries no user name the caller can trust, such as the callback of a provider: a
    ///     shared constant name would let a few bad requests from one shared address block every person behind
    ///     it, so only the address limit applies. The cap on the table and the eviction are those of
    ///     <see cref="TryBegin"/>.
    /// </remarks>
    public Reservation? TryBeginAddressOnly(string? clientAddress)
    {
        return Begin(clientAddress, null);
    }

    /// <summary>
    ///     Counts an attempt against the address counter and, when a name key is given, the name counter.
    /// </summary>
    /// <param name="clientAddress">The client address, or <c>null</c>.</param>
    /// <param name="nameKey">The name key, or <c>null</c> to count the address only.</param>
    /// <returns>The reservation; <c>null</c> when a limit is reached (nothing is counted then).</returns>
    private Reservation? Begin(string? clientAddress, string? nameKey)
    {
        var addressKey = AddressKey(clientAddress);
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            PruneExpired(now, window);
            if ((nameKey is not null && CountOf(nameKey, now, window) >= _options.MaxFailuresPerName)
                || CountOf(addressKey, now, window) >= _options.MaxFailuresPerAddress)
            {
                return null;
            }

            // Only keys that are not in the table need a new slot
            var missing = MissingKeys(nameKey, addressKey);
            if (_windows.Count + missing > MaxKeys)
            {
                // The table is full: look at every window, because a clock that stepped backwards breaks the order
                PruneAllExpired(now, window);
                missing = MissingKeys(nameKey, addressKey);
            }

            MakeRoom(missing, nameKey, addressKey);
            var nameGeneration = nameKey is null ? 0 : Increment(nameKey, now, window);
            var addressGeneration = Increment(addressKey, now, window);
            return new Reservation(nameKey, nameGeneration, addressKey, addressGeneration);
        }
    }

    /// <summary>
    ///     Gives the slots of a successful sign-in back: the name counter is cleared and one attempt is
    ///     returned to the address counter, each only when it is still the window the reservation counted in.
    /// </summary>
    /// <param name="reservation">The reservation returned by <see cref="TryBegin"/>. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reservation"/> is null.</exception>
    public void Succeeded(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (_lock)
        {
            if (reservation.Consumed)
            {
                return;
            }

            reservation.Consumed = true;
            if (reservation.CountedNameKey is not null
                && _windows.TryGetValue(reservation.CountedNameKey, out var name)
                && name.Generation == reservation.NameGeneration)
            {
                RemoveWindow(reservation.CountedNameKey, name);
            }

            if (_windows.TryGetValue(reservation.CountedAddressKey, out var address) && address.Generation == reservation.AddressGeneration)
            {
                if (address.Count <= 1)
                {
                    RemoveWindow(reservation.CountedAddressKey, address);
                }
                else
                {
                    address.Count--;
                }
            }
        }
    }

    /// <summary>
    ///     Gives only the address slot of a reservation back, leaving the name counter as it is.
    /// </summary>
    /// <param name="reservation">The reservation returned by <see cref="TryBegin"/>. Must not be null.</param>
    /// <remarks>
    ///     For a sign-in whose password was right but still owes a second factor: the attempt must not use up the
    ///     quota of a client that is signing in correctly, yet the name counter stays so that the password steps and
    ///     the code steps of one account together stay bounded; only the complete sign-in
    ///     (<see cref="Succeeded"/> of the second step) clears it. The reservation is spent: a later
    ///     <see cref="Succeeded"/> or <see cref="ReleaseAddress"/> with it does nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reservation"/> is null.</exception>
    public void ReleaseAddress(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (_lock)
        {
            if (reservation.Consumed)
            {
                return;
            }

            reservation.Consumed = true;
            if (_windows.TryGetValue(reservation.CountedAddressKey, out var address) && address.Generation == reservation.AddressGeneration)
            {
                if (address.Count <= 1)
                {
                    RemoveWindow(reservation.CountedAddressKey, address);
                }
                else
                {
                    address.Count--;
                }
            }
        }
    }

    /// <summary>
    ///     Forgets every counter; for tests. Generations are not reused, so earlier reservations stay stale.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _windows.Clear();
            _order.Clear();
        }
    }

    /// <summary>
    ///     Gets the number of keys held; for tests of the cap.
    /// </summary>
    /// <returns>The number of keys in the table.</returns>
    internal int KeyCount()
    {
        lock (_lock)
        {
            return _windows.Count;
        }
    }

    /// <summary>
    ///     Builds the key of the per-address counter.
    /// </summary>
    /// <param name="clientAddress">The client address, or <c>null</c>.</param>
    /// <returns>The key.</returns>
    private static string AddressKey(string? clientAddress)
    {
        return "a|" + (string.IsNullOrEmpty(clientAddress) ? UnknownAddress : clientAddress);
    }

    /// <summary>
    ///     Builds the key of the per-address-and-name counter.
    /// </summary>
    /// <param name="clientAddress">The client address, or <c>null</c>.</param>
    /// <param name="username">The typed name, or <c>null</c>.</param>
    /// <returns>The key; the name is lower-cased and cut to a fixed length.</returns>
    private static string NameKey(string? clientAddress, string? username)
    {
        var name = (username ?? string.Empty).ToLowerInvariant();
        if (name.Length > MaxNameKeyLength)
        {
            name = name[..MaxNameKeyLength];
        }

        return "n|" + (string.IsNullOrEmpty(clientAddress) ? UnknownAddress : clientAddress) + "|" + name;
    }

    /// <summary>
    ///     Gets the attempts counted for a key in its current window.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="now">The current time.</param>
    /// <param name="window">The window length.</param>
    /// <returns>The count; 0 when the key is unknown or its window has passed.</returns>
    private int CountOf(string key, DateTimeOffset now, TimeSpan window)
    {
        return _windows.TryGetValue(key, out var entry) && now - entry.Start < window ? entry.Count : 0;
    }

    /// <summary>
    ///     Counts one more attempt for a key, starting a new window when there is none or it has passed.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="now">The current time.</param>
    /// <param name="window">The window length.</param>
    /// <returns>The generation of the window the attempt was counted in.</returns>
    private long Increment(string key, DateTimeOffset now, TimeSpan window)
    {
        if (_windows.TryGetValue(key, out var entry))
        {
            if (now - entry.Start < window)
            {
                entry.Count++;
                return entry.Generation;
            }

            RemoveWindow(key, entry);
        }

        var created = new Window { Start = now, Count = 1, Generation = ++_nextGeneration };
        created.Node = _order.AddLast(key);
        _windows[key] = created;
        return created.Generation;
    }

    /// <summary>
    ///     Removes a window from the table and from the order.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The window of the key.</param>
    private void RemoveWindow(string key, Window entry)
    {
        _windows.Remove(key);
        _order.Remove(entry.Node);
    }

    /// <summary>
    ///     Removes the windows that have passed from the head of the order. This is cheap and is enough while the clock
    ///     only moves forward; when the clock has stepped backwards the order is not the order of the start times, so
    ///     this can stop early and <see cref="PruneAllExpired"/> finishes the work when the table is full.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="window">The window length.</param>
    private void PruneExpired(DateTimeOffset now, TimeSpan window)
    {
        while (_order.First is { } first)
        {
            var entry = _windows[first.Value];
            if (now - entry.Start < window)
            {
                return;
            }

            RemoveWindow(first.Value, entry);
        }
    }

    /// <summary>
    ///     Removes every window that has passed, wherever it is in the order. It scans the whole table (at most
    ///     <see cref="MaxKeys"/> windows), so it is used only when the table is full.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="window">The window length.</param>
    private void PruneAllExpired(DateTimeOffset now, TimeSpan window)
    {
        var node = _order.First;
        while (node is not null)
        {
            var next = node.Next;
            var entry = _windows[node.Value];
            if (now - entry.Start >= window)
            {
                RemoveWindow(node.Value, entry);
            }

            node = next;
        }
    }

    /// <summary>
    ///     Counts the keys of this attempt that are not in the table.
    /// </summary>
    /// <param name="nameKey">The name key of this attempt, or <c>null</c> when only the address is counted.</param>
    /// <param name="addressKey">The address key of this attempt.</param>
    /// <returns>The number of keys that need a new slot, 0 to 2.</returns>
    private int MissingKeys(string? nameKey, string addressKey)
    {
        return (nameKey is null || _windows.ContainsKey(nameKey) ? 0 : 1) + (_windows.ContainsKey(addressKey) ? 0 : 1);
    }

    /// <summary>
    ///     Makes sure the table can take more keys: removes the oldest windows by creation order, and only as many as
    ///     are needed. The expired windows have already been removed by <see cref="PruneAllExpired"/>.
    /// </summary>
    /// <param name="missing">The number of keys that are about to be added.</param>
    /// <param name="nameKey">The name key of this attempt, which is never removed; <c>null</c> when none.</param>
    /// <param name="addressKey">The address key of this attempt, which is never removed.</param>
    private void MakeRoom(int missing, string? nameKey, string addressKey)
    {
        var node = _order.First;
        while (_windows.Count + missing > MaxKeys && node is not null)
        {
            var next = node.Next;
            if (node.Value != nameKey && node.Value != addressKey)
            {
                RemoveWindow(node.Value, _windows[node.Value]);
            }

            node = next;
        }
    }

    /// <summary>
    ///     Proof that an attempt was counted, given back to <see cref="Succeeded"/>.
    /// </summary>
    internal sealed class Reservation
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Reservation"/> class.
        /// </summary>
        /// <param name="nameKey">The name key that was counted; <c>null</c> when only the address was counted.</param>
        /// <param name="nameGeneration">The generation of the name window that was counted.</param>
        /// <param name="addressKey">The address key that was counted.</param>
        /// <param name="addressGeneration">The generation of the address window that was counted.</param>
        internal Reservation(string? nameKey, long nameGeneration, string addressKey, long addressGeneration)
        {
            CountedNameKey = nameKey;
            NameGeneration = nameGeneration;
            CountedAddressKey = addressKey;
            AddressGeneration = addressGeneration;
        }

        /// <summary>
        ///     Gets the name key that was counted; <c>null</c> when only the address was counted.
        /// </summary>
        internal string? CountedNameKey { get; }

        /// <summary>
        ///     Gets the generation of the name window that was counted.
        /// </summary>
        internal long NameGeneration { get; }

        /// <summary>
        ///     Gets the address key that was counted.
        /// </summary>
        internal string CountedAddressKey { get; }

        /// <summary>
        ///     Gets the generation of the address window that was counted.
        /// </summary>
        internal long AddressGeneration { get; }

        /// <summary>
        ///     Gets or sets a value indicating whether the reservation has been given back; only touched under the throttle lock.
        /// </summary>
        internal bool Consumed { get; set; }
    }

    /// <summary>
    ///     The counter of one key.
    /// </summary>
    private sealed class Window
    {
        /// <summary>
        ///     Gets or sets when the window started.
        /// </summary>
        public DateTimeOffset Start { get; set; }

        /// <summary>
        ///     Gets or sets how many attempts the window has counted.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        ///     Gets or sets the identity of this window; a new window of the same key gets a new one.
        /// </summary>
        public long Generation { get; set; }

        /// <summary>
        ///     Gets or sets the position of the key in the order.
        /// </summary>
        public LinkedListNode<string> Node { get; set; } = null!;
    }
}
