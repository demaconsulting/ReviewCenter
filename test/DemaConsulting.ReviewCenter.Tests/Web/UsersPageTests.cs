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
///     Unit tests for the <see cref="UsersPage"/> component, rendered in memory with bUnit.
/// </summary>
public class UsersPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Test that the existing users are listed with their details and controls, and the creation form is there.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_ExistingUsers_ListsThemWithControls()
    {
        // Arrange: an administrator and a reviewer
        var root = await Users.CreateAsync("root", "Root User", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: each row shows the details and the controls; no password or hash is shown
        Assert.Equal("root", cut.Find($"[data-testid='user-username-{root.Id}']").TextContent);
        Assert.Equal("Root User", cut.Find($"[data-testid='user-display-name-{root.Id}']").TextContent);
        Assert.Equal("Admin", cut.Find($"[data-testid='user-role-{root.Id}']").TextContent);
        Assert.Equal("Enabled", cut.Find($"[data-testid='user-enabled-{ann.Id}']").TextContent);
        Assert.Equal("Disable", cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").TextContent);
        Assert.NotNull(cut.Find($"[data-testid='user-role-select-{ann.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='user-set-role-{ann.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='user-password-input-{ann.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='user-set-password-{ann.Id}']"));
        Assert.NotNull(cut.Find("[data-testid='create-user-button']"));
        Assert.DoesNotContain(Password, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("hash:", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the new-user form creates the user with the chosen role and clears the form.
    /// </summary>
    [Fact]
    public async Task UsersPage_CreateForm_ValidInput_CreatesUserAndClearsForm()
    {
        // Arrange: a signed-in administrator and the rendered page with the form filled in
        await SignInAsNewAdminAsync();
        var cut = Render<UsersPage>();
        await cut.Find("[data-testid='new-user-username-input']").InputAsync("bea");
        await cut.Find("[data-testid='new-user-display-name-input']").InputAsync("Bea");
        await cut.Find("[data-testid='new-user-role-select']").ChangeAsync("Admin");
        await cut.Find("[data-testid='new-user-password-input']").InputAsync(Password);

        // Act: submit
        await cut.Find("[data-testid='new-user-form']").SubmitAsync();

        // Assert: the user exists with the role, is listed, and the fields are empty
        var created = await Users.GetAsync("bea", TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal(UserRole.Admin, created.Role);
        Assert.Equal("bea", cut.Find($"[data-testid='user-username-{created.Id}']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='users-error']"));
        Assert.Contains("bea", cut.Find("[data-testid='users-status']").TextContent, StringComparison.Ordinal);
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-user-username-input']").GetAttribute("value") ?? string.Empty);
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-user-password-input']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that a rule violation on creation is shown and keeps the typed name but not the password.
    /// </summary>
    [Fact]
    public async Task UsersPage_CreateForm_TooShortPassword_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a signed-in administrator and the rendered page with a too short password
        await SignInAsNewAdminAsync();
        var cut = Render<UsersPage>();
        await cut.Find("[data-testid='new-user-username-input']").InputAsync("bea");
        await cut.Find("[data-testid='new-user-display-name-input']").InputAsync("Bea");
        await cut.Find("[data-testid='new-user-password-input']").InputAsync("short");

        // Act: submit
        await cut.Find("[data-testid='new-user-form']").SubmitAsync();

        // Assert: the error is shown, nothing was created, the name stays, the password is gone
        Assert.Contains("characters", cut.Find("[data-testid='users-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await Users.GetAsync("bea", TestContext.Current.CancellationToken));
        Assert.Equal("bea", cut.Find("[data-testid='new-user-username-input']").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-user-password-input']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that a user can be disabled and enabled again.
    /// </summary>
    [Fact]
    public async Task UsersPage_ToggleEnabled_User_DisablesAndEnablesAgain()
    {
        // Arrange: a signed-in administrator and a reviewer
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act: disable, then enable
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();
        var disabled = (await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled;
        var shownDisabled = cut.Find($"[data-testid='user-enabled-{ann.Id}']").TextContent;
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();
        var enabled = (await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled;

        // Assert: the stored state and the list follow
        Assert.False(disabled);
        Assert.Equal("Disabled", shownDisabled);
        Assert.True(enabled);
        Assert.Equal("Enabled", cut.Find($"[data-testid='user-enabled-{ann.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that the role chosen in a row is stored when the button is used.
    /// </summary>
    [Fact]
    public async Task UsersPage_SetRole_ChosenRole_IsStored()
    {
        // Arrange: a signed-in administrator and a reviewer
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act: choose the Admin role and save it
        await cut.Find($"[data-testid='user-role-select-{ann.Id}']").ChangeAsync("Admin");
        await cut.Find($"[data-testid='user-set-role-{ann.Id}']").ClickAsync();

        // Assert: stored and shown
        Assert.Equal(UserRole.Admin, (await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Role);
        Assert.Equal("Admin", cut.Find($"[data-testid='user-role-{ann.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that an administrator can set a new password for a user, and that it works.
    /// </summary>
    [Fact]
    public async Task UsersPage_SetPassword_ValidPassword_ChangesPasswordAndClearsField()
    {
        // Arrange: a signed-in administrator, a reviewer and a typed password
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();
        await cut.Find($"[data-testid='user-password-input-{ann.Id}']").InputAsync("brand new password");

        // Act: set the password
        await cut.Find($"[data-testid='user-set-password-{ann.Id}']").ClickAsync();

        // Assert: the new password works, the old does not, the field is empty, no error
        Assert.NotNull(await Users.AuthenticateAsync("ann", "brand new password", TestContext.Current.CancellationToken));
        Assert.Null(await Users.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken));
        Assert.Empty(cut.FindAll("[data-testid='users-error']"));
        Assert.Equal(string.Empty, cut.Find($"[data-testid='user-password-input-{ann.Id}']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that disabling or demoting the last enabled administrator is refused and shown as an error.
    /// </summary>
    [Fact]
    public async Task UsersPage_LastAdmin_DisableOrDemote_IsRefusedWithError()
    {
        // Arrange: a single administrator, who is signed in
        var root = await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<UsersPage>();

        // Act: try to disable, then try to demote
        await cut.Find($"[data-testid='user-toggle-enabled-{root.Id}']").ClickAsync();
        var afterDisable = cut.Find("[data-testid='users-error']").TextContent;
        await cut.Find($"[data-testid='user-role-select-{root.Id}']").ChangeAsync("Reviewer");
        await cut.Find($"[data-testid='user-set-role-{root.Id}']").ClickAsync();

        // Assert: both refused with the message, and the user is unchanged
        Assert.Contains("administrator", afterDisable, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cut.FindAll("[data-testid='users-error']"));
        var stored = await Users.GetAsync("root", TestContext.Current.CancellationToken);
        Assert.True(stored?.Enabled);
        Assert.Equal(UserRole.Admin, stored?.Role);
    }

    /// <summary>
    ///     Test that an action is refused with an error when the signed-in administrator was demoted after the
    ///     page was opened.
    /// </summary>
    [Fact]
    public async Task UsersPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError()
    {
        // Arrange: two administrators and a reviewer; one administrator is signed in and has the page open
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<UsersPage>();
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: try to disable a user from the page that is still open
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Assert: refused with an error, and nothing changed
        Assert.Contains("Administrator", cut.Find("[data-testid='users-error']").TextContent, StringComparison.Ordinal);
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
    }

    /// <summary>
    ///     Creates an enabled administrator in the user store and signs in as that user.
    /// </summary>
    /// <param name="username">The user name of the administrator.</param>
    /// <returns>A task that completes when the user is created and signed in.</returns>
    private async Task SignInAsNewAdminAsync(string username = "root")
    {
        await Users.CreateAsync(username, "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs(username, "Root", UserRole.Admin);
    }
}
