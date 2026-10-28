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
///     Unit tests for the <see cref="RepositoriesPage"/> and <see cref="NewRepositoryPage"/> components, rendered in
///     memory with bUnit. The repositories belong to a project; the pages are for whoever manages that project.
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
    ///     A second token, typed into a form, used to check that it never stays on the page after access is lost.
    /// </summary>
    private const string NewSecret = "tkn-TypedSecret-456";

    /// <summary>
    ///     The location used for private test repositories.
    /// </summary>
    private const string Location = "https://example.com/org/repo.git";

    /// <summary>
    ///     The administrator actor used to arrange data.
    /// </summary>
    private static readonly Actor _admin = new("root", "Root");

    /// <summary>
    ///     The identity of the project the tests work in; set by <see cref="SignInAsNewAdminAsync"/>.
    /// </summary>
    private int _projectId;

    /// <summary>
    ///     Test that repositories are listed with location, user name and the token state, and that neither the
    ///     token nor a delete control is ever shown.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Render_Repositories_ShowsTokenStateOnlyAndNoDelete()
    {
        // Arrange: a private and a public repository
        await SignInAsNewAdminAsync();
        var priv = await Repositories.CreateAsync(_projectId, "private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var pub = await Repositories.CreateAsync(_projectId, "public", "https://example.com/open.git", null, null, _admin, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = RenderList();

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
        Assert.Equal(
            $"/projects/{_projectId}/repositories/new", cut.Find("[data-testid='new-repository-button']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='new-repository-form']"));
    }

    /// <summary>
    ///     Test that only the repositories of the project are listed.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Render_OtherProjectsRepositories_AreNotListed()
    {
        // Arrange: a repository in this project and one in another project
        await SignInAsNewAdminAsync();
        var other = await Projects.CreateAsync("Other", null, TestContext.Current.CancellationToken);
        var mine = await Repositories.CreateAsync(_projectId, "mine", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var theirs = await Repositories.CreateAsync(other.Id, "theirs", Location, null, null, _admin, TestContext.Current.CancellationToken);

        // Act: render the page of this project
        var cut = RenderList();

        // Assert: only the repository of this project is on the page
        Assert.NotEmpty(cut.FindAll($"[data-testid='repository-row-{mine.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='repository-row-{theirs.Id}']"));
    }

    /// <summary>
    ///     Test that a project admin manages the repositories of the project.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Render_ProjectAdmin_ShowsRepositoriesAndActions()
    {
        // Arrange: a project admin of the project, signed in as an ordinary user
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var pam = await Users.CreateAsync("pam", "Pam", false, Password, TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(pam.Id, _projectId, ProjectRole.ProjectAdmin, TestContext.Current.CancellationToken);
        SignInAs("pam", "Pam", false);

        // Act: render the page and disable the repository
        var cut = RenderList();
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the project admin sees the repositories and may change them
        Assert.Equal("Disabled", cut.Find($"[data-testid='repository-enabled-{repo.Id}']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='new-repository-button']"));
    }

    /// <summary>
    ///     Test that a plain member and a user who is not a member see only the not-found message, with no
    ///     repositories and no controls, the same as for a project that does not exist.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepositoriesPage_Render_MemberOrNonMember_ShowsNotFoundOnly(bool member)
    {
        // Arrange: a repository, and an ordinary user who is (or is not) a plain member of the project
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var pat = await Users.CreateAsync("pat", "Pat", false, Password, TestContext.Current.CancellationToken);
        if (member)
        {
            await Store.AddMembershipAsync(pat.Id, _projectId, TestContext.Current.CancellationToken);
        }

        SignInAs("pat", "Pat", false);

        // Act: render the page and the add page
        var list = RenderList();
        var add = RenderNew();

        // Assert: nothing but the not-found message, which is the one of a missing project
        Assert.NotEmpty(list.FindAll("[data-testid='project-not-found']"));
        Assert.NotEmpty(add.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(list.FindAll($"[data-testid='repository-row-{repo.Id}']"));
        Assert.Empty(list.FindAll("[data-testid='new-repository-button']"));
        Assert.Empty(add.FindAll("[data-testid='new-repository-form']"));
    }

    /// <summary>
    ///     Test that the add form registers the repository in the project with a write-only token, never puts the
    ///     token into the markup and returns to the repositories of the project.
    /// </summary>
    [Fact]
    public async Task NewRepositoryPage_CreateForm_WithToken_StoresItWriteOnlyAndClearsFields()
    {
        // Arrange: a signed-in administrator and the form filled in
        await SignInAsNewAdminAsync();
        var cut = RenderNew();
        await cut.Find("[data-testid='new-repository-name-input']").InputAsync("alpha");
        await cut.Find("[data-testid='new-repository-location-input']").InputAsync(Location);
        await cut.Find("[data-testid='new-repository-username-input']").InputAsync("svc");
        await cut.Find("[data-testid='new-repository-token-input']").InputAsync(Secret);
        Assert.Equal("password", cut.Find("[data-testid='new-repository-token-input']").GetAttribute("type"));

        // Act: submit
        await cut.Find("[data-testid='new-repository-form']").SubmitAsync();

        // Assert: registered in the project with a token, the token is gone from the page and the browser returned
        var created = Assert.Single(await Repositories.ListAsync(_admin, _projectId, TestContext.Current.CancellationToken));
        Assert.True(created.HasToken);
        Assert.Equal(_projectId, created.ProjectId);
        Assert.Empty(cut.FindAll("[data-testid='repositories-error']"));
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(string.Empty, cut.Find("[data-testid='new-repository-token-input']").GetAttribute("value") ?? string.Empty);
        Assert.EndsWith(
            $"/projects/{_projectId}/repositories", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Equal(
            $"/projects/{_projectId}/repositories", cut.Find("[data-testid='cancel-new-repository-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a rule violation on creation is shown, keeps the typed name and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewRepositoryPage_CreateForm_InvalidLocation_ShowsErrorAndCreatesNothing()
    {
        // Arrange: a signed-in administrator and a location with credentials of its own
        await SignInAsNewAdminAsync();
        var cut = RenderNew();
        await cut.Find("[data-testid='new-repository-name-input']").InputAsync("alpha");
        await cut.Find("[data-testid='new-repository-location-input']").InputAsync("******example.com/r.git");

        // Act: submit
        await cut.Find("[data-testid='new-repository-form']").SubmitAsync();

        // Assert: the error is shown, nothing was created and the name stays
        Assert.NotEmpty(cut.FindAll("[data-testid='repositories-error']"));
        Assert.Empty(await Repositories.ListAsync(_admin, _projectId, TestContext.Current.CancellationToken));
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
        var repo = await Repositories.CreateAsync(_projectId, "private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var before = RepositoryStore.StoredToken(repo.Id);
        var cut = RenderList();
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
        var repo = await Repositories.CreateAsync(_projectId, "private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var before = RepositoryStore.StoredToken(repo.Id);
        var cut = RenderList();
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
        var repo = await Repositories.CreateAsync(_projectId, "private", Location, "svc", Secret, _admin, TestContext.Current.CancellationToken);
        var cut = RenderList();
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
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var cut = RenderList();
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
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var cut = RenderList();

        // Act: disable, then enable
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();
        var shownDisabled = cut.Find($"[data-testid='repository-enabled-{repo.Id}']").TextContent;
        var offered = await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken);
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the list and the offered summaries follow
        Assert.Equal("Disabled", shownDisabled);
        Assert.Empty(offered);
        Assert.Equal("Enabled", cut.Find($"[data-testid='repository-enabled-{repo.Id}']").TextContent);
        Assert.Single(await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an action shows the not-found state, and no repository data, when the signed-in administrator
    ///     was demoted after the page was opened.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Action_SignedInAdminLaterDemoted_ShowsNotFoundAndNoData()
    {
        // Arrange: two administrators and a repository; one has the page open and is then demoted
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("boss", "Boss", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        _projectId = project.Id;
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, new Actor("boss", "Boss"), TestContext.Current.CancellationToken);
        SignInAs("root", "Root", UserRole.Admin);
        var cut = RenderList();
        await Users.SetRoleAsync("root", UserRole.Reviewer, TestContext.Current.CancellationToken);

        // Act: try to disable the repository from the page that is still open
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the state of a non-member and nothing changed
        AssertNotFoundWithoutRepositoryData(cut, "alpha", null);
        Assert.Single(await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a project admin who is demoted to a plain member while the page is open, with an edit form and
    ///     a typed token on screen, gets the not-found state and none of the repository, edit or token state when
    ///     the next action runs, and that nothing is changed.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Action_ProjectAdminDemotedToMember_ShowsNotFoundAndNoRepositoryData()
    {
        // Arrange: a project admin with the page open, an edit form with a typed replacement token, then demoted
        var repo = await ArrangeProjectAdminAsync();
        var before = RepositoryStore.StoredToken(repo.Id);
        var cut = RenderList();
        await cut.Find($"[data-testid='repository-edit-{repo.Id}']").ClickAsync();
        await cut.Find("[data-testid='edit-repository-token-action-select']").ChangeAsync("Replace");
        await cut.Find("[data-testid='edit-repository-token-input']").InputAsync(NewSecret);
        await ChangeMemberRoleAsync(_projectId, "pam", ProjectRole.User);

        // Act: use a control of the page that is still open
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the state of a plain member, no edit or token state anywhere, and the repository is unchanged
        AssertNotFoundWithoutRepositoryData(cut, "alpha", "svc-user");
        Assert.DoesNotContain(NewSecret, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='edit-repository-token-input']"));
        Assert.Single(await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken));
        Assert.Equal(before, RepositoryStore.StoredToken(repo.Id));
    }

    /// <summary>
    ///     Test that opening the edit form after the project admin was demoted to a plain member does not copy the
    ///     stored values into a form, but shows the not-found state.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_StartEdit_ProjectAdminDemoted_ShowsNotFoundAndNoEditForm()
    {
        // Arrange: a project admin with the page open, then demoted
        var repo = await ArrangeProjectAdminAsync();
        var cut = RenderList();
        await ChangeMemberRoleAsync(_projectId, "pam", ProjectRole.User);

        // Act: open the edit form of the repository
        await cut.Find($"[data-testid='repository-edit-{repo.Id}']").ClickAsync();

        // Assert: no edit form and none of the stored values on the page
        AssertNotFoundWithoutRepositoryData(cut, "alpha", "svc-user");
        Assert.Empty(cut.FindAll($"[data-testid='repository-edit-form-{repo.Id}']"));
    }

    /// <summary>
    ///     Test that when the right to administer is lost during the action, so that the reload of the list is
    ///     refused, the page shows the not-found state and neither the old rows nor the outcome of the change.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Action_AccessLostBeforeTheReload_ShowsNotFoundNotStaleList()
    {
        // Arrange: a project admin with the page open; the registry loses the right right after it let the change
        // through, so that only the reload of the list is refused
        var repo = await ArrangeProjectAdminAsync();
        var revoking = new RevokingAccess(Access, () => ChangeMemberRoleAsync(_projectId, "pam", ProjectRole.User));
        Services.AddSingleton(new RepositoryService(RepositoryStore, new FakeCredentialProtector(), revoking, Time));
        var cut = RenderList();
        revoking.Arm();

        // Act: disable the repository
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the change was stored, but the page shows the state of a plain member, with no old rows and no
        // success message
        Assert.True(revoking.Revoked);
        AssertNotFoundWithoutRepositoryData(cut, "alpha", "svc-user");
        Assert.Empty(await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a project admin who is demoted to a plain member while the add form is filled in gets the
    ///     not-found state, no form and no typed values on screen, and that nothing is registered.
    /// </summary>
    [Fact]
    public async Task NewRepositoryPage_CreateForm_ProjectAdminDemoted_ShowsNotFoundAndCreatesNothing()
    {
        // Arrange: a project admin with the filled-in add form open, then demoted
        await ArrangeProjectAdminAsync();
        var cut = RenderNew();
        await cut.Find("[data-testid='new-repository-name-input']").InputAsync("typed-name");
        await cut.Find("[data-testid='new-repository-location-input']").InputAsync("https://example.com/typed.git");
        await cut.Find("[data-testid='new-repository-username-input']").InputAsync("typed-user");
        await cut.Find("[data-testid='new-repository-token-input']").InputAsync(NewSecret);
        await ChangeMemberRoleAsync(_projectId, "pam", ProjectRole.User);

        // Act: submit the form
        await cut.Find("[data-testid='new-repository-form']").SubmitAsync();

        // Assert: the state of a plain member, none of the typed values, and only the arranged repository exists
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-repository-form']"));
        Assert.Empty(cut.FindAll("[data-testid='repositories-error']"));
        Assert.DoesNotContain("typed-name", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("typed-user", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(NewSecret, cut.Markup, StringComparison.Ordinal);
        Assert.Single(await Repositories.ListAsync(_admin, _projectId, TestContext.Current.CancellationToken));
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
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        _projectId = project.Id;
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, new Actor("boss", "Boss"), TestContext.Current.CancellationToken);
        UseRevalidation(TimeSpan.FromMilliseconds(100));
        SignInAs("root", "Root", UserRole.Admin);
        var cut = RenderList();
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
        Assert.Single(await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an action sent after the session ended, but before the page learned of it, shows the same
    ///     not-found state as for a user without the right, with no repository data, and changes nothing.
    /// </summary>
    [Fact]
    public async Task RepositoriesPage_Action_SessionEndedBeforePageKnows_ShowsNotFoundAndChangesNothing()
    {
        // Arrange: an administrator with the page open, then the session ends without the page being told
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, null, null, _admin, TestContext.Current.CancellationToken);
        var cut = RenderList();
        SignOutQuietly();

        // Act: try to disable the repository
        await cut.Find($"[data-testid='repository-toggle-enabled-{repo.Id}']").ClickAsync();

        // Assert: the not-found state without data, and nothing changed
        AssertNotFoundWithoutRepositoryData(cut, "alpha", null);
        Assert.Single(await Repositories.ListEnabledAsync(_admin, _projectId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Creates a system administrator, a project with one private repository and a project admin of it, and
    ///     signs in as the project admin.
    /// </summary>
    /// <returns>The repository, with the name <c>alpha</c>, the user name <c>svc-user</c> and a token.</returns>
    private async Task<SourceRepository> ArrangeProjectAdminAsync()
    {
        await SignInAsNewAdminAsync();
        var repo = await Repositories.CreateAsync(_projectId, "alpha", Location, "svc-user", Secret, _admin, TestContext.Current.CancellationToken);
        var pam = await Users.CreateAsync("pam", "Pam", false, Password, TestContext.Current.CancellationToken);
        await Store.AddMembershipAsync(pam.Id, _projectId, ProjectRole.ProjectAdmin, TestContext.Current.CancellationToken);
        SignInAs("pam", "Pam", false);
        return repo;
    }

    /// <summary>
    ///     Asserts that a page shows only the not-found state of a non-member: no list, row, edit form, token input,
    ///     message or button, and none of the repository details in the markup.
    /// </summary>
    /// <param name="cut">The rendered repositories page.</param>
    /// <param name="name">The repository name that must not appear.</param>
    /// <param name="username">The repository user name that must not appear, or <c>null</c> when there is none.</param>
    private static void AssertNotFoundWithoutRepositoryData(IRenderedComponent<RepositoriesPage> cut, string name, string? username)
    {
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='repositories-list']"));
        Assert.Empty(cut.FindAll("[data-testid^='repository-row-']"));
        Assert.Empty(cut.FindAll("[data-testid^='repository-edit-form-']"));
        Assert.Empty(cut.FindAll("[data-testid='edit-repository-token-input']"));
        Assert.Empty(cut.FindAll("[data-testid='repositories-error']"));
        Assert.Empty(cut.FindAll("[data-testid='repositories-status']"));
        Assert.Empty(cut.FindAll("[data-testid='new-repository-button']"));
        Assert.DoesNotContain(Location, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(name, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        if (username is not null)
        {
            Assert.DoesNotContain(username, cut.Markup, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Renders the repositories page of the project of the test.
    /// </summary>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<RepositoriesPage> RenderList()
    {
        return Render<RepositoriesPage>(parameters => parameters.Add(page => page.ProjectId, _projectId));
    }

    /// <summary>
    ///     Renders the add repository page of the project of the test.
    /// </summary>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<NewRepositoryPage> RenderNew()
    {
        return Render<NewRepositoryPage>(parameters => parameters.Add(page => page.ProjectId, _projectId));
    }

    /// <summary>
    ///     Creates an enabled system administrator and a project in the stores and signs in as that user.
    /// </summary>
    /// <returns>A task that completes when the user is created and signed in.</returns>
    private async Task SignInAsNewAdminAsync()
    {
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        var project = await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        _projectId = project.Id;
        SignInAs("root", "Root", UserRole.Admin);
    }

    /// <summary>
    ///     Access rules that behave as the wrapped ones, except that once armed they run a change (the loss of a
    ///     right) right after the next administer check has passed, so that a later check of the same operation
    ///     fails: the right is lost in the middle of a page action.
    /// </summary>
    private sealed class RevokingAccess : IProjectAccess
    {
        /// <summary>
        ///     The rules everything is delegated to.
        /// </summary>
        private readonly IProjectAccess _inner;

        /// <summary>
        ///     The change that takes the right away.
        /// </summary>
        private readonly Func<Task> _revoke;

        /// <summary>
        ///     Whether the next passed administer check runs the change.
        /// </summary>
        private bool _armed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RevokingAccess"/> class.
        /// </summary>
        /// <param name="inner">The rules everything is delegated to.</param>
        /// <param name="revoke">The change that takes the right away.</param>
        public RevokingAccess(IProjectAccess inner, Func<Task> revoke)
        {
            _inner = inner;
            _revoke = revoke;
        }

        /// <summary>
        ///     Gets a value indicating whether the change has run.
        /// </summary>
        public bool Revoked { get; private set; }

        /// <summary>
        ///     Makes the next passed administer check run the change.
        /// </summary>
        public void Arm()
        {
            _armed = true;
        }

        /// <inheritdoc />
        public Task<bool> IsSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
        {
            return _inner.IsSystemAdminAsync(actor, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> CanViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
        {
            return _inner.CanViewAsync(actor, projectId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> CanAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
        {
            return _inner.CanAdministerAsync(actor, projectId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> IsMemberAsync(int userId, int projectId, CancellationToken cancellationToken = default)
        {
            return _inner.IsMemberAsync(userId, projectId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<Project>> ListVisibleProjectsAsync(Actor? actor, CancellationToken cancellationToken = default)
        {
            return _inner.ListVisibleProjectsAsync(actor, cancellationToken);
        }

        /// <inheritdoc />
        public Task RequireViewAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
        {
            return _inner.RequireViewAsync(actor, projectId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task RequireAdministerAsync(Actor? actor, int projectId, CancellationToken cancellationToken = default)
        {
            // The check passes first; then the right is taken away, once, so that the next check fails
            await _inner.RequireAdministerAsync(actor, projectId, cancellationToken);
            if (_armed)
            {
                _armed = false;
                Revoked = true;
                await _revoke();
            }
        }

        /// <inheritdoc />
        public Task RequireSystemAdminAsync(Actor? actor, CancellationToken cancellationToken = default)
        {
            return _inner.RequireSystemAdminAsync(actor, cancellationToken);
        }
    }
}
