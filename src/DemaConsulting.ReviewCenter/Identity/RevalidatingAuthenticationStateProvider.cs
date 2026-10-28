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
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace DemaConsulting.ReviewCenter.Identity;

/// <summary>
///     Re-checks the signed-in user of an open page (a Blazor circuit) against the user store at a fixed
///     interval and ends the sign-in state when the user is no longer valid.
/// </summary>
/// <remarks>
///     The cookie is only checked when a request is made, but an open interactive page makes none, so a user
///     who is disabled, removed or changed while a page is open would otherwise keep that page. The framework
///     base class runs the loop: after each interval it calls <see cref="ValidateAuthenticationStateAsync"/>
///     and, when the answer is <c>false</c>, replaces the state with an anonymous one, which the components
///     that listen to the state (see <c>SessionGate</c>) react to. The first check happens one interval after
///     the state is set, and the loop only runs while the state is authenticated. The rule is the one the cookie
///     uses (<see cref="SignIn.IsCurrent"/>). The check fails closed: when the user cannot be read (for example a
///     database error) the state ends, because an unverifiable user must not keep acting. A cancelled check is
///     not a failure; it only means the circuit is being closed. The loop continues on a thread-pool thread, so
///     the provider touches only the singleton <see cref="UserService"/>, which supports concurrent reads, and
///     the immutable state it is given; no other state is shared.
/// </remarks>
internal sealed partial class RevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    /// <summary>
    ///     The user service that supplies the stored user.
    /// </summary>
    private readonly UserService _users;

    /// <summary>
    ///     The logger for lookup failures.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     The time between two checks.
    /// </summary>
    private readonly TimeSpan _interval;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RevalidatingAuthenticationStateProvider"/> class for
    ///     dependency injection.
    /// </summary>
    /// <param name="loggerFactory">The logger factory of the framework base class. Must not be null.</param>
    /// <param name="users">The user service. Must not be null.</param>
    /// <param name="options">The sign-in settings that hold the interval. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="users"/> or <paramref name="options"/> is null.</exception>
    /// <remarks>
    ///     This is the only public constructor, which is the one the dependency injection container considers.
    /// </remarks>
    public RevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        UserService users,
        IOptions<AuthOptions> options)
        : this(loggerFactory, users, IntervalOf(options))
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="RevalidatingAuthenticationStateProvider"/> class with an
    ///     explicit interval.
    /// </summary>
    /// <param name="loggerFactory">The logger factory of the framework base class. Must not be null.</param>
    /// <param name="users">The user service. Must not be null.</param>
    /// <param name="interval">The time between two checks; must be positive.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="users"/> is null.</exception>
    /// <remarks>
    ///     Internal so tests can use an interval far below the configured minimum.
    /// </remarks>
    internal RevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory, UserService users, TimeSpan interval)
        : base(loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(users);
        _users = users;
        _logger = loggerFactory.CreateLogger<RevalidatingAuthenticationStateProvider>();
        _interval = interval;
    }

    /// <inheritdoc />
    protected override TimeSpan RevalidationInterval => _interval;

    /// <inheritdoc />
    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        return IsCurrentAsync(authenticationState, cancellationToken);
    }

    /// <summary>
    ///     Decides whether the signed-in user of a state still matches the user store.
    /// </summary>
    /// <param name="state">The state to check. Must not be null.</param>
    /// <param name="cancellationToken">Cancelled when the circuit is closed.</param>
    /// <returns>
    ///     <c>true</c> when the user exists, is enabled and has the same administrator flag and display name as in the state;
    ///     <c>false</c> when not, when the state carries no user name, and when the user could not be read.
    /// </returns>
    /// <exception cref="TaskCanceledException">
    ///     Thrown, carrying <paramref name="cancellationToken"/>, when the check was cancelled. The framework
    ///     loop ends quietly on exactly this exception; any other exception is logged by it as an error and
    ///     forces a sign-out on a provider that is being disposed.
    /// </exception>
    /// <remarks>
    ///     Failing closed is deliberate: a transient store error ends the open page, which loses what was typed
    ///     but never lets an unverifiable user keep acting.
    /// </remarks>
    internal async Task<bool> IsCurrentAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        // A state without a user name cannot be matched to a stored user
        var username = state.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return false;
        }

        try
        {
            // Apply the same rule as the cookie to the user as it is stored now
            var user = await _users.GetAsync(username, cancellationToken);
            return SignIn.IsCurrent(state.User, user);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // The circuit is closing: surface it as the exception the framework loop treats as a clean stop
            throw new TaskCanceledException(ex.Message, ex, cancellationToken);
        }
#pragma warning disable CA1031 // Fail closed: any other failure to read the user ends the state
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogLookupFailed(_logger, ex, username);
            return false;
        }
    }

    /// <summary>
    ///     Gets the check interval from the sign-in settings.
    /// </summary>
    /// <param name="options">The sign-in settings. Must not be null.</param>
    /// <returns>The configured interval.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    private static TimeSpan IntervalOf(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return TimeSpan.FromSeconds(options.Value.RevalidationSeconds);
    }

    /// <summary>
    ///     Logs that the user could not be read, so the state is ended.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="username">The user name; never a password or other secret.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not check user '{Username}' of an open page; the session is ended (fail closed).")]
    private static partial void LogLookupFailed(ILogger logger, Exception exception, string username);
}
