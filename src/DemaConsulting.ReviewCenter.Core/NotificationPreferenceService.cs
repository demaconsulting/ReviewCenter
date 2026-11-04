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
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     What a person sees of their notification address: the confirmed address and the address that waits for its code.
/// </summary>
/// <remarks>The record is immutable and thread-safe. The addresses are personal data of the person and are shown only to them.</remarks>
/// <param name="ConfirmedAddress">The confirmed address; <c>null</c> when there is none.</param>
/// <param name="PendingAddress">The address that waits for its code; <c>null</c> when none waits.</param>
/// <param name="PendingExpired">Whether the time of the code of the waiting address ran out.</param>
/// <param name="Mode">The notification mode the person chose; <see cref="NotificationMode.None"/> until one is chosen.</param>
public sealed record NotificationAddressStatus(
    string? ConfirmedAddress,
    string? PendingAddress,
    bool PendingExpired,
    NotificationMode Mode = NotificationMode.None);

/// <summary>
///     The rules of the e-mail address a person enters for notifications: entering it, confirming it with a code that
///     is mailed to it, and removing it.
/// </summary>
/// <remarks>
///     An address is never trusted because a person typed it: it is stored as pending, a six digit code is mailed to
///     it, and only when the person types the code back while signed in does it become the confirmed address. Mail
///     for notifications goes only to a confirmed address (<see cref="GetConfirmedAddressAsync"/>). The code is the
///     single place where the product mails a secret; it is useless without the signed-in session, valid for
///     <see cref="CodeLifetime"/>, limited to <see cref="MaxAttempts"/> attempts and stored only as a salted hash.
///     The server can not be used to mail strangers at scale: a user, a target address and the whole server each
///     have a limit (see <see cref="ConfirmationRequest"/>). Only signed-in people with a password account act: an
///     API token, a disabled account, an account that must replace its temporary password, and every caller while
///     the administrator has not set up a mail transport are refused with
///     <see cref="DomainRule.NotificationsNotAvailable"/>. The address and the code never appear in the audit trail
///     or in an exception message. The service holds no state and is thread-safe.
/// </remarks>
public sealed class NotificationPreferenceService
{
    /// <summary>
    ///     The audit action of a change of the pending address.
    /// </summary>
    private const string SettingsChangedAction = "notification.settings-changed";

    /// <summary>
    ///     The audit action of a confirmed address.
    /// </summary>
    private const string EmailConfirmedAction = "notification.email-confirmed";

    /// <summary>
    ///     The audit action of a removed address.
    /// </summary>
    private const string EmailRemovedAction = "notification.email-removed";

    /// <summary>
    ///     The longest accepted typed code in characters, so a huge input is never hashed.
    /// </summary>
    private const int MaxTypedCodeLength = 32;

    /// <summary>
    ///     The text that separates the parts of the hashed value, and versions the hash.
    /// </summary>
    private const string HashContext = "ReviewCenter.NotificationCode.v1";

    /// <summary>
    ///     How long a confirmation code is accepted.
    /// </summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    ///     How long a person must wait before another confirmation code is mailed.
    /// </summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The number of attempts to type the code of one waiting address.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    ///     The most confirmation mails one user receives in 24 hours.
    /// </summary>
    public const int MaxConfirmationsPerUserPerDay = 3;

    /// <summary>
    ///     The most confirmation mails one address receives in 24 hours, over all users.
    /// </summary>
    public const int MaxConfirmationsPerAddressPerDay = 3;

    /// <summary>
    ///     The store of the addresses and of the sending log.
    /// </summary>
    private readonly INotificationStore _store;

    /// <summary>
    ///     The transport of the confirmation mail.
    /// </summary>
    private readonly INotificationSender _sender;

    /// <summary>
    ///     The user service, used to find the signed-in account.
    /// </summary>
    private readonly UserService _users;

    /// <summary>
    ///     The settings of e-mail notifications.
    /// </summary>
    private readonly NotificationSettings _settings;

