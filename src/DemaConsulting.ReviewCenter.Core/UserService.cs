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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Creates users, checks passwords and manages accounts, enforcing the user rules.
/// </summary>
/// <remarks>
///     The service is the single place where user rules live: the user name and display name rules, the
///     password policy (12 to 128 characters, no composition rules, not trimmed), the lockout (5 consecutive
///     failures lock the account for 5 minutes) and the guarantee that at least one enabled administrator
///     remains. <see cref="AuthenticateAsync"/> answers every failure with the same <c>null</c> (unknown user,
///     wrong password, disabled and locked out), so a caller cannot tell the cases apart. Operations that change
///     an existing user run one at a time (a semaphore), which makes the last-administrator check and the
///     failure counter race-free; the service must therefore be a single instance (a singleton).
///     Passwords and hashes are never logged or put into messages.
/// </remarks>
public sealed class UserService
{
    /// <summary>
    ///     The number of consecutive failed password checks that locks an account.
    /// </summary>
    private const int MaxFailedAttempts = 5;

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
    ///     A hash of no account, verified against when the user is unknown so the response time is similar;
    ///     created on first use.
    /// </summary>
    private string? _dummyHash;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UserService"/> class.
    /// </summary>
    /// <param name="store">The store that persists users. Must not be null.</param>
    /// <param name="hasher">The password hasher. Must not be null.</param>
    /// <param name="timeProvider">The clock; injected so tests control time. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public UserService(IUserStore store, IPasswordHasher hasher, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _hasher = hasher;
        _timeProvider = timeProvider;
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
    /// <param name="role">The role of the user.</param>
    /// <param name="password">
    ///     The initial password: <see cref="ReviewLimits.MinPasswordLength"/> to
    ///     <see cref="ReviewLimits.MaxPasswordLength"/> characters, taken as it is (not trimmed).
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored user; only the hash of the password is kept.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">Thrown when a rule is violated (see <see cref="DomainRule"/>); nothing is stored then.</exception>
    public async Task<User> CreateAsync(
        string? username,
        string? displayName,
        UserRole role,
        string? password,
        CancellationToken cancellationToken = default)
    {
        // Validate everything that needs no I/O first so a bad request fails fast and cheaply
        ValidateUsername(username);
        var trimmedDisplayName = ValidateDisplayName(displayName);
        ValidatePassword(password);
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "The role is not a known value.");
        }

        // The store enforces the unique user name, which also covers two requests racing
        return await _store.AddAsync(
            new User
            {
                Username = username!,
                DisplayName = trimmedDisplayName,
                Role = role,
                Enabled = true,
                PasswordHash = _hasher.Hash(password!),
                Created = _timeProvider.GetUtcNow()
            },
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
    ///     account is locked, attempts are refused without counting, even with the right password.
    /// </returns>
    public async Task<User?> AuthenticateAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // An unknown or locked-out user still costs one verification, so the response time does not
            // reveal the account
            var user = await _store.GetByUsernameAsync(username ?? string.Empty, cancellationToken);
            if (user is null)
            {
                VerifyDummy(password);
                return null;
            }

            // A locked account refuses every attempt, without counting it
            if (IsLockedOut(user))
            {
                VerifyDummy(password);
                return null;
            }

            if (!_hasher.Verify(user.PasswordHash, password ?? string.Empty))
            {
                await RegisterFailureAsync(user, cancellationToken);
                return null;
            }

            // A disabled account never signs in, however right the password is
            if (!user.Enabled)
            {
                return null;
            }

            await ResetFailuresAsync(user, cancellationToken);
            return user;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Enables or disables a user.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="enabled"><c>true</c> to let the user sign in; <c>false</c> to stop it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.UserNotFound"/> when there is no such user, or with
    ///     <see cref="DomainRule.LastAdmin"/> when the change would disable the last enabled administrator.
    /// </exception>
    public async Task SetEnabledAsync(string username, bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (!enabled)
            {
                await EnsureNotLastAdminAsync(user, cancellationToken);
            }

            user.Enabled = enabled;
            await _store.UpdateAsync(user, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Changes the role of a user.
    /// </summary>
    /// <param name="username">The user name; compared without regard to case.</param>
    /// <param name="role">The new role.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.UserNotFound"/> when there is no such user, or with
    ///     <see cref="DomainRule.LastAdmin"/> when the change would demote the last enabled administrator.
    /// </exception>
    public async Task SetRoleAsync(string username, UserRole role, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "The role is not a known value.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (role != UserRole.Admin)
            {
                await EnsureNotLastAdminAsync(user, cancellationToken);
            }

            user.Role = role;
            await _store.UpdateAsync(user, cancellationToken);
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
    ///     passwords), and a locked-out account is refused like a wrong password.
    /// </remarks>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.PasswordInvalid"/> when the new password breaks the policy,
    ///     <see cref="DomainRule.UserNotFound"/> when there is no such user, or
    ///     <see cref="DomainRule.CurrentPasswordIncorrect"/> when the current password is wrong or the account is locked out.
    /// </exception>
    public async Task ChangeOwnPasswordAsync(
        string username,
        string? currentPassword,
        string? newPassword,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            if (IsLockedOut(user))
            {
                throw CurrentPasswordIncorrect();
            }

            if (!_hasher.Verify(user.PasswordHash, currentPassword ?? string.Empty))
            {
                await RegisterFailureAsync(user, cancellationToken);
                throw CurrentPasswordIncorrect();
            }

            user.PasswordHash = _hasher.Hash(newPassword!);
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            await _store.UpdateAsync(user, cancellationToken);
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
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the new hash is stored; a lockout of the user ends with it.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.PasswordInvalid"/> when the password breaks the policy, or with
    ///     <see cref="DomainRule.UserNotFound"/> when there is no such user.
    /// </exception>
    public async Task SetPasswordAsync(string username, string? newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var user = await LoadAsync(username, cancellationToken);
            user.PasswordHash = _hasher.Hash(newPassword!);
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
            await _store.UpdateAsync(user, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
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
        // Only an enabled administrator counts; anyone else can be changed freely
        if (user.Role != UserRole.Admin || !user.Enabled)
        {
            return;
        }

        var others = (await _store.ListAsync(cancellationToken))
            .Count(other => other.Id != user.Id && other.Role == UserRole.Admin && other.Enabled);
        if (others == 0)
        {
            throw new DomainRuleException(
                DomainRule.LastAdmin,
                "At least one enabled administrator must remain.");
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
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    private async Task RegisterFailureAsync(User user, CancellationToken cancellationToken)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= MaxFailedAttempts)
        {
            // The counter restarts after the lockout so the next window gets a fresh set of attempts
            user.LockoutUntil = _timeProvider.GetUtcNow() + LockoutDuration;
            user.FailedLoginCount = 0;
        }

        await _store.UpdateAsync(user, cancellationToken);
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
        await _store.UpdateAsync(user, cancellationToken);
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
    private static void ValidateUsername(string? username)
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
    private static string ValidateDisplayName(string? displayName)
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
