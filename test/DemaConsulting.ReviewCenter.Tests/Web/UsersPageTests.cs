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
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal("System admin", cut.Find($"[data-testid='user-admin-{root.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='user-admin-{ann.Id}']"));
        Assert.Equal("Enabled", cut.Find($"[data-testid='user-enabled-{ann.Id}']").TextContent);
        Assert.Equal("Disable", cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").TextContent);
        Assert.Equal("Make system admin", cut.Find($"[data-testid='user-toggle-admin-{ann.Id}']").TextContent);
        Assert.Equal("Remove system admin", cut.Find($"[data-testid='user-toggle-admin-{root.Id}']").TextContent);
        Assert.NotNull(cut.Find($"[data-testid='user-password-input-{ann.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='user-set-password-{ann.Id}']"));
        Assert.Equal("/users/new", cut.Find("[data-testid='new-user-button']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='new-user-form']"));
        Assert.DoesNotContain(Password, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("hash:", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the new-user form creates the user with the chosen role and returns to the user list.
    /// </summary>
    [Fact]
    public async Task NewUserPage_CreateForm_ValidInput_CreatesUserAndClearsForm()
    {
        // Arrange: a signed-in administrator and the rendered page with the form filled in
        await SignInAsNewAdminAsync();
        var cut = Render<NewUserPage>();
        await cut.Find("[data-testid='new-user-username-input']").InputAsync("bea");
        await cut.Find("[data-testid='new-user-display-name-input']").InputAsync("Bea");
        await cut.Find("[data-testid='new-user-system-admin-checkbox']").ChangeAsync(true);
        await cut.Find("[data-testid='new-user-password-input']").InputAsync(Password);

        // Act: submit
        await cut.Find("[data-testid='new-user-form']").SubmitAsync();

        // Assert: the user exists with the role, the password field is empty and the browser returned to the list
        var created = await Users.GetAsync("bea", TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.True(created.IsSystemAdmin);
        Assert.Empty(cut.FindAll("[data-testid='users-error']"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-user-password-input']").GetAttribute("value") ?? string.Empty);
        Assert.EndsWith("/users", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Equal("/users", cut.Find("[data-testid='cancel-new-user-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a rule violation on creation is shown and keeps the typed name but not the password.
    /// </summary>
    [Fact]
    public async Task NewUserPage_CreateForm_TooShortPassword_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a signed-in administrator and the rendered page with a too short password
        await SignInAsNewAdminAsync();
        var cut = Render<NewUserPage>();
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

        // Act: disable (first click opens the confirmation, the second carries it out), then enable
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();
        await cut.Find($"[data-testid='user-confirm-disable-yes-{ann.Id}']").ClickAsync();
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
    ///     Test that the system-admin button of a row grants and takes away the flag.
    /// </summary>
    [Fact]
    public async Task UsersPage_ToggleSystemAdmin_User_GrantsAndTakesAwayTheFlag()
    {
        // Arrange: a signed-in administrator and a reviewer
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act: make ann a system admin
        await cut.Find($"[data-testid='user-toggle-admin-{ann.Id}']").ClickAsync();

        // Assert: stored and shown
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.IsSystemAdmin);
        Assert.Equal("System admin", cut.Find($"[data-testid='user-admin-{ann.Id}']").TextContent);

        // Act: take it away again
        await cut.Find($"[data-testid='user-toggle-admin-{ann.Id}']").ClickAsync();

        // Assert: stored and shown
        Assert.False((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.IsSystemAdmin);
        Assert.Empty(cut.FindAll($"[data-testid='user-admin-{ann.Id}']"));
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

        // Act: set the password (first click opens the confirmation, the second carries it out)
        await cut.Find($"[data-testid='user-set-password-{ann.Id}']").ClickAsync();
        await cut.Find($"[data-testid='user-confirm-set-password-yes-{ann.Id}']").ClickAsync();

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
        await cut.Find($"[data-testid='user-confirm-disable-yes-{root.Id}']").ClickAsync();
        var afterDisable = cut.Find("[data-testid='users-error']").TextContent;
        await cut.Find($"[data-testid='user-toggle-admin-{root.Id}']").ClickAsync();

        // Assert: both refused with the message, and the user is unchanged
        Assert.Contains("administrator", afterDisable, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cut.FindAll("[data-testid='users-error']"));
        var stored = await Users.GetAsync("root", TestContext.Current.CancellationToken);
        Assert.True(stored?.Enabled);
        Assert.True(stored?.IsSystemAdmin);
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

        // Assert: refused with an error, no list and no controls on the page, and nothing changed
        Assert.Contains("System administrator", cut.Find("[data-testid='users-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='users-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='user-row-']"));
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
    }

    /// <summary>
    ///     Test that an administrator who was demoted after opening the page loses the user list and the passwords
    ///     typed into it when the next action runs, and that no password is changed.
    /// </summary>
    [Fact]
    public async Task UsersPage_Action_AdminDemotedAfterOpening_DropsUserListAndTypedPasswords()
    {
        // Arrange: two administrators and a reviewer; one has the page open with a typed password, then is demoted
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<UsersPage>();
        await cut.Find($"[data-testid='user-password-input-{ann.Id}']").InputAsync("typed secret password");
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: try to set the password from the page that is still open (the first click already checks who acts)
        await cut.Find($"[data-testid='user-set-password-{ann.Id}']").ClickAsync();

        // Assert: the reason is shown instead of the list, the typed password is gone, and nothing changed
        Assert.Contains("System administrator", cut.Find("[data-testid='users-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='users-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='user-row-']"));
        Assert.Empty(cut.FindAll("[data-testid^='user-password-input-']"));
        Assert.Empty(cut.FindAll("[data-testid='new-user-button']"));
        Assert.DoesNotContain("typed secret password", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(await Users.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an administrator who takes the right away from themselves gets the stored change acknowledged,
    ///     but is not shown the reloaded user list.
    /// </summary>
    [Fact]
    public async Task UsersPage_ToggleSystemAdmin_AdminDemotesSelf_ShowsStatusButDropsTheList()
    {
        // Arrange: two administrators, one has the page open
        var root = await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<UsersPage>();

        // Act: take the administrator right away from the signed-in user
        await cut.Find($"[data-testid='user-toggle-admin-{root.Id}']").ClickAsync();

        // Assert: the change is stored and acknowledged, and the list is no longer rendered
        Assert.False((await Users.GetAsync("root", TestContext.Current.CancellationToken))?.IsSystemAdmin);
        Assert.Contains("no longer a system administrator", cut.Find("[data-testid='users-status']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='users-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='user-row-']"));
    }

    /// <summary>
    ///     Test that the page of an administrator who was demoted ends its session when the sign-in state is
    ///     re-checked, so the page and its actions are gone, and nothing is changed.
    /// </summary>
    [Fact]
    public async Task UsersPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions()
    {
        // Arrange: two administrators and a reviewer; one administrator has the page open, re-checked every 100 ms
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        UseRevalidation(TimeSpan.FromMilliseconds(100));
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<UsersPage>();
        var toggle = cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']");

        // Act: root is demoted, and the next check ends the session
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);
        await cut.WaitForAssertionAsync(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='session-ended']")),
            TimeSpan.FromSeconds(30));
        _ = await Record.ExceptionAsync(() => toggle.ClickAsync());

        // Assert: the page and its actions are gone, and ann is unchanged
        Assert.Empty(cut.FindAll("[data-testid='users-page']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-toggle-enabled-{ann.Id}']"));
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
    }

    /// <summary>
    ///     Test that an action sent after the session ended, but before the page learned of it, shows a clear
    ///     message instead of failing the page, and changes nothing.
    /// </summary>
    [Fact]
    public async Task UsersPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing()
    {
        // Arrange: an administrator with the page open, then the session ends without the page being told
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();
        SignOutQuietly();

        // Act: try to disable a user
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Assert: a message says why, the list is gone, and ann is unchanged
        Assert.Contains("no longer signed in", cut.Find("[data-testid='users-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='users-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='user-row-']"));
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
    }

    /// <summary>
    ///     Test that the badges show a running lockout, a temporary password and the last sign-in, and that a user
    ///     without any of them shows none of the first two and "Never" as the last sign-in.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_StatusBadges_ShowLockedTemporaryAndLastSignIn()
    {
        // Arrange: a locked user, a user with a temporary password who signed in, and a plain user
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var bea = await Users.CreateAsync("bea", "Bea", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cyd = await Users.CreateAsync("cyd", "Cyd", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await UpdateUserAsync("ann", user => user.LockoutUntil = Time.GetUtcNow() + TimeSpan.FromMinutes(5));
        await UpdateUserAsync("bea", user =>
        {
            user.MustChangePassword = true;
            user.LastSignInUtc = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero);
        });

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: each badge shows where it applies and only there; the lockout end is the time of day in UTC
        Assert.Equal("Locked until 05:11 UTC", cut.Find($"[data-testid='user-locked-{ann.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='user-locked-{bea.Id}']"));
        Assert.Equal("Temporary password", cut.Find($"[data-testid='user-temporary-{bea.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='user-temporary-{ann.Id}']"));
        Assert.Equal("Last sign-in: 2025-02-03 04:05 UTC", cut.Find($"[data-testid='user-last-signin-{bea.Id}']").TextContent);
        Assert.Equal("Last sign-in: Never", cut.Find($"[data-testid='user-last-signin-{cyd.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that a lockout that has run out is not shown as locked, and has no Unlock button.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_ExpiredLockout_IsNotShownAsLocked()
    {
        // Arrange: a user whose lockout ended a minute ago
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await UpdateUserAsync("ann", user => user.LockoutUntil = Time.GetUtcNow() - TimeSpan.FromMinutes(1));

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: neither the badge nor the button is there
        Assert.Empty(cut.FindAll($"[data-testid='user-locked-{ann.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-unlock-{ann.Id}']"));
    }

    /// <summary>
    ///     Test that the search matches the user name and the display name without regard to case.
    /// </summary>
    [Fact]
    public async Task UsersPage_Search_TypedText_MatchesUserNameOrDisplayNameIgnoringCase()
    {
        // Arrange: three users
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Annabel Lee", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var bea = await Users.CreateAsync("bea", "Beatrice", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cyd = await Users.CreateAsync("cyd", "Cyd", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act: search for part of a display name in another case
        await cut.Find("[data-testid='users-search']").InputAsync("ANNABEL");
        var byDisplayName = ShownUserIds(cut);

        // Act: search for part of a user name
        await cut.Find("[data-testid='users-search']").InputAsync("yd");
        var byUserName = ShownUserIds(cut);

        // Act: clear the search
        await cut.Find("[data-testid='users-search']").InputAsync(string.Empty);

        // Assert: only the matching user is listed each time, and everyone again after clearing
        Assert.Equal([ann.Id], byDisplayName);
        Assert.Equal([cyd.Id], byUserName);
        Assert.Equal(4, ShownUserIds(cut).Count);
        Assert.Contains(bea.Id, ShownUserIds(cut));
    }

    /// <summary>
    ///     Test that each status filter lists exactly the users it names.
    /// </summary>
    [Fact]
    public async Task UsersPage_Filter_EachStatus_ListsOnlyMatchingUsers()
    {
        // Arrange: root (system admin, enabled), a disabled user, a locked user and a plain user
        await SignInAsNewAdminAsync();
        var root = (await Users.GetAsync("root", TestContext.Current.CancellationToken))!;
        var off = await Users.CreateAsync("off", "Off", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var locked = await Users.CreateAsync("locked", "Locked", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var plain = await Users.CreateAsync("plain", "Plain", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await UpdateUserAsync("off", user => user.Enabled = false);
        await UpdateUserAsync("locked", user => user.LockoutUntil = Time.GetUtcNow() + TimeSpan.FromMinutes(5));
        var cut = Render<UsersPage>();

        // Act and assert: every filter value shows the matching users only
        await cut.Find("[data-testid='users-filter']").ChangeAsync("enabled");
        Assert.Equal([locked.Id, plain.Id, root.Id], ShownUserIds(cut));
        await cut.Find("[data-testid='users-filter']").ChangeAsync("disabled");
        Assert.Equal([off.Id], ShownUserIds(cut));
        await cut.Find("[data-testid='users-filter']").ChangeAsync("locked");
        Assert.Equal([locked.Id], ShownUserIds(cut));
        await cut.Find("[data-testid='users-filter']").ChangeAsync("admins");
        Assert.Equal([root.Id], ShownUserIds(cut));
        await cut.Find("[data-testid='users-filter']").ChangeAsync("all");
        Assert.Equal(4, ShownUserIds(cut).Count);
    }

    /// <summary>
    ///     Test that the search and the filter work together.
    /// </summary>
    [Fact]
    public async Task UsersPage_SearchAndFilter_Combined_BothApply()
    {
        // Arrange: two users with "a" in the name, one of them disabled
        await SignInAsNewAdminAsync();
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var abe = await Users.CreateAsync("abe", "Abe", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await UpdateUserAsync("abe", user => user.Enabled = false);
        var cut = Render<UsersPage>();

        // Act
        await cut.Find("[data-testid='users-search']").InputAsync("a");
        await cut.Find("[data-testid='users-filter']").ChangeAsync("disabled");

        // Assert
        Assert.Equal([abe.Id], ShownUserIds(cut));
    }

    /// <summary>
    ///     Test that disabling opens a confirmation first, which lists the open reviews and the projects the user
    ///     is the only administrator of as links, and that nothing is changed before the second click.
    /// </summary>
    [Fact]
    public async Task UsersPage_Disable_UserWithImpact_ShowsLinksAndChangesNothingUntilConfirmed()
    {
        // Arrange: ann waits for a review in Apollo and is its only project administrator
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(ann.Id, project.Id, ProjectRole.ProjectAdmin, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id,
            "REV-1",
            "First review",
            [new UploadedFile("a.txt", TestContent.A)],
            new Actor("root", "Root"),
            TestContext.Current.CancellationToken);
        await Signing.AddSlotAsync(new Actor("root", "Root"), project.Id, "REV-1", ann.Id, null, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act: the first click
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Assert: the panel lists both with links, and ann is still enabled
        Assert.NotNull(cut.Find($"[data-testid='user-confirm-disable-{ann.Id}']"));
        var review = cut.Find($"[data-testid='user-impact-review-{ann.Id}-REV-1-1']");
        Assert.Equal($"/projects/{project.Id}/reviews/REV-1", review.GetAttribute("href"));
        Assert.Contains("First review", review.TextContent, StringComparison.Ordinal);
        var impactText = cut.Find($"[data-testid='user-impact-{ann.Id}']").TextContent;
        Assert.Contains("Not signed", impactText, StringComparison.Ordinal);
        Assert.DoesNotContain("NotSigned", impactText, StringComparison.Ordinal);
        var projectLink = cut.Find($"[data-testid='user-impact-project-{ann.Id}-{project.Id}']");
        Assert.Equal($"/projects/{project.Id}", projectLink.GetAttribute("href"));
        Assert.Empty(cut.FindAll($"[data-testid='user-impact-empty-{ann.Id}']"));
        Assert.Equal("Disable anyway", cut.Find($"[data-testid='user-confirm-disable-yes-{ann.Id}']").TextContent);
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);

        // Act: the second click
        await cut.Find($"[data-testid='user-confirm-disable-yes-{ann.Id}']").ClickAsync();

        // Assert: the warning does not block, the user is disabled and the panel is gone
        Assert.False((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-disable-{ann.Id}']"));
        Assert.Equal("Disabled 'ann'.", cut.Find("[data-testid='users-status']").TextContent);
    }

    /// <summary>
    ///     Test that an empty impact says so and still needs the second click.
    /// </summary>
    [Fact]
    public async Task UsersPage_Disable_UserWithoutImpact_SaysNothingIsWaitingAndStillNeedsTheClick()
    {
        // Arrange: a user nothing depends on
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Assert: the empty message is shown, no links, and the user is still enabled
        Assert.Contains("Nothing is waiting", cut.Find($"[data-testid='user-impact-empty-{ann.Id}']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll($"[data-testid='user-impact-{ann.Id}']"));
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
    }

    /// <summary>
    ///     Test that cancelling the disable confirmation closes it and changes nothing.
    /// </summary>
    [Fact]
    public async Task UsersPage_Disable_Cancel_ClosesThePanelAndChangesNothing()
    {
        // Arrange: the confirmation is open
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Act
        await cut.Find($"[data-testid='user-confirm-disable-cancel-{ann.Id}']").ClickAsync();

        // Assert: the panel is gone, the user is enabled, and nothing is reported
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-disable-{ann.Id}']"));
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
        Assert.Empty(cut.FindAll("[data-testid='users-status']"));
        Assert.Empty(cut.FindAll("[data-testid='users-error']"));
    }

    /// <summary>
    ///     Test that enabling a disabled user stays one click, without a confirmation.
    /// </summary>
    [Fact]
    public async Task UsersPage_Enable_DisabledUser_NeedsNoConfirmation()
    {
        // Arrange: a disabled user
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await UpdateUserAsync("ann", user => user.Enabled = false);
        var cut = Render<UsersPage>();

        // Act
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Assert
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-disable-{ann.Id}']"));
    }

    /// <summary>
    ///     Test that a domain error while loading the impact is shown in the page's error style, without a panel.
    /// </summary>
    [Fact]
    public async Task UsersPage_Disable_ImpactLoadRefused_ShowsErrorAndOpensNoPanel()
    {
        // Arrange: the impact service reads a user store that does not know ann, so it answers with "user not found"
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        Services.AddSingleton(new UserImpactService(Store, new FakeUserStore(Store), Access));
        var cut = Render<UsersPage>();

        // Act
        await cut.Find($"[data-testid='user-toggle-enabled-{ann.Id}']").ClickAsync();

        // Assert: the domain error is shown in the error style and no panel opened
        Assert.Contains("does not exist", cut.Find("[data-testid='users-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-disable-{ann.Id}']"));
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.Enabled);
    }

    /// <summary>
    ///     Test that the two-factor reset explains the consequence first and removes nothing before the second
    ///     click, and that cancelling keeps the enrollment.
    /// </summary>
    [Fact]
    public async Task UsersPage_ResetTotp_FirstClickAndCancel_ExplainsAndRemovesNothing()
    {
        // Arrange: an enrolled user
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await EnrollTotpAsync("ann", Password);
        var cut = Render<UsersPage>();

        // Act: the first click
        await cut.Find($"[data-testid='user-reset-totp-{ann.Id}']").ClickAsync();

        // Assert: the panel explains the consequence and nothing is removed
        var text = cut.Find($"[data-testid='user-confirm-reset-totp-{ann.Id}']").TextContent;
        Assert.Contains("set up two-factor sign-in again", text, StringComparison.Ordinal);
        Assert.Contains("password alone", text, StringComparison.Ordinal);
        Assert.True(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));

        // Act: cancel
        await cut.Find($"[data-testid='user-confirm-reset-totp-cancel-{ann.Id}']").ClickAsync();

        // Assert: closed and still enrolled
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-reset-totp-{ann.Id}']"));
        Assert.True(await Totp.IsEnrolledAsync("ann", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that setting a password explains the consequences first, changes nothing before the second click,
    ///     and makes the password temporary.
    /// </summary>
    [Fact]
    public async Task UsersPage_SetPassword_FirstClick_ExplainsThenSecondClickMakesItTemporary()
    {
        // Arrange: a typed password
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();
        await cut.Find($"[data-testid='user-password-input-{ann.Id}']").InputAsync("brand new password");

        // Act: the first click
        await cut.Find($"[data-testid='user-set-password-{ann.Id}']").ClickAsync();

        // Assert: the panel names the consequences and the old password still works
        var text = cut.Find($"[data-testid='user-confirm-set-password-{ann.Id}']").TextContent;
        Assert.Contains("must change it at the next sign-in", text, StringComparison.Ordinal);
        Assert.Contains("only use the Account, Help, and About pages", text, StringComparison.Ordinal);
        Assert.Contains("cannot sign reviews", text, StringComparison.Ordinal);
        Assert.Contains("API tokens", text, StringComparison.Ordinal);
        Assert.NotNull(await Users.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken));

        // Act: the second click
        await cut.Find($"[data-testid='user-confirm-set-password-yes-{ann.Id}']").ClickAsync();

        // Assert: the password is temporary and shown so
        Assert.True((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.MustChangePassword);
        Assert.Equal("Temporary password", cut.Find($"[data-testid='user-temporary-{ann.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-set-password-{ann.Id}']"));
        Assert.Equal(
            "Password of 'ann' set; the user must change it at the next sign-in.",
            cut.Find("[data-testid='users-status']").TextContent);
    }

    /// <summary>
    ///     Test that the panel and the message for the own row of the signed-in administrator say that the password
    ///     is not temporary, and that it really is not.
    /// </summary>
    [Fact]
    public async Task UsersPage_SetPassword_OwnRow_SaysThePasswordIsNotTemporary()
    {
        // Arrange: the signed-in administrator types a new password in the own row
        await SignInAsNewAdminAsync();
        var root = (await Users.GetAsync("root", TestContext.Current.CancellationToken))!;
        var cut = Render<UsersPage>();
        await cut.Find($"[data-testid='user-password-input-{root.Id}']").InputAsync("brand new password");

        // Act: the first click
        await cut.Find($"[data-testid='user-set-password-{root.Id}']").ClickAsync();

        // Assert: the panel does not claim a forced change or a restriction
        var text = cut.Find($"[data-testid='user-confirm-set-password-{root.Id}']").TextContent;
        Assert.Contains("Set your own password", text, StringComparison.Ordinal);
        Assert.Contains("is not temporary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("must change it", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Account, Help", text, StringComparison.Ordinal);

        // Act: the second click
        await cut.Find($"[data-testid='user-confirm-set-password-yes-{root.Id}']").ClickAsync();

        // Assert: the password is set, not temporary, and the message says so
        Assert.NotNull(await Users.AuthenticateAsync("root", "brand new password", TestContext.Current.CancellationToken));
        Assert.False((await Users.GetAsync("root", TestContext.Current.CancellationToken))?.MustChangePassword);
        Assert.Empty(cut.FindAll($"[data-testid='user-temporary-{root.Id}']"));
        Assert.Equal("Password of 'root' set; it is not temporary.", cut.Find("[data-testid='users-status']").TextContent);
    }

    /// <summary>
    ///     Test that cancelling the set-password confirmation changes nothing, and that an empty field opens no
    ///     confirmation and asks for a password.
    /// </summary>
    [Fact]
    public async Task UsersPage_SetPassword_CancelOrEmptyField_ChangesNothing()
    {
        // Arrange
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var cut = Render<UsersPage>();

        // Act: an empty field
        await cut.Find($"[data-testid='user-set-password-{ann.Id}']").ClickAsync();

        // Assert: asked to type a password, no panel
        Assert.Equal("Type a new password first.", cut.Find("[data-testid='users-error']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='user-confirm-set-password-{ann.Id}']"));

        // Act: type, open, cancel
        await cut.Find($"[data-testid='user-password-input-{ann.Id}']").InputAsync("brand new password");
        await cut.Find($"[data-testid='user-set-password-{ann.Id}']").ClickAsync();
        await cut.Find($"[data-testid='user-confirm-set-password-cancel-{ann.Id}']").ClickAsync();

        // Assert: the old password still works and the user is not marked
        Assert.NotNull(await Users.AuthenticateAsync("ann", Password, TestContext.Current.CancellationToken));
        Assert.False((await Users.GetAsync("ann", TestContext.Current.CancellationToken))?.MustChangePassword);
    }

    /// <summary>
    ///     Test that the Unlock button ends a lockout in one click and is shown for locked users only.
    /// </summary>
    [Fact]
    public async Task UsersPage_Unlock_LockedUser_ClearsLockoutInOneClick()
    {
        // Arrange: a locked and an unlocked user
        await SignInAsNewAdminAsync();
        var ann = await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        var bea = await Users.CreateAsync("bea", "Bea", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        await UpdateUserAsync("ann", user =>
        {
            user.LockoutUntil = Time.GetUtcNow() + TimeSpan.FromMinutes(5);
            user.FailedLoginCount = 5;
        });
        var cut = Render<UsersPage>();
        Assert.Empty(cut.FindAll($"[data-testid='user-unlock-{bea.Id}']"));

        // Act
        await cut.Find($"[data-testid='user-unlock-{ann.Id}']").ClickAsync();

        // Assert: unlocked in the store, the badge and the button are gone, and the page says so
        var stored = await Users.GetAsync("ann", TestContext.Current.CancellationToken);
        Assert.Null(stored?.LockoutUntil);
        Assert.Equal(0, stored?.FailedLoginCount);
        Assert.Empty(cut.FindAll($"[data-testid='user-locked-{ann.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-unlock-{ann.Id}']"));
        Assert.Equal("Unlocked 'ann'.", cut.Find("[data-testid='users-status']").TextContent);
    }

    /// <summary>
    ///     Reads the identities of the users that are listed, in the order shown.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <returns>The user identities of the rows.</returns>
    private static List<int> ShownUserIds(IRenderedComponent<UsersPage> cut)
    {
        const string prefix = "user-row-";
        return [.. cut.FindAll("[data-testid^='user-row-']")
            .Select(row => int.Parse(row.GetAttribute("data-testid")![prefix.Length..], System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>
    ///     Changes a stored user directly, as a fixture would.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="change">The change to apply to the stored user.</param>
    /// <returns>A task that completes when the change is stored.</returns>
    private async Task UpdateUserAsync(string username, Action<User> change)
    {
        var user = (await UserStore.GetByUsernameAsync(username, TestContext.Current.CancellationToken))!;
        change(user);
        await UserStore.UpdateAsync(user, null, TestContext.Current.CancellationToken);
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
