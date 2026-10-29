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

using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     The optional time-based one-time password (RFC 6238) second factor of local password accounts: enrollment,
///     the second step of sign-in, the code at signing time, recovery codes, and removal.
/// </summary>
/// <remarks>
///     <para>
///     The service holds the rules; the page and endpoint code only collects input. A second factor is available to
///     a local account that is enabled and has no temporary password, when the feature is switched on and the
///     request does not come with an API token. Accounts of an identity provider never have one (the provider
///     owns it), and an API token can never sign, so neither is affected.
///     </para>
///     <para>
///     A code is accepted for the current 30 second step and one step before and after. The step of an accepted
///     code is stored with a conditional update that succeeds for exactly one caller and only when the step is
///     greater than the last accepted one, so a code works once, even when two requests present it at the same
///     moment. A wrong code counts toward the same lockout as a wrong password
///     (<see cref="UserService.RegisterSecondFactorFailureAsync"/>). Recovery codes are random, hashed with the
///     user, and spent by a conditional update, so they work once too.
///     </para>
///     <para>
///     Neither a code, a secret, a recovery code nor a challenge token is ever written to the audit trail, a log
///     or an error message; the only secrets that leave this class are the ones shown to the user once (the
///     enrollment secret and the new recovery codes). The service is stateless apart from the challenge store and
///     is safe for concurrent use; register it as a singleton.
///     </para>
/// </remarks>
public sealed class TotpService
{
    /// <summary>
    ///     The audit action code of a completed enrollment.
    /// </summary>
    public const string EnrolledAction = "auth.totp-enrolled";

    /// <summary>
    ///     The audit action code of a second factor removed by its owner.
    /// </summary>
    public const string DisabledAction = "auth.totp-disabled";

    /// <summary>
    ///     The audit action code of a second factor removed by an administrator.
    /// </summary>
    public const string ResetAction = "auth.totp-reset";

    /// <summary>
    ///     The audit action code of a recovery code that was spent.
    /// </summary>
    public const string RecoveryUsedAction = "auth.totp-recovery-used";

    /// <summary>
    ///     The audit action code of a new set of recovery codes.
    /// </summary>
    public const string RecoveryRegeneratedAction = "auth.totp-recovery-regenerated";

    /// <summary>
    ///     The audit action code of a wrong second factor code.
    /// </summary>
    public const string FailedAction = "auth.totp-failed";

    /// <summary>
    ///     The number of recovery codes generated at a time.
    /// </summary>
    public const int RecoveryCodeCount = 10;

    /// <summary>
    ///     How long a started enrollment can be confirmed.
    /// </summary>
    private static readonly TimeSpan EnrollmentLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     The audit action code of a failed re-authentication at signing time.
    /// </summary>
    private const string ReauthFailedAction = "signature.reauth-failed";

    /// <summary>
    ///     The characters of a recovery code: digits and upper-case letters without I, L, O and U (Crockford Base32),
    ///     32 symbols, so every random byte maps to a symbol without bias.
    /// </summary>
    private const string RecoveryAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    ///     The number of symbols in a recovery code (80 bits).
    /// </summary>
    private const int RecoveryCodeLength = 16;

    /// <summary>
    ///     The prefix that separates the recovery code hashes from every other use of SHA-256.
    /// </summary>
    private const string RecoveryHashContext = "ReviewCenter.TotpRecovery.v1:";

    /// <summary>
    ///     The message of a wrong or reused code; the same for every cause.
    /// </summary>
    private const string CodeIncorrectMessage = "The code is not valid or was already used.";

    /// <summary>
    ///     The persistence of enrollments and recovery codes.
    /// </summary>
    private readonly ITotpStore _store;

    /// <summary>
    ///     The code algorithm.
    /// </summary>
    private readonly ITotpAlgorithm _algorithm;

    /// <summary>
    ///     The encryption of stored secrets.
    /// </summary>
    private readonly ITotpSecretProtector _protector;

    /// <summary>
    ///     The user service: passwords, lockout and users.
    /// </summary>
    private readonly UserService _users;

    /// <summary>
    ///     The pending second steps of sign-ins.
    /// </summary>
    private readonly TotpChallengeStore _challenges;

