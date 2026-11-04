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
///     Unit tests for the notification mode of <see cref="NotificationPreferenceService"/> over in-memory fakes:
///     the status, the choice of a mode, the refusals, the audit entry and the baseline marker.
/// </summary>
public class NotificationPreferenceServiceModeTests
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
    ///     The identity of the user the tests create.
    /// </summary>
    private const int AnnId = 1;

    /// <summary>
    ///     The actor of the user the tests create.
    /// </summary>
    private static readonly Actor Ann = new("ann", "Ann");

    /// <summary>
    ///     Test that a user without a stored row has the mode None.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_GetStatus_NoRow_ModeNone()
    {
        // Arrange
        var world = await World.CreateAsync();

        // Act
        var status = await world.Service.GetStatusAsync(Ann, world.Token);

        // Assert
        Assert.Equal(NotificationMode.None, status.Mode);
    }

    /// <summary>
    ///     Test that the status shows the stored mode.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_GetStatus_ModeDaily_ShowsDaily()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);

        // Act
        var status = await world.Service.GetStatusAsync(Ann, world.Token);

        // Assert
        Assert.Equal(NotificationMode.Daily, status.Mode);
        Assert.Equal(AnnAddress, status.ConfirmedAddress);
    }

    /// <summary>
    ///     Test that the mode Daily is stored.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_Daily_StoresDaily()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();

        // Act
        var changed = await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);

        // Assert
        Assert.True(changed);
        Assert.Equal(NotificationMode.Daily, (await world.Store.GetAsync(AnnId, world.Token))!.Mode);
    }

    /// <summary>
    ///     Test that the mode Every is stored.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_Every_StoresEvery()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();

        // Act
        var changed = await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Assert
        Assert.True(changed);
        Assert.Equal(NotificationMode.Every, (await world.Store.GetAsync(AnnId, world.Token))!.Mode);
    }

    /// <summary>
    ///     Test that the mode None is stored when the user switches back.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_None_StoresNone()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Act
        var changed = await world.Service.SetModeAsync(Ann, NotificationMode.None, world.Token);

        // Assert
        Assert.True(changed);
        Assert.Equal(NotificationMode.None, (await world.Store.GetAsync(AnnId, world.Token))!.Mode);
    }

    /// <summary>
    ///     Test that a mode other than None is refused while no address is confirmed.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_NoConfirmedAddress_Throws()
    {
        // Arrange: no row at all, and then an address that only waits for its code
        var world = await World.CreateAsync();

        // Act
        var withoutRow = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token));
        await world.Service.RequestConfirmationAsync(Ann, AnnAddress, world.Token);
        var pending = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token));

        // Assert
        Assert.Equal(DomainRule.NotificationAddressNotConfirmed, withoutRow.Rule);
        Assert.Equal(DomainRule.NotificationAddressNotConfirmed, pending.Rule);
        Assert.Equal(NotificationMode.None, (await world.Store.GetAsync(AnnId, world.Token))!.Mode);
    }

    /// <summary>
    ///     Test that an actor who signed in with an API token can not change the mode.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_ApiTokenActor_Throws()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann with { ApiTokenId = "token-1" }, NotificationMode.Every, world.Token));

        // Assert
        Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule);
        Assert.Equal(NotificationMode.None, (await world.Store.GetAsync(AnnId, world.Token))!.Mode);
    }

    /// <summary>
    ///     Test that the mode can not be changed while notifications are not available.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_NotAvailable_Throws()
    {
        // Arrange: the feature is off
        var world = await World.CreateAsync(NotificationSettings.Off);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token));

        // Assert: refused
        Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule);
    }

    /// <summary>
    ///     Test that a refused change of the mode while notifications are not available leaves the store untouched.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_NotAvailable_StoreNotTouched()
    {
        // Arrange: the feature is off
        var world = await World.CreateAsync(NotificationSettings.Off);

        // Act
        await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token));

        // Assert
        Assert.Equal(0, world.Store.Calls);
    }

    /// <summary>
    ///     Test that a disabled user can not change the mode.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_DisabledUser_Throws()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Users.SetEnabledAsync("ann", false, Actor.CommandLine, world.Token);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token));

        // Assert
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
    }

    /// <summary>
    ///     Test that a user with a temporary password can not change the mode.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_MustChangePassword_Throws()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Users.SetPasswordAsync("ann", "another long passphrase", Actor.CommandLine, world.Token);

        // Act
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token));

        // Assert
        Assert.Equal(DomainRule.NotificationsNotAvailable, exception.Rule);
    }

    /// <summary>
    ///     Test that a value outside the enumeration is refused.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_UndefinedValue_Throws()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();

        // Act and Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => world.Service.SetModeAsync(Ann, (NotificationMode)99, world.Token));
    }

    /// <summary>
    ///     Test that a missing actor is a programming error.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_NullActor_ThrowsArgumentNull()
    {
        // Arrange
        var world = await World.CreateAsync();

        // Act and Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => world.Service.SetModeAsync(null!, NotificationMode.Every, world.Token));
    }

    /// <summary>
    ///     Test that a changed mode is written to the audit trail by its actor.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_Changed_AuditEntryWritten()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        var before = world.Store.Audits.Count;

        // Act
        await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);

        // Assert
        Assert.Equal(before + 1, world.Store.Audits.Count);
        var entry = world.Store.Audits[^1];
        Assert.Equal("notification.settings-changed", entry.Action);
        Assert.Equal("ann", entry.ActorUsername);
        Assert.Contains("Daily", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the audit text of a mode change holds no address.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_Changed_AuditHasNoAddress()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();

        // Act
        await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Assert
        var entry = world.Store.Audits[^1];
        Assert.DoesNotContain("@", entry.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(AnnAddress, entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that choosing the mode that is stored already writes no audit entry.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_Same_NoAuditNoChange()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);
        var audits = world.Store.Audits.Count;

        // Act
        await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);

        // Assert
        Assert.Equal(audits, world.Store.Audits.Count);
    }

    /// <summary>
    ///     Test that choosing the mode that is stored already reports no change.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_Same_ReportsNoChange()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);

        // Act
        var changed = await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);

        // Assert
        Assert.False(changed);
    }

    /// <summary>
    ///     Test that choosing None for a user without a stored mode is not a change.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_NoneWithoutRow_NoChange()
    {
        // Arrange
        var world = await World.CreateAsync();

        // Act
        var changed = await world.Service.SetModeAsync(Ann, NotificationMode.None, world.Token);

        // Assert
        Assert.False(changed);
        Assert.Empty(world.Store.Audits);
    }

    /// <summary>
    ///     Test that switching a mode on from None marks the baseline as pending.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_NoneToEvery_BaselinePending()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();

        // Act
        await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Assert
        Assert.True((await world.Store.GetAsync(AnnId, world.Token))!.BaselinePending);
    }

    /// <summary>
    ///     Test that switching between Daily and Every sets no new baseline.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_DailyToEvery_NoBaseline()
    {
        // Arrange: Daily, with the baseline taken and one item already notified
        var world = await ArrangeDailyWithNotifiedItemAsync();

        // Act
        await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Assert
        Assert.False((await world.Store.GetAsync(AnnId, world.Token))!.BaselinePending);
    }

    /// <summary>
    ///     Test that switching between Daily and Every keeps what was notified.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_SetMode_DailyToEvery_ItemsKept()
    {
        // Arrange
        var world = await ArrangeDailyWithNotifiedItemAsync();

        // Act
        await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Assert
        Assert.Single(world.Store.NotifiedOf(AnnId));
    }

    /// <summary>
    ///     Arranges a user in the mode Daily whose baseline is taken and who was notified of one item.
    /// </summary>
    /// <returns>The world.</returns>
    private static async Task<World> ArrangeDailyWithNotifiedItemAsync()
    {
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Service.SetModeAsync(Ann, NotificationMode.Daily, world.Token);
        var recipients = await world.Store.ListRecipientsAsync(world.Token);
        var recipient = recipients.Single();
        world.Store.AddNotified(AnnId, new NotifiedKey(1, "r1", InboxSection.ToAnswer, "to-answer"), "to-answer");
        await world.Store.ApplyAsync(
            AnnId,
            new NotificationCommit(
                world.Clock.GetUtcNow(),
                [],
                [],
                null,
                recipient.State with { BaselinePending = false },
                recipient.Address,
                recipient.Generation),
            world.Token);
        return world;
    }

    /// <summary>
    ///     Test that removing the address returns the user to the mode None.
    /// </summary>
    [Fact]
    public async Task NotificationPreferenceService_RemoveAddress_AfterMode_ModeNone()
    {
        // Arrange
        var world = await World.CreateAsync();
        await world.ConfirmAsync();
        await world.Service.SetModeAsync(Ann, NotificationMode.Every, world.Token);

        // Act
        await world.Service.RemoveAddressAsync(Ann, world.Token);

        // Assert
        var status = await world.Service.GetStatusAsync(Ann, world.Token);
        Assert.Equal(NotificationMode.None, status.Mode);
        Assert.Null(status.ConfirmedAddress);
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
            await world.Users.CreateAsync("ann", "ann", UserRole.Reviewer, Password, world.Token);
            return world;
        }

        /// <summary>
        ///     Enters the address of <c>ann</c> and confirms it with the mailed code.
        /// </summary>
        /// <returns>A task that completes when the address is confirmed.</returns>
        public async Task ConfirmAsync()
        {
            await Service.RequestConfirmationAsync(Ann, AnnAddress, Token);
            await Service.ConfirmAsync(Ann, Sender.LastCode(), Token);
        }
    }
}
