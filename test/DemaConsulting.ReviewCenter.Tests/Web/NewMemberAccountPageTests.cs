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
///     Unit tests for the <see cref="NewMemberAccountPage"/> component, rendered in memory with bUnit: who gets the
///     form, that creating an account stores it with its membership and a temporary password, that the password is
///     shown once, and that a revoked right stops every handler and drops the password.
/// </summary>
public class NewMemberAccountPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates the project Apollo with the project admin <c>padm</c> and the plain member <c>mem</c>, the
    ///     outsider <c>out</c> and the system administrator <c>root</c>.
    /// </summary>
    /// <returns>The identity of Apollo.</returns>
    private async Task<int> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        var padm = await Users.CreateAsync("padm", "Padm", UserRole.Reviewer, Password, cancellationToken);
        var mem = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, Password, cancellationToken);
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, Password, cancellationToken);
        var apollo = await Projects.CreateAsync("Apollo", null, cancellationToken);
        await Store.AddMembershipAsync(padm.Id, apollo.Id, ProjectRole.ProjectAdmin, cancellationToken);
        await Store.AddMembershipAsync(mem.Id, apollo.Id, ProjectRole.User, cancellationToken);
        return apollo.Id;
    }

    /// <summary>
    ///     Renders the page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<NewMemberAccountPage> RenderPage(int projectId)
    {
        return Render<NewMemberAccountPage>(parameters => parameters.Add(p => p.ProjectId, projectId));
    }

    /// <summary>
    ///     Types the names into the form of the page and submits it.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="username">The user name to type.</param>
    /// <param name="displayName">The display name to type.</param>
    /// <returns>A task that completes when the form is handled.</returns>
    private static async Task SubmitAsync(
        IRenderedComponent<NewMemberAccountPage> cut, string username, string displayName)
    {
        await cut.Find("[data-testid='new-account-username-input']").InputAsync(username);
        await cut.Find("[data-testid='new-account-display-name-input']").InputAsync(displayName);
        await cut.Find("[data-testid='new-account-submit']").ClickAsync();
    }

    /// <summary>
    ///     Test that whoever administers the project gets the form, with a button back to the add member page.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="systemAdmin">Whether that user is a system administrator.</param>
    [Theory]
    [InlineData("padm", false)]
    [InlineData("root", true)]
    public async Task NewMemberAccountPage_Render_Administrator_ShowsTheForm(string username, bool systemAdmin)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, systemAdmin);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='new-account-username-input']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-account-display-name-input']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-account-admin-checkbox']"));
        Assert.Empty(cut.FindAll("input[type='password']"));
        Assert.Equal(
            $"/projects/{apollo}/members/new",
            cut.Find("[data-testid='new-account-cancel']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a plain member, a user who is no member and a missing project all give the not-found state and
    ///     no form.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="missing">Whether to ask for a project that does not exist.</param>
    [Theory]
    [InlineData("mem", false)]
    [InlineData("out", false)]
    [InlineData("padm", true)]
    public async Task NewMemberAccountPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm(
        string username, bool missing)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, isSystemAdmin: false);

        // Act
        var cut = RenderPage(missing ? 9999 : apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-account-form']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that submitting the form stores the account with its membership and the flag, shows the temporary
    ///     password once with the user name, and that Done hides the password for good and returns to the project.
    /// </summary>
    [Fact]
    public async Task NewMemberAccountPage_Submit_ValidNames_CreatesAccountShowsPasswordOnceAndDoneHidesIt()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await cut.Find("[data-testid='new-account-admin-checkbox']").ChangeAsync(true);

        // Act
        await SubmitAsync(cut, "newbie", "New Bie");

        // Assert: the box shows the name and a password that works once, the account is stored as planned
        var shown = cut.Find("[data-testid='new-account-password-value']").TextContent;
        Assert.Equal("newbie", cut.Find("[data-testid='new-account-username-value']").TextContent);
        Assert.Equal(16, shown.Length);
        Assert.Contains(
            "only once",
            cut.Find("[data-testid='new-account-password-box']").TextContent,
            StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-account-form']"));
        var stored = (await UserStore.GetByUsernameAsync("newbie", TestContext.Current.CancellationToken))!;
        Assert.True(stored.MustChangePassword);
        Assert.False(stored.IsSystemAdmin);
        Assert.Equal("New Bie", stored.DisplayName);
        Assert.Equal(
            ProjectRole.ProjectAdmin,
            (await Store.GetMembershipAsync(stored.Id, apollo, TestContext.Current.CancellationToken))!.Role);
        Assert.NotNull(await Users.AuthenticateAsync("newbie", shown, TestContext.Current.CancellationToken));

        // Act: Done
        await cut.Find("[data-testid='done-account-button']").ClickAsync();

        // Assert: the password is gone from the page, and the page went back to the project
        Assert.DoesNotContain(shown, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-account-password-box']"));
        Assert.EndsWith(
            $"/projects/{apollo}",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Copy hands the password to the browser and says so, and says how to go on when the browser
    ///     refuses.
    /// </summary>
    /// <param name="copied">Whether the browser accepts the copy.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewMemberAccountPage_Copy_Password_CopiesAndReportsTheResult(bool copied)
    {
        // Arrange: an account was created
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        JSInterop.Setup<bool>("reviewCenter.copyText", _ => true).SetResult(copied);
        var cut = RenderPage(apollo);
        await SubmitAsync(cut, "newbie", "New Bie");
        var shown = cut.Find("[data-testid='new-account-password-value']").TextContent;

        // Act
        await cut.Find("[data-testid='copy-account-password-button']").ClickAsync();

        // Assert
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "reviewCenter.copyText");
        Assert.Equal([shown], call.Arguments);
        var status = cut.Find("[data-testid='account-password-copy-status']").TextContent;
        Assert.Equal(copied ? "Copied." : "Could not copy; select the password and copy it by hand.", status);
    }

    /// <summary>
    ///     Test that a taken name and an invalid name each show a message next to the form, keep what was typed and
    ///     create nothing.
    /// </summary>
    /// <param name="username">The user name typed.</param>
    [Theory]
    [InlineData("out")]
    [InlineData("x")]
    public async Task NewMemberAccountPage_Submit_TakenOrInvalidName_ShowsMessageAndCreatesNothing(string username)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        var before = (await Store.ListMembershipsAsync(apollo, TestContext.Current.CancellationToken)).Count;

        // Act
        await SubmitAsync(cut, username, "Some One");

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='new-account-error']"));
        Assert.Empty(cut.FindAll("[data-testid='new-account-password-box']"));
        Assert.Equal(username, cut.Find("[data-testid='new-account-username-input']").GetAttribute("value"));
        Assert.Equal(before, (await Store.ListMembershipsAsync(apollo, TestContext.Current.CancellationToken)).Count);
        Assert.Null(await UserStore.GetByUsernameAsync("x", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a submission after the right was lost while the page was open (membership revoked, demoted to
    ///     plain member, or session ended without the page knowing) creates nothing and leaves the not-found state
    ///     with no form, no error and nothing typed.
    /// </summary>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    [Theory]
    [InlineData("revoked")]
    [InlineData("demoted")]
    [InlineData("ended")]
    public async Task NewMemberAccountPage_Submit_RightLostWhileOpen_CreatesNothingAndDropsTheForm(string how)
    {
        // Arrange: the form is open for a project admin and filled in
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await cut.Find("[data-testid='new-account-username-input']").InputAsync("newbie");
        await cut.Find("[data-testid='new-account-display-name-input']").InputAsync("New Bie");
        await LoseRightAsync(how, apollo);

        // Act
        await cut.Find("[data-testid='new-account-submit']").ClickAsync();

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-account-form']"));
        Assert.Empty(cut.FindAll("[data-testid='new-account-error']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        Assert.Null(await UserStore.GetByUsernameAsync("newbie", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that Copy after the right was lost while the password was shown drops the password at once and does
    ///     not hand it to the browser.
    /// </summary>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    [Theory]
    [InlineData("revoked")]
    [InlineData("demoted")]
    [InlineData("ended")]
    public async Task NewMemberAccountPage_Copy_RightLostWhilePasswordShown_DropsThePasswordAndCopiesNothing(string how)
    {
        // Arrange: an account was created and its password is shown
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SubmitAsync(cut, "newbie", "New Bie");
        var shown = cut.Find("[data-testid='new-account-password-value']").TextContent;
        await LoseRightAsync(how, apollo);

        // Act
        await cut.Find("[data-testid='copy-account-password-button']").ClickAsync();

        // Assert
        Assert.DoesNotContain(shown, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-account-password-box']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "reviewCenter.copyText");
    }

    /// <summary>
    ///     Test that loading the page again for a project the user may not administer drops the typed names and the
    ///     password that belonged to the previous project.
    /// </summary>
    [Fact]
    public async Task NewMemberAccountPage_Load_OtherProject_DropsNamesAndPassword()
    {
        // Arrange: an account was created in Apollo; padm does not administer Gemini
        var apollo = await ArrangeAsync();
        var gemini = (await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken)).Id;
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SubmitAsync(cut, "newbie", "New Bie");
        var shown = cut.Find("[data-testid='new-account-password-value']").TextContent;

        // Act
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(NewMemberAccountPage.ProjectId)] = gemini })));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.DoesNotContain(shown, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemini", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public async Task NewMemberAccountPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.Equal("true", cut.Find("[data-testid='new-account-page']").GetAttribute("data-interactive"));
    }

    /// <summary>
    ///     Takes the right to administer the project away from <c>padm</c> while the page is open.
    /// </summary>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    /// <param name="projectId">The project identity.</param>
    /// <returns>A task that completes when the right is gone.</returns>
    private async Task LoseRightAsync(string how, int projectId)
    {
        switch (how)
        {
            case "revoked":
                await RevokeMembershipAsync(projectId, "padm");
                break;
            case "demoted":
                await ChangeMemberRoleAsync(projectId, "padm", ProjectRole.User);
                break;
            default:
                SignOutQuietly();
                break;
        }
    }
}
