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

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="EvidenceSettingsPage"/> component, rendered in memory with bUnit: who gets the
///     page, the defaults, the warnings that follow the switches, saving with its audit entry, a change by someone
///     else, and that a revoked right stops a save and drops the settings.
/// </summary>
public class EvidenceSettingsPageTests : PageTestBase
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
    private IRenderedComponent<EvidenceSettingsPage> RenderPage(int projectId)
    {
        return Render<EvidenceSettingsPage>(parameters => parameters.Add(p => p.ProjectId, projectId));
    }

    /// <summary>
    ///     Reads whether a switch of the page is on.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="key">The key of the switch, for example <c>reviewer-names</c>.</param>
    /// <returns><c>true</c> when the switch is checked.</returns>
    private static bool IsOn(IRenderedComponent<EvidenceSettingsPage> cut, string key)
    {
        return cut.Find($"[data-testid='evidence-{key}-switch']").HasAttribute("checked");
    }

    /// <summary>
    ///     Turns a switch of the page on or off.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <param name="key">The key of the switch.</param>
    /// <param name="on">The new state.</param>
    /// <returns>A task that completes when the page has handled the change.</returns>
    private static async Task SetAsync(IRenderedComponent<EvidenceSettingsPage> cut, string key, bool on)
    {
        await cut.Find($"[data-testid='evidence-{key}-switch']").ChangeAsync(on);
    }

    /// <summary>
    ///     Test that whoever administers the project gets the defaults: the two anonymous switches off, the five
    ///     content switches on, no warning and no banner, a Save button and a button back to the project.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="systemAdmin">Whether that user is a system administrator.</param>
    [Theory]
    [InlineData("padm", false)]
    [InlineData("root", true)]
    public async Task EvidenceSettingsPage_Render_Administrator_ShowsDefaultsWithoutWarnings(
        string username,
        bool systemAdmin)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, systemAdmin);

        // Act
        var cut = RenderPage(apollo);

        // Assert: anonymous access is off, the content is complete
        Assert.False(IsOn(cut, "anonymous-index"));
        Assert.False(IsOn(cut, "anonymous-attestations"));
        foreach (var key in new[] { "threads", "reviewer-names", "checklist-wording", "files", "git-context" })
        {
            Assert.True(IsOn(cut, key), key);
        }

        // Assert: nothing to warn about, and the two buttons are there
        Assert.Empty(cut.FindAll("[data-testid='evidence-reduced-banner']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-anonymous-warning']"));
        Assert.Empty(cut.FindAll(".notice.warning"));
        Assert.NotEmpty(cut.FindAll("[data-testid='evidence-settings-save']"));
        Assert.Equal($"/projects/{apollo}", cut.Find("[data-testid='evidence-settings-back']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that every switch has a short description, and that the one of the index says that the index alone
    ///     is useless to ReviewMark without the PDFs or credentials.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Render_EverySwitchHasADescription()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        var rows = cut.FindAll("[data-testid^='evidence-setting-']");
        Assert.Equal(7, rows.Count);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.QuerySelector(".hint")!.TextContent)));
        var index = cut.Find("[data-testid='evidence-setting-anonymous-index']").TextContent;
        Assert.Contains("useless to ReviewMark", index, StringComparison.Ordinal);
        Assert.Contains("credentials", index, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a plain member, a user who is no member and a missing project all give the not-found state and
    ///     no switches.
    /// </summary>
    /// <param name="username">The signed-in user.</param>
    /// <param name="missing">Whether to ask for a project that does not exist.</param>
    [Theory]
    [InlineData("mem", false)]
    [InlineData("out", false)]
    [InlineData("padm", true)]
    public async Task EvidenceSettingsPage_Render_NoAdministerRight_ShowsNotFoundAndNoSwitches(
        string username,
        bool missing)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs(username, username, isSystemAdmin: false);

        // Act
        var cut = RenderPage(missing ? 9999 : apollo);

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-form']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-save']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the page shows the stored settings of the project, not the defaults.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Render_StoredSettings_AreShown()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        var stored = EvidenceSettings.Default with { AnonymousIndex = true, IncludeFiles = false };
        await SettingsService.SetAsync(
            TestActors.Ann, apollo, EvidenceSettings.Default, stored,
            TestContext.Current.CancellationToken);
        SignInAs("padm", "Padm", isSystemAdmin: false);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.True(IsOn(cut, "anonymous-index"));
        Assert.False(IsOn(cut, "files"));
        Assert.True(IsOn(cut, "threads"));
    }

    /// <summary>
    ///     Test that turning off each content switch shows its own regulatory warning right away, that the summary
    ///     banner names what is left out, and that turning the switch on again removes both.
    /// </summary>
    /// <param name="key">The key of the switch.</param>
    /// <param name="section">The section name the banner lists.</param>
    [Theory]
    [InlineData("threads", "Review discussion threads")]
    [InlineData("reviewer-names", "Reviewer names")]
    [InlineData("checklist-wording", "Checklist wording")]
    [InlineData("files", "File list and hashes")]
    [InlineData("git-context", "Git context")]
    public async Task EvidenceSettingsPage_Switch_ContentOff_ShowsItsWarningAndTheBanner(string key, string section)
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act: turn the switch off
        await SetAsync(cut, key, on: false);

        // Assert: its own warning mentions the regulatory regime and the compliance lead; the banner lists it
        var warning = cut.Find($"[data-testid='evidence-{key}-warning']").TextContent;
        Assert.Contains("regulatory regime", warning, StringComparison.Ordinal);
        Assert.Contains("quality or compliance lead", warning, StringComparison.Ordinal);
        Assert.Single(cut.FindAll(".setting .notice.warning"));
        Assert.Contains(section, cut.Find("[data-testid='evidence-reduced-banner']").TextContent, StringComparison.Ordinal);

        // Act: turn it on again
        await SetAsync(cut, key, on: true);

        // Assert: nothing is left
        Assert.Empty(cut.FindAll($"[data-testid='evidence-{key}-warning']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-reduced-banner']"));
    }

    /// <summary>
    ///     Test that the reviewer names warning has the tailored wording the design asks for, and that the warnings
    ///     differ from one switch to the next.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Switch_AllContentOff_WarningsAreTailoredAndBannerListsAll()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        var keys = new[] { "threads", "reviewer-names", "checklist-wording", "files", "git-context" };

        // Act
        foreach (var key in keys)
        {
            await SetAsync(cut, key, on: false);
        }

        // Assert
        Assert.Equal(
            "Omitting reviewer names may not satisfy your regulatory regime. Confirm with your quality or "
            + "compliance lead.",
            cut.Find("[data-testid='evidence-reviewer-names-warning']").TextContent);
        Assert.Equal(5, keys.Select(k => cut.Find($"[data-testid='evidence-{k}-warning']").TextContent).Distinct().Count());
        var banner = cut.Find("[data-testid='evidence-reduced-banner']").TextContent;
        foreach (var section in new[]
                 {
                     "Review discussion threads", "Reviewer names", "Checklist wording", "File list and hashes",
                     "Git context",
                 })
        {
            Assert.Contains(section, banner, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that the exposure warning shows while an anonymous switch is on and not otherwise, and that the
    ///     stronger line about comments and names shows only when anonymous PDFs would still carry them.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Switch_AnonymousOn_ShowsExposureWarningAndPeopleLine()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act and assert: the index alone exposes no PDF content, so there is no line about people
        await SetAsync(cut, "anonymous-index", on: true);
        Assert.NotEmpty(cut.FindAll("[data-testid='evidence-anonymous-warning']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-anonymous-people-warning']"));

        // Act and assert: anonymous PDFs with threads and names on name both
        await SetAsync(cut, "anonymous-attestations", on: true);
        var people = cut.Find("[data-testid='evidence-anonymous-people-warning']").TextContent;
        Assert.Contains("review discussion and the reviewer names", people, StringComparison.Ordinal);

        // Act and assert: with the names off only the discussion is named
        await SetAsync(cut, "reviewer-names", on: false);
        people = cut.Find("[data-testid='evidence-anonymous-people-warning']").TextContent;
        Assert.Contains("the review discussion;", people, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewer names", people, StringComparison.Ordinal);

        // Act and assert: with both off the line goes, the general warning stays
        await SetAsync(cut, "threads", on: false);
        Assert.Empty(cut.FindAll("[data-testid='evidence-anonymous-people-warning']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='evidence-anonymous-warning']"));

        // Act and assert: nothing anonymous, no exposure warning
        await SetAsync(cut, "anonymous-index", on: false);
        await SetAsync(cut, "anonymous-attestations", on: false);
        Assert.Empty(cut.FindAll("[data-testid='evidence-anonymous-warning']"));
    }

    /// <summary>
    ///     Test that saving stores the choices, confirms in words, keeps the page open, and records one audit entry
    ///     of the project that names the changed keys with their old and new values and the actor.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Save_Change_StoresConfirmsAndAudits()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SetAsync(cut, "anonymous-attestations", on: true);
        await SetAsync(cut, "reviewer-names", on: false);

        // Act
        await cut.Find("[data-testid='evidence-settings-save']").ClickAsync();

        // Assert: stored, confirmed, still on the page with the choices
        var stored = await SettingsService.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.True(stored.AnonymousAttestations);
        Assert.False(stored.IncludeReviewerNames);
        Assert.False(stored.AnonymousIndex);
        Assert.Equal("Evidence settings saved.", cut.Find("[data-testid='evidence-settings-status']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-error']"));
        Assert.True(IsOn(cut, "anonymous-attestations"));

        // Assert: one audit entry of the project, by the actor, with the changed keys and values
        var entry = Assert.Single(Store.Audits, a => a.Action == EvidenceSettingsService.ChangedAction);
        Assert.Equal(apollo, entry.ProjectId);
        Assert.Equal("padm", entry.ActorUsername);
        Assert.Equal("AnonymousAttestations off->on; IncludeReviewerNames on->off", entry.Detail);
    }

    /// <summary>
    ///     Test that saving the settings unchanged says so and records nothing.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Save_NoChange_SaysNothingToSaveAndRecordsNothing()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);

        // Act
        await cut.Find("[data-testid='evidence-settings-save']").ClickAsync();

        // Assert
        Assert.Contains("Nothing to save", cut.Find("[data-testid='evidence-settings-status']").TextContent);
        Assert.DoesNotContain(Store.Audits, a => a.Action == EvidenceSettingsService.ChangedAction);
    }

    /// <summary>
    ///     Test that a save based on settings that someone else changed in the meantime overwrites nothing, says so,
    ///     and shows the settings that are stored now.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Save_ChangedByOthers_ShowsMessageAndTheCurrentSettings()
    {
        // Arrange: the page is open; then someone else turns the index on
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SettingsService.SetAsync(
            TestActors.Ann,
            apollo,
            EvidenceSettings.Default,
            EvidenceSettings.Default with { AnonymousIndex = true },
            TestContext.Current.CancellationToken);
        await SetAsync(cut, "files", on: false);

        // Act
        await cut.Find("[data-testid='evidence-settings-save']").ClickAsync();

        // Assert: the message, the other person's change shown, ours not stored
        Assert.Equal(
            EvidenceSettingsService.ChangedByOthersMessage,
            cut.Find("[data-testid='evidence-settings-error']").TextContent);
        Assert.True(IsOn(cut, "anonymous-index"));
        Assert.True(IsOn(cut, "files"));
        var stored = await SettingsService.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.True(stored.IncludeFiles);
        Assert.Equal(1, Store.Audits.Count(a => a.Action == EvidenceSettingsService.ChangedAction));
    }

    /// <summary>
    ///     Test that a save after the right was lost while the page was open (membership revoked, demoted to plain
    ///     member, or session ended without the page knowing) stores nothing, records nothing, and leaves the
    ///     not-found state with no switches, no message and none of the project's data.
    /// </summary>
    /// <param name="how">How the right is lost: <c>revoked</c>, <c>demoted</c> or <c>ended</c>.</param>
    [Theory]
    [InlineData("revoked")]
    [InlineData("demoted")]
    [InlineData("ended")]
    public async Task EvidenceSettingsPage_Save_RightLostWhileOpen_StoresNothingAndDropsThePage(string how)
    {
        // Arrange: the page is open for a project admin who has changed a switch
        var apollo = await ArrangeAsync();
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SetAsync(cut, "anonymous-index", on: true);
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
        await cut.Find("[data-testid='evidence-settings-save']").ClickAsync();

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-form']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-error']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-status']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        var stored = await SettingsService.GetAsync(TestActors.Ann, apollo, TestContext.Current.CancellationToken);
        Assert.False(stored.AnonymousIndex);
        Assert.DoesNotContain(Store.Audits, a => a.Action == EvidenceSettingsService.ChangedAction);
    }

    /// <summary>
    ///     Test that the page loads again when its project changes, and that the choices made for the first project
    ///     do not follow to a project the user may not administer.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_Load_OtherProject_DropsTheChoicesAndTheProject()
    {
        // Arrange: padm administers Apollo only
        var apollo = await ArrangeAsync();
        var gemini = (await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken)).Id;
        SignInAs("padm", "Padm", isSystemAdmin: false);
        var cut = RenderPage(apollo);
        await SetAsync(cut, "anonymous-index", on: true);

        // Act
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(EvidenceSettingsPage.ProjectId)] = gemini })));

        // Assert
        Assert.NotEmpty(cut.FindAll("[data-testid='project-not-found']"));
        Assert.Empty(cut.FindAll("[data-testid='evidence-settings-form']"));
        Assert.DoesNotContain("Apollo", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemini", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the interactive marker is set once the component has rendered on a live circuit.
    /// </summary>
    [Fact]
    public async Task EvidenceSettingsPage_AfterRender_Interactive_SetsMarker()
    {
        // Arrange
        var apollo = await ArrangeAsync();
        SignInAs("root", "Root", UserRole.Admin);

        // Act
        var cut = RenderPage(apollo);

        // Assert
        Assert.Equal("true", cut.Find("[data-testid='evidence-settings-page']").GetAttribute("data-interactive"));
    }
}
