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

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ProjectsPage"/> component, rendered in memory with bUnit.
/// </summary>
public class ProjectsPageTests : PageTestBase
{
    /// <summary>
    ///     Test that with no projects the page says so and shows the form.
    /// </summary>
    [Fact]
    public void ProjectsPage_Render_NoProjects_ShowsEmptyMessageAndForm()
    {
        // Arrange: no projects exist

        // Act: render the page
        var cut = Render<ProjectsPage>();

        // Assert: the empty message and the form controls are present, the list is not
        Assert.NotEmpty(cut.FindAll("[data-testid='projects-empty']"));
        Assert.Empty(cut.FindAll("[data-testid='projects-list']"));
        Assert.NotNull(cut.Find("[data-testid='project-name-input']"));
        Assert.NotNull(cut.Find("[data-testid='project-description-input']"));
        Assert.NotNull(cut.Find("[data-testid='create-project-button']"));
    }

    /// <summary>
    ///     Test that existing projects are listed with links to their pages.
    /// </summary>
    [Fact]
    public async Task ProjectsPage_Render_ExistingProjects_ListsThemWithLinks()
    {
        // Arrange: two projects
        var apollo = await Projects.CreateAsync("Apollo", "Moon landing", TestContext.Current.CancellationToken);
        var gemini = await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = Render<ProjectsPage>();

        // Assert: names, links and the description are shown; the empty message is not
        Assert.Equal("Apollo", cut.Find($"[data-testid='project-link-{apollo.Id}']").TextContent);
        Assert.Equal($"/projects/{apollo.Id}", cut.Find($"[data-testid='project-link-{apollo.Id}']").GetAttribute("href"));
        Assert.Equal("Moon landing", cut.Find($"[data-testid='project-description-{apollo.Id}']").TextContent);
        Assert.Equal("Gemini", cut.Find($"[data-testid='project-link-{gemini.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='project-description-{gemini.Id}']"));
        Assert.Empty(cut.FindAll("[data-testid='projects-empty']"));
    }

    /// <summary>
    ///     Test that submitting the form creates the project and lists it without an error.
    /// </summary>
    [Fact]
    public async Task ProjectsPage_CreateForm_ValidInput_AddsProjectToList()
    {
        // Arrange: the rendered page with the form filled in
        var cut = Render<ProjectsPage>();
        await cut.Find("[data-testid='project-name-input']").InputAsync("Apollo");
        await cut.Find("[data-testid='project-description-input']").InputAsync("Moon landing");

        // Act: submit the form
        await cut.Find("[data-testid='new-project-form']").SubmitAsync();

        // Assert: the project is stored and shown, and no error is displayed
        var projects = await Projects.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Apollo", Assert.Single(projects).Name);
        Assert.Equal("Apollo", cut.Find("[data-testid^='project-link-']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='project-error']"));
    }

    /// <summary>
    ///     Test that submitting a blank name shows the validation message and creates nothing.
    /// </summary>
    [Fact]
    public async Task ProjectsPage_CreateForm_BlankName_ShowsErrorAndCreatesNothing()
    {
        // Arrange: the rendered page with an empty form
        var cut = Render<ProjectsPage>();

        // Act: submit the form
        await cut.Find("[data-testid='new-project-form']").SubmitAsync();

        // Assert: the error is shown and nothing was stored
        Assert.Contains("name", cut.Find("[data-testid='project-error']").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await Projects.ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public void ProjectsPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange: no setup required

        // Act: render the page (bUnit runs OnAfterRender like a live circuit does)
        var cut = Render<ProjectsPage>();

        // Assert: the root element reports interactivity
        Assert.Equal("true", cut.Find("[data-testid='projects-page']").GetAttribute("data-interactive"));
    }
}
