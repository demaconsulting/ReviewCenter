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

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     A pending second step of a sign-in: the password was right, the code is still to come.
/// </summary>
/// <param name="TokenHash">The SHA-256 hash of the token, which is all the store keeps of it.</param>
/// <param name="Username">The user whose password was right.</param>
/// <param name="ClientAddress">The address of the client the challenge was issued to.</param>
/// <param name="ReturnUrl">The local address to go to after the sign-in.</param>
/// <param name="Expires">The moment the challenge ends.</param>
/// <param name="Failures">How many wrong codes were tried with this challenge.</param>
/// <param name="Generation">
///     Set by <see cref="TotpChallengeStore.Take"/> to the invalidation count at that moment; 0 in the store.
/// </param>
public sealed record TotpChallenge(
    string TokenHash,
    string Username,
    string ClientAddress,
    string ReturnUrl,
    DateTimeOffset Expires,
    int Failures,
    long Generation = 0);

/// <summary>
///     Remembers, for a short time, that a user has typed the right password and still owes the second factor,
///     so the sign-in can continue in the next request without any session.
/// </summary>
/// <remarks>
///     The challenge is a random 256-bit token that travels only in a cookie; the store keeps just its SHA-256 hash,
///     so a copy of the memory cannot be replayed. The token is a pure lookup key (nothing the client holds can be
///     changed to impersonate anybody), bound to the user and the client address, valid for <see cref="Lifetime"/>,
///     and taken by <see cref="Take"/> atomically, so two requests can never use one challenge at the same time.
///     After a wrong code the caller gives it back with <see cref="Return"/>, which counts the failure and drops
///     the challenge at <see cref="MaxFailures"/>. It lives in memory only (a restart asks the user to sign in
///     again) and holds at most <see cref="MaxEntries"/> challenges: ended ones are dropped first, then the oldest.
///     The class is safe for concurrent use.
/// </remarks>
public sealed class TotpChallengeStore
{
    /// <summary>
    ///     How long a challenge stays valid after it is issued.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The largest number of challenges kept at the same time.
    /// </summary>
    public const int MaxEntries = 10_000;

    /// <summary>
    ///     The number of wrong codes after which a challenge is dropped.
    /// </summary>
    public const int MaxFailures = 5;

    /// <summary>
    ///     The address text used when the client address is not known.
    /// </summary>
    private const string UnknownAddress = "unknown";

    /// <summary>
    ///     The number of random bytes of a token (256 bits).
    /// </summary>
    private const int TokenBytes = 32;

    /// <summary>
    ///     The clock that decides when a challenge ends.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The challenges by the hash of their token.
    /// </summary>
    private readonly Dictionary<string, TotpChallenge> _challenges = new(StringComparer.Ordinal);

    /// <summary>
    ///     Guards <see cref="_challenges"/>.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     Counts the invalidations of users; a challenge records the value at <see cref="Take"/>.
    /// </summary>
    private long _sequence;

    /// <summary>
    ///     Challenges taken before this value are refused by <see cref="Return"/> (the fail-safe of a full <see cref="_invalidated"/>).
    /// </summary>
    private long _floor;

    /// <summary>
    ///     The last invalidation of each user, by upper-cased name: its sequence and moment. Bounded by
    ///     <see cref="MaxEntries"/> and pruned after <see cref="Lifetime"/>.
    /// </summary>
    private readonly Dictionary<string, (long Sequence, DateTimeOffset At)> _invalidated = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="TotpChallengeStore"/> class.
    /// </summary>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public TotpChallengeStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Issues a challenge after the password was right.
    /// </summary>
    /// <param name="username">The user whose password was right. Must not be null.</param>
    /// <param name="clientAddress">The client address as text; <c>null</c> is treated as one unknown address.</param>
    /// <param name="returnUrl">The local address to go to after the sign-in. Must not be null.</param>
    /// <returns>The token for the cookie (URL-safe text, 256 random bits); it is never stored, only its hash.</returns>
    /// <remarks>When the store is full, ended challenges are dropped first and then the oldest one.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="username"/> or <paramref name="returnUrl"/> is null.</exception>
    public string Issue(string username, string? clientAddress, string returnUrl)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(returnUrl);
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = _timeProvider.GetUtcNow();
        var challenge = new TotpChallenge(
            HashToken(token),
            username,
            Normalize(clientAddress),
            returnUrl,
            now + Lifetime,
            0);
        lock (_lock)
        {
            if (_challenges.Count >= MaxEntries)
            {
                MakeRoom(now);
            }

            _challenges[challenge.TokenHash] = challenge;
        }

