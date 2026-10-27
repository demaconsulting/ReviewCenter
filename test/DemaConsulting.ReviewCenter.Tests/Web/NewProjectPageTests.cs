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
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="NewProjectPage"/> component, rendered in memory with bUnit.
/// </summary>
public class NewProjectPageTests : PageTestBase
{
    /// <summary>
    ///     Test that the page shows only the creation form and a Cancel link back to the project list.
    /// </summary>
    [Fact]
    public void NewProjectPage_Render_Default_ShowsFormAndCancelLink()
    {
        // Arrange: no setup required

        // Act: render the page
        var cut = Render<NewProjectPage>();

        // Assert: the form controls and the Cancel link are present, and no project list is
        Assert.NotNull(cut.Find("[data-testid='project-name-input']"));
        Assert.NotNull(cut.Find("[data-testid='project-description-input']"));
        Assert.NotNull(cut.Find("[data-testid='create-project-button']"));
        Assert.Equal("/projects", cut.Find("[data-testid='cancel-new-project-link']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='projects-list']"));
        Assert.Equal("true", cut.Find("[data-testid='new-project-page']").GetAttribute("data-interactive"));
    }

    /// <summary>
    ///     Test that submitting the form creates the project, without an error, and opens the new project.
    /// </summary>
    [Fact]
    public async Task NewProjectPage_CreateForm_ValidInput_CreatesProjectAndOpensIt()
    {
        // Arrange: the rendered page with the form filled in
        var cut = Render<NewProjectPage>();
        await cut.Find("[data-testid='project-name-input']").InputAsync("Apollo");
        await cut.Find("[data-testid='project-description-input']").InputAsync("Moon landing");

        // Act: submit the form
        await cut.Find("[data-testid='new-project-form']").SubmitAsync();

        // Assert: the project is stored, no error is displayed, and the browser was sent to the project
        var project = Assert.Single(await Projects.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Apollo", project.Name);
        Assert.Empty(cut.FindAll("[data-testid='project-error']"));
        Assert.EndsWith($"/projects/{project.Id}", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a project submitted after the session ended, but before the page learned of it, shows a clear
    ///     message and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewProjectPage_CreateForm_SessionEndedBeforePageKnows_ShowsClearErrorAndCreatesNothing()
    {
        // Arrange: a filled-in form, and then the session ends without the page being told
        var cut = Render<NewProjectPage>();
        await cut.Find("[data-testid='project-name-input']").InputAsync("Apollo");
        SignOutQuietly();

        // Act: submit the form
        await cut.Find("[data-testid='new-project-form']").SubmitAsync();

        // Assert: the page says why nothing was saved, and no project exists
        Assert.Contains("no longer signed in", cut.Find("[data-testid='project-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await Projects.ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that submitting a blank name shows the validation message and creates nothing.
    /// </summary>
    [Fact]
    public async Task NewProjectPage_CreateForm_BlankName_ShowsErrorAndCreatesNothing()
    {
        // Arrange: the rendered page with an empty form
        var cut = Render<NewProjectPage>();

        // Act: submit the form
        await cut.Find("[data-testid='new-project-form']").SubmitAsync();

        // Assert: the error is shown and nothing was stored
        Assert.Contains("name", cut.Find("[data-testid='project-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await Projects.ListAsync(TestContext.Current.CancellationToken));
    }
}
