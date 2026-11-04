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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the notification card of the <see cref="AccountPage"/> component, rendered in memory with
///     bUnit: the unavailable state, the confirmation of an e-mail address and its removal.
/// </summary>
public class AccountNotificationPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Initializes a new instance of the <see cref="AccountNotificationPageTests"/> class; the page asks the
    ///     browser for the stored appearance, which these tests do not script.
    /// </summary>
    public AccountNotificationPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    ///     Test that the card shows one sentence and no fields while e-mail notifications are not set up, and that
    ///     it reads nothing from the store.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NotificationsNotSetUp_ShowsOneSentenceAndTouchesNoStore()
    {
        // Arrange: a local user and notifications that are off
        await CreateAnnAsync();

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the single sentence, no field, no button and no call to the store
        Assert.Equal(
            "E-mail notifications are not set up by your administrator.",
            cut.Find("[data-testid='notifications-unavailable']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='notifications-card'] input"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-card'] button"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-explanation']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-mode-select']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-help-link']"));
        Assert.Equal(0, NotificationStore.Calls);
    }

    /// <summary>
    ///     Test that a user without an address sees the explanation and the address form.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NotificationsOnNoAddress_ShowsExplanationAndAddressForm()
    {
        // Arrange: a local user and notifications that are on
        await CreateAnnAsync();
        SwitchNotificationsOn();

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the explanation names what is not mailed, the form is there, nothing else is
        var explanation = cut.Find("[data-testid='notifications-explanation']").TextContent;
        Assert.Contains("never contains", explanation, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='notifications-address-input']"));
        Assert.NotNull(cut.Find("[data-testid='notifications-send-code-button']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-confirm-form']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-remove-button']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-unavailable']"));
    }

    /// <summary>
    ///     Test that the card links to the e-mail notifications help topic while the feature is set up.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NotificationCard_LinksToEmailNotificationsTopic()
    {
        // Arrange: a local user and notifications that are on
        await CreateAnnAsync();
        SwitchNotificationsOn();

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the link is in the card and leads to the topic
        var link = cut.Find("[data-testid='notifications-card'] [data-testid='notifications-help-link']");
        Assert.Equal("/help/email-notifications", link.GetAttribute("href"));
        Assert.Equal("About e-mail notifications", link.TextContent.Trim());
    }

    /// <summary>
    ///     Test that a user with a temporary password does not see the card.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_TemporaryPassword_HidesNotificationsCard()
    {
        // Arrange: the user must replace the password first
        await CreateAnnAsync();
        SwitchNotificationsOn();
        SignInAsTemporaryPassword("ann", "Ann");

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the page rendered its temporary-password banner, and the card is absent (also no field of it)
        Assert.NotEmpty(cut.FindAll("[data-testid='password-change-required-banner']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-card']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-address-input']"));
    }

    /// <summary>
    ///     Test that sending a code mails it, shows the pending address and the code form.
    /// </summary>
    [Fact]
    public async Task AccountPage_SendCode_ValidAddress_MailsCodeAndShowsConfirmForm()
    {
        // Arrange: notifications on and an address typed
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='notifications-address-input']").InputAsync("Ann@Example.com");

        // Act: send the code
        await cut.Find("[data-testid='notifications-address-form']").SubmitAsync();

        // Assert: one mail to the normalized address, and the page asks for the code
        Assert.Equal("ann@example.com", Assert.Single(NotificationSender.Sent).ToAddress);
        Assert.Equal("ann@example.com", cut.Find("[data-testid='notifications-pending-address']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='notifications-code-input']"));
        Assert.Contains("code was sent", cut.Find("[data-testid='notifications-message']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='notifications-confirmed-address']"));
    }

    /// <summary>
    ///     Test that an invalid address shows an error and mails nothing.
    /// </summary>
    [Fact]
    public async Task AccountPage_SendCode_InvalidAddress_ShowsErrorAndMailsNothing()
    {
        // Arrange: notifications on and a bad address typed
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='notifications-address-input']").InputAsync("not an address");

        // Act: send the code
        await cut.Find("[data-testid='notifications-address-form']").SubmitAsync();

        // Assert: an error, no mail and no code form
        Assert.NotEmpty(cut.Find("[data-testid='notifications-error']").TextContent);
        Assert.Empty(NotificationSender.Sent);
        Assert.Empty(cut.FindAll("[data-testid='notifications-confirm-form']"));
    }

    /// <summary>
    ///     Test that the right code confirms the address and the page then shows it as confirmed.
    /// </summary>
    [Fact]
    public async Task AccountPage_Confirm_RightCode_ShowsConfirmedAddress()
    {
        // Arrange: a pending address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await StartConfirmationAsync();

        // Act: type the mailed code
        await cut.Find("[data-testid='notifications-code-input']").InputAsync(NotificationSender.LastCode());
        await cut.Find("[data-testid='notifications-confirm-form']").SubmitAsync();

        // Assert: confirmed, no code form, and a remove button
        Assert.Equal("ann@example.com", cut.Find("[data-testid='notifications-confirmed-address']").TextContent);
        Assert.Equal("Your e-mail address is confirmed.", cut.Find("[data-testid='notifications-message']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='notifications-confirm-form']"));
        Assert.NotNull(cut.Find("[data-testid='notifications-remove-button']"));
    }

    /// <summary>
    ///     Test that a wrong code shows an error, keeps the code form and empties the field.
    /// </summary>
    [Fact]
    public async Task AccountPage_Confirm_WrongCode_ShowsErrorAndKeepsForm()
    {
        // Arrange: a pending address and a wrong code
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await StartConfirmationAsync();
        var wrong = NotificationSender.LastCode() == "000000" ? "000001" : "000000";
        await cut.Find("[data-testid='notifications-code-input']").InputAsync(wrong);

        // Act: confirm
        await cut.Find("[data-testid='notifications-confirm-form']").SubmitAsync();

        // Assert: an error, the form stays, nothing is confirmed, and the field is empty
        Assert.NotEmpty(cut.Find("[data-testid='notifications-error']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='notifications-confirm-form']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-confirmed-address']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='notifications-code-input']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that asking for a new code too soon shows an error, and after the cooldown mails a new one.
    /// </summary>
    [Fact]
    public async Task AccountPage_Resend_TooSoonThenAfterCooldown_ShowsErrorThenMailsAgain()
    {
        // Arrange: a pending address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await StartConfirmationAsync();

        // Act: ask at once
        await cut.Find("[data-testid='notifications-resend-button']").ClickAsync();

        // Assert: refused, one mail
        Assert.NotEmpty(cut.Find("[data-testid='notifications-error']").TextContent);
        Assert.Single(NotificationSender.Sent);

        // Act: ask after the cooldown
        Time.Advance(NotificationPreferenceService.ResendCooldown + TimeSpan.FromSeconds(1));
        await cut.Find("[data-testid='notifications-resend-button']").ClickAsync();

        // Assert: a second mail
        Assert.Equal(2, NotificationSender.Sent.Count);
        Assert.Empty(cut.FindAll("[data-testid='notifications-error']"));
    }

    /// <summary>
    ///     Test that removing the address returns the page to the address form.
    /// </summary>
    [Fact]
    public async Task AccountPage_Remove_ConfirmedAddress_ShowsAddressFormAgain()
    {
        // Arrange: a confirmed address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await StartConfirmationAsync();
        await cut.Find("[data-testid='notifications-code-input']").InputAsync(NotificationSender.LastCode());
        await cut.Find("[data-testid='notifications-confirm-form']").SubmitAsync();

        // Act: remove
        await cut.Find("[data-testid='notifications-remove-button']").ClickAsync();

        // Assert: no address, the form again, and a confirmation
        Assert.Empty(cut.FindAll("[data-testid='notifications-confirmed-address']"));
        Assert.NotNull(cut.Find("[data-testid='notifications-address-form']"));
        Assert.Equal("Your e-mail address was removed.", cut.Find("[data-testid='notifications-message']").TextContent);
    }

    /// <summary>
    ///     Test that the card never shows the address of a confirmation code or the code outside the form fields.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_PendingAddress_NeverShowsTheCode()
    {
        // Arrange: a pending address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await StartConfirmationAsync();

        // Act: read the card
        var text = cut.Find("[data-testid='notifications-card']").TextContent;

        // Assert: the code is not on the page
        Assert.DoesNotContain(NotificationSender.LastCode(), text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an account of an identity provider can use the card.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_ExternalUser_ShowsAddressForm()
    {
        // Arrange: a provider account with notifications on
        Services.Configure<OidcOptions>(options => options.Enabled = true);
        var user = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-1", "zoe", "Zoe", TestContext.Current.CancellationToken)).User!;
        SignInAs(user.Username, "Zoe", false);
        SwitchNotificationsOn();

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the address form is offered
        Assert.NotNull(cut.Find("[data-testid='notifications-address-form']"));
    }

    /// <summary>
    ///     Types an address and sends the code, so that the address waits for its code.
    /// </summary>
    /// <returns>The rendered page.</returns>
    private async Task<IRenderedComponent<AccountPage>> StartConfirmationAsync()
    {
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='notifications-address-input']").InputAsync("ann@example.com");
        await cut.Find("[data-testid='notifications-address-form']").SubmitAsync();
        return cut;
    }

    /// <summary>
    ///     Creates the signed-in user as a local user with the test password.
    /// </summary>
    /// <returns>A task that completes when the user exists.</returns>
    private async Task CreateAnnAsync()
    {
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
    }
}
