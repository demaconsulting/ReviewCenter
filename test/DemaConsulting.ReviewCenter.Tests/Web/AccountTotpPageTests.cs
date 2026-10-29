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
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the two-factor card of the <see cref="AccountPage"/> component, rendered in memory with bUnit.
/// </summary>
public class AccountTotpPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Initializes a new instance of the <see cref="AccountTotpPageTests"/> class; the page asks the browser for
    ///     the stored appearance, which these tests do not script.
    /// </summary>
    public AccountTotpPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    ///     Test that a local user without two-factor sign-in sees the card with the status Off and the start form.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_LocalUserNotEnrolled_ShowsStatusOffAndStartForm()
    {
        // Arrange: a local user who has not turned the second factor on
        await CreateAnnAsync();

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the card shows Off, the start form, and nothing else
        Assert.Equal("Off", cut.Find("[data-testid='totp-status']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='totp-start-password']"));
        Assert.NotNull(cut.Find("[data-testid='totp-start-button']"));
        Assert.Equal("/help/two-factor", cut.Find("[data-testid='totp-help-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='totp-secret']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-disable-form']"));
    }

    /// <summary>
    ///     Test that the card is absent when the feature is switched off in the settings.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_FeatureDisabled_HidesCard()
    {
        // Arrange: a local user, and the feature is off
        await CreateAnnAsync();
        SwitchTotpOff();

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: no card at all
        Assert.Empty(cut.FindAll("[data-testid='totp-card']"));
    }

    /// <summary>
    ///     Test that an account of an identity provider sees a note that the provider handles it, and no controls.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_ExternalUser_ShowsNoteAndNoControls()
    {
        // Arrange: the signed-in user is a provider account
        Services.Configure<OidcOptions>(options =>
        {
            options.Enabled = true;
            options.DisplayName = "Contoso";
        });
        var user = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-1", "zoe", "Zoe", TestContext.Current.CancellationToken)).User!;
        SignInAs(user.Username, "Zoe", false);

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: a sentence names the provider, and there is nothing to set up
        Assert.Contains("Contoso", cut.Find("[data-testid='totp-external']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='totp-start-form']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-status']"));
    }

    /// <summary>
    ///     Test that a user who signed in with a temporary password does not see the card.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_TemporaryPassword_HidesCard()
    {
        // Arrange: the user must replace the password first
        await CreateAnnAsync();
        SignInAsTemporaryPassword("ann", "Ann");

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: only the password form is offered
        Assert.Empty(cut.FindAll("[data-testid='totp-card']"));
    }

    /// <summary>
    ///     Test that a wrong password stops the set-up with an error and empties the field.
    /// </summary>
    [Fact]
    public async Task AccountPage_Start_WrongPassword_ShowsErrorAndNoSecret()
    {
        // Arrange: a local user and a wrong password
        await CreateAnnAsync();
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-start-password']").InputAsync("not the password 1");

        // Act: start
        await cut.Find("[data-testid='totp-start-form']").SubmitAsync();

        // Assert: an error, no secret, and the field is empty
        Assert.NotEmpty(cut.Find("[data-testid='totp-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='totp-secret']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='totp-start-password']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that the right password shows the secret and the address as text, and empties the password field.
    /// </summary>
    [Fact]
    public async Task AccountPage_Start_RightPassword_ShowsSecretAndAddress()
    {
        // Arrange: a local user
        await CreateAnnAsync();
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-start-password']").InputAsync(Password);

        // Act: start
        await cut.Find("[data-testid='totp-start-form']").SubmitAsync();

        // Assert: the secret in groups of four, the address with the same secret, and the confirm form
        var secret = SecretShown(cut);
        Assert.Equal(32, secret.Length);
        var uri = cut.Find("[data-testid='totp-uri']").TextContent;
        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains($"secret={secret}", uri, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='totp-confirm-code']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-start-form']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-error']"));
    }

    /// <summary>
    ///     Test that a wrong confirmation code shows the generic error, keeps the set-up open and turns nothing on.
    /// </summary>
    [Fact]
    public async Task AccountPage_Confirm_WrongCode_ShowsGenericErrorAndStaysPending()
    {
        // Arrange: a set-up in progress and a wrong code
        await CreateAnnAsync();
        var cut = await StartEnrollmentAsync();
        await cut.Find("[data-testid='totp-confirm-code']").InputAsync("000000");

        // Act: confirm
        await cut.Find("[data-testid='totp-confirm-form']").SubmitAsync();

        // Assert: the generic error, the set-up is still open, no recovery codes, and the field is empty
        Assert.Equal("The code is not valid or was already used.", cut.Find("[data-testid='totp-error']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='totp-secret']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-recovery-codes']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='totp-confirm-code']").GetAttribute("value") ?? string.Empty);
        Assert.False(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the right code turns the second factor on, shows ten recovery codes once, and that the secret
    ///     is gone from the page from then on.
    /// </summary>
    [Fact]
    public async Task AccountPage_Confirm_RightCode_ShowsTenRecoveryCodesOnceAndNoSecret()
    {
        // Arrange: a set-up in progress and the code the authenticator app shows
        await CreateAnnAsync();
        var cut = await StartEnrollmentAsync();
        var secret = SecretShown(cut);
        await cut.Find("[data-testid='totp-confirm-code']").InputAsync(TotpOracle.CodeAt(TotpOracle.FromBase32(secret), Time.GetUtcNow()));

        // Act: confirm
        await cut.Find("[data-testid='totp-confirm-form']").SubmitAsync();

        // Assert: the status is On, ten distinct recovery codes with the warning, and no secret anywhere
        Assert.Equal("On", cut.Find("[data-testid='totp-status']").TextContent);
        Assert.Contains("10 recovery codes left", cut.Find("[data-testid='totp-recovery-left']").TextContent, StringComparison.Ordinal);
        var codes = cut.FindAll("[data-testid='totp-recovery-codes'] code").Select(code => code.TextContent).ToArray();
        Assert.Equal(10, codes.Length);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.Contains("cannot be shown again", cut.Find("[data-testid='totp-recovery-box']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='totp-secret']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-uri']"));
        Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        Assert.True(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));

        // Act: dismiss the codes
        await cut.Find("[data-testid='totp-recovery-dismiss']").ClickAsync();

        // Assert: none of the codes is on the page any more
        Assert.Empty(cut.FindAll("[data-testid='totp-recovery-box']"));
        foreach (var code in codes)
        {
            Assert.DoesNotContain(code, cut.Markup, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that cancelling the set-up removes the secret from the page and leaves the user not enrolled.
    /// </summary>
    [Fact]
    public async Task AccountPage_Cancel_PendingEnrollment_RemovesSecretAndShowsStartForm()
    {
        // Arrange: a set-up in progress
        await CreateAnnAsync();
        var cut = await StartEnrollmentAsync();
        var secret = SecretShown(cut);

        // Act: cancel
        await cut.Find("[data-testid='totp-cancel-button']").ClickAsync();

        // Assert: the start form is back and the secret is not on the page
        Assert.NotNull(cut.Find("[data-testid='totp-start-form']"));
        Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        Assert.False(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an enrolled user sees status On and the number of recovery codes left, but no secret.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_Enrolled_ShowsOnAndRecoveryCodesLeft()
    {
        // Arrange: an enrolled user
        await CreateAnnAsync();
        await EnrollTotpAsync("ann", Password);

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: the status, the count, the two forms, and no secret or codes
        Assert.Equal("On", cut.Find("[data-testid='totp-status']").TextContent);
        Assert.Contains("10 recovery codes left", cut.Find("[data-testid='totp-recovery-left']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid='totp-disable-form']"));
        Assert.NotNull(cut.Find("[data-testid='totp-regen-form']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-secret']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-recovery-codes']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-start-form']"));
    }

    /// <summary>
    ///     Test that turning it off with a wrong code shows the generic error, keeps it on and empties both fields.
    /// </summary>
    [Fact]
    public async Task AccountPage_Disable_WrongCode_ShowsGenericErrorKeepsItOnAndClearsFields()
    {
        // Arrange: an enrolled user, the right password and a wrong code
        await CreateAnnAsync();
        await EnrollTotpAsync("ann", Password);
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-disable-password']").InputAsync(Password);
        await cut.Find("[data-testid='totp-disable-code']").InputAsync("000000");

        // Act: turn off
        await cut.Find("[data-testid='totp-disable-form']").SubmitAsync();

        // Assert: the generic error, still on, both fields empty
        Assert.Equal("The code is not valid or was already used.", cut.Find("[data-testid='totp-error']").TextContent);
        Assert.Equal("On", cut.Find("[data-testid='totp-status']").TextContent);
        Assert.Equal(string.Empty, cut.Find("[data-testid='totp-disable-password']").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, cut.Find("[data-testid='totp-disable-code']").GetAttribute("value") ?? string.Empty);
        Assert.True(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a recovery code and the password turn the second factor off.
    /// </summary>
    [Fact]
    public async Task AccountPage_Disable_RecoveryCode_TurnsItOff()
    {
        // Arrange: an enrolled user who lost the authenticator and kept a recovery code
        await CreateAnnAsync();
        var (_, recovery) = await EnrollTotpAsync("ann", Password);
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-disable-password']").InputAsync(Password);
        await cut.Find("[data-testid='totp-disable-code']").InputAsync(recovery[0]);

        // Act: turn off
        await cut.Find("[data-testid='totp-disable-form']").SubmitAsync();

        // Assert: it is off and the start form is back
        Assert.Equal("Off", cut.Find("[data-testid='totp-status']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='totp-start-form']"));
        Assert.Empty(cut.FindAll("[data-testid='totp-error']"));
        Assert.False(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that new recovery codes are shown once, replace the old ones, and that the fields are emptied.
    /// </summary>
    [Fact]
    public async Task AccountPage_Regenerate_RightPasswordAndCode_ShowsNewCodesOnce()
    {
        // Arrange: an enrolled user; the code of the next time step, as the one of enrolling was used
        await CreateAnnAsync();
        var (secret, recovery) = await EnrollTotpAsync("ann", Password);
        Time.Advance(TimeSpan.FromSeconds(30));
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-regen-password']").InputAsync(Password);
        await cut.Find("[data-testid='totp-regen-code']").InputAsync(TotpOracle.CodeAt(secret, Time.GetUtcNow()));

        // Act: get new codes
        await cut.Find("[data-testid='totp-regen-form']").SubmitAsync();

        // Assert: ten new codes, none of the old ones, and empty fields
        var codes = cut.FindAll("[data-testid='totp-recovery-codes'] code").Select(code => code.TextContent).ToArray();
        Assert.Equal(10, codes.Length);
        Assert.Empty(codes.Intersect(recovery));
        Assert.Equal(string.Empty, cut.Find("[data-testid='totp-regen-password']").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, cut.Find("[data-testid='totp-regen-code']").GetAttribute("value") ?? string.Empty);

        // Act: dismiss
        await cut.Find("[data-testid='totp-recovery-dismiss']").ClickAsync();

        // Assert: the codes are gone from the page
        Assert.Empty(cut.FindAll("[data-testid='totp-recovery-codes']"));
    }

    /// <summary>
    ///     Test that a wrong code when asking for new recovery codes shows the generic error and shows no codes.
    /// </summary>
    [Fact]
    public async Task AccountPage_Regenerate_WrongCode_ShowsGenericErrorAndNoCodes()
    {
        // Arrange: an enrolled user and a wrong code
        await CreateAnnAsync();
        await EnrollTotpAsync("ann", Password);
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-regen-password']").InputAsync(Password);
        await cut.Find("[data-testid='totp-regen-code']").InputAsync("000000");

        // Act: get new codes
        await cut.Find("[data-testid='totp-regen-form']").SubmitAsync();

        // Assert: the generic error and no codes
        Assert.Equal("The code is not valid or was already used.", cut.Find("[data-testid='totp-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='totp-recovery-codes']"));
    }

    /// <summary>
    ///     Creates the signed-in user as a local user with the test password.
    /// </summary>
    /// <returns>A task that completes when the user exists.</returns>
    private async Task CreateAnnAsync()
    {
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Renders the page and starts the set-up with the right password.
    /// </summary>
    /// <returns>The page, showing the secret.</returns>
    private async Task<IRenderedComponent<AccountPage>> StartEnrollmentAsync()
    {
        var cut = Render<AccountPage>();
        await cut.Find("[data-testid='totp-start-password']").InputAsync(Password);
        await cut.Find("[data-testid='totp-start-form']").SubmitAsync();
        return cut;
    }

    /// <summary>
    ///     Reads the secret the page shows, without the spaces between the groups.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <returns>The secret as the authenticator app would take it.</returns>
    private static string SecretShown(IRenderedComponent<AccountPage> cut)
    {
        return cut.Find("[data-testid='totp-secret']").TextContent.Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
