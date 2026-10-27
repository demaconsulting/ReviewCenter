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
