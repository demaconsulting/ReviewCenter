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
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="AccountPage"/> component, rendered in memory with bUnit.
/// </summary>
public class AccountPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string OldPassword = "old password 12345";

    /// <summary>
    ///     Another password that satisfies the policy.
    /// </summary>
    private const string NewPassword = "new password 67890";

    /// <summary>
    ///     Initializes a new instance of the <see cref="AccountPageTests"/> class; the page asks the browser for the
    ///     stored appearance, which these tests do not script unless they say so.
    /// </summary>
    public AccountPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    ///     Test that the page shows the password form with its controls and no message.
    /// </summary>
    [Fact]
    public void AccountPage_Render_SignedIn_ShowsFormControls()
    {
        // Arrange: nothing to prepare

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the form controls are present, no message is shown
        Assert.NotNull(cut.Find("[data-testid='account-page']"));
        Assert.NotNull(cut.Find("[data-testid='current-password-input']"));
        Assert.NotNull(cut.Find("[data-testid='new-password-input']"));
        Assert.NotNull(cut.Find("[data-testid='confirm-password-input']"));
        Assert.NotNull(cut.Find("[data-testid='change-password-button']"));
        Assert.Empty(cut.FindAll("[data-testid='account-error']"));
        Assert.Empty(cut.FindAll("[data-testid='account-message']"));
    }

    /// <summary>
    ///     Test that the right current password and a valid new one change the password of the signed-in user
    ///     and empty the fields.
    /// </summary>
    [Fact]
    public async Task AccountPage_ChangePassword_ValidInput_ChangesOwnPasswordAndClearsFields()
    {
        // Arrange: the signed-in user exists, and the form is filled in
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var cut = Render<AccountPage>();
        await FillAsync(cut, OldPassword, NewPassword, NewPassword);

        // Act: submit
        await cut.Find("[data-testid='change-password-form']").SubmitAsync();

        // Assert: the new password works, the old one does not, the confirmation shows and the fields are empty
        Assert.Equal("Your password was changed.", cut.Find("[data-testid='account-message']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='account-error']"));
        Assert.NotNull(await Users.AuthenticateAsync("ann", NewPassword, TestContext.Current.CancellationToken));
        Assert.Null(await Users.AuthenticateAsync("ann", OldPassword, TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-password-input']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that a wrong current password, a mismatch and a too short new password are each shown as an
    ///     error and change nothing.
    /// </summary>
    /// <param name="current">The current password typed.</param>
    /// <param name="newPassword">The new password typed.</param>
    /// <param name="confirm">The repeated new password typed.</param>
    /// <param name="expected">The text the error must contain.</param>
    [Theory]
    [InlineData("wrong password 123", "new password 67890", "new password 67890", "current password is incorrect")]
    [InlineData("old password 12345", "new password 67890", "another password 1", "do not match")]
    [InlineData("old password 12345", "short", "short", "characters")]
    public async Task AccountPage_ChangePassword_InvalidInput_ShowsErrorAndChangesNothing(
        string current,
        string newPassword,
        string confirm,
        string expected)
    {
        // Arrange: the signed-in user exists, and the form is filled in
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var cut = Render<AccountPage>();
        await FillAsync(cut, current, newPassword, confirm);

        // Act: submit
        await cut.Find("[data-testid='change-password-form']").SubmitAsync();

        // Assert: an error is shown, no confirmation, and the old password still works
        Assert.Contains(expected, cut.Find("[data-testid='account-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='account-message']"));
        Assert.NotNull(await Users.AuthenticateAsync("ann", OldPassword, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the appearance card offers System, Light and Dark, with System selected by default.
    /// </summary>
    [Fact]
    public void AccountPage_Render_AppearanceCard_ShowsThreeOptionsWithSystemSelected()
    {
        // Arrange: no JS answer, as in a browser without a stored choice
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: three radio options, System checked
        Assert.NotNull(cut.Find("[data-testid='appearance-card']"));
        Assert.True(cut.Find("[data-testid='theme-system']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='theme-light']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='theme-dark']").HasAttribute("checked"));
        Assert.Equal("radio", cut.Find("[data-testid='theme-dark']").GetAttribute("type"));
    }

    /// <summary>
    ///     Test that choosing an appearance is applied to the browser at once and shown as selected.
    /// </summary>
    [Fact]
    public async Task AccountPage_ChooseDark_AppliesThemeAndSelectsOption()
    {
        // Arrange: the rendered page
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<AccountPage>();

        // Act: choose Dark
        await cut.Find("[data-testid='theme-dark']").ChangeAsync(new ChangeEventArgs { Value = "dark" });

        // Assert: the browser script was told and the option is selected
        Assert.Equal(["dark"], JSInterop.Invocations["reviewCenter.setTheme"][^1].Arguments);
        Assert.True(cut.Find("[data-testid='theme-dark']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='theme-system']").HasAttribute("checked"));
    }

    /// <summary>
    ///     Test that the stored browser choice is shown as selected when the page is first rendered.
    /// </summary>
    [Fact]
    public void AccountPage_Render_StoredLightChoice_IsSelected()
    {
        // Arrange: the browser reports a stored Light choice
        JSInterop.Setup<string?>("reviewCenter.getTheme").SetResult("light");

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: Light is selected
        Assert.True(cut.Find("[data-testid='theme-light']").HasAttribute("checked"));
    }

    /// <summary>
    ///     Test that the token card is there with its controls, an empty list and no value box, and that its only
    ///     link is the button that opens the Help topic about API tokens.
    /// </summary>
    [Fact]
    public void AccountPage_Render_TokensCard_ShowsFormEmptyListAndHelpLink()
    {
        // Arrange: nothing to prepare

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the form controls and the three expiry choices (90 days selected) are present
        var card = cut.Find("[data-testid='tokens-card']");
        Assert.NotNull(cut.Find("[data-testid='token-name-input']"));
        Assert.NotNull(cut.Find("[data-testid='create-token-button']"));
        Assert.True(cut.Find("[data-testid='token-expiry-90']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='token-expiry-30']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='token-expiry-365']").HasAttribute("checked"));

        // Assert: an empty list, no value box, and the Help topic as the only anchor in the card, shown as a button
        Assert.NotNull(cut.Find("[data-testid='token-list-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='new-token-box']"));
        var link = Assert.Single(card.QuerySelectorAll("a"));
        Assert.Equal("tokens-help-link", link.GetAttribute("data-testid"));
        Assert.Equal("/help/api-tokens", link.GetAttribute("href"));
        Assert.Contains("action", link.ClassList);
    }

    /// <summary>
    ///     Test that creating a token shows its value once with the chosen expiry, lists it without the value, and
    ///     that dismissing the box removes the value for good.
    /// </summary>
    [Fact]
    public async Task AccountPage_CreateToken_ValidInput_ShowsValueOnceAndListsToken()
    {
        // Arrange: the signed-in user exists, a name is typed and 30 days is chosen
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='token-name-input']").InputAsync("build server");
        await cut.Find("[data-testid='token-expiry-30']").ChangeAsync(new ChangeEventArgs { Value = "30" });

        // Act: submit the form
        await cut.Find("[data-testid='create-token-form']").SubmitAsync();

        // Assert: the value box shows a token once; the confirmation shows; the name field is empty
        var value = cut.Find("[data-testid='new-token-value']").TextContent;
        Assert.StartsWith("rcpat_", value, StringComparison.Ordinal);
        Assert.Equal("The token was created.", cut.Find("[data-testid='token-message']").TextContent);
        Assert.Equal(string.Empty, cut.Find("[data-testid='token-name-input']").GetAttribute("value") ?? string.Empty);

        // Assert: the list has the token with its name, display id and 30-day expiry, and never the value
        var token = Assert.Single(await Tokens.ListAsync(new Actor("ann", "Ann"), TestContext.Current.CancellationToken));
        Assert.Equal("build server", cut.Find($"[data-testid='token-name-{token.Id}']").TextContent);
        Assert.Equal(token.DisplayId, cut.Find($"[data-testid='token-display-id-{token.Id}']").TextContent);
        Assert.Equal("Active", cut.Find($"[data-testid='token-status-{token.Id}']").TextContent);
        Assert.Contains("2025-04-03", cut.Find($"[data-testid='token-expires-{token.Id}']").TextContent, StringComparison.Ordinal);
        Assert.Contains("never", cut.Find($"[data-testid='token-last-used-{token.Id}']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(value, cut.Find("[data-testid='token-list']").TextContent, StringComparison.Ordinal);

        // Act: dismiss the box
        await cut.Find("[data-testid='dismiss-token-button']").ClickAsync();

        // Assert: the value is nowhere on the page any more
        Assert.Empty(cut.FindAll("[data-testid='new-token-box']"));
        Assert.DoesNotContain(value, cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a missing name is shown as an error and no token is created.
    /// </summary>
    [Fact]
    public async Task AccountPage_CreateToken_EmptyName_ShowsErrorAndCreatesNothing()
    {
        // Arrange: the signed-in user exists and the name is empty
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var cut = Render<AccountPage>();

        // Act: submit
        await cut.Find("[data-testid='create-token-form']").SubmitAsync();

        // Assert: an error, no value box, nothing stored
        Assert.NotEmpty(cut.Find("[data-testid='token-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='new-token-box']"));
        Assert.Empty(TokenStore.Rows);
    }

    /// <summary>
    ///     Test that the copy button hands the token to the browser script and shows the result.
    /// </summary>
    [Fact]
    public async Task AccountPage_CopyToken_NewToken_CopiesThroughBrowserScript()
    {
        // Arrange: a created token, and a browser that can copy
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        JSInterop.Setup<bool>("reviewCenter.copyText", _ => true).SetResult(true);
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='token-name-input']").InputAsync("build server");
        await cut.Find("[data-testid='create-token-form']").SubmitAsync();
        var value = cut.Find("[data-testid='new-token-value']").TextContent;

        // Act: press copy
        await cut.Find("[data-testid='copy-token-button']").ClickAsync();

        // Assert: the script received exactly the value, and the page says so
        Assert.Equal([value], JSInterop.Invocations["reviewCenter.copyText"][^1].Arguments);
        Assert.Equal("Copied.", cut.Find("[data-testid='token-copy-status']").TextContent);
    }

    /// <summary>
    ///     Test that a browser that cannot copy gets a hint to copy by hand, and the token stays visible.
    /// </summary>
    [Fact]
    public async Task AccountPage_CopyToken_BrowserRefuses_ShowsHintAndKeepsToken()
    {
        // Arrange: a created token, and a browser that refuses
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        JSInterop.Setup<bool>("reviewCenter.copyText", _ => true).SetResult(false);
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='token-name-input']").InputAsync("build server");
        await cut.Find("[data-testid='create-token-form']").SubmitAsync();

        // Act: press copy
        await cut.Find("[data-testid='copy-token-button']").ClickAsync();

        // Assert: a hint to copy by hand, and the value is still shown
        Assert.Contains("copy it by hand", cut.Find("[data-testid='token-copy-status']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='new-token-value']"));
    }

    /// <summary>
    ///     Test that revoking marks the token as revoked, removes its button and does not touch other tokens.
    /// </summary>
    [Fact]
    public async Task AccountPage_RevokeToken_ActiveToken_MarksRevokedAndRemovesButton()
    {
        // Arrange: two tokens of the signed-in user
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var ann = new Actor("ann", "Ann");
        var first = await Tokens.CreateAsync(ann, "first", 90, TestContext.Current.CancellationToken);
        var second = await Tokens.CreateAsync(ann, "second", 90, TestContext.Current.CancellationToken);
        var cut = Render<AccountPage>();

        // Act: revoke the first
        await cut.Find($"[data-testid='revoke-token-{first.Token.Id}']").ClickAsync();

        // Assert: the first is revoked and has no button; the second is active and keeps its button
        Assert.Equal("Revoked", cut.Find($"[data-testid='token-status-{first.Token.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='revoke-token-{first.Token.Id}']"));
        Assert.Equal("Active", cut.Find($"[data-testid='token-status-{second.Token.Id}']").TextContent);
        Assert.NotNull(cut.Find($"[data-testid='revoke-token-{second.Token.Id}']"));
        Assert.Equal("The token was revoked.", cut.Find("[data-testid='token-message']").TextContent);
    }

    /// <summary>
    ///     Test that an expired token is listed as expired and cannot be revoked.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_ExpiredToken_ShowsExpiredWithoutRevokeButton()
    {
        // Arrange: a 30-day token and a page rendered after it expired
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var created = await Tokens.CreateAsync(new Actor("ann", "Ann"), "old", 30, TestContext.Current.CancellationToken);
        Time.Advance(TimeSpan.FromDays(31));

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: expired, without a revoke button
        Assert.Equal("Expired", cut.Find($"[data-testid='token-status-{created.Token.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='revoke-token-{created.Token.Id}']"));
    }

    /// <summary>
    ///     Test that the twenty-first active token is refused with the limit shown as an error.
    /// </summary>
    [Fact]
    public async Task AccountPage_CreateToken_TwentyActive_ShowsLimitError()
    {
        // Arrange: twenty active tokens and a filled-in form
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        for (var i = 0; i < ReviewLimits.MaxActiveTokensPerUser; i++)
        {
            await Tokens.CreateAsync(new Actor("ann", "Ann"), $"token {i}", 90, TestContext.Current.CancellationToken);
        }

        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='token-name-input']").InputAsync("one too many");

        // Act: submit
        await cut.Find("[data-testid='create-token-form']").SubmitAsync();

        // Assert: the limit is explained, no value is shown, and nothing was added
        Assert.Contains("20", cut.Find("[data-testid='token-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-token-box']"));
        Assert.Equal(ReviewLimits.MaxActiveTokensPerUser, TokenStore.Rows.Count);
    }

    /// <summary>
    ///     Test that a user who signed in with a temporary password sees the banner and the password form only:
    ///     no tokens and no appearance card.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_TemporaryPassword_ShowsBannerAndOnlyPasswordForm()
    {
        // Arrange: a user whose session carries the must-change-password claim
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        SignInAsTemporaryPassword("ann", "Ann");

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the banner and the form are shown; tokens and appearance are not
        Assert.NotNull(cut.Find("[data-testid='password-change-required-banner']"));
        Assert.NotNull(cut.Find("[data-testid='change-password-form']"));
        Assert.Empty(cut.FindAll("[data-testid='tokens-card']"));
        Assert.Empty(cut.FindAll("[data-testid='appearance-card']"));
    }

    /// <summary>
    ///     Test that a normal session shows no banner.
    /// </summary>
    [Fact]
    public void AccountPage_Render_NormalSession_ShowsNoBanner()
    {
        // Arrange: nothing to prepare

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: no banner, and the tokens card is there
        Assert.Empty(cut.FindAll("[data-testid='password-change-required-banner']"));
        Assert.NotNull(cut.Find("[data-testid='tokens-card']"));
    }

    /// <summary>
    ///     Test that replacing a temporary password sends the user to the sign-in page with a notice.
    /// </summary>
    [Fact]
    public async Task AccountPage_ChangePassword_TemporaryPassword_RedirectsToLoginWithNotice()
    {
        // Arrange: a flagged user and a filled-in form
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, OldPassword, TestContext.Current.CancellationToken);
        var flagged = (await UserStore.GetByUsernameAsync("ann", TestContext.Current.CancellationToken))!;
        flagged.MustChangePassword = true;
        await UserStore.UpdateAsync(flagged, null, TestContext.Current.CancellationToken);
        SignInAsTemporaryPassword("ann", "Ann");
        var cut = Render<AccountPage>();
        await FillAsync(cut, OldPassword, NewPassword, NewPassword);

        // Act: submit
        await cut.Find("[data-testid='change-password-form']").SubmitAsync();

        // Assert: the browser is sent to the login page with the notice flag, and the flag is cleared
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/login?changed=1", navigation.Uri, StringComparison.Ordinal);
        var user = await UserStore.GetByUsernameAsync("ann", TestContext.Current.CancellationToken);
        Assert.False(user!.MustChangePassword);
    }

    /// <summary>
    ///     Fills the three password fields.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="current">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="confirm">The repeated new password.</param>
    /// <returns>A task that completes when the fields are filled.</returns>
    private static async Task FillAsync(IRenderedComponent<AccountPage> cut, string current, string newPassword, string confirm)
    {
        await cut.Find("[data-testid='current-password-input']").InputAsync(current);
        await cut.Find("[data-testid='new-password-input']").InputAsync(newPassword);
        await cut.Find("[data-testid='confirm-password-input']").InputAsync(confirm);
    }
}
