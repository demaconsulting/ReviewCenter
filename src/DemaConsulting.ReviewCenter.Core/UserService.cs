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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Why a sign-in with an external identity was refused.
/// </summary>
public enum ExternalSignInRefusal
{
    /// <summary>The sign-in was accepted.</summary>
    None,

    /// <summary>The account is disabled.</summary>
    Disabled,

    /// <summary>The identity, or what was needed to create an account from it, is missing, too long or unusable.</summary>
    Invalid
}

/// <summary>
///     The answer to a sign-in with an external identity.
/// </summary>
/// <param name="User">The account to sign in, or <c>null</c> when the sign-in was refused.</param>
/// <param name="Refusal">Why the sign-in was refused; <see cref="ExternalSignInRefusal.None"/> when it was accepted.</param>
/// <param name="Created"><c>true</c> when the account was created by this sign-in.</param>
public sealed record ExternalSignInResult(User? User, ExternalSignInRefusal Refusal, bool Created);

/// <summary>
///     Why a password is checked; decides which extra rules apply.
/// </summary>
internal enum PasswordCheckPurpose
{
    /// <summary>A sign-in.</summary>
    SignIn,

    /// <summary>A fresh proof of identity for an action such as signing.</summary>
    Reauthentication,

    /// <summary>A confirmation before a change of the second factor.</summary>
    Confirmation
}

/// <summary>
///     Creates users, checks passwords and manages accounts, enforcing the user rules.
/// </summary>
/// <remarks>
///     The service is the single place where user rules live: the user name and display name rules, the
///     password policy (12 to 128 characters, no composition rules, not trimmed), the lockout (5 consecutive
///     failures lock the account for 5 minutes) and the guarantee that at least one enabled system administrator
///     remains. <see cref="AuthenticateAsync"/> and <see cref="ReauthenticateAsync"/> answer every failure with
///     the same <c>null</c> (unknown user, wrong password, disabled and locked out), so a caller cannot tell the
///     cases apart; the audit trail does tell them apart (see <see cref="AuthenticateAsync"/>). Operations that
///     change an existing user run one at a time (a semaphore), which makes the last-administrator check and the
///     failure counter race-free; the service must therefore be a single instance (a singleton).
///     Passwords and hashes are never logged or put into messages. An account of an external identity provider
///     (see <see cref="SignInExternalAsync"/>) has no password: every password path refuses it.
/// </remarks>
public sealed class UserService
{
    /// <summary>
    ///     The number of consecutive failed password checks that locks an account.
    /// </summary>
    private const int MaxFailedAttempts = 5;

    /// <summary>
    ///     The room kept free at the end of a derived user name for a collision suffix such as <c>-12</c>.
    /// </summary>
    private const int UsernameSuffixRoom = 6;

    /// <summary>
    ///     The number of suffixed user names tried before the name derived from the identity itself is used.
    /// </summary>
    private const int MaxUsernameSuffix = 50;

    /// <summary>
    ///     The audit action code of a failed sign-in with an external identity.
    /// </summary>
    private const string OidcSignInFailedAction = "auth.oidc-sign-in-failed";

    /// <summary>
    ///     The audit action code of a failed re-authentication (for example at signature time).
    /// </summary>
    private const string ReauthFailedAction = "signature.reauth-failed";

    /// <summary>
    ///     How long an account stays locked after too many failures.
    /// </summary>
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The store that persists users.
    /// </summary>
    private readonly IUserStore _store;

    /// <summary>
    ///     The password hasher.
    /// </summary>
    private readonly IPasswordHasher _hasher;

    /// <summary>
    ///     The clock that supplies creation and lockout times.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Serializes the operations that change an existing user.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     Records the sign-in failures and lockouts, or <c>null</c> when they are not audited.
    /// </summary>
    private readonly AuditService? _audit;

    /// <summary>
    ///     A hash of no account, verified against when the user is unknown so the response time is similar;
    ///     created on first use.
    /// </summary>
    private string? _dummyHash;

    /// <summary>
    ///     Tells whether a user has a confirmed second factor, or <c>null</c> when the deployment has none.
    /// </summary>
    private readonly ITotpStore? _totpStore;

