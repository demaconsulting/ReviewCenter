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

using System.Text;
using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="ProjectChecklistPage"/> component, rendered in memory with bUnit: who gets the
///     page, the summary of the current checklist, the export, the import by paste and by file, the validation with
///     all errors and paths, the preview and the differences, the replace and the reset with their confirmations and
///     audit entries, a change by someone else, a refused version label, and that a lost right drops the page.
/// </summary>
public class ProjectChecklistPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProjectChecklistPageTests"/> class; the page asks the browser
    ///     to download a file, which the tests only observe.
    /// </summary>
    public ProjectChecklistPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    ///     Builds a valid definition with the given version label and two sections with three checks.
    /// </summary>
    /// <param name="version">The version label.</param>
    /// <param name="wording">The wording of the first check.</param>
    /// <returns>The JSON text.</returns>
    private static string Definition(string version, string wording = "Is it right?")
    {
        return "{\"version\":\"" + version + "\",\"sections\":["
            + "{\"id\":\"alpha\",\"title\":\"Alpha\",\"applicabilityHint\":\"Always\",\"items\":["
            + "{\"id\":\"alpha-1\",\"text\":\"" + wording + "\"},{\"id\":\"alpha-2\",\"text\":\"Is it complete?\"}]},"
            + "{\"id\":\"beta\",\"title\":\"Beta\",\"items\":[{\"id\":\"beta-1\",\"text\":\"Is it tested?\"}]}]}";
    }

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
    ///     Gives the project a checklist of its own, as someone who may do that.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="json">The definition.</param>
    /// <returns>A task that completes when the checklist is stored.</returns>
    private async Task SetChecklistAsync(int projectId, string json)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var current = await ChecklistTemplates.GetAsync(TestActors.Ann, projectId, cancellationToken);
        Assert.True(await ChecklistTemplates.SetAsync(TestActors.Ann, projectId, current.Hash, json, cancellationToken));
    }

    /// <summary>
    ///     Renders the page for a project.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ProjectChecklistPage> RenderPage(int projectId)
    {
        return Render<ProjectChecklistPage>(parameters => parameters.Add(p => p.ProjectId, projectId));
    }

    /// <summary>
    ///     Pastes a text into the import box, the way a person leaves the box after typing.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the page has handled the change.</returns>
    private static async Task PasteAsync(IRenderedComponent<ProjectChecklistPage> cut, string text)
    {
        await cut.Find("[data-testid='checklist-import-text']").ChangeAsync(new ChangeEventArgs { Value = text });
    }

    /// <summary>
    ///     Pastes a text and validates it.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the page has shown the result.</returns>
    private static async Task PasteAndValidateAsync(IRenderedComponent<ProjectChecklistPage> cut, string text)
    {
        await PasteAsync(cut, text);
        await cut.Find("[data-testid='checklist-validate']").ClickAsync();
    }

    /// <summary>
    ///     Pastes a text, validates it, asks for the replace and confirms it.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the page has handled the confirmation.</returns>
    private static async Task ReplaceAsync(IRenderedComponent<ProjectChecklistPage> cut, string text)
    {
        await PasteAndValidateAsync(cut, text);
        await cut.Find("[data-testid='checklist-replace']").ClickAsync();
        await cut.Find("[data-testid='checklist-replace-confirm']").ClickAsync();
    }

    /// <summary>
    ///     Takes the right to administer the project away from <c>padm</c>, without the open page knowing.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    /// <returns>A task that completes when the right is gone.</returns>
    private async Task LoseRightAsync(int projectId, string how)
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

    /// <summary>
    ///     Asserts that the page shows the not-found state and nothing of the project.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    private static void AssertDropped(IRenderedComponent<ProjectChecklistPage> cut)
    {
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-import-text']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-current']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-preview']"));
        Assert.Empty(cut.FindAll("[data-testid='project-checklist-error']"));
        Assert.Empty(cut.FindAll("[data-testid='project-checklist-status']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that whoever administers the project sees the current checklist: the version label, the number of
    ///     sections and checks and the badge of the built-in default, with the export buttons and a reset that is not
    ///     offered while the default is in use.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="systemAdmin">Whether that user is a system administrator.</param>
    [Theory]
    [InlineData("padm", false)]
    [InlineData("root", true)]
    public async Task ProjectChecklistPage_Render_Administrator_ShowsDefaultSummary(string username, bool systemAdmin)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, systemAdmin);

        // Act
        var cut = RenderPage(apollo);

        // Assert: the label, the size and the badge
        Assert.Equal("default-1", cut.Find("[data-testid='checklist-current-version']").TextContent);
        Assert.Equal("default-1 (14 sections, 58 checks)", cut.Find("[data-testid='checklist-current-counts']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-default-badge']"));

        // Assert: export is offered, the reset is not, and there is a way back
        Assert.False(cut.Find("[data-testid='checklist-export']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='checklist-export-default']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='checklist-reset']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='checklist-validate']").HasAttribute("disabled"));
        Assert.Equal($"/projects/{apollo}", cut.Find("[data-testid='project-checklist-back']").GetAttribute("href"));
        Assert.Equal("/help/checklist-template", cut.Find("[data-testid='checklist-help-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that a project with a checklist of its own shows its label and size without the default badge, and
    ///     offers the reset.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Render_CustomChecklist_ShowsLabelWithoutBadgeAndOffersReset()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.Equal("custom-1 (2 sections, 3 checks)", cut.Find("[data-testid='checklist-current-counts']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='checklist-default-badge']"));
        Assert.False(cut.Find("[data-testid='checklist-reset']").HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that a plain member, a user who is no member and a missing project all give the not-found state and
    ///     nothing of the page.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="missing">Whether to ask for a project that does not exist.</param>
    [Theory]
    [InlineData("mem", false)]
    [InlineData("out", false)]
    [InlineData("padm", true)]
    public async Task ProjectChecklistPage_Render_NoAdministerRight_ShowsNotFoundAndNothingElse(
        string username,
        bool missing)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, isSystemAdmin: false);

        // Act
        var cut = RenderPage(missing ? 9999 : apollo);

        // Assert
        AssertDropped(cut);
        Assert.Empty(cut.FindAll("[data-testid='checklist-export']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-reset']"));
    }

    /// <summary>
    ///     Test that exporting the current checklist asks the browser to download a JSON file named after the version
    ///     label whose text is the definition, valid and with the same content.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Export_Current_DownloadsTheDefinition()
    {
        // Arrange: a project with its own checklist
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom 1/x"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await cut.Find("[data-testid='checklist-export']").ClickAsync();

        // Assert: one download, a safe file name, the media type, and a text that is the same definition
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "reviewCenter.downloadText");
        Assert.Equal("checklist-custom-1-x.json", call.Arguments[0]);
        Assert.Equal("application/json", call.Arguments[2]);
        var text = Assert.IsType<string>(call.Arguments[1]);
        var result = ChecklistValidator.Validate(text);
        Assert.True(result.IsValid);
        Assert.Equal(ChecklistValidator.Validate(Definition("custom 1/x")).Definition!.ToJson(), result.Definition!.ToJson());
        Assert.Contains("\n  ", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that exporting the built-in default downloads the default even when the project uses another one,
    ///     and that the file imports unchanged (it is valid and has the default's content).
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Export_Default_DownloadsTheBuiltInDefault()
    {
        // Arrange: a project with its own checklist
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await cut.Find("[data-testid='checklist-export-default']").ClickAsync();

        // Assert
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "reviewCenter.downloadText");
        Assert.Equal("checklist-default-1.json", call.Arguments[0]);
        var result = ChecklistValidator.Validate(Assert.IsType<string>(call.Arguments[1]));
        Assert.True(result.IsValid);
        Assert.Equal(DefaultChecklist.Current.ToJson(), result.Definition!.ToJson());
    }

    /// <summary>
    ///     Test that an invalid definition lists every error with its path, says that nothing was changed, and offers
    ///     no replace.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Validate_InvalidDefinition_ShowsAllErrorsWithPaths()
    {
        // Arrange: a definition with an empty version, a bad identifier, an empty title and a duplicate identifier
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        const string Invalid = "{\"version\":\"\",\"sections\":[{\"id\":\"a b\",\"title\":\"\",\"items\":["
            + "{\"id\":\"x\",\"text\":\"One\"},{\"id\":\"x\",\"text\":\"Two\"}]}]}";

        // Act
        await PasteAndValidateAsync(cut, Invalid);

        // Assert: every error is listed, each with its path
        var paths = cut.FindAll("[data-testid='checklist-error-path']").Select(e => e.TextContent).ToList();
        Assert.Equal(["version", "sections[0].id", "sections[0].title", "sections[0].items[1].id"], paths);
        Assert.All(
            cut.FindAll("[data-testid='checklist-error-message']"),
            e => Assert.False(string.IsNullOrWhiteSpace(e.TextContent)));
        Assert.Contains("4 errors", TextOf(cut, "checklist-validation-errors"), StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", TextOf(cut, "checklist-validation-errors"), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='checklist-replace']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-preview']"));
    }

    /// <summary>
    ///     Test that validating with nothing pasted or chosen gives one error, not a crash and not a valid result.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Validate_NothingImported_ShowsOneError()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await cut.Find("[data-testid='checklist-validate']").ClickAsync();

        // Assert
        Assert.Single(cut.FindAll("[data-testid='checklist-error']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-valid']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-replace']"));
    }

    /// <summary>
    ///     Test that text that is not JSON at all gives one error with a path, not a crash.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Validate_NotJson_ShowsOneError()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await PasteAndValidateAsync(cut, "this is not json");

        // Assert
        Assert.Single(cut.FindAll("[data-testid='checklist-error']"));
        Assert.Equal("json", cut.Find("[data-testid='checklist-error-path']").TextContent);
    }

    /// <summary>
    ///     Test that a valid definition shows its label and size, the preview of every section and check, the
    ///     differences from the current checklist, and an enabled replace; and that nothing is stored by validating.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Validate_ValidDefinition_ShowsPreviewAndDifferencesAndStoresNothing()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await PasteAndValidateAsync(cut, Definition("custom-1"));

        // Assert: the summary of the candidate
        Assert.Equal("custom-1", cut.Find("[data-testid='checklist-preview-version']").TextContent);
        Assert.Contains("2 sections, 3 checks", TextOf(cut, "checklist-valid"), StringComparison.Ordinal);

        // Assert: the preview lists every section and check with the wording
        Assert.Equal(2, cut.FindAll("[data-testid^='checklist-preview-section-']").Count);
        Assert.Equal(3, cut.FindAll("[data-testid^='checklist-preview-item-']").Count);
        Assert.Contains("Is it tested?", cut.Find("[data-testid='checklist-preview-item-beta-1']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Always", cut.Find("[data-testid='checklist-preview-section-alpha']").TextContent, StringComparison.Ordinal);

        // Assert: the differences against the default: another label, two sections added, 14 removed
        Assert.Contains("default-1 becomes custom-1", TextOf(cut, "checklist-diff-version"), StringComparison.Ordinal);
        Assert.Equal("Sections added: alpha, beta", cut.Find("[data-testid='checklist-diff-sections-added']").TextContent);
        Assert.Equal(14, cut.Find("[data-testid='checklist-diff-sections-removed']").TextContent.Split(',').Length);
        Assert.Contains("Checks added: alpha-1, alpha-2, beta-1", cut.Find("[data-testid='checklist-diff-items-added']").TextContent, StringComparison.Ordinal);
        Assert.False(cut.Find("[data-testid='checklist-replace']").HasAttribute("disabled"));

        // Assert: nothing was stored
        var stored = await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.True(stored.IsDefault);
        Assert.DoesNotContain(Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a definition with the content of the current checklist says so and offers no replace.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Validate_SameAsCurrent_SaysNothingToReplaceAndDisablesReplace()
    {
        // Arrange: the default, exported and imported again
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await PasteAndValidateAsync(cut, ChecklistDiff.ToIndentedJson(DefaultChecklist.Current));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-valid']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-diff-same']"));
        Assert.True(cut.Find("[data-testid='checklist-replace']").HasAttribute("disabled"));
    }

    /// <summary>
    ///     Test that a changed definition that keeps the version label warns about it before the replace.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Validate_SameLabelOtherWording_WarnsAboutTheLabel()
    {
        // Arrange: a project with its own checklist
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await PasteAndValidateAsync(cut, Definition("custom-1", "Is it correct?"));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-diff-label-warning']"));
        Assert.Equal("Checks with other wording or in another section: alpha-1", cut.Find("[data-testid='checklist-diff-items-changed']").TextContent);
        Assert.Contains("custom-1 (unchanged)", TextOf(cut, "checklist-diff-version"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a file can be imported the way the paste is: the file is read, validated and previewed, and its
    ///     name is shown as the source.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Import_File_IsReadValidatedAndPreviewed()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act: select a file that starts with a byte order mark, then validate
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Definition("from-file"))).ToArray();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, "mine.json"));
        await cut.WaitForAssertionAsync(() => Assert.Contains("mine.json", cut.Find("[data-testid='checklist-import-source']").TextContent, StringComparison.Ordinal));
        await cut.Find("[data-testid='checklist-validate']").ClickAsync();

        // Assert
        Assert.Equal("from-file", cut.Find("[data-testid='checklist-preview-version']").TextContent);
        Assert.Equal(3, cut.FindAll("[data-testid^='checklist-preview-item-']").Count);
    }

    /// <summary>
    ///     Test that a file above the size limit is refused with a message, is not kept for validation, and drops an
    ///     earlier import, so the page does not offer to replace the checklist with the old definition.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Import_FileTooLarge_IsRefused()
    {
        // Arrange: a valid definition is pasted and validated, so that the replace is offered
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await PasteAndValidateAsync(cut, Definition("earlier"));
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-replace']"));

        // Act
        var bytes = new byte[ReviewLimits.MaxChecklistBytes + 1];
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, "big.json"));

        // Assert: the message, and nothing of the earlier import
        await cut.WaitForAssertionAsync(() => Assert.Contains("256 KiB", cut.Find("[data-testid='project-checklist-error']").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("[data-testid='checklist-import-source']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-replace']"));
        Assert.Empty(cut.FindAll("[data-testid='checklist-preview-version']"));
    }

    /// <summary>
    ///     Test that the replace asks for a confirmation that says that existing reviews keep their checklist, and
    ///     that cancelling changes nothing.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Replace_Cancelled_ChangesNothing()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await PasteAndValidateAsync(cut, Definition("custom-1"));

        // Act: ask, read the confirmation, cancel
        await cut.Find("[data-testid='checklist-replace']").ClickAsync();
        var confirmation = TextOf(cut, "checklist-replace-confirmation");
        await cut.Find("[data-testid='checklist-replace-cancel']").ClickAsync();

        // Assert: the consequence was stated, and nothing was stored
        Assert.Contains("Existing reviews keep the", confirmation, StringComparison.Ordinal);
        Assert.Contains("only reviews created afterwards use the new one", confirmation, StringComparison.Ordinal);
        Assert.Contains("custom-1 instead of default-1", confirmation, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='checklist-replace-confirmation']"));
        Assert.True((await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken)).IsDefault);
        Assert.DoesNotContain(Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
    }

    /// <summary>
    ///     Test that a confirmed replace stores the definition, shows the new label and size without the default
    ///     badge, confirms in words, clears the import, and records one audit entry of the project.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Replace_Confirmed_StoresShowsAndAudits()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await ReplaceAsync(cut, Definition("custom-1"));

        // Assert: stored and shown
        var stored = await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.False(stored.IsDefault);
        Assert.Equal("custom-1", stored.Version);
        Assert.Equal("custom-1 (2 sections, 3 checks)", cut.Find("[data-testid='checklist-current-counts']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='checklist-default-badge']"));
        Assert.False(cut.Find("[data-testid='checklist-reset']").HasAttribute("disabled"));

        // Assert: confirmed in words with the consequence; the import is cleared
        var status = cut.Find("[data-testid='project-checklist-status']").TextContent;
        Assert.Contains("custom-1 (2 sections, 3 checks)", status, StringComparison.Ordinal);
        Assert.Contains("Existing reviews keep the checklist they were created with", status, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='checklist-preview']"));
        Assert.Empty(cut.FindAll("[data-testid='project-checklist-error']"));

        // Assert: one audit entry of the project by the actor
        var entry = Assert.Single(Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction);
        Assert.Equal(apollo, entry.ProjectId);
        Assert.Equal("padm", entry.ActorUsername);
        Assert.Contains("version custom-1", entry.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a replace of other wording under the same version label is refused with the message of the
    ///     service, stores nothing, and keeps the imported definition on the page.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Replace_SameLabelOtherWording_ShowsTheRefusalAndStoresNothing()
    {
        // Arrange: a project with its own checklist
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await ReplaceAsync(cut, Definition("custom-1", "Is it correct?"));

        // Assert: the reason is shown, the stored wording is unchanged, the candidate is still there
        Assert.Contains("version label must change", cut.Find("[data-testid='project-checklist-error']").TextContent, StringComparison.Ordinal);
        var stored = await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.Contains("Is it right?", stored.Json, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-preview']"));
        Assert.Equal(1, Store.Audits.Count(a => a.Action == ProjectChecklistService.ChangedAction));
    }

    /// <summary>
    ///     Test that a replace based on a checklist that someone else changed in the meantime overwrites nothing,
    ///     shows the message and the checklist that is stored now, and compares the imported definition with it.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Replace_ChangedByOthers_ShowsMessageAndTheCurrentChecklist()
    {
        // Arrange: the page is open and a definition validated; then someone else replaces the checklist
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await PasteAndValidateAsync(cut, Definition("mine-1"));
        await cut.Find("[data-testid='checklist-replace']").ClickAsync();
        await SetChecklistAsync(apollo, Definition("theirs-1", "Is it theirs?"));

        // Act
        await cut.Find("[data-testid='checklist-replace-confirm']").ClickAsync();

        // Assert: the message, their checklist shown, ours not stored
        Assert.Equal(ProjectChecklistService.ChangedByOthersMessage, cut.Find("[data-testid='project-checklist-error']").TextContent);
        Assert.Equal("theirs-1", cut.Find("[data-testid='checklist-current-version']").TextContent);
        var stored = await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.Equal("theirs-1", stored.Version);
        Assert.Equal(1, Store.Audits.Count(a => a.Action == ProjectChecklistService.ChangedAction));

        // Assert: the candidate is compared with the checklist that is stored now
        Assert.Contains("theirs-1 becomes mine-1", TextOf(cut, "checklist-diff-version"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the reset asks for a confirmation, that cancelling changes nothing, and that confirming returns
    ///     the project to the built-in default, shows the badge, confirms in words and records the change.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Reset_Confirmed_ReturnsToTheDefaultAndAudits()
    {
        // Arrange: a project with its own checklist
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act: ask and cancel
        await cut.Find("[data-testid='checklist-reset']").ClickAsync();
        var confirmation = TextOf(cut, "checklist-reset-confirmation");
        await cut.Find("[data-testid='checklist-reset-cancel']").ClickAsync();

        // Assert: the consequence was stated and nothing changed
        Assert.Contains("Existing reviews keep the checklist they were created with", confirmation, StringComparison.Ordinal);
        Assert.Equal("custom-1", cut.Find("[data-testid='checklist-current-version']").TextContent);

        // Act: ask and confirm
        await cut.Find("[data-testid='checklist-reset']").ClickAsync();
        await cut.Find("[data-testid='checklist-reset-confirm']").ClickAsync();

        // Assert: default again, shown, confirmed and recorded
        Assert.True((await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken)).IsDefault);
        Assert.Equal("default-1 (14 sections, 58 checks)", cut.Find("[data-testid='checklist-current-counts']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='checklist-default-badge']"));
        Assert.True(cut.Find("[data-testid='checklist-reset']").HasAttribute("disabled"));
        Assert.Contains("built-in default", cut.Find("[data-testid='project-checklist-status']").TextContent, StringComparison.Ordinal);
        var entry = Assert.Single(Store.Audits, a => a.Action == ProjectChecklistService.ChangedAction && a.Detail.Contains("reset to default", StringComparison.Ordinal));
        Assert.Equal(apollo, entry.ProjectId);
    }

    /// <summary>
    ///     Test that a reset based on a checklist that someone else changed in the meantime overwrites nothing and
    ///     shows the message and the checklist that is stored now.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Reset_ChangedByOthers_ShowsMessageAndTheCurrentChecklist()
    {
        // Arrange: the page shows one custom checklist, then someone else replaces it
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SetChecklistAsync(apollo, Definition("theirs-2", "Is it theirs?"));
        await cut.Find("[data-testid='checklist-reset']").ClickAsync();

        // Act
        await cut.Find("[data-testid='checklist-reset-confirm']").ClickAsync();

        // Assert
        Assert.Equal(ProjectChecklistService.ChangedByOthersMessage, cut.Find("[data-testid='project-checklist-error']").TextContent);
        Assert.Equal("theirs-2", cut.Find("[data-testid='checklist-current-version']").TextContent);
        Assert.False((await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken)).IsDefault);
    }

    /// <summary>
    ///     Test that every action after the right was lost while the page was open (membership revoked, demoted to
    ///     plain member, or session ended without the page knowing) takes no effect: the page drops to the
    ///     not-found state, nothing is stored or recorded, and no project data stays rendered.
    /// </summary>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    /// <param name="action">The action taken after the loss: <c>paste</c>, <c>validate</c>, <c>replace</c> or <c>reset</c>.</param>
    [Theory]
    [InlineData("revoked", "paste")]
    [InlineData("demoted", "validate")]
    [InlineData("ended", "validate")]
    [InlineData("revoked", "replace")]
    [InlineData("demoted", "replace")]
    [InlineData("ended", "replace")]
    [InlineData("revoked", "reset")]
    [InlineData("ended", "reset")]
    public async Task ProjectChecklistPage_Action_RightLostWhileOpen_DropsThePageAndChangesNothing(string how, string action)
    {
        // Arrange: a project with its own checklist and an open page with a validated candidate and a pending replace
        var apollo = await ArrangeAsync();
        await SetChecklistAsync(apollo, Definition("custom-1"));
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await PasteAndValidateAsync(cut, Definition("custom-2", "Is it new?"));
        await cut.Find("[data-testid='checklist-replace']").ClickAsync();
        await LoseRightAsync(apollo, how);

        // Act
        switch (action)
        {
            case "paste":
                await PasteAsync(cut, Definition("custom-3"));
                break;
            case "validate":
                await cut.Find("[data-testid='checklist-validate']").ClickAsync();
                break;
            case "replace":
                await cut.Find("[data-testid='checklist-replace-confirm']").ClickAsync();
                break;
            default:
                await cut.Find("[data-testid='checklist-reset']").ClickAsync();
                break;
        }

        // Assert: dropped, and the stored checklist is unchanged
        AssertDropped(cut);
        var stored = await ChecklistTemplates.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.Equal("custom-1", stored.Version);
        Assert.Equal(1, Store.Audits.Count(a => a.Action == ProjectChecklistService.ChangedAction));
    }

    /// <summary>
    ///     Test that an export after the right was lost downloads nothing and drops the page.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Export_RightLostWhileOpen_DownloadsNothing()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await LoseRightAsync(apollo, "revoked");

        // Act
        await cut.Find("[data-testid='checklist-export']").ClickAsync();

        // Assert
        AssertDropped(cut);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "reviewCenter.downloadText");
    }

    /// <summary>
    ///     Test that the page loads again when its project changes, and that the imported text of the first project
    ///     does not follow to a project the user may not administer.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_Load_OtherProject_DropsTheImportAndTheProject()
    {
        // Arrange: padm administers Apollo only
        var apollo = await ArrangeAsync();
        var gemini = (await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken)).Id;
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await PasteAndValidateAsync(cut, Definition("custom-1"));

        // Act
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ProjectChecklistPage.ProjectId)] = gemini })));

        // Assert
        AssertDropped(cut);
        Assert.DoesNotContain("Gemini", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the example definition shipped in the user guide passes the validator, has the structure the
    ///     user guide describes, and does not claim compliance with a standard.
    /// </summary>
    [Fact]
    public void ProjectChecklistPage_Sample_Iec62304ExampleIsValid()
    {
        // Arrange: the sample file of the user guide
        var path = Path.Combine(FindRepositoryRoot(), "docs", "user_guide", "samples", "iec62304-checklist.json");
        var json = File.ReadAllText(path);

        // Act
        var result = ChecklistValidator.Validate(json);

        // Assert: valid, six sections of four to six checks, a version label that names it an example
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
        var definition = result.Definition!;
        Assert.Equal("iec62304-example-1", definition.Version);
        Assert.Equal(6, definition.Sections.Count);
        Assert.All(definition.Sections, s => Assert.InRange(s.Items.Count, 4, 6));
        Assert.DoesNotContain("compliant", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certification", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public async Task ProjectChecklistPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.Equal("true", cut.Find("[data-testid='project-checklist-page']").GetAttribute("data-interactive"));
    }

    /// <summary>
    ///     Reads the text of an element with every run of white space (line breaks of the markup) as one blank.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="testId">The test id of the element.</param>
    /// <returns>The normalized text.</returns>
    private static string TextOf(IRenderedComponent<ProjectChecklistPage> cut, string testId)
    {
        return System.Text.RegularExpressions.Regex.Replace(cut.Find($"[data-testid='{testId}']").TextContent, @"\s+", " ").Trim();
    }

    /// <summary>
    ///     Finds the root of the repository: the folder above the test binaries that has the solution file.
    /// </summary>
    /// <returns>The folder.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DemaConsulting.ReviewCenter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test binaries.");
    }
}