    /// <summary>
    ///     The clock.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="NotificationPreferenceService"/> class.
    /// </summary>
    /// <param name="store">The store of the addresses. Must not be null.</param>
    /// <param name="sender">The transport; <see cref="NoNotificationSender"/> while none is configured. Must not be null.</param>
    /// <param name="users">The user service. Must not be null.</param>
    /// <param name="settings">The settings. Must not be null.</param>
    /// <param name="clock">The clock. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public NotificationPreferenceService(
        INotificationStore store,
        INotificationSender sender,
        UserService users,
        NotificationSettings settings,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        _store = store;
        _sender = sender;
        _users = users;
        _settings = settings;
        _clock = clock;
    }

    /// <summary>
    ///     Gets a value indicating whether e-mail notifications are set up: switched on and a transport is configured.
    /// </summary>
    /// <remarks>Reading it touches no table, so a page can ask it while the feature is off.</remarks>
    public bool IsAvailable => _settings.Enabled && _sender.IsConfigured;

    /// <summary>
    ///     Gets the address status of the signed-in person.
    /// </summary>
    /// <param name="actor">The signed-in person. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The confirmed and the waiting address; both <c>null</c> when none was entered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.NotificationsNotAvailable"/> or <see cref="DomainRule.ActorNotAllowed"/>.</exception>
    public async Task<NotificationAddressStatus> GetStatusAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        var row = await _store.GetAsync(user.Id, cancellationToken);
        if (row is null)
        {
            return new NotificationAddressStatus(null, null, false);
        }

        var expired = row.PendingEmail is not null && _clock.GetUtcNow() >= row.PendingCodeExpiresUtc;
        return new NotificationAddressStatus(row.Email, row.PendingEmail, expired, row.Mode);
    }

    /// <summary>
    ///     Sets how the signed-in person wants to be told about news in the inbox.
    /// </summary>
    /// <param name="actor">The signed-in person. Must not be null.</param>
    /// <param name="mode">The mode to set.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when the mode changed; <c>false</c> when it already was the given mode.</returns>
    /// <remarks>
    ///     A mode other than <see cref="NotificationMode.None"/> needs a confirmed address. Switching on from
    ///     <see cref="NotificationMode.None"/> marks the items that exist now as already known, so the person is
    ///     not mailed about the backlog; switching to <see cref="NotificationMode.None"/> forgets what was mailed.
    ///     The change is audited without the address.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode"/> is not a defined value.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.NotificationsNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/> or
    ///     <see cref="DomainRule.NotificationAddressNotConfirmed"/>.
    /// </exception>
    public async Task<bool> SetModeAsync(Actor actor, NotificationMode mode, CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "The notification mode is not known.");
        }

        var row = await _store.GetAsync(user.Id, cancellationToken);
        if (mode != NotificationMode.None && row?.Email is null)
        {
            throw new DomainRuleException(
                DomainRule.NotificationAddressNotConfirmed,
                "Confirm an e-mail address before you choose how to be notified.");
        }

        // Without a row the mode is None already
        if (row is null || row.Mode == mode)
        {
            return false;
        }

        var audit = AuditDraft.By(actor, SettingsChangedAction, $"Notification mode set to {mode}.");
        return await _store.SetModeAsync(user.Id, mode, _clock.GetUtcNow(), audit, cancellationToken);
    }

    /// <summary>
    ///     Stores an address as pending and mails a confirmation code to it.
    /// </summary>
    /// <param name="actor">The signed-in person. Must not be null.</param>
    /// <param name="address">The address the person typed; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the mail was handed to the transport.</returns>
    /// <remarks>
    ///     Asking again for the same address mails a new code (the old one stops working). The confirmed address, if
    ///     any, stays in use until the new one is confirmed. When the mail can not be sent, or the send is canceled,
    ///     nothing is stored and the mail is not counted against the limits.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.NotificationsNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.NotificationAddressInvalid"/>, <see cref="DomainRule.NotificationResendTooSoon"/>,
    ///     <see cref="DomainRule.NotificationLimitReached"/> or <see cref="DomainRule.NotificationSendFailed"/>.
    /// </exception>
    public async Task RequestConfirmationAsync(Actor actor, string? address, CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);

        // The address must be exactly one plain address before anything is stored or mailed
        if (!NotificationAddress.TryNormalize(address, out var normalized))
        {
            throw new DomainRuleException(
                DomainRule.NotificationAddressInvalid,
                "Enter one valid e-mail address, for example name@example.com.");
        }

        // A fresh random code; only its salted hash is stored
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var hash = HashCode(salt, user.Id, code);
        var now = _clock.GetUtcNow();

        // The limits and the pending state are decided together in the store, so two requests can not both pass
        var request = new ConfirmationRequest(
            user.Id,
            normalized,
            hash,
            salt,
            now,
            now + CodeLifetime,
            ResendCooldown,
            MaxConfirmationsPerUserPerDay,
            MaxConfirmationsPerAddressPerDay,
            _settings.MaxMailsPerHour);
        var audit = AuditDraft.By(actor, SettingsChangedAction, "Notification e-mail address entered; confirmation pending.");
        var (outcome, logId) = await _store.ReserveConfirmationAsync(request, audit, cancellationToken);
        if (outcome != ConfirmationReservation.Reserved)
        {
            throw RefusedBy(outcome);
        }

        // Send after the reservation; a failure takes the reservation back so it costs the person nothing
        try
        {
            await _sender.SendAsync(BuildConfirmationMessage(normalized, code), cancellationToken);
        }
        catch (NotificationSendException)
        {
            await _store.AbortConfirmationAsync(user.Id, logId, hash, CancellationToken.None);
            throw new DomainRuleException(
                DomainRule.NotificationSendFailed,
                "The confirmation e-mail could not be sent. Try again later or ask your administrator.");
        }
        catch (Exception)
        {
            // A cancellation or an unexpected failure also means that no mail was sent: take the reservation back,
            // then let the original exception go on
            await _store.AbortConfirmationAsync(user.Id, logId, hash, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    ///     Confirms the waiting address with the code that was mailed to it.
    /// </summary>
    /// <param name="actor">The signed-in person. Must not be null.</param>
    /// <param name="code">The code the person typed; spaces are ignored; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the address is confirmed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.NotificationsNotAvailable"/>, <see cref="DomainRule.ActorNotAllowed"/>,
    ///     <see cref="DomainRule.NotificationNothingToConfirm"/>, <see cref="DomainRule.NotificationCodeExpired"/>,
    ///     <see cref="DomainRule.NotificationTooManyAttempts"/> or <see cref="DomainRule.NotificationCodeIncorrect"/>.
    /// </exception>
    public async Task ConfirmAsync(Actor actor, string? code, CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        var row = await _store.GetAsync(user.Id, cancellationToken);
        if (row?.PendingEmail is null || row.PendingCodeHash is null || row.PendingCodeSalt is null)
        {
            throw new DomainRuleException(
                DomainRule.NotificationNothingToConfirm,
                "No e-mail address is waiting for a code. Enter your address first.");
        }

        var now = _clock.GetUtcNow();
        if (now >= row.PendingCodeExpiresUtc)
        {
            throw new DomainRuleException(
                DomainRule.NotificationCodeExpired,
                "The code is no longer valid. Request a new code.");
        }

        // Every attempt is counted before it is judged, so wrong guesses can not be repeated without limit
        if (!await _store.TryCountAttemptAsync(user.Id, row.PendingCodeHash, MaxAttempts, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.NotificationTooManyAttempts,
                "Too many wrong codes were typed. Request a new code.");
        }

        var typed = new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).Take(MaxTypedCodeLength).ToArray());
        var candidate = Encoding.ASCII.GetBytes(HashCode(row.PendingCodeSalt, user.Id, typed));
        var expected = Encoding.ASCII.GetBytes(row.PendingCodeHash);
        if (!CryptographicOperations.FixedTimeEquals(candidate, expected))
        {
            throw new DomainRuleException(DomainRule.NotificationCodeIncorrect, "The code is not correct.");
        }

        // Only one request can turn the waiting address into the confirmed one
        var audit = AuditDraft.By(actor, EmailConfirmedAction, "Notification e-mail address confirmed.");
        if (!await _store.ConfirmAsync(user.Id, row.PendingCodeHash, now, audit, cancellationToken))
        {
            throw new DomainRuleException(
                DomainRule.NotificationNothingToConfirm,
                "No e-mail address is waiting for a code. Enter your address first.");
        }
    }

    /// <summary>
    ///     Removes the confirmed and the waiting address of the signed-in person.
    /// </summary>
    /// <param name="actor">The signed-in person. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><c>true</c> when an address was removed; <c>false</c> when the person had none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.NotificationsNotAvailable"/> or <see cref="DomainRule.ActorNotAllowed"/>.</exception>
    public async Task<bool> RemoveAddressAsync(Actor actor, CancellationToken cancellationToken = default)
    {
        var user = await LoadUsableAsync(actor, cancellationToken);
        var audit = AuditDraft.By(actor, EmailRemovedAction, "Notification e-mail address removed.");
        return await _store.RemoveAsync(user.Id, audit, cancellationToken);
    }

    /// <summary>
    ///     Gets the address a notification may be mailed to: the confirmed address, never a waiting one.
    /// </summary>
    /// <param name="userId">The identity of the recipient.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The confirmed address; <c>null</c> when there is none or e-mail notifications are not available.</returns>
    public async Task<string?> GetConfirmedAddressAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var row = await _store.GetAsync(userId, cancellationToken);
        return row?.Email;
    }

    /// <summary>
    ///     Loads the signed-in person and checks that notifications are available to the account.
    /// </summary>
    /// <param name="actor">The signed-in person. Must not be null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The user as stored now.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="actor"/> is null.</exception>
    /// <exception cref="DomainRuleException">Thrown with <see cref="DomainRule.NotificationsNotAvailable"/> or <see cref="DomainRule.ActorNotAllowed"/>.</exception>
    private async Task<User> LoadUsableAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // An API token is a stored credential and never a person who can read a mailbox
        if (actor.ViaApiToken || !IsAvailable)
        {
            throw NotAvailable();
        }

        var user = await _users.GetAsync(actor.Username, cancellationToken);
        if (user is null || !user.Enabled)
        {
            throw new DomainRuleException(DomainRule.ActorNotAllowed, "The account can not be used.");
        }

        // A temporary password must be replaced before anything else is set up
        return user.MustChangePassword ? throw NotAvailable() : user;
    }

    /// <summary>
    ///     Builds the exception for a notification feature that is not available.
    /// </summary>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException NotAvailable()
    {
        return new DomainRuleException(
            DomainRule.NotificationsNotAvailable,
            "E-mail notifications are not available for this account.");
    }

    /// <summary>
    ///     Builds the exception for a reservation that was refused.
    /// </summary>
    /// <param name="outcome">The refusal.</param>
    /// <returns>The exception to throw.</returns>
    private static DomainRuleException RefusedBy(ConfirmationReservation outcome)
    {
        return outcome switch
        {
            ConfirmationReservation.TooSoon => new DomainRuleException(
                DomainRule.NotificationResendTooSoon,
                "A code was sent a moment ago. Wait a minute before asking for another."),
            ConfirmationReservation.UserLimitReached => new DomainRuleException(
                DomainRule.NotificationLimitReached,
                "You asked for too many codes today. Try again tomorrow."),
            ConfirmationReservation.AddressLimitReached => new DomainRuleException(
                DomainRule.NotificationLimitReached,
                "Too many codes were sent to that address today. Try again tomorrow or use another address."),
            _ => new DomainRuleException(
                DomainRule.NotificationLimitReached,
                "The server has reached its limit of e-mails for this hour. Try again later.")
        };
    }

    /// <summary>
    ///     Builds the confirmation mail: short fixed text with the name of the installation and the code, and no link.
    /// </summary>
    /// <param name="address">The normalized address to mail.</param>
    /// <param name="code">The code.</param>
    /// <returns>The message.</returns>
    private NotificationMessage BuildConfirmationMessage(string address, string code)
    {
        // The name comes from the operator's settings; control characters are dropped so it can not break the subject
        var name = new string(_settings.InstanceName.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = NotificationSettings.DefaultInstanceName;
        }

        var minutes = (int)CodeLifetime.TotalMinutes;
        var text =
            $"Your {name} confirmation code is {code}.\n\n" +
            $"Type it on your Account page while you are signed in to {name}. It is valid for {minutes} minutes.\n\n" +
            "If you did not ask for this, ignore this message.\n";
        var encodedName = WebUtility.HtmlEncode(name);
        var html =
            $"<p>Your {encodedName} confirmation code is <strong>{code}</strong>.</p>" +
            $"<p>Type it on your Account page while you are signed in to {encodedName}. It is valid for {minutes} minutes.</p>" +
            "<p>If you did not ask for this, ignore this message.</p>";
        return new NotificationMessage(address, $"{name}: your confirmation code", text, html);
    }

    /// <summary>
    ///     Computes the salted hash of a confirmation code.
    /// </summary>
    /// <param name="salt">The salt.</param>
    /// <param name="userId">The user the code belongs to, so a hash is useless for another user.</param>
    /// <param name="code">The code.</param>
    /// <returns>The hash as lower case hexadecimal text.</returns>
    private static string HashCode(string salt, int userId, string code)
    {
        var value = $"{HashContext}:{salt}:{userId.ToString(CultureInfo.InvariantCulture)}:{code}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