    /// <summary>
    ///     The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The settings.
    /// </summary>
    private readonly TotpSettings _settings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TotpService"/> class.
    /// </summary>
    /// <param name="store">The persistence of enrollments. Must not be null.</param>
    /// <param name="algorithm">The code algorithm. Must not be null.</param>
    /// <param name="protector">The encryption of stored secrets. Must not be null.</param>
    /// <param name="users">The user service. Must not be null.</param>
    /// <param name="challenges">The pending sign-in challenges. Must not be null.</param>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <param name="settings">The settings; <c>null</c> for the defaults (enabled, issuer <see cref="TotpSettings.DefaultIssuer"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public TotpService(
        ITotpStore store,
        ITotpAlgorithm algorithm,
        ITotpSecretProtector protector,
        UserService users,
        TotpChallengeStore challenges,
        TimeProvider timeProvider,
        TotpSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(challenges);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _algorithm = algorithm;
        _protector = protector;
        _users = users;
        _challenges = challenges;
        _timeProvider = timeProvider;
        _settings = settings ?? new TotpSettings(true, TotpSettings.DefaultIssuer);
    }

    /// <summary>
    ///     Gets a value indicating whether the second factor is switched on for the server.
    /// </summary>
    public bool FeatureEnabled => _settings.Enabled;

    /// <summary>
    ///     Reads what a page needs to show the second factor of the signed-in user.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status; it never throws for a user that can not use the feature (<see cref="TotpStatus.Available"/> is <c>false</c> then).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    public async Task<TotpStatus> GetStatusAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var user = await _users.GetAsync(actor.Username, cancellationToken);
        var available = FeatureEnabled
                        && !actor.ViaApiToken
                        && user is { Enabled: true, IsExternal: false, MustChangePassword: false };
        if (user is null || !FeatureEnabled)
        {
            return new TotpStatus(FeatureEnabled, false, false, 0);
        }

        var row = await _store.GetAsync(user.Id, cancellationToken);
        var enrolled = row?.IsActive == true;
        var left = enrolled ? await _store.CountUnusedRecoveryCodesAsync(user.Id, cancellationToken) : 0;
        return new TotpStatus(true, available, enrolled, left);
    }

    /// <summary>
    ///     Lists the users that have a confirmed second factor, for the administrator's user list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The identities of the users.</returns>
    public Task<IReadOnlySet<int>> ListEnrolledUserIdsAsync(CancellationToken cancellationToken = default)
    {
        return _store.ListActiveUserIdsAsync(cancellationToken);
    }

    /// <summary>
    ///     Tells whether a user has a confirmed second factor that applies.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the feature is on and the user has a confirmed enrollment.</returns>
    public async Task<bool> IsEnrolledAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        if (!FeatureEnabled)
        {
            return false;
        }

        var user = await _users.GetAsync(username, cancellationToken);
        return user is not null && (await _store.GetAsync(user.Id, cancellationToken))?.IsActive == true;
    }

    /// <summary>
    ///     Starts the enrollment: creates a new secret and stores it as pending.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="password">The current password of the user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The secret as Base32 text and as an <c>otpauth://</c> address, to show once; it is not shown again. The
    ///     enrollment has no effect on sign-in until <see cref="ConfirmEnrollmentAsync"/> succeeds; starting again
    ///     replaces the pending secret.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TotpNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.TotpAlreadyEnrolled"/> or <see cref="DomainRule.CurrentPasswordIncorrect"/>.
    /// </exception>
    public async Task<TotpEnrollment> BeginEnrollmentAsync(
        Actor actor,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        if ((await _store.GetAsync(user.Id, cancellationToken))?.IsActive == true)
        {
            throw new DomainRuleException(
                DomainRule.TotpAlreadyEnrolled,
                "A second factor is already set up. Remove it before setting up a new one.");
        }

        await RequirePasswordAsync(user, password, cancellationToken);
        var secret = _algorithm.GenerateSecret();
        try
        {
            var saved = await _store.SavePendingAsync(
                new UserTotp
                {
                    UserId = user.Id,
                    ProtectedSecret = _protector.Protect(user.Id, secret),
                    Created = _timeProvider.GetUtcNow()
                },
                cancellationToken);
            if (!saved)
            {
                throw new DomainRuleException(
                    DomainRule.TotpAlreadyEnrolled,
                    "A second factor is already set up. Remove it before setting up a new one.");
            }

            return new TotpEnrollment(
                _algorithm.ToBase32(secret),
                _algorithm.BuildUri(secret, _settings.Issuer, user.Username));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    ///     Confirms the pending enrollment with a code from the authenticator app and turns the second factor on.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="code">The six digit code the app shows now.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The new recovery codes, to show once: only their hashes are stored. The accepted code can not be used
    ///     again to sign in.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TotpNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.TotpNotEnrolled"/> (nothing was started), <see cref="DomainRule.TotpAlreadyEnrolled"/>,
    ///     <see cref="DomainRule.TotpEnrollmentExpired"/> or <see cref="DomainRule.TotpCodeIncorrect"/> (a wrong code
    ///     counts toward the lockout).
    /// </exception>
    public async Task<IReadOnlyList<string>> ConfirmEnrollmentAsync(
        Actor actor,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        var row = await _store.GetAsync(user.Id, cancellationToken)
                  ?? throw new DomainRuleException(DomainRule.TotpNotEnrolled, "No second factor setup was started.");
        if (row.IsActive)
        {
            throw new DomainRuleException(DomainRule.TotpAlreadyEnrolled, "A second factor is already set up.");
        }

        var now = _timeProvider.GetUtcNow();
        if (now - row.Created > EnrollmentLifetime)
        {
            throw new DomainRuleException(
                DomainRule.TotpEnrollmentExpired,
                "The setup took too long. Start the setup again.");
        }

        if (await _users.IsLockedOutAsync(user.Username, cancellationToken))
        {
            throw new DomainRuleException(DomainRule.TotpCodeIncorrect, CodeIncorrectMessage);
        }

        // The confirming code proves possession of the secret; the store makes that step the last accepted one in
        // the same change that turns the second factor on
        var step = 0L;
        var stripped = StripSeparators(code);
        var valid = false;
        if (IsSixDigits(stripped))
        {
            var secret = _protector.Unprotect(user.Id, row.ProtectedSecret);
            try
            {
                valid = _algorithm.TryVerify(secret, stripped, now, out step);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        if (!valid)
        {
            await FailAsync(user, "wrong or reused code (enrollment)", cancellationToken);
        }

        var codes = GenerateRecoveryCodes();
        var confirmed = await _store.ConfirmAsync(
            user.Id,
            step,
            now,
            codes.Select(recoveryCode => HashRecoveryCode(user.Id, recoveryCode)).ToList(),
            AuditDraft.By(
                new Actor(user.Username, user.DisplayName),
                EnrolledAction,
                $"second factor set up, {RecoveryCodeCount} recovery codes issued"),
            cancellationToken);
        if (!confirmed)
        {
            // Another request confirmed first, or the setup was cancelled meanwhile
            throw new DomainRuleException(DomainRule.TotpCodeIncorrect, CodeIncorrectMessage);
        }

        await _users.ClearFailuresAsync(user.Username, cancellationToken);
        _challenges.InvalidateUser(user.Username);
        return codes.Select(FormatRecoveryCode).ToList();
    }

    /// <summary>
    ///     Gives up a started enrollment that was not confirmed.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the pending enrollment is removed; a confirmed one is left alone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.TotpNotAvailable"/> or <see cref="DomainRule.ActorNotAllowed"/>.</exception>
    public async Task CancelEnrollmentAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        _ = await _store.RemovePendingAsync(user.Id, cancellationToken);
    }

    /// <summary>
    ///     Removes the second factor of the signed-in user.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="password">The current password.</param>
    /// <param name="codeOrRecoveryCode">A current authenticator code, or an unused recovery code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the second factor is removed, with its recovery codes; pending sign-ins of the user end.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TotpNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.CurrentPasswordIncorrect"/>, <see cref="DomainRule.TotpNotEnrolled"/> or
    ///     <see cref="DomainRule.TotpCodeIncorrect"/>.
    /// </exception>
    public async Task DisableAsync(
        Actor actor,
        string? password,
        string? codeOrRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        await RequirePasswordAsync(user, password, cancellationToken);
        var row = await RequireActiveAsync(user, cancellationToken);
        await RequireCodeAsync(user, row, codeOrRecoveryCode, true, "recovery code used to remove the second factor", cancellationToken);
        var removed = await _store.RemoveAsync(
            user.Id,
            AuditDraft.By(new Actor(user.Username, user.DisplayName), DisabledAction, "second factor removed by its owner"),
            cancellationToken);
        _challenges.InvalidateUser(user.Username);
        if (!removed)
        {
            throw new DomainRuleException(DomainRule.TotpNotEnrolled, "No second factor is set up.");
        }
    }

    /// <summary>
    ///     Replaces the recovery codes of the signed-in user.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="password">The current password.</param>
    /// <param name="code">A current authenticator code (a recovery code is not accepted here).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new recovery codes, to show once; the old ones stop working.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.TotpNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.CurrentPasswordIncorrect"/>, <see cref="DomainRule.TotpNotEnrolled"/> or
    ///     <see cref="DomainRule.TotpCodeIncorrect"/>.
    /// </exception>
    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(
        Actor actor,
        string? password,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        await RequirePasswordAsync(user, password, cancellationToken);
        var row = await RequireActiveAsync(user, cancellationToken);
        await RequireCodeAsync(user, row, code, false, string.Empty, cancellationToken);
        var codes = GenerateRecoveryCodes();
        var replaced = await _store.ReplaceRecoveryCodesAsync(
            user.Id,
            codes.Select(recoveryCode => HashRecoveryCode(user.Id, recoveryCode)).ToList(),
            _timeProvider.GetUtcNow(),
            AuditDraft.By(
                new Actor(user.Username, user.DisplayName),
                RecoveryRegeneratedAction,
                $"{RecoveryCodeCount} new recovery codes issued"),
            cancellationToken);
        if (!replaced)
        {
            throw new DomainRuleException(DomainRule.TotpNotEnrolled, "No second factor is set up.");
        }

        return codes.Select(FormatRecoveryCode).ToList();
    }

    /// <summary>
    ///     Removes the second factor of a user who lost the device and the recovery codes.
    /// </summary>
    /// <param name="administrator">The system administrator who resets it. Must not be null.</param>
    /// <param name="username">The user name of the account; compared without regard to case.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the second factor is removed; the sessions of the user stay valid, pending sign-ins end.</returns>
    /// <remarks>
    ///     Only an enabled system administrator may do this, not through an API token, and not while still using a
    ///     temporary password. It works while the feature is switched off, so an enrollment can still be cleared.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="administrator"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ActorNotAllowed"/> (not a system administrator),
    ///     <see cref="DomainRule.UserNotFound"/> or <see cref="DomainRule.TotpNotEnrolled"/>.
    /// </exception>
    public async Task ResetAsync(Actor administrator, string? username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        var admin = administrator.ViaApiToken ? null : await _users.GetAsync(administrator.Username, cancellationToken);
        if (admin is not { IsSystemAdmin: true, Enabled: true, MustChangePassword: false })
        {
            throw new DomainRuleException(
                DomainRule.ActorNotAllowed,
                "Only a system administrator can reset a second factor.");
        }

        var target = await _users.GetAsync(username ?? string.Empty, cancellationToken)
                     ?? throw new DomainRuleException(DomainRule.UserNotFound, "The user does not exist.");
        var removed = await _store.RemoveAsync(
            target.Id,
            AuditDraft.By(administrator, ResetAction, $"second factor of user '{target.Username}' reset"),
            cancellationToken);
        _challenges.InvalidateUser(target.Username);
        if (!removed)
        {
            throw new DomainRuleException(DomainRule.TotpNotEnrolled, "The user has no second factor set up.");
        }
    }

    /// <summary>
    ///     Starts the second step of a sign-in after the password was right.
    /// </summary>
    /// <param name="user">The user whose password was right. Must not be null.</param>
    /// <param name="clientAddress">The client address as text; <c>null</c> when unknown.</param>
    /// <param name="returnUrl">The local address to go to after the sign-in. Must not be null.</param>
    /// <returns>The challenge token for the cookie. It is never stored itself, only its hash.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user"/> or <paramref name="returnUrl"/> is null.</exception>
    public string BeginSignIn(User user, string? clientAddress, string returnUrl)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _challenges.Issue(user.Username, clientAddress, returnUrl);
    }

    /// <summary>
    ///     Looks a pending sign-in up without using it.
    /// </summary>
    /// <param name="token">The token from the cookie; may be null.</param>
    /// <param name="clientAddress">The client address as text; <c>null</c> when unknown.</param>
    /// <returns>The challenge, or <c>null</c> when it is unknown, ended or was issued to another client.</returns>
    public TotpChallenge? PeekChallenge(string? token, string? clientAddress)
    {
        return _challenges.Peek(token, clientAddress);
    }

    /// <summary>
    ///     Completes the second step of a sign-in with a code.
    /// </summary>
    /// <param name="token">The token from the cookie; may be null.</param>
    /// <param name="clientAddress">The client address as text; <c>null</c> when unknown.</param>
    /// <param name="code">The typed authenticator code or recovery code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     The outcome. Only <see cref="TotpSignInOutcome.Success"/> carries the user to sign in. A wrong, reused
    ///     or malformed code is <see cref="TotpSignInOutcome.Failed"/> in every case, counts toward the lockout like
    ///     a wrong password, and keeps the challenge for another try (up to <see cref="TotpChallengeStore.MaxFailures"/>).
    /// </returns>
    public async Task<TotpSignInResult> CompleteSignInAsync(
        string? token,
        string? clientAddress,
        string? code,
        CancellationToken cancellationToken = default)
    {
        // The challenge is taken, so two requests can never use it at the same moment
        var challenge = _challenges.Take(token, clientAddress);
        if (challenge is null)
        {
            return Refused(TotpSignInOutcome.ChallengeInvalid);
        }

        var user = await _users.GetAsync(challenge.Username, cancellationToken);
        var row = user is null || !FeatureEnabled ? null : await _store.GetAsync(user.Id, cancellationToken);
        if (user is null || !user.Enabled || user.IsExternal || row is not { IsActive: true })
        {
            return Refused(TotpSignInOutcome.ChallengeInvalid);
        }

        // A locked account is refused without checking or counting anything
        if (await _users.IsLockedOutAsync(user.Username, cancellationToken))
        {
            return Refused(TotpSignInOutcome.LockedOut);
        }

        var result = CodeResult.Invalid;
        try
        {
            result = await CheckCodeAsync(user, row, code, true, "recovery code used to sign in", cancellationToken);
        }
        catch (DomainRuleException exception) when (exception.Rule == DomainRule.TotpSecretUnreadable)
        {
            // An unreadable secret is a failure like any other for the caller; the recovery codes still work
        }

        if (result == CodeResult.Invalid)
        {
            var locked = await _users.RegisterSecondFactorFailureAsync(
                user.Username,
                FailedAction,
                "wrong or reused code (sign-in)",
                cancellationToken);
            if (locked)
            {
                return Refused(TotpSignInOutcome.LockedOut);
            }

            // The challenge stays for another try until it is used up
            _ = _challenges.Return(challenge);
            return Refused(TotpSignInOutcome.Failed);
        }

        await _users.ClearFailuresAsync(user.Username, cancellationToken);
        var recovery = result == CodeResult.Recovery;
        return new TotpSignInResult(
            TotpSignInOutcome.Success,
            user,
            recovery,
            recovery ? await _store.CountUnusedRecoveryCodesAsync(user.Id, cancellationToken) : 0);
    }

    /// <summary>
    ///     Checks the authenticator code a user typed to sign a review; the password was checked before.
    /// </summary>
    /// <param name="user">The signing user, whose password was right. Must not be null.</param>
    /// <param name="code">The typed code; a recovery code is not accepted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     <c>true</c> when the code is right and was not used before. A failure counts toward the lockout and is
    ///     recorded as <c>signature.reauth-failed</c>; a success clears the failure counter.
    /// </returns>
    internal async Task<bool> VerifyForSignatureAsync(User user, string? code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var row = await _store.GetAsync(user.Id, cancellationToken);
        var valid = false;
        if (row is { IsActive: true })
        {
            try
            {
                valid = await CheckCodeAsync(user, row, code, false, string.Empty, cancellationToken) == CodeResult.Totp;
            }
            catch (DomainRuleException exception) when (exception.Rule == DomainRule.TotpSecretUnreadable)
            {
                // An unreadable secret refuses the signature like a wrong code
            }
        }

        if (!valid)
        {
            _ = await _users.RegisterSecondFactorFailureAsync(user.Username, ReauthFailedAction, "wrong code", cancellationToken);
            return false;
        }

        await _users.ClearFailuresAsync(user.Username, cancellationToken);
        return true;
    }

    /// <summary>
    ///     The kinds of code that <see cref="CheckCodeAsync"/> can accept.
    /// </summary>
    private enum CodeResult
    {
        /// <summary>The code was refused.</summary>
        Invalid,

        /// <summary>An authenticator code was accepted and its step recorded.</summary>
        Totp,

        /// <summary>A recovery code was accepted and spent.</summary>
        Recovery
    }

    /// <summary>
    ///     Builds a refusal result.
    /// </summary>
    /// <param name="outcome">The outcome, never <see cref="TotpSignInOutcome.Success"/>.</param>
    /// <returns>The result without a user.</returns>
    private static TotpSignInResult Refused(TotpSignInOutcome outcome)
    {
        return new TotpSignInResult(outcome, null, false, 0);
    }

    /// <summary>
    ///     Loads the signed-in user and checks that the second factor is available to the account.
    /// </summary>
    /// <param name="actor">The signed-in user. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user as stored now.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.TotpNotAvailable"/> or <see cref="DomainRule.ActorNotAllowed"/>.</exception>
    private async Task<User> LoadUsableAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ViaApiToken || !FeatureEnabled)
        {
            throw NotAvailable();
        }

        var user = await _users.GetAsync(actor.Username, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "The account can not be used.");
        }

        // The identity provider owns the second factor of its accounts, and a temporary password must be replaced first
        return user.IsExternal || user.MustChangePassword ? throw NotAvailable() : user;
    }

    /// <summary>
    ///     Builds the exception for an unavailable second factor.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException NotAvailable()
    {
        return new DomainRuleException(
            DomainRule.TotpNotAvailable,
            "A second factor is not available for this account.");
    }

    /// <summary>
    ///     Checks the password of the user again before a change.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="password">The typed password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the password is right.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.CurrentPasswordIncorrect"/> when it is wrong, or the account is locked out.</exception>
    private async Task RequirePasswordAsync(User user, string? password, CancellationToken cancellationToken)
    {
        if (await _users.ConfirmPasswordAsync(user.Username, password, cancellationToken) is null)
        {
            throw new DomainRuleException(DomainRule.CurrentPasswordIncorrect, "The password is incorrect.");
        }
    }

    /// <summary>
    ///     Reads the confirmed enrollment of a user that must have one.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The enrollment.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.TotpNotEnrolled"/> when there is none.</exception>
    private async Task<UserTotp> RequireActiveAsync(User user, CancellationToken cancellationToken)
    {
        var row = await _store.GetAsync(user.Id, cancellationToken);
        return row is { IsActive: true }
            ? row
            : throw new DomainRuleException(DomainRule.TotpNotEnrolled, "No second factor is set up.");
    }

    /// <summary>
    ///     Requires a right code for a change, counting a wrong one toward the lockout.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="row">The confirmed enrollment of the user.</param>
    /// <param name="code">The typed code.</param>
    /// <param name="allowRecovery"><c>true</c> when a recovery code is accepted too.</param>
    /// <param name="recoveryDetail">The audit detail when a recovery code is spent.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the code is right.</returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.TotpCodeIncorrect"/> when the code is wrong or the account is locked out.</exception>
    private async Task RequireCodeAsync(
        User user,
        UserTotp row,
        string? code,
        bool allowRecovery,
        string recoveryDetail,
        CancellationToken cancellationToken)
    {
        if (await _users.IsLockedOutAsync(user.Username, cancellationToken))
        {
            throw new DomainRuleException(DomainRule.TotpCodeIncorrect, CodeIncorrectMessage);
        }

        if (await CheckCodeAsync(user, row, code, allowRecovery, recoveryDetail, cancellationToken) == CodeResult.Invalid)
        {
            await FailAsync(user, "wrong or reused code (account page)", cancellationToken);
        }
    }

    /// <summary>
    ///     Counts a wrong code and refuses it.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="detail">A fixed detail text; never the code.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that never completes normally.</returns>
    /// <exception cref="DomainRuleException">Always thrown, with <see cref="DomainRule.TotpCodeIncorrect"/>.</exception>
    private async Task FailAsync(User user, string detail, CancellationToken cancellationToken)
    {
        _ = await _users.RegisterSecondFactorFailureAsync(user.Username, FailedAction, detail, cancellationToken);
        throw new DomainRuleException(DomainRule.TotpCodeIncorrect, CodeIncorrectMessage);
    }

    /// <summary>
    ///     Checks a typed code against the enrollment and uses it up when it is right.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="row">The confirmed enrollment of the user.</param>
    /// <param name="code">The typed code: six digits for an authenticator code, 16 symbols for a recovery code.</param>
    /// <param name="allowRecovery"><c>true</c> when a recovery code is accepted.</param>
    /// <param name="recoveryDetail">The audit detail when a recovery code is spent.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    ///     What was accepted. Nothing is counted here: the caller decides what a refusal means. An authenticator code
    ///     is accepted only when the conditional update of the last accepted step succeeds for this caller.
    /// </returns>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.TotpSecretUnreadable"/> when the stored secret can not be decrypted.</exception>
    private async Task<CodeResult> CheckCodeAsync(
        User user,
        UserTotp row,
        string? code,
        bool allowRecovery,
        string recoveryDetail,
        CancellationToken cancellationToken)
    {
        var stripped = StripSeparators(code);
        if (IsSixDigits(stripped))
        {
            var secret = _protector.Unprotect(user.Id, row.ProtectedSecret);
            try
            {
                return _algorithm.TryVerify(secret, stripped, _timeProvider.GetUtcNow(), out var step)
                       && await _store.TryAdvanceStepAsync(user.Id, step, cancellationToken)
                    ? CodeResult.Totp
                    : CodeResult.Invalid;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        var normalized = allowRecovery ? NormalizeRecoveryCode(code) : null;
        if (normalized is null)
        {
            return CodeResult.Invalid;
        }

        var spent = await _store.TryConsumeRecoveryCodeAsync(
            user.Id,
            HashRecoveryCode(user.Id, normalized),
            _timeProvider.GetUtcNow(),
            AuditDraft.By(new Actor(user.Username, user.DisplayName), RecoveryUsedAction, recoveryDetail),
            cancellationToken);
        return spent ? CodeResult.Recovery : CodeResult.Invalid;
    }

    /// <summary>
    ///     Removes the spaces and hyphens a user may type into a code.
    /// </summary>
    /// <param name="code">The typed code; may be null.</param>
    /// <returns>The code without separators.</returns>
    private static string StripSeparators(string? code)
    {
        return (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Tells whether text is exactly six ASCII digits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><c>true</c> for an authenticator code.</returns>
    private static bool IsSixDigits(string text)
    {
        return text.Length == 6 && text.All(char.IsAsciiDigit);
    }

    /// <summary>
    ///     Brings a typed recovery code into its stored form.
    /// </summary>
    /// <param name="code">The typed code; may be null.</param>
    /// <returns>
    ///     The upper-case code without separators, with the look-alike letters O, I and L read as 0, 1 and 1;
    ///     <c>null</c> when it is not made of 16 symbols of the alphabet.
    /// </returns>
    private static string? NormalizeRecoveryCode(string? code)
    {
        var text = StripSeparators(code).ToUpperInvariant().Replace('O', '0').Replace('I', '1').Replace('L', '1');
        return text.Length == RecoveryCodeLength && text.All(symbol => RecoveryAlphabet.Contains(symbol, StringComparison.Ordinal))
            ? text
            : null;
    }

    /// <summary>
    ///     Generates a new set of recovery codes.
    /// </summary>
    /// <returns>The codes in their stored form (16 symbols).</returns>
    private static List<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>(RecoveryCodeCount);
        while (codes.Count < RecoveryCodeCount)
        {
            // 32 symbols and a mask of 5 bits: every symbol is equally likely
            var bytes = RandomNumberGenerator.GetBytes(RecoveryCodeLength);
            var code = new string(bytes.Select(value => RecoveryAlphabet[value & 31]).ToArray());
            if (!codes.Contains(code, StringComparer.Ordinal))
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>
    ///     Writes a recovery code for people: four groups of four symbols.
    /// </summary>
    /// <param name="code">The code in its stored form.</param>
    /// <returns>The code in the form <c>XXXX-XXXX-XXXX-XXXX</c>.</returns>
    private static string FormatRecoveryCode(string code)
    {
        return string.Join('-', Enumerable.Range(0, 4).Select(group => code.Substring(group * 4, 4)));
    }

    /// <summary>
    ///     Hashes a recovery code for storage and lookup.
    /// </summary>
    /// <param name="userId">The identity of the user the code belongs to; part of the hash, so equal codes of two users differ.</param>
    /// <param name="code">The code in its stored form.</param>
    /// <returns>The lowercase hexadecimal SHA-256 hash.</returns>
    /// <remarks>A fast hash is enough: the codes carry 80 random bits, so there is nothing to guess.</remarks>
    internal static string HashRecoveryCode(int userId, string code)
    {
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{RecoveryHashContext}{userId}:{code}")));
    }
}
