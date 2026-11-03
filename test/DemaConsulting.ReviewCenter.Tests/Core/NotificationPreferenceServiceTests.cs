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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="NotificationPreferenceService"/> over in-memory fakes: the confirmation of the
///     address with a mailed code, the limits, the audit entries and the rule that only a confirmed address is used.
/// </summary>
public class NotificationPreferenceServiceTests
{
    /// <summary>
    ///     A password that satisfies the password policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The address the tests confirm.
    /// </summary>
    private const string AnnAddress = "ann@example.com";

    /// <summary>
    ///     The actor of the user the tests create.
    /// </summary>
    private static readonly Actor Ann = new("ann", "Ann");

    /// <summary>
    ///     Test that a request mails a 6-digit code to the normalized address and stores it as pending only.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_ValidAddress_MailsCodeAndStoresPending()
    {
        // Arrange: a user and the service with notifications on
        var world = await World.CreateAsync();

        // Act: request a confirmation for a capitalized address
        await world.Service.RequestConfirmationAsync(Ann, "Ann@Example.com", world.Token);

        // Assert: one mail to the lower-case address with a 6-digit code, and the address is pending, not confirmed
        var mail = Assert.Single(world.Sender.Sent);
        Assert.Equal(AnnAddress, mail.ToAddress);
        Assert.Matches("^[0-9]{6}$", world.Sender.LastCode());
        var status = await world.Service.GetStatusAsync(Ann, world.Token);
        Assert.Null(status.ConfirmedAddress);
        Assert.Equal(AnnAddress, status.PendingAddress);
        Assert.False(status.PendingExpired);
    }

    /// <summary>
    ///     Test that the confirmation mail carries the installation name, the validity, and no link.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_Mail_HasNameValidityAndNoLink()
    {
        // Arrange: a user
        var world = await World.CreateAsync();

        // Act: request a confirmation
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);

        // Assert: subject and bodies
        var mail = world.Sender.Sent[0];
        Assert.Equal("Test Center: your confirmation code", mail.Subject);
        Assert.Contains("30 minutes", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("ignore this message", mail.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("http", mail.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", mail.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that an installation name with markup is encoded in the HTML part.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_NameWithMarkup_IsEncodedInHtml()
    {
        // Arrange: a name with markup
        var world = await World.CreateAsync(new NotificationSettings(true, "A<b>&Co", 200));

        // Act: request a confirmation
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);

        // Assert: the HTML part holds the encoded name only
        var mail = world.Sender.Sent[0];
        Assert.Contains("A&lt;b&gt;&amp;Co", mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", mail.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the right code confirms the address.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Confirm_RightCode_ConfirmsAddress()
    {
        // Arrange: a pending address
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);

        // Act: type the code, with spaces around it as a paste might have
        await world.Service.ConfirmAsync(Ann, $" {world.Sender.LastCode()} ", world.Token);

        // Assert: confirmed, nothing pending, and usable as a recipient
        var status = await world.Service.GetStatusAsync(Ann, world.Token);
        Assert.Equal(AnnAddress, status.ConfirmedAddress);
        Assert.Null(status.PendingAddress);
        var user = await world.Users.GetAsync("ann", world.Token);
        Assert.Equal(AnnAddress, await world.Service.GetConfirmedAddressAsync(user!.Id, world.Token));
    }

    /// <summary>
    ///     Test that a wrong code is refused and the address stays pending.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Confirm_WrongCode_ThrowsAndStaysPending()
    {
        // Arrange: a pending address
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var wrong = world.Sender.LastCode() == "000000" ? "000001" : "000000";

        // Act: type a wrong code
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ConfirmAsync(Ann, wrong, world.Token));

        // Assert: refused, still pending, no confirmed address
        Assert.Equal(DomainRule.NotificationCodeIncorrect, exception.Rule);
        var status = await world.Service.GetStatusAsync(Ann, world.Token);
        Assert.Null(status.ConfirmedAddress);
        Assert.Equal(AnnAddress, status.PendingAddress);
    }

    /// <summary>
    ///     Test that five wrong attempts use up the code, even when the sixth attempt is right.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Confirm_AfterMaxWrongAttempts_RefusesTheRightCode()
    {
        // Arrange: a pending address and five wrong attempts
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var right = world.Sender.LastCode();
        var wrong = right == "000000" ? "000001" : "000000";
        for (var attempt = 0; attempt < NotificationPreferenceService.MaxAttempts; attempt++)
        {
            await Assert.ThrowsAsync<DomainRuleException>(() => world.Service.ConfirmAsync(Ann, wrong, world.Token));
        }

        // Act: type the right code
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ConfirmAsync(Ann, right, world.Token));

        // Assert: refused because no attempt is left
        Assert.Equal(DomainRule.NotificationTooManyAttempts, exception.Rule);
        Assert.Null((await world.Service.GetStatusAsync(Ann, world.Token)).ConfirmedAddress);
    }

    /// <summary>
    ///     Test that a code stops working after its lifetime.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Confirm_AfterLifetime_ThrowsExpired()
    {
        // Arrange: a pending address whose code has expired
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        world.Clock.Advance(NotificationPreferenceService.CodeLifetime + TimeSpan.FromSeconds(1));

        // Act: type the right code
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ConfirmAsync(Ann, world.Sender.LastCode(), world.Token));

        // Assert: expired, and the status says so
        Assert.Equal(DomainRule.NotificationCodeExpired, exception.Rule);
        Assert.True((await world.Service.GetStatusAsync(Ann, world.Token)).PendingExpired);
    }

