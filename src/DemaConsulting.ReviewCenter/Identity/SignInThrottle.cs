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
///     checked, so a burst of parallel requests cannot all pass; <see cref="Succeeded"/> gives the slots back.
///     Every attempt is counted whether or not the user name exists, so the state never depends on the account.
///     The tables are capped at <see cref="MaxKeys"/> keys: expired keys are removed first and, when the table is
///     still full, it is cleared, so an attacker cannot grow the memory without limit but a flood of distinct
///     keys can reset the counters (the throttle fails open; the account lockout is kept in the database).
///     The state is per process and lost on restart. All state is behind one lock, so the class is thread-safe.
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
    ///     Guards <see cref="_windows"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     For each key, when its window started and how many attempts it has counted.
    /// </summary>
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _windows = new(StringComparer.Ordinal);

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
    ///     <c>true</c> when the attempt may go on (and has been counted); <c>false</c> when either limit is
    ///     reached (nothing is counted then).
    /// </returns>
    public bool TryBegin(string? clientAddress, string? username)
    {
        var addressKey = AddressKey(clientAddress);
        var nameKey = NameKey(clientAddress, username);
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            if (CountOf(nameKey, now, window) >= _options.MaxFailuresPerName
                || CountOf(addressKey, now, window) >= _options.MaxFailuresPerAddress)
            {
                return false;
            }

            MakeRoom(2, now, window);
            Increment(nameKey, now, window);
            Increment(addressKey, now, window);
            return true;
        }
    }

    /// <summary>
    ///     Gives the slots of a successful sign-in back: the name counter is cleared and one attempt is
    ///     returned to the address counter.
    /// </summary>
    /// <param name="clientAddress">The client address as text, as given to <see cref="TryBegin"/>.</param>
    /// <param name="username">The typed user name, as given to <see cref="TryBegin"/>.</param>
    public void Succeeded(string? clientAddress, string? username)
    {
        var addressKey = AddressKey(clientAddress);
        lock (_lock)
        {
            _windows.Remove(NameKey(clientAddress, username));
            if (_windows.TryGetValue(addressKey, out var address))
            {
                if (address.Count <= 1)
                {
                    _windows.Remove(addressKey);
                }
                else
                {
                    _windows[addressKey] = (address.Start, address.Count - 1);
                }
            }
        }
    }

    /// <summary>
    ///     Forgets every counter; for tests.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _windows.Clear();
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
    private void Increment(string key, DateTimeOffset now, TimeSpan window)
    {
        _windows[key] = _windows.TryGetValue(key, out var entry) && now - entry.Start < window
            ? (entry.Start, entry.Count + 1)
            : (now, 1);
    }

    /// <summary>
    ///     Makes sure the table can take more keys: removes expired keys and, when it is still too full, clears it.
    /// </summary>
    /// <param name="additional">The number of keys that may be added.</param>
    /// <param name="now">The current time.</param>
    /// <param name="window">The window length.</param>
    private void MakeRoom(int additional, DateTimeOffset now, TimeSpan window)
    {
        if (_windows.Count + additional <= MaxKeys)
        {
            return;
        }

        var expired = _windows.Where(pair => now - pair.Value.Start >= window).Select(pair => pair.Key).ToList();
        foreach (var key in expired)
        {
            _windows.Remove(key);
        }

        if (_windows.Count + additional > MaxKeys)
        {
            // Fail open rather than grow without limit or refuse everybody
            _windows.Clear();
        }
    }
}