    /// <summary>
    ///     The settings of the second factor, or <c>null</c> for the defaults (on).
    /// </summary>
    private readonly TotpSettings? _totpSettings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UserService"/> class.
    /// </summary>
    /// <param name="store">The store that persists users. Must not be null.</param>
    /// <param name="hasher">The password hasher. Must not be null.</param>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <param name="audit">
    ///     Records failed sign-ins and lockouts in the audit trail on a best-effort basis; <c>null</c> to record
    ///     nothing. Changes of users are audited by the store regardless.
    /// </param>
    /// <param name="totpStore">
    ///     The store of the second factor enrollments; <c>null</c> when the deployment has none, in which case no
    ///     account ever requires a second factor.
    /// </param>
    /// <param name="totpSettings">The second factor settings; <c>null</c> for the defaults (enabled).</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public UserService(
        IUserStore store,
        IPasswordHasher hasher,
        TimeProvider timeProvider,
        AuditService? audit = null,
        ITotpStore? totpStore = null,
        TotpSettings? totpSettings = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _hasher = hasher;
        _timeProvider = timeProvider;
        _audit = audit;
        _totpStore = totpStore;
        _totpSettings = totpSettings;
    }

    /// <summary>
    ///     Creates an enabled user.
    /// </summary>
    /// <param name="username">
    ///     The user name: 3 to <see cref="ReviewLimits.MaxUsernameLength"/> characters of letters, digits, dot,
    ///     underscore and hyphen. Unique without regard to case.
    /// </param>
    /// <param name="displayName">
    ///     The display name; leading and trailing white space is removed. Must not be empty and must not exceed
    ///     <see cref="ReviewLimits.MaxDisplayNameLength"/> characters.
    /// </param>
    /// <param name="isSystemAdmin"><c>true</c> to make the user a system administrator.</param>
    /// <param name="password">
    ///     The initial password: <see cref="ReviewLimits.MinPasswordLength"/> to
    ///     <see cref="ReviewLimits.MaxPasswordLength"/> characters, taken as it is (not trimmed).
    /// </param>
    /// <param name="actor">The administrator (or <see cref="Actor.CommandLine"/>) who creates the user; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user; only the hash of the password is kept.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.</exception>
    public async Task<User> CreateAsync(
        string? username,
        string? displayName,
        bool isSystemAdmin,
        string? password,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateUsername(username);
        var trimmedDisplayName = ValidateDisplayName(displayName);
        ValidatePassword(password);

        // The store enforces the unique user name, which also covers two requests racing; the audit entry never
        // carries the password or its hash
        return await _store.AddAsync(
            new User
            {
                Username = username!,
                DisplayName = trimmedDisplayName,
                IsSystemAdmin = isSystemAdmin,
                Enabled = true,
                PasswordHash = _hasher.Hash(password!),
                Created = _timeProvider.GetUtcNow()
            },
            stored => AuditDraft.By(
                actor,
                "user.created",
                $"user '{stored.Username}'{(stored.IsSystemAdmin ? ", system administrator" : string.Empty)}"),
            cancellationToken);
    }

    /// <summary>
    ///     Lists all users.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>All users ordered by user name.</returns>
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _store.ListAsync(cancellationToken);
    }

    /// <summary>
    ///     Reads a user by user name.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user, or <c>null</c> when there is none.</returns>
    public Task<User?> GetAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        return _store.GetByUsernameAsync(username, cancellationToken);
    }

    /// <summary>
    ///     Checks a user name and password for sign-in.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The user when the account is enabled, not locked out and the password is right; otherwise <c>null</c>
    ///     for every failure, so the cause is not revealed. A right password resets the failure counter; a
    ///     wrong one counts, and the fifth consecutive failure locks the account for 5 minutes. While the
    ///     account is locked, attempts are refused without counting, even with the right password. An account with a
    ///     confirmed second factor is refused here too (fail closed, recorded as "second factor required"): a caller
    ///     that can only check a password must never let such an account in; use
    ///     <see cref="AuthenticatePasswordAsync"/> when a code can follow.
    /// </returns>
    /// <remarks>
    ///     Every failure is recorded in the audit trail as <c>auth.sign-in-failed</c> (wrong password, disabled
    ///     account, unknown user), the lockout as <c>auth.locked-out</c>, and an attempt on a locked account as
    ///     <c>auth.sign-in-locked</c>. For an unknown user the entry carries the typed name, made one line and
    ///     cut to <see cref="ReviewLimits.MaxUsernameLength"/> characters, and never the password. The unknown
    ///     user, disabled account and locked account entries are coalesced (see
    ///     <see cref="AuditService.TryRecordCoalescedAsync"/>), because nothing else limits how often they can be
    ///     triggered; wrong passwords are bounded by the lockout and recorded every time.
    /// </remarks>
    public async Task<User?> AuthenticateAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return await FailClosedAsync(
            await AuthenticateCoreAsync(username, password, PasswordCheckPurpose.SignIn, cancellationToken),
            false,
            cancellationToken);
    }

    /// <summary>
    ///     Checks a user name and password for sign-in when a second factor may follow.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <see cref="PasswordStep.User"/> is <c>null</c> for every failure, exactly as for
    ///     <see cref="AuthenticateAsync"/> (a wrong password looks the same for an account with and without a second
    ///     factor). When the password is right and the account has a confirmed second factor that applies,
    ///     <see cref="PasswordStep.SecondFactorRequired"/> is set and the failure counter is deliberately
    ///     <b>not</b> reset: only a complete sign-in (<see cref="ClearFailuresAsync"/>) resets it, so an attacker who
    ///     knows the password still runs into the lockout while guessing codes.
    /// </returns>
    public Task<PasswordStep> AuthenticatePasswordAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return AuthenticateCoreAsync(username, password, PasswordCheckPurpose.SignIn, cancellationToken);
    }

    /// <summary>
    ///     Checks the password of a signed-in user for an action that needs a fresh proof, when a code may also be
    ///     needed (signing a review).
    /// </summary>
    /// <param name="username">The user name of the signed-in user; compared without regard to case.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The rules are those of <see cref="ReauthenticatePasswordAsync"/> and <see cref="AuthenticatePasswordAsync"/>:
    ///     <see cref="PasswordStep.SecondFactorRequired"/> tells the caller to check a code too.
    /// </returns>
    public Task<PasswordStep> ReauthenticatePasswordAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return AuthenticateCoreAsync(username, password, PasswordCheckPurpose.Reauthentication, cancellationToken);
    }

    /// <summary>
    ///     Checks the password of a signed-in user before a change of the second factor.
    /// </summary>
    /// <param name="username">The user name of the signed-in user; compared without regard to case.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The user when the account is local, enabled, not locked out and the password is right; otherwise
    ///     <c>null</c>. A wrong password counts toward the lockout like at sign-in. Unlike sign-in, a right password
    ///     does not reset the failure counter and a temporary password is accepted (the caller decides whether the
    ///     account may use the feature).
    /// </returns>
    public async Task<User?> ConfirmPasswordAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return (await AuthenticateCoreAsync(username, password, PasswordCheckPurpose.Confirmation, cancellationToken)).User;
    }

    /// <summary>
    ///     Checks the password of the signed-in user again, for an action that needs a fresh proof of identity
    ///     (signing a review).
    /// </summary>
    /// <param name="username">The user name of the signed-in user; compared without regard to case.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The user when the password is right and the account is enabled and not locked out; otherwise
    ///     <c>null</c> for every failure. The rules and the failure counting are those of
    ///     <see cref="AuthenticateAsync"/>.
    /// </returns>
    /// <remarks>
    ///     A failure is recorded as <c>signature.reauth-failed</c> (with the reason in the detail) instead of
    ///     <c>auth.sign-in-failed</c>, so the trail shows where the wrong password was typed. A refusal for a
    ///     locked or disabled account is recorded too, once per minute, account and reason. An account that still
    ///     has a temporary password (<see cref="User.MustChangePassword"/>) is refused with the reason
    ///     "password change pending".
    /// </remarks>
    public async Task<User?> ReauthenticateAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        return await FailClosedAsync(
            await AuthenticateCoreAsync(username, password, PasswordCheckPurpose.Reauthentication, cancellationToken),
            true,
            cancellationToken);
    }

    /// <summary>
    ///     Refuses an account whose second factor is required, for the callers that can only check a password.
    /// </summary>
    /// <param name="step">The answer of the password check.</param>
    /// <param name="reauthentication"><c>true</c> for a re-authentication; <c>false</c> for a sign-in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The user when no second factor is required; otherwise <c>null</c> (fail closed), so a caller that does
    ///     not know about the second factor can never let a password alone through.
    /// </returns>
    private async Task<User?> FailClosedAsync(PasswordStep step, bool reauthentication, CancellationToken cancellationToken)
    {
        if (!step.SecondFactorRequired)
        {
            return step.User;
        }

        await RecordCoalescedAsync(
            step.User,
            reauthentication ? ReauthFailedAction : "auth.sign-in-failed",
            "second factor required",
            cancellationToken);
        return null;
    }

    /// <summary>
    ///     Checks a user name and password; the shared path of sign-in, re-authentication and confirmation.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="password">The password to check.</param>
    /// <param name="purpose">Why the password is checked.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user (or <c>null</c> for every failure) and whether a second factor is required.</returns>
    private async Task<PasswordStep> AuthenticateCoreAsync(
        string? username,
        string? password,
        PasswordCheckPurpose purpose,
        CancellationToken cancellationToken)
    {
        var reauthentication = purpose == PasswordCheckPurpose.Reauthentication;
        var failed = new PasswordStep(null, false);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // An unknown or locked-out user still costs one verification, so the response time does not
            // reveal the account
            var user = await _store.GetByUsernameAsync(username ?? string.Empty, cancellationToken);
            if (user is null)
            {
                VerifyDummy(password);
                await RecordUnknownUserAsync(username, reauthentication, cancellationToken);
                return failed;
            }

            // An account of an identity provider has no password to check: a password never signs it in or confirms
            // a signature, whatever is stored or typed
            if (user.IsExternal)
            {
                VerifyDummy(password);
                await RecordCoalescedAsync(
                    user,
                    reauthentication ? ReauthFailedAction : "auth.sign-in-failed",
                    "external account has no password",
                    cancellationToken);
                return failed;
            }

            // A locked account refuses every attempt, without counting it
            if (IsLockedOut(user))
            {
                VerifyDummy(password);
                await RecordCoalescedAsync(
                    user,
                    reauthentication ? ReauthFailedAction : "auth.sign-in-locked",
                    "account locked",
                    cancellationToken);
                return failed;
            }

            if (!_hasher.Verify(user.PasswordHash, password ?? string.Empty))
            {
                await RegisterFailureAsync(user, reauthentication, cancellationToken);
                return failed;
            }

            // A disabled account never signs in, however right the password is
            if (!user.Enabled)
            {
                await RecordCoalescedAsync(
                    user,
                    reauthentication ? ReauthFailedAction : "auth.sign-in-failed",
                    "account disabled",
                    cancellationToken);
                return failed;
            }

            // A temporary password proves identity only well enough to reach the password-change page: it can not
            // confirm a signature, whoever knows it
            if (reauthentication && user.MustChangePassword)
            {
                await RecordCoalescedAsync(user, ReauthFailedAction, "password change pending", cancellationToken);
                return failed;
            }

            // A password confirmation only proves the password: it neither asks for a code nor clears the counter
            if (purpose == PasswordCheckPurpose.Confirmation)
            {
                return new PasswordStep(user, false);
            }

            // With a second factor the password alone is not enough, and the counter is left as it is: only a
            // complete sign-in resets it, so guessing codes with a known password still ends in the lockout
            if (await HasActiveSecondFactorAsync(user, cancellationToken))
            {
                return new PasswordStep(user, true);
            }

            await ResetFailuresAsync(user, cancellationToken);
            return new PasswordStep(user, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Tells whether the user has a confirmed second factor that applies right now.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the feature is on and the enrollment is confirmed; <c>false</c> when there is no
    ///     enrollment store (a deployment without the feature), the feature is switched off or nothing is confirmed.
    /// </returns>
    private async Task<bool> HasActiveSecondFactorAsync(User user, CancellationToken cancellationToken)
    {
        if (_totpStore is null || !(_totpSettings?.Enabled ?? true))
        {
            return false;
        }

        return (await _totpStore.GetAsync(user.Id, cancellationToken))?.IsActive == true;
    }

    /// <summary>
    ///     Counts a wrong second factor (code) toward the same lockout as a wrong password.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="action">The audit action code of the failure.</param>
    /// <param name="detail">A fixed detail text; never a code, secret or token.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when this failure locked the account.</returns>
    /// <remarks>
    ///     The user is read again under the gate, so a counter changed meanwhile is never overwritten with a stale
    ///     copy. An unknown or external account is ignored.
    /// </remarks>
    internal async Task<bool> RegisterSecondFactorFailureAsync(
        string username,
        string action,
        string detail,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await _store.GetByUsernameAsync(username, cancellationToken);
            if (user is null || user.IsExternal)
            {
                return false;
            }

            return await RegisterFailureAsync(user, action, detail, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Clears the failure counter after a complete sign-in with a second factor.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the counter is cleared; an unknown user is ignored.</returns>
    internal async Task ClearFailuresAsync(string username, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await _store.GetByUsernameAsync(username, cancellationToken);
            if (user is not null)
            {
                await ResetFailuresAsync(user, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Tells whether an account is locked out right now, recording a refusal.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> while the lockout lasts; <c>false</c> for an unlocked or unknown account.</returns>
    internal async Task<bool> IsLockedOutAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _store.GetByUsernameAsync(username, cancellationToken);
        if (user is null || !IsLockedOut(user))
        {
            return false;
        }

        await RecordCoalescedAsync(user, "auth.sign-in-locked", "account locked", cancellationToken);
        return true;
    }

    /// <summary>
    ///     Enables or disables a user.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="enabled"><c>true</c> to let the user sign in; <c>false</c> to stop it.</param>
    /// <param name="actor">The administrator who makes the change; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.UserNotFound"/> when there is no such user, or with
    ///     <see cref="DomainRule.LastAdmin"/> when the change would disable the last enabled administrator.
    /// </exception>
    public async Task SetEnabledAsync(
        string username,
        bool enabled,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (!enabled)
            {
                await EnsureNotLastAdminAsync(user, cancellationToken);
            }

            user.Enabled = enabled;
            await _store.UpdateAsync(
                user,
                AuditDraft.By(actor, enabled ? "user.enabled" : "user.disabled", $"user '{user.Username}'"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Makes a user a system administrator or takes that right away.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="isSystemAdmin"><c>true</c> to make the user a system administrator; <c>false</c> to demote.</param>
    /// <param name="actor">The administrator who makes the change; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.UserNotFound"/> when there is no such user, or with
    ///     <see cref="DomainRule.LastAdmin"/> when the change would demote the last enabled system administrator.
    /// </exception>
    public async Task SetSystemAdminAsync(
        string username,
        bool isSystemAdmin,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (!isSystemAdmin)
            {
                await EnsureNotLastAdminAsync(user, cancellationToken);
            }

            var previous = user.IsSystemAdmin;
            user.IsSystemAdmin = isSystemAdmin;
            await _store.UpdateAsync(
                user,
                AuditDraft.By(
                    actor,
                    "user.admin-changed",
                    $"user '{user.Username}': system administrator {previous} -> {isSystemAdmin}"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Changes the password of a user who knows the current one.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password; same policy as for a new user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the new hash is stored.</returns>
    /// <remarks>
    ///     A wrong current password counts as a failed sign-in (so this operation is no free way to guess
    ///     passwords), and a locked-out account is refused like a wrong password. A successful change clears
    ///     <see cref="User.MustChangePassword"/>; while it is set, the new password must differ from the current one.
    ///     This overload can not check a second factor, so it fails closed for a user who has one: it is refused
    ///     without changing anything, and <see cref="TotpService.ChangeOwnPasswordAsync"/> is the way to change
    ///     the password of such a user.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.PasswordInvalid"/> when the new password breaks the policy,
    ///     <see cref="DomainRule.UserNotFound"/> when there is no such user,
    ///     <see cref="DomainRule.CurrentPasswordIncorrect"/> when the current password is wrong or the account is locked out, or
    ///     <see cref="DomainRule.TotpCodeIncorrect"/> when the user has a second factor.
    /// </exception>
    public Task ChangeOwnPasswordAsync(
        string username,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        return ChangeOwnPasswordCoreAsync(username, currentPassword, newPassword, null, cancellationToken);
    }

    /// <summary>
    ///     Changes the password of a user who knows the current one and, when the user has a second factor, proves it.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password; same policy as for a new user.</param>
    /// <param name="verifySecondFactor">
    ///     Checks the second factor of the user and uses the code up; <c>null</c> when the caller can not check one.
    ///     It runs inside the gate, so it must not call back into this service's gated members.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the new hash is stored.</returns>
    /// <remarks>
    ///     With an active second factor the password alone is never enough: no verifier refuses without touching
    ///     the failure counter, a verifier that returns <c>false</c> counts as a failure (same lockout as a wrong
    ///     password), and the counter is cleared only after the password and the second factor were both right.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.PasswordInvalid"/>, <see cref="DomainRule.UserNotFound"/>,
    ///     <see cref="DomainRule.CurrentPasswordIncorrect"/> or <see cref="DomainRule.TotpCodeIncorrect"/>.
    /// </exception>
    internal async Task ChangeOwnPasswordCoreAsync(
        string username,
        string? currentPassword,
        string? newPassword,
        Func<User, CancellationToken, Task<bool>>? verifySecondFactor,
        CancellationToken cancellationToken)
    {
        ValidatePassword(newPassword);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (user.IsExternal || IsLockedOut(user))
            {
                throw CurrentPasswordIncorrect();
            }

            if (!_hasher.Verify(user.PasswordHash, currentPassword ?? string.Empty))
            {
                await RegisterFailureAsync(user, false, cancellationToken);
                throw CurrentPasswordIncorrect();
            }

            // A temporary password must really be replaced, not typed in again
            if (user.MustChangePassword && newPassword == currentPassword)
            {
                throw new DomainRuleException(
                    DomainRule.PasswordInvalid,
                    "Choose a password different from the temporary one.");
            }

            // With a second factor the password alone never changes anything and never clears the counter that
            // wrong codes share: the clearing below happens only after the whole proof succeeded
            if (await HasActiveSecondFactorAsync(user, cancellationToken))
            {
                if (verifySecondFactor is null)
                {
                    await RecordCoalescedAsync(
                        user,
                        TotpService.FailedAction,
                        "second factor required (password change)",
                        cancellationToken);
                    throw new DomainRuleException(DomainRule.TotpCodeIncorrect, TotpService.PasswordChangeCodeMessage);
                }

                if (!await verifySecondFactor(user, cancellationToken))
                {
                    _ = await RegisterFailureAsync(
                        user,
                        TotpService.FailedAction,
                        "wrong or reused code (password change)",
                        cancellationToken);
                    throw new DomainRuleException(DomainRule.TotpCodeIncorrect, TotpService.CodeIncorrectMessage);
                }
            }

            user.PasswordHash = _hasher.Hash(newPassword!);
            user.MustChangePassword = false;
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;

            // The person who changes their own password is the actor; neither password reaches the entry
            await _store.UpdateAsync(
                user,
                AuditDraft.By(new Actor(user.Username, user.DisplayName), "user.password-changed", $"user '{user.Username}'"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Sets the password of a user without knowing the current one (for an administrator).
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="newPassword">The new password; same policy as for a new user.</param>
    /// <param name="actor">The administrator who sets the password; recorded in the audit trail. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the new hash is stored; a lockout of the user ends with it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.PasswordInvalid"/> when the password breaks the policy, or with
    ///     <see cref="DomainRule.UserNotFound"/> when there is no such user.
    /// </exception>
    public async Task SetPasswordAsync(
        string username,
        string? newPassword,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ValidatePassword(newPassword);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (user.IsExternal)
            {
                throw new DomainRuleException(
                    DomainRule.PasswordInvalid,
                    $"The user '{user.Username}' signs in with an identity provider and has no password.");
            }

            user.PasswordHash = _hasher.Hash(newPassword!);
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            await _store.UpdateAsync(
                user,
                AuditDraft.By(actor, "user.password-set-by-admin", $"user '{user.Username}'"),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Signs in a person who proved their identity at an external identity provider, creating the account on
    ///     the first sign-in.
    /// </summary>
    /// <param name="issuer">The issuer of the provider, as validated by the sign-in handler. Must not be empty.</param>
    /// <param name="subject">The identifier of the person at the provider (the <c>sub</c> claim). Must not be empty.</param>
    /// <param name="usernameCandidate">
    ///     The name the provider suggests for the account (a preferred user name or the start of an e-mail address);
    ///     <c>null</c> or unusable text gives a generic name. It is only a starting point: it is reduced to letters,
    ///     digits, dot, underscore and hyphen, and made unique.
    /// </param>
    /// <param name="displayName">The name the provider gives for the person; cleaned before it is stored.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The account when the sign-in is accepted. A refusal (a disabled account, or an identity that is missing
    ///     or too long) has no account; the caller must answer every refusal the same way.
    /// </returns>
    /// <remarks>
    ///     The account is found by the pair of issuer and subject only, compared exactly; it is never found or
    ///     linked by user name, display name or e-mail address, so no provider can take over a local account or the
    ///     account of another provider. A new account is enabled, is not a system administrator, has no project
    ///     membership and no password. When the derived user name is taken by any account, a numeric suffix is
    ///     added (<c>-2</c>, <c>-3</c>, and so on), and after <see cref="MaxUsernameSuffix"/> tries a name derived
    ///     from the identity itself is used, which is numbered the same way when a local account holds it; no
    ///     existing name is ever taken over. A later sign-in does not change the stored names. Accepted sign-ins
    ///     and refusals of a disabled account are recorded in the audit trail on a best-effort basis; neither
    ///     tokens nor claims are recorded. Runs one at a time with the other changes of users.
    /// </remarks>
    public async Task<ExternalSignInResult> SignInExternalAsync(
        string? issuer,
        string? subject,
        string? usernameCandidate,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        // An identity that is missing or longer than the stored columns can never match or create an account
        if (string.IsNullOrEmpty(issuer)
            || issuer.Length > ReviewLimits.MaxExternalIssuerLength
            || string.IsNullOrEmpty(subject)
            || subject.Length > ReviewLimits.MaxExternalSubjectLength)
        {
            return new ExternalSignInResult(null, ExternalSignInRefusal.Invalid, false);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Match by the exact pair only; create the account when there is none yet
            var user = await _store.GetByExternalAsync(issuer, subject, cancellationToken);
            var created = false;
            if (user is null)
            {
                (user, created) = await CreateExternalAsync(
                    issuer,
                    subject,
                    usernameCandidate,
                    displayName,
                    cancellationToken);
                if (user is null)
                {
                    return new ExternalSignInResult(null, ExternalSignInRefusal.Invalid, false);
                }
            }

            // A disabled account never signs in; the audit trail says why, the caller must not
            if (!user.Enabled)
            {
                await RecordCoalescedAsync(user, OidcSignInFailedAction, "account disabled", cancellationToken);
                return new ExternalSignInResult(null, ExternalSignInRefusal.Disabled, false);
            }

            await RecordAsync(user, "auth.oidc-sign-in", $"issuer {IssuerHost(issuer)}", cancellationToken);
            return new ExternalSignInResult(user, ExternalSignInRefusal.None, created);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Records a refused or failed sign-in with an external identity provider, coalesced.
    /// </summary>
    /// <param name="reasonClass">
    ///     A short fixed word for what went wrong (for example <c>throttled</c> or <c>token-invalid</c>); the only
    ///     detail recorded. Never pass text taken from a token, a claim or an error message.
    /// </param>
    /// <param name="address">The client address, used to coalesce repeated entries; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the entry is stored, skipped or its failure has been reported.</returns>
    /// <remarks>
    ///     Nobody is signed in, so the actor is the plain text <c>not signed in</c>. The entry is limited to one per
    ///     minute, address and reason, because nothing else limits how often a visitor can trigger it.
    /// </remarks>
    public Task RecordExternalSignInFailureAsync(
        string reasonClass,
        string? address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reasonClass);
        if (_audit is null)
        {
            return Task.CompletedTask;
        }

        var reason = AuditText.Clean(reasonClass, AuditText.MaxActorNameLength);
        return _audit.TryRecordCoalescedAsync(
            new AuditDraft(OidcSignInFailedAction, null, "not signed in", null, null, reason),
            $"{OidcSignInFailedAction}|{reason}|{address}",
            cancellationToken);
    }

    /// <summary>
    ///     Records that an external-provider user could not confirm their identity for a signature, coalesced.
    /// </summary>
    /// <param name="user">The signed-in user who tried to sign. Must not be null.</param>
    /// <param name="reasonClass">
    ///     A short fixed word for what went wrong (for example <c>stale-auth-time</c> or <c>no-proof</c>); the only
    ///     detail recorded after the prefix <c>oidc:</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the entry is stored, skipped or its failure has been reported.</returns>
    /// <remarks>
    ///     This is the counterpart of the failed password check for an account without a password: the same action
    ///     code <c>signature.reauth-failed</c>, so the trail shows every refused confirmation in one place.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public Task RecordExternalReauthFailureAsync(
        User user,
        string reasonClass,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(reasonClass);
        return RecordCoalescedAsync(
            user,
            ReauthFailedAction,
            $"oidc: {AuditText.Clean(reasonClass, 50)}",
            cancellationToken);
    }

    /// <summary>
    ///     Creates the account of an external identity that signs in for the first time.
    /// </summary>
    /// <param name="issuer">The issuer of the provider.</param>
    /// <param name="subject">The subject at the provider.</param>
    /// <param name="usernameCandidate">The suggested user name, or <c>null</c>.</param>
    /// <param name="displayName">The suggested display name, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The account and <c>true</c> when it was created; the account of the identity and <c>false</c> when another
    ///     process created it first; <c>null</c> and <c>false</c> when no free user name was found.
    /// </returns>
    private async Task<(User? User, bool Created)> CreateExternalAsync(
        string issuer,
        string subject,
        string? usernameCandidate,
        string? displayName,
        CancellationToken cancellationToken)
    {
        // The first name that no account has: the derived one, then numbered ones, then one from the identity itself
        // and, when a local account has even that name, numbered variants of it, so the sign-in always finds a name
        var baseName = DeriveUsername(usernameCandidate);
        var identityName = "oidc-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer}|{subject}")))[..12];
        var names = new List<string> { baseName };
        names.AddRange(Enumerable.Range(2, MaxUsernameSuffix - 1).Select(number => $"{baseName}-{number}"));
        names.Add(identityName);
        names.AddRange(Enumerable.Range(2, MaxUsernameSuffix - 1).Select(number => $"{identityName}-{number}"));

        foreach (var name in names)
        {
            if (await _store.GetByUsernameAsync(name, cancellationToken) is not null)
            {
                continue;
            }

            try
            {
                var created = await _store.AddAsync(
                    new User
                    {
                        Username = name,
                        DisplayName = SanitizeDisplayName(displayName, name),
                        IsSystemAdmin = false,
                        Enabled = true,
                        PasswordHash = string.Empty,
                        Created = _timeProvider.GetUtcNow(),
                        ExternalIssuer = issuer,
                        ExternalSubject = subject
                    },
                    stored => AuditDraft.By(
                        new Actor(stored.Username, stored.DisplayName),
                        "account.oidc-created",
                        $"issuer {IssuerHost(issuer)}"),
                    cancellationToken);
                return (created, true);
            }
            catch (DomainRuleException ex) when (ex.Rule == DomainRule.UserNameTaken)
            {
                // The store refuses a taken name and a second account of the same identity alike: another process
                // may have created the account just now, otherwise the name was taken and the next one is tried
                var raced = await _store.GetByExternalAsync(issuer, subject, cancellationToken);
                if (raced is not null)
                {
                    return (raced, false);
                }
            }
        }

        return (null, false);
    }

    /// <summary>
    ///     Derives a valid user name from a name suggested by an identity provider.
    /// </summary>
    /// <param name="candidate">The suggested name; may be <c>null</c> or hostile.</param>
    /// <returns>
    ///     Only letters, digits, dot, underscore and hyphen of the candidate, at most
    ///     <see cref="ReviewLimits.MaxUsernameLength"/> minus the room for a suffix, and at least
    ///     <see cref="ReviewLimits.MinUsernameLength"/> characters (<c>user</c> when nothing is left).
    /// </returns>
    private static string DeriveUsername(string? candidate)
    {
        var kept = new string((candidate ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            .Take(ReviewLimits.MaxUsernameLength - UsernameSuffixRoom)
            .ToArray());
        return kept.Length switch
        {
            0 => "user",
            < ReviewLimits.MinUsernameLength => "user-" + kept,
            _ => kept
        };
    }

    /// <summary>
    ///     Cleans a display name that comes from an identity provider.
    /// </summary>
    /// <param name="displayName">The suggested name; may be <c>null</c> or hostile.</param>
    /// <param name="fallback">The text used when nothing is left (the user name).</param>
    /// <returns>
    ///     One line without control, separator or invisible formatting characters (which could reverse or hide text),
    ///     with runs of white space collapsed, trimmed and at most <see cref="ReviewLimits.MaxDisplayNameLength"/>
    ///     characters.
    /// </returns>
    private static string SanitizeDisplayName(string? displayName, string fallback)
    {
        var visible = new string((displayName ?? string.Empty)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format)
            .ToArray());
        var oneLine = AuditText.Clean(visible, ReviewLimits.MaxDisplayNameLength);
        var collapsed = string.Join(' ', oneLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? fallback : collapsed;
    }

    /// <summary>
    ///     Gets the host name of an issuer, for the audit trail.
    /// </summary>
    /// <param name="issuer">The issuer URL.</param>
    /// <returns>The host name, or <c>unknown</c> when the issuer is not an absolute URL.</returns>
    private static string IssuerHost(string issuer)
    {
        return Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            ? AuditText.Clean(uri.Host, 100)
            : "unknown";
    }

    /// <summary>
    ///     Reads a user that must exist.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.UserNotFound"/> when there is no such user.</exception>
    private async Task<User> LoadAsync(string username, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(username);
        return await _store.GetByUsernameAsync(username, cancellationToken)
               ?? throw new DomainRuleException(DomainRule.UserNotFound, $"The user '{username}' does not exist.");
    }

    /// <summary>
    ///     Refuses a change that would leave no enabled administrator.
    /// </summary>
    /// <param name="user">The user that is about to lose administrator rights (by being disabled or demoted).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the check is done.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.LastAdmin"/> when the user is the only enabled administrator.</exception>
    private async Task EnsureNotLastAdminAsync(User user, CancellationToken cancellationToken)
    {
        // Only an enabled system administrator counts; anyone else can be changed freely
        if (!user.IsSystemAdmin || !user.Enabled)
        {
            return;
        }

        var others = (await _store.ListAsync(cancellationToken))
            .Count(other => other.Id != user.Id && other.IsSystemAdmin && other.Enabled);
        if (others == 0)
        {
            throw new DomainRuleException(
                DomainRule.LastAdmin,
                "At least one enabled system administrator must remain.");
        }
    }

    /// <summary>
    ///     Spends one password verification on a hash of no account, so a refusal that never checks the real
    ///     hash takes about as long as one that does.
    /// </summary>
    /// <param name="password">The password that was offered.</param>
    private void VerifyDummy(string? password)
    {
        _dummyHash ??= _hasher.Hash("dummy-password-of-no-account");
        _ = _hasher.Verify(_dummyHash, password ?? string.Empty);
    }

    /// <summary>
    ///     Tells whether sign-in is refused for the account right now.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns><c>true</c> while the lockout lasts.</returns>
    private bool IsLockedOut(User user)
    {
        return user.LockoutUntil is { } until && until > _timeProvider.GetUtcNow();
    }

    /// <summary>
    ///     Counts a failed password check and starts the lockout when the limit is reached.
    /// </summary>
    /// <param name="user">The user, as read from the store.</param>
    /// <param name="reauthentication"><c>true</c> when the check was a re-authentication, which changes the action code recorded.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    private async Task RegisterFailureAsync(User user, bool reauthentication, CancellationToken cancellationToken)
    {
        await RegisterFailureAsync(
            user,
            reauthentication ? ReauthFailedAction : "auth.sign-in-failed",
            "wrong password",
            cancellationToken);
    }

    /// <summary>
    ///     Counts a failed credential check (password or second factor) and starts the lockout when the limit is reached.
    /// </summary>
    /// <param name="user">The user, as read from the store.</param>
    /// <param name="action">The audit action code of the failure.</param>
    /// <param name="detail">A fixed detail text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when this failure started the lockout.</returns>
    private async Task<bool> RegisterFailureAsync(
        User user,
        string action,
        string detail,
        CancellationToken cancellationToken)
    {
        user.FailedLoginCount++;
        var lockedOut = user.FailedLoginCount >= MaxFailedAttempts;
        if (lockedOut)
        {
            // The counter restarts after the lockout so the next window gets a fresh set of attempts
            user.LockoutUntil = _timeProvider.GetUtcNow() + LockoutDuration;
            user.FailedLoginCount = 0;
        }

        // The counter and the lockout are state, not events: the events below are recorded separately
        await _store.UpdateAsync(user, null, cancellationToken);
        // Wrong credentials are bounded by the lockout, so every one of them is recorded
        await RecordAsync(user, action, detail, cancellationToken);
        if (lockedOut)
        {
            await RecordAsync(
                user,
                "auth.locked-out",
                $"locked for {LockoutDuration.TotalMinutes:0} minutes after {MaxFailedAttempts} failed attempts",
                cancellationToken);
        }

        return lockedOut;
    }

    /// <summary>
    ///     Records the refusal of an unknown user name on a best-effort basis, coalesced.
    /// </summary>
    /// <param name="typedName">The name that was typed; recorded after cleaning (one line, at most <see cref="ReviewLimits.MaxUsernameLength"/> characters). The password is never passed here.</param>
    /// <param name="reauthentication"><c>true</c> for a re-authentication; <c>false</c> for a sign-in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the entry is stored, skipped or its failure has been reported.</returns>
    private Task RecordUnknownUserAsync(string? typedName, bool reauthentication, CancellationToken cancellationToken)
    {
        var name = AuditText.Clean(typedName, ReviewLimits.MaxUsernameLength);
        return RecordCoalescedAsync(
            null,
            reauthentication ? ReauthFailedAction : "auth.sign-in-failed",
            $"unknown user, name '{name}'",
            cancellationToken,
            name);
    }

    /// <summary>
    ///     Records a sign-in event on a best-effort basis, at most once per minute for the same action, reason and name.
    /// </summary>
    /// <param name="user">The account, or <c>null</c> for an unknown user (recorded as plain <c>unknown user</c>).</param>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The detail text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="typedName">The cleaned typed name for an unknown user; the user name of <paramref name="user"/> is used when <c>null</c>.</param>
    /// <returns>A task that completes when the entry is stored, skipped or its failure has been reported.</returns>
    private Task RecordCoalescedAsync(
        User? user,
        string action,
        string detail,
        CancellationToken cancellationToken,
        string? typedName = null)
    {
        if (_audit is null)
        {
            return Task.CompletedTask;
        }

        var name = (typedName ?? user?.Username ?? string.Empty).ToLowerInvariant();
        // The reason is part of the key of an account (a fixed text), so one reason cannot hide another
        var kind = user is null ? "unknown" : $"account:{detail}";
        var key = $"{action}|{kind}|{name}";
        return _audit.TryRecordCoalescedAsync(BuildDraft(user, action, detail), key, cancellationToken);
    }

    /// <summary>
    ///     Records a sign-in event on a best-effort basis.
    /// </summary>
    /// <param name="user">The account, or <c>null</c> for an unknown user (recorded as plain <c>unknown user</c>).</param>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The detail text.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the entry is stored or its failure has been reported.</returns>
    private async Task RecordAsync(User? user, string action, string detail, CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }

        await _audit.TryRecordAsync(BuildDraft(user, action, detail), cancellationToken);
    }

    /// <summary>
    ///     Builds the audit draft of a sign-in event.
    /// </summary>
    /// <param name="user">The account, or <c>null</c> for an unknown user.</param>
    /// <param name="action">The action code.</param>
    /// <param name="detail">The detail text.</param>
    /// <returns>The draft; the actor is the account, or the plain text <c>unknown user</c>.</returns>
    private static AuditDraft BuildDraft(User? user, string action, string detail)
    {
        return user is null
            ? new AuditDraft(action, null, "unknown user", null, null, detail)
            : AuditDraft.By(new Actor(user.Username, user.DisplayName), action, detail);
    }

    /// <summary>
    ///     Clears the failure counter after a successful sign-in.
    /// </summary>
    /// <param name="user">The user, as read from the store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored; nothing is written when there was nothing to clear.</returns>
    private async Task ResetFailuresAsync(User user, CancellationToken cancellationToken)
    {
        if (user.FailedLoginCount == 0 && user.LockoutUntil is null)
        {
            return;
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _store.UpdateAsync(user, null, cancellationToken);
    }

    /// <summary>
    ///     Builds the exception for a wrong current password.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException CurrentPasswordIncorrect()
    {
        return new DomainRuleException(DomainRule.CurrentPasswordIncorrect, "The current password is incorrect.");
    }

    /// <summary>
    ///     Checks a user name.
    /// </summary>
    /// <param name="username">The candidate user name.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.UserNameInvalid"/> when the name breaks the rules.</exception>
    internal static void ValidateUsername(string? username)
    {
        if (username is null
            || username.Length < ReviewLimits.MinUsernameLength
            || username.Length > ReviewLimits.MaxUsernameLength
            || !username.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            throw new DomainRuleException(
                DomainRule.UserNameInvalid,
                $"The user name must be {ReviewLimits.MinUsernameLength} to {ReviewLimits.MaxUsernameLength} " +
                "characters of letters, digits, dot, underscore and hyphen.");
        }
    }

    /// <summary>
    ///     Checks and normalizes a display name.
    /// </summary>
    /// <param name="displayName">The candidate display name.</param>
    /// <returns>The name without leading and trailing white space.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.DisplayNameInvalid"/> when the name is empty or too long.</exception>
    internal static string ValidateDisplayName(string? displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > ReviewLimits.MaxDisplayNameLength)
        {
            throw new DomainRuleException(
                DomainRule.DisplayNameInvalid,
                $"The display name must be 1 to {ReviewLimits.MaxDisplayNameLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    ///     Checks a password against the policy.
    /// </summary>
    /// <param name="password">The candidate password.</param>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.PasswordInvalid"/> when the length is outside the policy.</exception>
    private static void ValidatePassword(string? password)
    {
        if (password is null
            || password.Length < ReviewLimits.MinPasswordLength
            || password.Length > ReviewLimits.MaxPasswordLength)
        {
            throw new DomainRuleException(
                DomainRule.PasswordInvalid,
                $"The password must be {ReviewLimits.MinPasswordLength} to {ReviewLimits.MaxPasswordLength} characters.");
        }
    }
}