    /// <summary>
    ///     Test that confirming without a waiting address is refused.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Confirm_NothingPending_Throws()
    {
        // Arrange: a user who entered no address
        var world = await World.CreateAsync();

        // Act: type a code
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ConfirmAsync(Ann, "123456", world.Token));

        // Assert: nothing to confirm
        Assert.Equal(DomainRule.NotificationNothingToConfirm, exception.Rule);
    }

    /// <summary>
    ///     Test that a code can not confirm twice.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Confirm_SameCodeTwice_SecondIsRefused()
    {
        // Arrange: a confirmed address
        var world = await World.CreateAsync();
        await world.ConfirmAsync(AnnAddress);

        // Act: type the used code again
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ConfirmAsync(Ann, world.Sender.LastCode(), world.Token));

        // Assert: refused, the address stays confirmed
        Assert.Equal(DomainRule.NotificationNothingToConfirm, exception.Rule);
        Assert.Equal(AnnAddress, (await world.Service.GetStatusAsync(Ann, world.Token)).ConfirmedAddress);
    }

    /// <summary>
    ///     Test that an invalid address is refused and nothing is mailed or stored.
    /// </summary>
    [Theory]
    [InlineData("not an address")]
    [InlineData("ann@example.com\r\nBcc: eve@example.org")]
    [InlineData("")]
    public async Task NotificationPreferenceService_RequestConfirmation_InvalidAddress_ThrowsAndSendsNothing(string text)
    {
        // Arrange: a user
        var world = await World.CreateAsync();

        // Act: request a confirmation
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, text, world.Token));

        // Assert: refused, nothing mailed, nothing stored, nothing counted
        Assert.Equal(DomainRule.NotificationAddressInvalid, exception.Rule);
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Store.Log);
        Assert.Null((await world.Service.GetStatusAsync(Ann, world.Token)).PendingAddress);
    }

    /// <summary>
    ///     Test that asking again within the cooldown is refused.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_WithinCooldown_ThrowsTooSoon()
    {
        // Arrange: a code was just sent
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        world.Clock.Advance(TimeSpan.FromSeconds(30));

        // Act: ask again
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));

        // Assert: refused, one mail only
        Assert.Equal(DomainRule.NotificationResendTooSoon, exception.Rule);
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that a new request after the cooldown replaces the code, so only the newest works.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_AfterCooldown_OldCodeStopsWorking()
    {
        // Arrange: a first code, and a second one after the cooldown
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var first = world.Sender.LastCode();
        world.Clock.Advance(NotificationPreferenceService.ResendCooldown + TimeSpan.FromSeconds(1));
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var second = world.Sender.LastCode();

        // Act: type the old code, unless the two codes happen to be equal
        if (first != second)
        {
            var exception = await Assert.ThrowsAsync<DomainRuleException>(
                () => world.Service.ConfirmAsync(Ann, first, world.Token));
            Assert.Equal(DomainRule.NotificationCodeIncorrect, exception.Rule);
        }

        // Assert: the new code confirms
        await world.Service.ConfirmAsync(Ann, second, world.Token);
        Assert.Equal(AnnAddress, (await world.Service.GetStatusAsync(Ann, world.Token)).ConfirmedAddress);
    }

    /// <summary>
    ///     Test that a user gets at most the allowed number of confirmation mails per day.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_UserDailyLimit_ThrowsLimitReached()
    {
        // Arrange: the allowed number of mails to different addresses, each after the cooldown
        var world = await World.CreateAsync();
        for (var index = 0; index < NotificationPreferenceService.MaxConfirmationsPerUserPerDay; index++)
        {
            await world.Service.RequestConfirmationAsync(Ann, $"ann{index}@example.com", world.Token);
            world.Clock.Advance(NotificationPreferenceService.ResendCooldown + TimeSpan.FromSeconds(1));
        }

        // Act: ask once more
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, "another@example.com", world.Token));

        // Assert: refused; the day after it works again
        Assert.Equal(DomainRule.NotificationLimitReached, exception.Rule);
        world.Clock.Advance(TimeSpan.FromHours(24));
        await world.Service.RequestConfirmationAsync(Ann, "another@example.com", world.Token);
    }

    /// <summary>
    ///     Test that one address gets at most the allowed number of confirmation mails per day over all users.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_AddressDailyLimit_ThrowsLimitReached()
    {
        // Arrange: the address already received the allowed number of mails
        var world = await World.CreateAsync();
        await world.AddUserAsync("bob");
        var bobId = (await world.Users.GetAsync("bob", world.Token))!.Id;
        for (var index = 0; index < NotificationPreferenceService.MaxConfirmationsPerAddressPerDay; index++)
        {
            world.Store.AddLog(bobId, "shared@example.com", world.Clock.GetUtcNow().AddMinutes(-index));
        }

        // Act: another user asks for the same address
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, "Shared@Example.com", world.Token));

        // Assert: refused and nothing mailed
        Assert.Equal(DomainRule.NotificationLimitReached, exception.Rule);
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that the server-wide hourly limit stops further mails.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_ServerHourlyLimit_ThrowsLimitReached()
    {
        // Arrange: the server already sent its mails of this hour
        var world = await World.CreateAsync(new NotificationSettings(true, "Test Center", 2));
        world.Store.AddLog(9001, "x@example.com", world.Clock.GetUtcNow().AddMinutes(-5));
        world.Store.AddLog(9002, "y@example.com", world.Clock.GetUtcNow().AddMinutes(-4));

        // Act: ask for a confirmation
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));

        // Assert: refused
        Assert.Equal(DomainRule.NotificationLimitReached, exception.Rule);
    }

    /// <summary>
    ///     Test that a failing transport leaves nothing pending, counts nothing and reports a clear refusal.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_SendFails_RollsBackAndThrows()
    {
        // Arrange: a transport that fails
        var world = await World.CreateAsync();
        world.Sender.Fail = true;

        // Act: request a confirmation
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));

        // Assert: failure reported, no pending address, no counted mail, and a retry is not "too soon"
        Assert.Equal(DomainRule.NotificationSendFailed, exception.Rule);
        Assert.Empty(world.Store.Log);
        Assert.Null((await world.Service.GetStatusAsync(Ann, world.Token)).PendingAddress);
        world.Sender.Fail = false;
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that a canceled or unexpectedly failing send takes the reservation back and passes the exception on.
    /// </summary>
    /// <param name="failure">The exception the sender throws.</param>
    [Theory]
    [MemberData(nameof(SendFailures))]
    public async Task NotificationPreferenceService_RequestConfirmation_CanceledOrUnexpectedSendFailure_RollsBackAndRethrows(
        Exception failure)
    {
        // Arrange: a transport that throws a cancellation or an unexpected exception
        var world = await World.CreateAsync();
        world.Sender.FailWith = failure;

        // Act: request a confirmation
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));

        // Assert: the same exception, no pending address, no counted mail, and a retry is not "too soon"
        Assert.Same(failure, exception);
        Assert.Empty(world.Store.Log);
        Assert.Null((await world.Service.GetStatusAsync(Ann, world.Token)).PendingAddress);
        world.Sender.FailWith = null;
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        Assert.Single(world.Sender.Sent);
    }

    /// <summary>
    ///     Gets the exceptions a sender may throw besides <see cref="NotificationSendException"/>.
    /// </summary>
    /// <returns>The test rows.</returns>
    public static TheoryData<Exception> SendFailures() =>
        new()
        {
            new OperationCanceledException(),
            new InvalidOperationException("The fake sender broke."),
        };

    /// <summary>
    ///     Test that the confirmed address stays in use while a new address waits for its code.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_NewAddress_KeepsConfirmedAddressUntilConfirmed()
    {
        // Arrange: a confirmed address, then a request for another one after the cooldown
        var world = await World.CreateAsync();
        await world.ConfirmAsync(AnnAddress);
        world.Clock.Advance(NotificationPreferenceService.ResendCooldown + TimeSpan.FromSeconds(1));

        // Act: ask for a different address
        await world.Service.RequestConfirmationAsync(Ann, "ann.new@example.com", world.Token);

        // Assert: the old address is still the confirmed one, the new one waits
        var user = await world.Users.GetAsync("ann", world.Token);
        Assert.Equal(AnnAddress, await world.Service.GetConfirmedAddressAsync(user!.Id, world.Token));
        Assert.Equal("ann.new@example.com", (await world.Service.GetStatusAsync(Ann, world.Token)).PendingAddress);

        // Act and Assert: confirming replaces the address
        await world.Service.ConfirmAsync(Ann, world.Sender.LastCode(), world.Token);
        Assert.Equal("ann.new@example.com", await world.Service.GetConfirmedAddressAsync(user.Id, world.Token));
    }

    /// <summary>
    ///     Test that only a confirmed address is returned for mailing, never a waiting one.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_GetConfirmedAddress_OnlyPending_ReturnsNull()
    {
        // Arrange: an address that waits for its code
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var user = await world.Users.GetAsync("ann", world.Token);

        // Act: ask for the address to mail
        var address = await world.Service.GetConfirmedAddressAsync(user!.Id, world.Token);

        // Assert: none
        Assert.Null(address);
    }

    /// <summary>
    ///     Test that removing deletes the confirmed and the waiting address.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RemoveAddress_RemovesAddressAndReportsIt()
    {
        // Arrange: a confirmed address
        var world = await World.CreateAsync();
        await world.ConfirmAsync(AnnAddress);

        // Act: remove, twice
        var first = await world.Service.RemoveAddressAsync(Ann, world.Token);
        var second = await world.Service.RemoveAddressAsync(Ann, world.Token);

        // Assert: the first removed something, the second found nothing, and no address is left
        Assert.True(first);
        Assert.False(second);
        var status = await world.Service.GetStatusAsync(Ann, world.Token);
        Assert.Null(status.ConfirmedAddress);
        Assert.Null(status.PendingAddress);
    }

    /// <summary>
    ///     Test that the audit trail records each change without the address or the code.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Changes_AreAuditedWithoutAddressOrCode()
    {
        // Arrange and Act: enter, confirm and remove an address
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var code = world.Sender.LastCode();
        await world.Service.ConfirmAsync(Ann, code, world.Token);
        await world.Service.RemoveAddressAsync(Ann, world.Token);

        // Assert: three entries with the notification actions, and neither the address nor the code in any of them
        Assert.Equal(
            ["notification.settings-changed", "notification.email-confirmed", "notification.email-removed"],
            world.Store.Audits.Select(a => a.Action).ToArray());
        Assert.All(world.Store.Audits, audit => Assert.Equal("ann", audit.ActorUsername));
        var text = string.Join('\n', world.Store.Audits.Select(a => $"{a.Action}|{a.Detail}|{a.ActorUsername}"));
        Assert.DoesNotContain("example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain(code, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the code is stored only as a salted hash.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RequestConfirmation_StoresOnlyASaltedHashOfTheCode()
    {
        // Arrange and Act: request a code
        var world = await World.CreateAsync();
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var user = await world.Users.GetAsync("ann", world.Token);
        var row = await world.Store.GetAsync(user!.Id, world.Token);

        // Assert: a 64-digit hexadecimal hash and a salt, and the code appears in neither
        Assert.Matches("^[0-9a-f]{64}$", row!.PendingCodeHash!);
        Assert.False(string.IsNullOrEmpty(row.PendingCodeSalt));
        Assert.DoesNotContain(world.Sender.LastCode(), row.PendingCodeHash, StringComparison.Ordinal);
        Assert.Equal(world.Clock.GetUtcNow() + NotificationPreferenceService.CodeLifetime, row.PendingCodeExpiresUtc);
    }

    /// <summary>
    ///     Test that an actor who signed in with an API token can never use notifications.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Actions_ApiTokenActor_AreRefused()
    {
        // Arrange: an actor that comes from an API token
        var world = await World.CreateAsync();
        var tokenActor = Ann with { ApiTokenId = "token-1" };

        // Act and Assert: every action is refused as not available, and nothing is mailed
        var request = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(tokenActor, AnnAddress, world.Token));
        var confirm = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.ConfirmAsync(tokenActor, "123456", world.Token));
        var remove = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RemoveAddressAsync(tokenActor, world.Token));
        var status = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.GetStatusAsync(tokenActor, world.Token));
        Assert.All(
            new[] { request, confirm, remove, status },
            exception => Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule));
        Assert.Empty(world.Sender.Sent);
    }

    /// <summary>
    ///     Test that nothing works while e-mail notifications are switched off.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Actions_FeatureOff_AreRefusedAndTouchNoStore()
    {
        // Arrange: the feature is off
        var world = await World.CreateAsync(NotificationSettings.Off);

        // Act and Assert: not available, the confirmed address is hidden, nothing is mailed or stored
        Assert.False(world.Service.IsAvailable);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));
        Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule);
        Assert.Null(await world.Service.GetConfirmedAddressAsync(1, world.Token));
        Assert.Empty(world.Sender.Sent);
        Assert.Empty(world.Store.Log);
    }

    /// <summary>
    ///     Test that nothing works while no transport is configured.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_IsAvailable_SenderNotConfigured_IsFalse()
    {
        // Arrange: the feature is on, but the sender is not configured
        var world = await World.CreateAsync();
        world.Sender.Configured = false;

        // Act and Assert: not available
        Assert.False(world.Service.IsAvailable);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));
        Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule);
    }

    /// <summary>
    ///     Test that a disabled account and an unknown account are refused.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Actions_DisabledOrUnknownUser_AreRefused()
    {
        // Arrange: a disabled user and an actor without an account
        var world = await World.CreateAsync();
        await world.AddUserAsync("dora");
        await world.Users.SetEnabledAsync("dora", false, Actor.CommandLine, world.Token);

        // Act and Assert: refused as not allowed
        var disabled = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(new Actor("dora", "Dora"), AnnAddress, world.Token));
        var unknown = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(new Actor("nobody", "Nobody"), AnnAddress, world.Token));
        Assert.Equal(DomainRule.ActorNotAllowed, disabled.Rule);
        Assert.Equal(DomainRule.ActorNotAllowed, unknown.Rule);
    }

    /// <summary>
    ///     Test that a user with a temporary password can not set up notifications.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Actions_TemporaryPassword_AreRefused()
    {
        // Arrange: an administrator set a temporary password
        var world = await World.CreateAsync();
        await world.Users.SetPasswordAsync("ann", "another long passphrase", Actor.CommandLine, world.Token);

        // Act and Assert: refused as not available until the password is replaced
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token));
        Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule);
    }

    /// <summary>
    ///     Test that a missing actor is a programming error.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_Actions_NullActor_ThrowsArgumentNull()
    {
        // Arrange: the service
        var world = await World.CreateAsync();

        // Act and Assert: a null actor is not a rule violation
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.GetStatusAsync(null!, world.Token));
    }

    /// <summary>
    ///     The services of a test over in-memory fakes.
    /// </summary>
    private sealed class World
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="World"/> class.
        /// </summary>
        /// <param name="settings">The notification settings.</param>
        private World(NotificationSettings settings)
        {
            Clock = new MovableTimeProvider(new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero));
            var users = new FakeUserStore();
            var audit = new AuditService(new FakeAuditStore(Clock), null, Clock);
            Users = new UserService(users, new FakePasswordHasher(), Clock, audit, new FakeTotpStore());
            Store = new FakeNotificationStore();
            Sender = new FakeNotificationSender();
            Service = new NotificationPreferenceService(Store, Sender, Users, settings, Clock);
        }

        /// <summary>
        ///     Gets the cancellation token of the running test.
        /// </summary>
        public CancellationToken Token { get; } = TestContext.Current.CancellationToken;

        /// <summary>
        ///     Gets the clock the tests move.
        /// </summary>
        public MovableTimeProvider Clock { get; }

        /// <summary>
        ///     Gets the user service.
        /// </summary>
        public UserService Users { get; }

        /// <summary>
        ///     Gets the store of the notification addresses.
        /// </summary>
        public FakeNotificationStore Store { get; }

        /// <summary>
        ///     Gets the recording sender.
        /// </summary>
        public FakeNotificationSender Sender { get; }

        /// <summary>
        ///     Gets the service under test.
        /// </summary>
        public NotificationPreferenceService Service { get; }

        /// <summary>
        ///     Creates the world with the user <c>ann</c>.
        /// </summary>
        /// <param name="settings">The settings; <c>null</c> for the settings that turn notifications on.</param>
        /// <returns>The world.</returns>
        public static async Task<World> CreateAsync(NotificationSettings? settings = null)
        {
            var world = new World(settings ?? new NotificationSettings(true, "Test Center", 200));
            await world.AddUserAsync("ann");
            return world;
        }

        /// <summary>
        ///     Creates a local user with the standard password.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <returns>A task that completes when the user exists.</returns>
        public async Task AddUserAsync(string username)
        {
            await Users.CreateAsync(username, username, UserRole.Reviewer, Password, Token);
        }

        /// <summary>
        ///     Enters an address for <c>ann</c> and confirms it with the mailed code.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <returns>A task that completes when the address is confirmed.</returns>
        public async Task ConfirmAsync(string address)
        {
            await Service.RequestConfirmationAsync(Ann, address, Token);
            await Service.ConfirmAsync(Ann, Sender.LastCode(), Token);
        }
    }
}
