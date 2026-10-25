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
///     Unit tests for the <see cref="AuditPage"/> component, rendered in memory with bUnit.
/// </summary>
public class AuditPageTests : PageTestBase
{
    /// <summary>
    ///     A password that satisfies the policy.
    /// </summary>
    private const string Password = "correct horse battery";

    /// <summary>
    ///     Creates an enabled administrator and signs in as that user.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <returns>The user.</returns>
    private async Task<User> SignInAsAdminAsync(string username = "root")
    {
        var user = await Users.CreateAsync(username, "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs(username, "Root", UserRole.Admin);
        return user;
    }

    /// <summary>
    ///     Renders the page at an address, so that the query values reach it as in the browser.
    /// </summary>
    /// <param name="address">The address, relative to the site root.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<AuditPage> RenderAt(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return Render<AuditPage>();
    }

    /// <summary>
    ///     Records entries without a review.
    /// </summary>
    /// <param name="count">How many entries.</param>
    private async Task RecordAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "project.created", $"entry {i}", 1, i % 2 == 0 ? "REV-1" : null), TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Test that an administrator sees the trail, newest first, with sequence, actor, action and detail.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_Admin_ShowsEntriesNewestFirst()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(3);

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.Equal(["3", "2", "1"], cut.FindAll("[data-testid='audit-sequence']").Select(e => e.TextContent));
        Assert.Equal(["entry 3", "entry 2", "entry 1"], cut.FindAll("[data-testid='audit-detail']").Select(e => e.TextContent));
        Assert.Equal(["Ann (ann)", "Ann (ann)", "Ann (ann)"], cut.FindAll("[data-testid='audit-actor']").Select(e => e.TextContent));
        Assert.Equal(["project.created", "project.created", "project.created"], cut.FindAll("[data-testid='audit-action']").Select(e => e.TextContent));
        Assert.Equal(["project 1", "1/REV-1", "project 1"], cut.FindAll("[data-testid='audit-review']").Select(e => e.TextContent));
        Assert.Empty(cut.FindAll("[data-testid='audit-error']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-result']"));
    }

    /// <summary>
    ///     Test that an empty trail says so.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_EmptyTrail_ShowsEmptyMessage()
    {
        // Arrange
        await SignInAsAdminAsync();

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.NotNull(cut.Find("[data-testid='audit-empty']"));
    }

    /// <summary>
    ///     Test that a reviewer who gets the component anyway is refused and sees no entries.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_NonAdmin_IsRefusedAndShowsNoEntries()
    {
        // Arrange: a reviewer is signed in and the trail has an entry
        await Users.CreateAsync("ann", "Ann", UserRole.Reviewer, Password, TestContext.Current.CancellationToken);
        SignInAs("ann", "Ann");
        await RecordAsync(2);

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.Contains("Administrator rights are required", cut.Find("[data-testid='audit-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='audit-table']"));
        Assert.DoesNotContain("entry 1", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a user whose claims say administrator but who is a reviewer (or disabled) in the store is refused.
    /// </summary>
    /// <param name="disabled">Whether the user is disabled instead of demoted.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditPage_Render_ClaimsAdminButStoreSaysNot_IsRefused(bool disabled)
    {
        // Arrange: two administrators so that one can be demoted or disabled; the second one is signed in
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("dee", "Dee", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        if (disabled)
        {
            await Users.SetEnabledAsync("dee", false, TestContext.Current.CancellationToken);
        }
        else
        {
            await Users.SetRoleAsync("dee", UserRole.Reviewer, TestContext.Current.CancellationToken);
        }

        SignInAs("dee", "Dee", UserRole.Admin);
        await RecordAsync(1);

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.NotNull(cut.Find("[data-testid='audit-error']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-table']"));
    }

    /// <summary>
    ///     Test that Verify on an intact chain shows the count and the full head hash.
    /// </summary>
    [Fact]
    public async Task AuditPage_Verify_IntactChain_ShowsCountAndFullHeadHash()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(3);
        var head = AuditStore.Entries[^1].Hash;
        var cut = Render<AuditPage>();

        // Act
        await cut.Find("[data-testid='audit-verify']").ClickAsync();

        // Assert
        Assert.Equal(64, head.Length);
        Assert.Equal($"Chain intact: 3 entries, head hash {head}", cut.Find("[data-testid='audit-result']").TextContent);
        Assert.Equal(head, cut.Find("[data-testid='audit-head-hash']").TextContent);
    }

    /// <summary>
    ///     Test that Verify on a tampered chain names the first bad entry and still shows the head hash.
    /// </summary>
    [Fact]
    public async Task AuditPage_Verify_TamperedChain_ShowsFirstBadEntry()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(4);
        AuditStore.TamperDetail(2, "forged");
        var cut = Render<AuditPage>();

        // Act
        await cut.Find("[data-testid='audit-verify']").ClickAsync();

        // Assert
        Assert.StartsWith("Chain broken at entry 2", cut.Find("[data-testid='audit-result']").TextContent, StringComparison.Ordinal);
        Assert.Equal(AuditStore.Entries[0].Hash, cut.Find("[data-testid='audit-head-hash']").TextContent);
        Assert.DoesNotContain("Chain intact", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Verify on an empty trail reports zero entries.
    /// </summary>
    [Fact]
    public async Task AuditPage_Verify_EmptyTrail_ReportsIntactWithZeroEntries()
    {
        // Arrange
        await SignInAsAdminAsync();
        var cut = Render<AuditPage>();

        // Act
        await cut.Find("[data-testid='audit-verify']").ClickAsync();

        // Assert
        Assert.StartsWith("Chain intact: 0 entries", cut.Find("[data-testid='audit-result']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the Verify action is refused, without reading the trail, once the user is no longer an
    ///     administrator, even though the page was opened as one.
    /// </summary>
    [Fact]
    public async Task AuditPage_Verify_UserDemotedAfterOpening_IsRefusedWithoutReadingTrail()
    {
        // Arrange: two administrators; the page is opened as the second one, who is then demoted
        await Users.CreateAsync("root", "Root", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        await Users.CreateAsync("dee", "Dee", UserRole.Admin, Password, TestContext.Current.CancellationToken);
        SignInAs("dee", "Dee", UserRole.Admin);
        await RecordAsync(2);
        var cut = Render<AuditPage>();
        await Users.SetRoleAsync("dee", UserRole.Reviewer, TestContext.Current.CancellationToken);
        var readsBefore = AuditStore.ReadAfterCalls;

        // Act
        await cut.Find("[data-testid='audit-verify']").ClickAsync();

        // Assert
        Assert.Contains("Administrator rights are required", cut.Find("[data-testid='audit-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='audit-result']"));
        Assert.Equal(readsBefore, AuditStore.ReadAfterCalls);
    }

    /// <summary>
    ///     Test that a long trail is paged by 50 with Older and Newer links.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_LongTrail_PagesByFifty()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(120);

        // Act and assert, page by page: newest 50 first with Older only; then both links; then the last 20 with Newer only
        var first = Render<AuditPage>();
        Assert.Equal(50, first.FindAll("[data-testid='audit-row']").Count);
        Assert.Equal("120", first.FindAll("[data-testid='audit-sequence']")[0].TextContent);
        Assert.Equal("/audit?before=71", first.Find("[data-testid='audit-older']").GetAttribute("href"));
        Assert.Empty(first.FindAll("[data-testid='audit-newer']"));
        await DisposeComponentsAsync();

        var second = RenderAt("/audit?before=71");
        Assert.Equal("70", second.FindAll("[data-testid='audit-sequence']")[0].TextContent);
        Assert.Equal("/audit?after=70", second.Find("[data-testid='audit-newer']").GetAttribute("href"));
        await DisposeComponentsAsync();

        var last = RenderAt("/audit?before=21");
        Assert.Equal(20, last.FindAll("[data-testid='audit-row']").Count);
        Assert.Empty(last.FindAll("[data-testid='audit-older']"));
        Assert.NotNull(last.Find("[data-testid='audit-newer']"));
    }

    /// <summary>
    ///     Test that markup in entries is shown as text and never becomes elements.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_MarkupInEntries_IsEscaped()
    {
        // Arrange
        await SignInAsAdminAsync();
        await Audit.RecordAsync(
            AuditDraft.By(new Actor("mal", "<b>Mal</b>"), "x.y", "<script>alert(1)</script>", 1, "<i>R</i>"),
            TestContext.Current.CancellationToken);

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.Equal("<script>alert(1)</script>", cut.Find("[data-testid='audit-detail']").TextContent);
        Assert.Equal("<b>Mal</b> (mal)", cut.Find("[data-testid='audit-actor']").TextContent);
        Assert.Equal("1/<i>R</i>", cut.Find("[data-testid='audit-review']").TextContent);
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("td b"));
        Assert.Empty(cut.FindAll("td i"));
    }

    /// <summary>
    ///     Test that a reviewer who opens the audit route through the router sees access denied and no entries.
    /// </summary>
    [Fact]
    public async Task Routes_Render_ReviewerOpensAuditRoute_ShowsAccessDeniedNotTrail()
    {
        // Arrange
        SignInAs("ann", "Ann");
        await RecordAsync(1);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/audit");

        // Act
        var cut = Render<Routes>();

        // Assert
        Assert.NotNull(cut.Find("[data-testid='access-denied']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-verify']"));
        Assert.DoesNotContain("entry 1", cut.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an administrator who opens the audit route through the router is shown the page.
    /// </summary>
    [Fact]
    public async Task Routes_Render_AdminOpensAuditRoute_ShowsAuditPage()
    {
        // Arrange
        await SignInAsAdminAsync();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/audit");

        // Act
        var cut = Render<Routes>();

        // Assert
        Assert.NotNull(cut.Find("[data-testid='audit-verify']"));
        Assert.Empty(cut.FindAll("[data-testid='access-denied']"));
    }
}
