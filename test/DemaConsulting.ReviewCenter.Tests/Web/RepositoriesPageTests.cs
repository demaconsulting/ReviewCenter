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
///     Unit tests for the <see cref="RepositoriesPage"/> component, rendered in memory with bUnit.
/// </summary>
public class RepositoriesPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     A token used to check that it never reaches the markup.
    /// </summary>
    private const string Secret = "tkn-PageSecret-123";

    /// <summary>
    ///     The location used for private test repositories.
    /// </summary>
    private const string Location = "https://example.com/org/repo.git";

    /// <summary>
    ///     The administrator actor used to arrange data.
    /// </summary>
    private static readonly Actor _admin = new("root", "Root");

    /// <summary>
    ///     Test that repositories are listed with location, user name and the token state, and that neither the
    ///     token nor a delete control is ever shown.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete()
    {
        // Arrange: a private and a public repository
        await SignInAsNewAdminAsync();
        var priv = await Repositories.CreateAsync("private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var pub = await Repositories.CreateAsync("public", "https://example.com/open.git", null, null, _admin, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = Render<RepositoriesPage>();

        // Assert: details and state are shown, the token is not, and there is no delete control
        Assert.Equal("private", cut.Find($"[data-testid='repository-name-{priv.Id}']").TextContent);
        Assert.Equal(Location, cut.Find($"[data-testid='repository-location-{priv.Id}']").TextContent);
        Assert.Equal("user svc", cut.Find($"[data-testid='repository-username-{priv.Id}']").TextContent);
        Assert.Equal("token set", cut.Find($"[data-testid='repository-token-state-{priv.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='repository-token-state-{pub.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='repository-username-{pub.Id}']"));
        Assert.Equal("Enabled", cut.Find($"[data-testid='repository-enabled-{priv.Id}']").TextContent);
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("protected:", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("delete", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/repositories/new", cut.Find("[data-testid='new-repository-button']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='new-repository-form']"));
    }

    /// <summary>
    ///     Test that the add form registers the repository with a write-only token, never puts the token into the
    ///     markup and returns to the registry.
    /// </summary>
    [Fact]
    public async Task NewRepositoryPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields()
    {
        // Arrange: a signed-in administrator and the form filled in
        await SignInAsNewAdminAsync();
        var cut = Render<NewRepositoryPage>();
        await cut.Find("[data-testid='new-repository-name-input']").InputAsync("alpha");
        await cut.Find("[data-testid='new-repository-location-input']").InputAsync(Location);
        await cut.Find("[data-testid='new-repository-username-input']").InputAsync("svc");
        await cut.Find("[data-testid='new-repository-token-input']").InputAsync(Secret);
        Assert.Equal("password", cut.Find("[data-testid='new-repository-token-input']").GetAttribute("type"));

        // Act: submit
        await cut.Find("[data-testid='new-repository-form']").SubmitAsync();

        // Assert: registered with a token, the token is gone from the page and the browser returned to the registry
        var created = Assert.Single(await Repositories.ListAsync(_admin, TestContext.Current.CancellationToken));
        Assert.True(created.HasToken);
        Assert.Empty(cut.FindAll("[data-testid='repositories-error']"));
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-repository-token-input']").GetAttribute("value") ?? string.Empty);
        Assert.EndsWith("/repositories", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Equal("/repositories", cut.Find("[data-testid='cancel-new-repository-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a rule violation on creation is shown, keeps the typed name and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewRepositoryPage_CreateForm_InvalidLocation_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a signed-in administrator and a location with credentials of its own
        await SignInAsNewAdminAsync();
        var cut = Render<NewRepositoryPage>();
        await cut.Find("[data-testid='new-repository-name-input']").InputAsync("alpha");
        await cut.Find("[data-testid='new-repository-location-input']").InputAsync("https://user:pw@example.com/r.git");

        // Act: submit
        await cut.Find("[data-testid='new-repository-form']").SubmitAsync();

        // Assert: the error is shown, nothing was created and the name stays
        Assert.NotEmpty(cut.FindAll("[data-testid='repositories-error']"));
        Assert.Empty(await Repositories.ListAsync(_admin, TestContext.Current.CancellationToken));
        Assert.Equal("alpha", cut.Find("[data-testid='new-repository-name-input']").GetAttribute("value"));
    }

    /// <summary>
    ///     Test that editing with the default action keeps the stored token while other fields change.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Edit_KeepToken_ChangesFieldsAndKeepsToken()
    {
        // Arrange: a private repository and its open edit form
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync("private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var before = RepositoryStore.StoredToken(repo.Id);
        var cut = Render<RepositoriesPage>();
        await cut.Find($"[data-testid='repository-edit-{repo.Id}']").ClickAsync();

        // Act: change the user name and save without touching the token action
        Assert.Equal("Keep", cut.Find("[data-testid='edit-repository-token-action-select']").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[data-testid='edit-repository-token-input']"));
        await cut.Find("[data-testid='edit-repository-username-input']").InputAsync("other");
        await cut.Find("[data-testid='edit-repository-save-button']").ClickAsync();

        // Assert: the user name changed and the token is the same stored value
        Assert.Equal("user other", cut.Find($"[data-testid='repository-username-{repo.Id}']").TextContent);
        Assert.Equal("token set", cut.Find($"[data-testid='repository-token-state-{repo.Id}']").TextContent);
        Assert.Equal(before, RepositoryStore.StoredToken(repo.Id));
        Assert.Empty(cut.FindAll($"[data-testid='repository-edit-form-{repo.Id}']"));
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that replacing the token stores the new one, and that the new token never appears in the markup.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Edit_ReplaceToken_StoresNewTokenWriteOnly()
    {
        // Arrange: a private repository and its open edit form
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync("private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var before = RepositoryStore.StoredToken(repo.Id);
        var cut = Render<RepositoriesPage>();
        await cut.Find($"[data-testid='repository-edit-{repo.Id}']").ClickAsync();

        // Act: choose Replace, type a new token and save
        await cut.Find("[data-testid='edit-repository-token-action-select']").ChangeAsync("Replace");
        await cut.Find("[data-testid='edit-repository-token-input']").InputAsync("tkn-replacement");
        await cut.Find("[data-testid='edit-repository-save-button']").ClickAsync();

        // Assert: another encrypted value is stored, the state says token set and the token is not on the page
        Assert.NotEqual(before, RepositoryStore.StoredToken(repo.Id));
        Assert.Equal("token set", cut.Find($"[data-testid='repository-token-state-{repo.Id}']").TextContent);
        Assert.DoesNotContain("tkn-replacement", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that clearing the token removes it, so the repository is shown as having no token.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Edit_ClearToken_RemovesToken()
    {
        // Arrange: a private repository and its open edit form
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync("private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var cut = Render<RepositoriesPage>();
        await cut.Find($"[data-testid='repository-edit-{repo.Id}']").ClickAsync();

        // Act: choose Clear and save (an https repository may have a user name without a token)
        await cut.Find("[data-testid='edit-repository-token-action-select']").ChangeAsync("Clear");
        await cut.Find("[data-testid='edit-repository-save-button']").ClickAsync();

        // Assert: no token is stored
        Assert.Null(RepositoryStore.StoredToken(repo.Id));
        Assert.Empty(cut.FindAll($"[data-testid='repository-token-state-{repo.Id}']"));
    }

    /// <summary>
    ///     Test that cancelling an edit closes the form and changes nothing.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Edit_Cancel_ClosesFormAndChangesNothing()
    {
        // Arrange: a repository and its open edit form with a typed name
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync("alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var cut = Render<RepositoriesPage>();
        await cut.Find($"[data-testid='repository-edit-{repo.Id}']").ClickAsync();
        await cut.Find("[data-testid='edit-repository-name-input']").InputAsync("changed");

        // Act: cancel
        await cut.Find("[data-testid='edit-repository-cancel-button']").ClickAsync();

        // Assert: the form is gone and the name is unchanged
        Assert.Empty(cut.FindAll($"[data-testid='repository-edit-form-{repo.Id}']"));
        Assert.Equal("alpha", cut.Find($"[data-testid='repository-name-{repo.Id}']").TextContent);
    }

    /// <summary>
    ///     Test that a repository can be disabled and enabled again, and that disabling is the only way to retire it.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_ToggleEnabled_Repository_DisablesAndEnablesAgain()
    {
        // Arrange: a signed-in administrator and a repository
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync("alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var cut = Render<RepositoriesPage>();

        // Act: disable, then enable
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();
        var shownDisabled = cut.Find($"[data-testid='repository-enabled-{repo.Id}']").TextContent;
        var offered = await Repositories.ListEnabledAsync(TestContext.Current.CancellationToken);
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the list and the offered summaries follow
        Assert.Equal("Disabled", shownDisabled);
        Assert.Empty(offered);
        Assert.Equal("Enabled", cut.Find($"[data-testid='repository-enabled-{repo.Id}']").TextContent);
        Assert.Single(await Repositories.ListEnabledAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an action is refused with an error when the signed-in administrator was demoted after the
    ///     page was opened.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Action_SignedInAdminLaterDemoted_IsRefusedWithError()
    {
        // Arrange: two administrators and a repository; one has the page open and is then demoted
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var repo = await Repositories.CreateAsync("alpha", Location, null, null, new Actor("boss", "Boss"), TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<RepositoriesPage>();
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: try to disable the repository from the page that is still open
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: refused with an error and nothing changed
        Assert.Contains("Administrator", cut.Find("[data-testid='repositories-error']").TextContent, StringComparison.Ordinal);
        Assert.Single(await Repositories.ListEnabledAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the page of an administrator who was demoted ends its session when the sign-in state is
    ///     re-checked, so the page and its actions are gone.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Revalidation_AdminDemoted_PageShowsSessionEndedAndNoActions()
    {
        // Arrange: two administrators and a repository; one has the page open, re-checked every 100 ms
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var repo = await Repositories.CreateAsync("alpha", Location, null, null, new Actor("boss", "Boss"), TestContext.Current.CancellationToken);
        UseRevalidation(TimeSpan.FromMilliseconds(100));
        SignInAs("root", "Root", UserRole.Admin);
        var cut = Render<RepositoriesPage>();
        var toggle = cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']");

        // Act: root is demoted and the next check ends the session
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);
        await cut.WaitForAssertionAsync(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='session-ended']")),
            TimeSpan.FromSeconds(10));
        _ = await Record.ExceptionAsync(() => toggle.ClickAsync());

        // Assert: the page and its actions are gone, and the repository is unchanged
        Assert.Empty(cut.FindAll("[data-testid='repositories-page']"));
        Assert.Empty(cut.FindAll($"[data-testid='repository-toggle-enabled-{repo.Id}']"));
        Assert.Single(await Repositories.ListEnabledAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an action sent after the session ended, but before the page learned of it, shows a clear
    ///     message and changes nothing.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Action_SessionEndedBeforePageKnows_ShowsClearErrorAndChangesNothing()
    {
        // Arrange: an administrator with the page open, then the session ends without the page being told
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync("alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var cut = Render<RepositoriesPage>();
        SignOutQuietly();

        // Act: try to disable the repository
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: a message says why and nothing changed
        Assert.Contains("no longer signed in", cut.Find("[data-testid='repositories-error']").TextContent, StringComparison.Ordinal);
        Assert.Single(await Repositories.ListEnabledAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Creates an enabled administrator in the user store and signs in as that user.
    /// </summary>
    /// <returns>A task that completes when the user is created and signed in.</returns>
    private async Task SignInAsNewAdminAsync()
    {
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
    }
}
