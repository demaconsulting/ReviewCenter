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
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the two-factor parts of the <see cref="UsersPage"/> component: the badge and the reset.
/// </summary>
public class UsersTotpPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Test that only an enrolled user has the badge and the reset button.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_EnrolledAndPlainUsers_BadgeAndResetOnlyForEnrolled()
    {
        // Arrange: an administrator, an enrolled reviewer and a reviewer without a second factor
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var bea = await Users.CreateAsync("bea", "Bea", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await EnrollTotpAsync("ann", Password);

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: the enrolled user has the badge and the button; the others have neither
        Assert.Equal("TOTP", cut.Find($"[data-testid='user-totp-{ann.Id}']").TextContent);
        Assert.NotNull(cut.Find($"[data-testid='user-reset-totp-{ann.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-totp-{bea.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-reset-totp-{bea.Id}']"));
        Assert.Single(cut.FindAll("[data-testid^='user-totp-']"));
    }

    /// <summary>
    ///     Test that an account of an identity provider never has the badge or the button.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_ExternalUser_HasNoTotpControls()
    {
        // Arrange: an administrator and an account of the provider
        await SignInAsNewAdminAsync();
        var external = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-1", "zoe", "Zoe", TestContext.Current.CancellationToken)).User!;

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: no badge and no button
        Assert.Empty(cut.FindAll($"[data-testid='user-totp-{external.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-reset-totp-{external.Id}']"));
    }

    /// <summary>
    ///     Test that the reset removes the second factor, says so, and removes the badge and the button.
    /// </summary>
    [Fact]
    public async Task UsersPage_ResetTotp_EnrolledUser_RemovesEnrollmentAndShowsStatus()
    {
        // Arrange: an administrator and an enrolled reviewer
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await EnrollTotpAsync("ann", Password);
        var cut = Render<UsersPage>();

        // Act: reset (first click opens the confirmation, the second carries it out)
        await cut.Find($"[data-testid='user-reset-totp-{ann.Id}']").ClickAsync();
        await cut.Find($"[data-testid='user-confirm-reset-totp-yes-{ann.Id}']").ClickAsync();

        // Assert: nothing is enrolled any more, the page says so, and the badge and the button are gone
        Assert.False(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
        Assert.Contains("was reset", cut.Find("[data-testid='users-status']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll($"[data-testid='user-totp-{ann.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-reset-totp-{ann.Id}']"));
        Assert.Empty(cut.FindAll("[data-testid='users-error']"));
    }

    /// <summary>
    ///     Test that the badge and the button stay while the feature is switched off, so an enrollment that is
    ///     dormant can still be cleared.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_FeatureDisabled_StillShowsBadgeAndReset()
    {
        // Arrange: an enrolled reviewer, and the feature is then switched off
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await EnrollTotpAsync("ann", Password);
        SwitchTotpOff();

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: the badge and the button are there
        Assert.NotNull(cut.Find($"[data-testid='user-totp-{ann.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='user-reset-totp-{ann.Id}']"));
    }

    /// <summary>
    ///     Test that a reset after the session ended shows the clear message and keeps the enrollment.
    /// </summary>
    [Fact]
    public async Task UsersPage_ResetTotp_SessionEndedBeforePageKnows_ShowsErrorAndKeepsEnrollment()
    {
        // Arrange: an administrator with the page open, then the session ends without the page being told
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await EnrollTotpAsync("ann", Password);
        var cut = Render<UsersPage>();
        SignOutQuietly();

        // Act: try to reset (the second click is the one that is checked)
        await cut.Find($"[data-testid='user-reset-totp-{ann.Id}']").ClickAsync();
        await cut.Find($"[data-testid='user-confirm-reset-totp-yes-{ann.Id}']").ClickAsync();

        // Assert: a message says why, and the enrollment is unchanged
        Assert.Contains("no longer signed in", cut.Find("[data-testid='users-error']").TextContent, StringComparison.Ordinal);
        Assert.True(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the service behind the button refuses a user who is not a system administrator, so that a
    ///     project administrator or a reviewer cannot reset a second factor even by sending the request directly.
    ///     A project administrator is an ordinary user who holds a membership, so the user store has no flag for it.
    /// </summary>
    [Fact]
    public async Task TotpService_Reset_NotSystemAdmin_IsRefusedAndKeepsEnrollment()
    {
        // Arrange: an enrolled user and another user who is not a system administrator
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("pat", "Pat", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await EnrollTotpAsync("ann", Password);

        // Act: the other user tries to reset
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => Totp.ResetAsync(new Actor("pat", "Pat"), "ann", TestContext.Current.CancellationToken));

        // Assert: refused, and ann is still enrolled
        Assert.Equal(DomainRule.ActorNotAllowed, exception.Rule);
        Assert.True(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Creates an enabled system administrator in the user store and signs in as that user.
    /// </summary>
    /// <returns>A task that completes when the user is created and signed in.</returns>
    private async Task SignInAsNewAdminAsync()
    {
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
    }
}
