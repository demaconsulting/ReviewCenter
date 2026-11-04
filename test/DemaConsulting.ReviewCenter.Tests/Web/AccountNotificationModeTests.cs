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

using AngleSharp.Html.Dom;
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the notification mode selector and the explanation of the notification card of the
///     <see cref="AccountPage"/> component, rendered in memory with bUnit.
/// </summary>
public class AccountNotificationModeTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     The test id of the mode selector.
    /// </summary>
    private const string Selector = "[data-testid='notifications-mode-select']";

    /// <summary>
    ///     The actor of the signed-in user.
    /// </summary>
    private static readonly Actor Ann = new("ann", "Ann");

    /// <summary>
    ///     Initializes a new instance of the <see cref="AccountNotificationModeTests"/> class; the page asks the
    ///     browser for the stored appearance, which these tests do not script.
    /// </summary>
    public AccountNotificationModeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    ///     Test that the mode selector and its save button are shown once an address is confirmed.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_ConfirmedAddress_ShowsModeSelector()
    {
        // Arrange: a confirmed address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();

        // Assert: the selector is enabled and can be saved
        var select = (IHtmlSelectElement)cut.Find(Selector);
        Assert.False(select.IsDisabled);
        Assert.NotNull(cut.Find("[data-testid='notifications-mode-save']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-mode-needs-address']"));
    }

    /// <summary>
    ///     Test that the selector offers exactly the modes None, Daily and Every.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_ModeSelector_OffersThreeModes()
    {
        // Arrange: a confirmed address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();

        // Act
        var values = cut.FindAll($"{Selector} option").Select(o => o.GetAttribute("value")).ToArray();

        // Assert
        Assert.Equal(["None", "Daily", "Every"], values);
    }

    /// <summary>
    ///     Test that the stored mode is preselected.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_StoredEvery_EverySelected()
    {
        // Arrange: a confirmed address and the stored mode Every
        await CreateAnnAsync();
        SwitchNotificationsOn();
        await ConfirmAddressAsync();
        await Notifications.SetModeAsync(Ann, NotificationMode.Every, TestContext.Current.CancellationToken);

        // Act: a new rendering of the page
        var cut = Render<AccountPage>();

        // Assert
        Assert.Equal("Every", ((IHtmlSelectElement)cut.Find(Selector)).Value);
    }

    /// <summary>
    ///     Test that a user without a stored mode sees None.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NoMode_NoneSelected()
    {
        // Arrange: a confirmed address without a mode
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();

        // Assert
        Assert.Equal("None", ((IHtmlSelectElement)cut.Find(Selector)).Value);
    }

    /// <summary>
    ///     Test that the selector is disabled while no address is confirmed.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NoConfirmedAddress_SelectorDisabled()
    {
        // Arrange: notifications on, no address
        await CreateAnnAsync();
        SwitchNotificationsOn();

        // Act
        var cut = Render<AccountPage>();

        // Assert
        Assert.True(((IHtmlSelectElement)cut.Find(Selector)).IsDisabled);
    }

    /// <summary>
    ///     Test that the page has no save button for the mode while no address is confirmed.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NoConfirmedAddress_NoSaveButton()
    {
        // Arrange: notifications on, no address
        await CreateAnnAsync();
        SwitchNotificationsOn();

        // Act
        var cut = Render<AccountPage>();

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='notifications-mode-save']"));
    }

    /// <summary>
    ///     Test that the page explains that a confirmed address is needed.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NoConfirmedAddress_ExplainsNeed()
    {
        // Arrange: notifications on, no address
        await CreateAnnAsync();
        SwitchNotificationsOn();

        // Act
        var cut = Render<AccountPage>();

        // Assert
        Assert.Contains(
            "Confirm an e-mail address first",
            cut.Find("[data-testid='notifications-mode-needs-address']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that no mode selector is shown while notifications are not set up.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_NotificationsNotSetUp_NoModeSelector()
    {
        // Arrange: notifications off
        await CreateAnnAsync();

        // Act
        var cut = Render<AccountPage>();

        // Assert
        Assert.Empty(cut.FindAll(Selector));
        Assert.Empty(cut.FindAll("[data-testid='notifications-mode-form']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-mode-save']"));
    }

    /// <summary>
    ///     Test that saving calls the service with the chosen mode.
    /// </summary>
    [Fact]
    public async Task AccountPage_SaveMode_Daily_CallsService()
    {
        // Arrange: a confirmed address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();
        await cut.Find(Selector).ChangeAsync("Daily");

        // Act
        await cut.Find("[data-testid='notifications-mode-form']").SubmitAsync();

        // Assert: the service stored the mode
        var status = await Notifications.GetStatusAsync(Ann, TestContext.Current.CancellationToken);
        Assert.Equal(NotificationMode.Daily, status.Mode);
        Assert.Equal("Daily", ((IHtmlSelectElement)cut.Find(Selector)).Value);
    }

    /// <summary>
    ///     Test that a saved mode is confirmed to the user.
    /// </summary>
    [Fact]
    public async Task AccountPage_SaveMode_Success_ShowsSavedMessage()
    {
        // Arrange: a confirmed address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();
        await cut.Find(Selector).ChangeAsync("Every");

        // Act
        await cut.Find("[data-testid='notifications-mode-form']").SubmitAsync();

        // Assert
        Assert.Equal(
            "Your e-mail setting was saved.",
            cut.Find("[data-testid='notifications-mode-saved']").TextContent);
    }

    /// <summary>
    ///     Test that no error is shown when a mode was saved.
    /// </summary>
    [Fact]
    public async Task AccountPage_SaveMode_Success_ShowsNoError()
    {
        // Arrange: a confirmed address
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();
        await cut.Find(Selector).ChangeAsync("Every");

        // Act
        await cut.Find("[data-testid='notifications-mode-form']").SubmitAsync();

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='notifications-error']"));
        Assert.Empty(cut.FindAll("[data-testid='notifications-message']"));
    }

    /// <summary>
    ///     Test that the refusal of the service is shown.
    /// </summary>
    [Fact]
    public async Task AccountPage_SaveMode_Refused_ShowsMessage()
    {
        // Arrange: the page shows a confirmed address, which is removed behind its back
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();
        await Notifications.RemoveAddressAsync(Ann, TestContext.Current.CancellationToken);
        await cut.Find(Selector).ChangeAsync("Daily");

        // Act
        await cut.Find("[data-testid='notifications-mode-form']").SubmitAsync();

        // Assert
        Assert.NotEmpty(cut.Find("[data-testid='notifications-error']").TextContent);
    }

    /// <summary>
    ///     Test that no saved message appears when the service refuses the mode.
    /// </summary>
    [Fact]
    public async Task AccountPage_SaveMode_Refused_ShowsNoSavedMessage()
    {
        // Arrange: the page shows a confirmed address, which is removed behind its back
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();
        await Notifications.RemoveAddressAsync(Ann, TestContext.Current.CancellationToken);
        await cut.Find(Selector).ChangeAsync("Daily");

        // Act
        await cut.Find("[data-testid='notifications-mode-form']").SubmitAsync();

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='notifications-mode-saved']"));
    }

    /// <summary>
    ///     Test that removing the address shows None again.
    /// </summary>
    [Fact]
    public async Task AccountPage_RemoveAddress_AfterMode_ShowsNone()
    {
        // Arrange: a confirmed address and the mode Every
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = await ConfirmAddressAsync();
        await cut.Find(Selector).ChangeAsync("Every");
        await cut.Find("[data-testid='notifications-mode-form']").SubmitAsync();

        // Act
        await cut.Find("[data-testid='notifications-remove-button']").ClickAsync();

        // Assert: None, and the selector is disabled
        var select = (IHtmlSelectElement)cut.Find(Selector);
        Assert.Equal("None", select.Value);
        Assert.True(select.IsDisabled);
    }

    /// <summary>
    ///     Test that the card says what kind of news a mail names.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_Card_ExplainsWhatIsMailed()
    {
        // Arrange and Act
        var text = await ExplanationAsync();

        // Assert
        Assert.Contains("names the kind", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the card says that a mail names the project and the review.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_Card_ExplainsProjectAndReview()
    {
        // Arrange and Act
        var text = await ExplanationAsync();

        // Assert
        Assert.Contains("the project and the review", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the card says that a mail links to the review.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_Card_ExplainsLinkToReview()
    {
        // Arrange and Act
        var text = await ExplanationAsync();

        // Assert
        Assert.Contains("links to the review", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the card says what is never mailed.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_Card_ExplainsWhatIsNotMailed()
    {
        // Arrange and Act
        var text = await ExplanationAsync();

        // Assert
        Assert.Contains("never contains comments, answers, verdicts, outcomes", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the card describes the three modes.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_Card_ExplainsModes()
    {
        // Arrange and Act
        var text = await ExplanationAsync();

        // Assert
        Assert.Contains("\"No e-mail\" nothing is sent", text, StringComparison.Ordinal);
        Assert.Contains("\"Daily summary\" you get at most one e-mail a day", text, StringComparison.Ordinal);
        Assert.Contains("\"Every change\" you get an e-mail a few minutes after", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Renders the card with notifications on and returns the text of the explanation, with the white space
    ///     of the markup folded to single blanks.
    /// </summary>
    /// <returns>The text.</returns>
    private async Task<string> ExplanationAsync()
    {
        await CreateAnnAsync();
        SwitchNotificationsOn();
        var cut = Render<AccountPage>();
        return string.Join(
            ' ',
            cut.Find("[data-testid='notifications-explanation']")
                .TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    ///     Types an address, sends the code and confirms it, so that the page shows a confirmed address.
    /// </summary>
    /// <returns>The rendered page.</returns>
    private async Task<IRenderedComponent<AccountPage>> ConfirmAddressAsync()
    {
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='notifications-address-input']").InputAsync("ann@example.com");
        await cut.Find("[data-testid='notifications-address-form']").SubmitAsync();
        await cut.Find("[data-testid='notifications-code-input']").InputAsync(NotificationSender.LastCode());
        await cut.Find("[data-testid='notifications-confirm-form']").SubmitAsync();
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
