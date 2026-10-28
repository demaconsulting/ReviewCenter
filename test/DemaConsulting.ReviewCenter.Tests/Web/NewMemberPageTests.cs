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
///     Unit tests for the <see cref="NewMemberPage"/> component, rendered in memory with bUnit: who gets the form, how
///     an existing user is added by the exact name, that a refusal does not reveal which accounts exist, and that a
///     revoked right stops the submission.
/// </summary>
public class NewMemberPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates the project Apollo with the project admin <c>padm</c> and the plain member <c>mem</c>, the
    ///     outsiders <c>out</c> and <c>gone</c> (the latter disabled) and the system administrator <c>root</c>.
    /// </summary>
    /// <returns>The identity of Apollo.</returns>
    private async Task<int> ArrangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, cancellationToken);
        var padm = await Users.CreateAsync("padm", "Padm", UserRole.Reviewer, Password, cancellationToken);
        var mem = await Users.CreateAsync("mem", "Mem", UserRole.Reviewer, Password, cancellationToken);
        await Users.CreateAsync("out", "Out", UserRole.Reviewer, Password, cancellationToken);
        await Users.CreateAsync("gone", "Gone", UserRole.Reviewer, Password, cancellationToken);
        await Users.SetEnabledAsync("gone", false, cancellationToken);
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
    private IRenderedComponent<NewMemberPage> RenderPage(int projectId)
    {
        return Render<NewMemberPage>(parameters => parameters.Add(p => p.ProjectId, projectId));
    }

    /// <summary>
    ///     Types a name into the form of the page and submits it.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="username">The name to type.</param>
    /// <returns>A task that completes when the form is handled.</returns>
    private static async Task SubmitAsync(IRenderedComponent<NewMemberPage> cut, string username)
    {
        await cut.Find("[data-testid='new-member-username-input']").InputAsync(username);
        await cut.Find("[data-testid='new-member-submit']").ClickAsync();
    }

    /// <summary>
    ///     Test that whoever administers the project gets a form with a name field, the role choice, and buttons to
    ///     cancel and to create a new account, and no list of users to pick from.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="systemAdmin">Whether that user is a system administrator.</param>
    [Theory]
    [InlineData("padm", false)]
    [InlineData("root", true)]
    public async Task NewMemberPage_Render_Administrator_ShowsFormWithoutUserList(string username, bool systemAdmin)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, systemAdmin);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='new-member-username-input']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='new-member-admin-checkbox']"));
        Assert.Equal($"/projects/{apollo}", cut.Find("[data-testid='new-member-cancel']").GetAttribute("href"));
        Assert.Equal(
            $"/projects/{apollo}/members/account",
            cut.Find("[data-testid='new-member-create-account-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("datalist"));
        Assert.DoesNotContain("Gone", cut.Markup, StringComparison.Ordinal);
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
    public async Task NewMemberPage_Render_NoAdministerRight_ShowsNotFoundAndNoForm(string username, bool missing)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, isSystemAdmin: false);

        // Act
        var cut = RenderPage(missing ? 9999 : apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-member-form']"));
        Assert.Empty(cut.FindAll("[data-testid='new-member-submit']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the exact name of an existing user is added with the chosen role and the page returns to the
    ///     project.
    /// </summary>
    /// <param name="asProjectAdmin">Whether the project admin box is ticked.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewMemberPage_Submit_ExistingUser_AddsWithRoleAndReturnsToTheProject(bool asProjectAdmin)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        if (asProjectAdmin)
        {
            await cut.Find("[data-testid='new-member-admin-checkbox']").ChangeAsync(true);
        }

        // Act
        await SubmitAsync(cut, "out");

        // Assert
        var user = (await UserStore.GetByUsernameAsync("out", TestContext.Current.CancellationToken))!;
        var stored = await Store.GetMembershipAsync(user.Id, apollo, TestContext.Current.CancellationToken);
        Assert.Equal(asProjectAdmin ? ProjectRole.ProjectAdmin : ProjectRole.User, stored!.Role);
        Assert.EndsWith(
            $"/projects/{apollo}",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='new-member-error']"));
    }

    /// <summary>
    ///     Test that a missing account, a disabled account and a name with the wrong case that does not exist give one
    ///     and the same message, that the form keeps what was typed, and that nothing is added.
    /// </summary>
    [Fact]
    public async Task NewMemberPage_Submit_UnusableNames_GiveOneGenericMessageAndAddNothing()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var before = (await Store.ListMembershipsAsync(apollo, TestContext.Current.CancellationToken)).Count;
        var messages = new List<string>();

        // Act: a name that does not exist, and the name of a disabled account
        foreach (var name in new[] { "nobody-by-that-name", "gone" })
        {
            var cut = RenderPage(apollo);
            await SubmitAsync(cut, name);
            messages.Add(cut.Find("[data-testid='new-member-error']").TextContent);
            Assert.Equal(name, cut.Find("[data-testid='new-member-username-input']").GetAttribute("value"));
            await DisposeComponentsAsync();
        }

        // Assert: one message, which says to check the spelling, and no membership was added
        Assert.Single(messages.Distinct());
        Assert.Contains("cannot be added", messages[0], StringComparison.Ordinal);
        Assert.Equal(before, (await Store.ListMembershipsAsync(apollo, TestContext.Current.CancellationToken)).Count);
    }

    /// <summary>
    ///     Test that a user who already is a member gets a message and no second membership.
    /// </summary>
    [Fact]
    public async Task NewMemberPage_Submit_AlreadyMember_ShowsMessageAndKeepsTheRole()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await SubmitAsync(cut, "mem");

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='new-member-error']"));
        var mem = (await UserStore.GetByUsernameAsync("mem", TestContext.Current.CancellationToken))!;
        var memMembership = await Store.GetMembershipAsync(mem.Id, apollo, TestContext.Current.CancellationToken);
        Assert.Equal(ProjectRole.User, memMembership!.Role);
    }

    /// <summary>
    ///     Test that a submission after the right was lost while the page was open (membership revoked, demoted to
    ///     plain member, or session ended without the page knowing) adds nothing and leaves the not-found state with
    ///     no form, no error and nothing typed.
    /// </summary>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    [Theory]
    [InlineData("revoked")]
    [InlineData("demoted")]
    [InlineData("ended")]
    public async Task NewMemberPage_Submit_RightLostWhileOpen_AddsNothingAndDropsTheForm(string how)
    {
        // Arrange: the form is open for a project admin
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await cut.Find("[data-testid='new-member-username-input']").InputAsync("out");
        switch (how)
        {
            case "revoked":
                await RevokeMembershipAsync(apollo, "padm");
                break;
            case "demoted":
                await ChangeMemberRoleAsync(apollo, "padm", ProjectRole.User);
                break;
            default:
                SignOutQuietly();
                break;
        }

        // Act
        await cut.Find("[data-testid='new-member-submit']").ClickAsync();

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-member-form']"));
        Assert.Empty(cut.FindAll("[data-testid='new-member-error']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        var user = (await UserStore.GetByUsernameAsync("out", TestContext.Current.CancellationToken))!;
        Assert.Null(await Store.GetMembershipAsync(user.Id, apollo, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the page loads again when its project changes, and that the typed name of the first project does
    ///     not follow to a project the user may not administer.
    /// </summary>
    [Fact]
    public async Task NewMemberPage_Load_OtherProject_DropsWhatWasTypedAndTheProject()
    {
        // Arrange: padm administers Apollo only
        var apollo = await ArrangeAsync();
        var gemini = (await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken)).Id;
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await cut.Find("[data-testid='new-member-username-input']").InputAsync("out");

        // Act
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(NewMemberPage.ProjectId)] = gemini })));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='new-member-form']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemini", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public async Task NewMemberPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.Equal("true", cut.Find("[data-testid='new-member-page']").GetAttribute("data-interactive"));
    }
}