        return token;
    }

    /// <summary>
    ///     Looks a challenge up without using it.
    /// </summary>
    /// <param name="token">The token from the cookie; may be null.</param>
    /// <param name="clientAddress">The client address as text; <c>null</c> is treated as one unknown address.</param>
    /// <returns>The challenge; <c>null</c> when it is unknown, ended or was issued to another client address.</returns>
    public TotpChallenge? Peek(string? token, string? clientAddress)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            return _challenges.TryGetValue(HashToken(token), out var challenge)
                   && challenge.Expires > now
                   && challenge.ClientAddress == Normalize(clientAddress)
                ? challenge
                : null;
        }
    }

    /// <summary>
    ///     Takes a challenge to use it: it is removed, so no other request can use it meanwhile.
    /// </summary>
    /// <param name="token">The token from the cookie; may be null.</param>
    /// <param name="clientAddress">The client address as text; <c>null</c> is treated as one unknown address.</param>
    /// <returns>
    ///     The challenge, for exactly one caller; <c>null</c> when it is unknown, ended, already taken, or was issued
    ///     to another client address (which leaves it in place). A caller whose code was wrong gives it back with
    ///     <see cref="Return"/>.
    /// </returns>
    public TotpChallenge? Take(string? token, string? clientAddress)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = HashToken(token);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!_challenges.TryGetValue(hash, out var challenge))
            {
                return null;
            }

            // An ended challenge is removed whoever asks; a foreign one is left for its owner
            if (challenge.Expires <= now)
            {
                _challenges.Remove(hash);
                return null;
            }

            if (challenge.ClientAddress != Normalize(clientAddress))
            {
                return null;
            }

            _challenges.Remove(hash);
            return challenge with { Generation = _sequence };
        }
    }

    /// <summary>
    ///     Gives a taken challenge back after a wrong code, counting the failure.
    /// </summary>
    /// <param name="challenge">The challenge from <see cref="Take"/>. Must not be null.</param>
    /// <returns><c>true</c> when the challenge is kept for another try; <c>false</c> when it was used up, ended or invalidated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="challenge"/> is null.</exception>
    public bool Return(TotpChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var failed = challenge with { Failures = challenge.Failures + 1, Generation = 0 };
        var now = _timeProvider.GetUtcNow();
        if (failed.Failures >= MaxFailures || failed.Expires <= now)
        {
            return false;
        }

        lock (_lock)
        {
            // A challenge taken before its user was invalidated is not given back, however long the check took
            if (challenge.Generation < _floor
                || (_invalidated.TryGetValue(UserKey(challenge.Username), out var invalidation)
                    && invalidation.Sequence > challenge.Generation))
            {
                return false;
            }

            // The cap holds here too: ended challenges make room, otherwise this one is dropped (nobody else is evicted)
            if (!_challenges.ContainsKey(failed.TokenHash) && _challenges.Count >= MaxEntries)
            {
                RemoveEnded(now);
                if (_challenges.Count >= MaxEntries)
                {
                    return false;
                }
            }

            _challenges[failed.TokenHash] = failed;
        }

        return true;
    }

    /// <summary>
    ///     Drops every challenge of a user, for example because the second factor was removed or replaced.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case. Must not be null.</param>
    /// <remarks>
    ///     A challenge that was taken and is being checked is not in the store, so the invalidation is remembered
    ///     (bounded, see <see cref="TombstoneCount"/>) and <see cref="Return"/> refuses such a challenge.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="username"/> is null.</exception>
    public void InvalidateUser(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        var now = _timeProvider.GetUtcNow();
        lock (_lock)
        {
            foreach (var hash in _challenges
                         .Where(pair => string.Equals(pair.Value.Username, username, StringComparison.OrdinalIgnoreCase))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _challenges.Remove(hash);
            }

            _sequence++;

            // A taken challenge ends at most Lifetime after it was issued, so older notes can not matter any more
            foreach (var key in _invalidated.Where(pair => pair.Value.At + Lifetime <= now).Select(pair => pair.Key).ToList())
            {
                _invalidated.Remove(key);
            }

            _invalidated[UserKey(username)] = (_sequence, now);
            if (_invalidated.Count >= MaxEntries)
            {
                // Fail safe: forget the notes and refuse every challenge taken so far instead
                _invalidated.Clear();
                _floor = _sequence;
            }
        }
    }

    /// <summary>
    ///     Gets the number of challenges held; for tests of the cap.
    /// </summary>
    /// <returns>The number of challenges.</returns>
    internal int Count()
    {
        lock (_lock)
        {
            return _challenges.Count;
        }
    }

    /// <summary>
    ///     Gets the number of remembered invalidations; for tests of the bound.
    /// </summary>
    /// <returns>The number of users with a remembered invalidation.</returns>
    internal int TombstoneCount()
    {
        lock (_lock)
        {
            return _invalidated.Count;
        }
    }

    /// <summary>
    ///     Builds the key of a user name for the remembered invalidations.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The name without regard to case.</returns>
    private static string UserKey(string username)
    {
        return username.ToUpperInvariant();
    }

    /// <summary>
    ///     Hashes a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The lowercase hexadecimal SHA-256 hash.</returns>
    private static string HashToken(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>
    ///     Normalizes a client address for comparison.
    /// </summary>
    /// <param name="clientAddress">The address, or <c>null</c>.</param>
    /// <returns>The address, or the fixed text for an unknown one.</returns>
    private static string Normalize(string? clientAddress)
    {
        return string.IsNullOrEmpty(clientAddress) ? UnknownAddress : clientAddress;
    }

    /// <summary>
    ///     Frees one place: drops the ended challenges, and the oldest one when none had ended.
    /// </summary>
    /// <param name="now">The current moment.</param>
    /// <remarks>The caller holds the lock.</remarks>
    private void MakeRoom(DateTimeOffset now)
    {
        RemoveEnded(now);
        if (_challenges.Count >= MaxEntries)
        {
            _challenges.Remove(_challenges.MinBy(pair => pair.Value.Expires).Key);
        }
    }

    /// <summary>
    ///     Drops the ended challenges.
    /// </summary>
    /// <param name="now">The current moment.</param>
    /// <remarks>The caller holds the lock.</remarks>
    private void RemoveEnded(DateTimeOffset now)
    {
        foreach (var hash in _challenges.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToList())
        {
            _challenges.Remove(hash);
        }
    }
}
