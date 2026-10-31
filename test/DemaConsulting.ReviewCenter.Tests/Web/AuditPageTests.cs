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
    ///     Test that a long hexadecimal value in an entry detail is shown shortened, with the full value in the tooltip.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_DetailWithLongHash_ShowsShortenedValueWithFullValueInTooltip()
    {
        // Arrange: an entry whose detail carries a 64-character hash between plain words
        await SignInAsAdminAsync();
        var hash = new string('a', 32) + new string('b', 32);
        await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "review.created", $"fingerprint {hash} recorded", 1, null), TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = Render<AuditPage>();

        // Assert: the cell shows 12 characters and an ellipsis, keeps the words, and the full value stays available
        var detail = cut.Find("[data-testid='audit-detail']");
        var shortened = detail.QuerySelector(".audit-hash")!;
        Assert.Equal(new string('a', 12) + "…", shortened.TextContent);
        Assert.Equal(hash, shortened.GetAttribute("title"));
        Assert.Contains("fingerprint", detail.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, detail.TextContent, StringComparison.Ordinal);
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
        SignInAs("ann", "Ann", UserRole.Reviewer);
        await RecordAsync(2);

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.Contains("System administrator rights are required", cut.Find("[data-testid='audit-error']").TextContent, StringComparison.Ordinal);
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
        Assert.Contains("report this entry number", cut.Find("[data-testid='audit-broken-hint']").TextContent, StringComparison.Ordinal);
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
        Assert.Contains("System administrator rights are required", cut.Find("[data-testid='audit-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='audit-result']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-table']"));
        Assert.Empty(cut.FindAll("[data-testid='audit-row']"));
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
        SignInAs("ann", "Ann", UserRole.Reviewer);
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

    /// <summary>
    ///     Test that the page shows the filter form with every field and the project selector, with all projects the
    ///     administrator can see, and no Clear link while nothing is filtered.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_Admin_ShowsFilterFormWithProjectSelector()
    {
        // Arrange
        await SignInAsAdminAsync();
        await Projects.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);
        await Projects.CreateAsync("Gemini", null, TestContext.Current.CancellationToken);

        // Act
        var cut = Render<AuditPage>();

        // Assert
        Assert.Equal("get", cut.Find("[data-testid='audit-filter-form']").GetAttribute("method"));
        foreach (var id in new[] { "actor", "action", "review", "from", "to", "size", "project" })
        {
            Assert.NotNull(cut.Find($"[data-testid='audit-filter-{id}']"));
        }

        Assert.Equal(
            ["All projects", "Apollo", "Gemini"],
            cut.FindAll("[data-testid='audit-filter-project'] option").Select(o => o.TextContent));
        Assert.Equal(
            ["25", "50", "100"],
            cut.FindAll("[data-testid='audit-filter-size'] option").Select(o => o.TextContent));
        Assert.Contains(
            "review.created",
            cut.FindAll("[data-testid='audit-filter-action'] optgroup[label='review'] option").Select(o => o.GetAttribute("value")));
        Assert.Empty(cut.FindAll("[data-testid='audit-filter-clear']"));
    }

    /// <summary>
    ///     Test that the actor, the action, the group and the review in the address limit the entries, and that the
    ///     form shows the values back and offers to clear them.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_QueryFilters_LimitTheEntriesAndFillTheForm()
    {
        // Arrange
        await SignInAsAdminAsync();
        var token = TestContext.Current.CancellationToken;
        await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "review.created", "first", 1, "REV-1"), token);
        await Audit.RecordAsync(AuditDraft.By(TestActors.Bob, "review.locked", "second", 1, "REV-2"), token);
        await Audit.RecordAsync(AuditDraft.By(TestActors.Bob, "project.created", "third", 1), token);

        // Act: every view is read right after it is rendered, because the next navigation re-renders the earlier ones
        var byActor = Snapshot(RenderAt("/audit?actor=bob"));
        var byAction = Snapshot(RenderAt("/audit?action=review.created"));
        var byGroup = Snapshot(RenderAt("/audit?action=review."));
        var byReview = Snapshot(RenderAt("/audit?review=REV-2&actor=Bob&size=25"));

        // Assert
        Assert.Equal(["third", "second"], byActor.Details);
        Assert.Equal(["first"], byAction.Details);
        Assert.Equal(["second", "first"], byGroup.Details);
        Assert.Equal(["second"], byReview.Details);
        Assert.Equal("bob", byActor.Actor);
        Assert.Equal("REV-2", byReview.Review);
        Assert.Equal("/audit", byActor.Clear);
        Assert.Equal("25", byReview.Size);
    }

    /// <summary>
    ///     Reads what a test checks from a rendered page.
    /// </summary>
    /// <param name="cut">The rendered page.</param>
    /// <returns>The entry details, the form values and the target of the Clear link.</returns>
    private static (List<string> Details, string? Actor, string? Review, string? Clear, string? Size) Snapshot(
        IRenderedComponent<AuditPage> cut)
    {
        return (
            cut.FindAll("[data-testid='audit-detail']").Select(e => e.TextContent).ToList(),
            cut.Find("[data-testid='audit-filter-actor']").GetAttribute("value"),
            cut.Find("[data-testid='audit-filter-review']").GetAttribute("value"),
            cut.FindAll("[data-testid='audit-filter-clear']").FirstOrDefault()?.GetAttribute("href"),
            cut.Find("[data-testid='audit-filter-size'] option[selected]").GetAttribute("value"));
    }

    /// <summary>
    ///     Test that the project selector limits the whole-trail page to that project, and never shows the entries
    ///     that belong to no project.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_ProjectQuery_ShowsOnlyThatProjectsEntries()
    {
        // Arrange
        await SignInAsAdminAsync();
        var token = TestContext.Current.CancellationToken;
        await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "of project 1", 1), token);
        await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "of project 2", 2), token);
        await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "test.event", "of no project"), token);

        // Act
        var cut = RenderAt("/audit?project=2");

        // Assert
        Assert.Equal(["of project 2"], cut.FindAll("[data-testid='audit-detail']").Select(e => e.TextContent));
    }

    /// <summary>
    ///     Test that the Older and Newer links carry the filter and the page size, and that following them pages
    ///     through the matching entries only.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_FilterWithManyMatches_LinksCarryTheFilter()
    {
        // Arrange: 60 matching entries between non-matching ones
        await SignInAsAdminAsync();
        var token = TestContext.Current.CancellationToken;
        for (var i = 1; i <= 60; i++)
        {
            await Audit.RecordAsync(AuditDraft.By(TestActors.Ann, "review.created", $"match {i}", 1, "REV-1"), token);
            await Audit.RecordAsync(AuditDraft.By(TestActors.Bob, "review.created", $"other {i}", 1, "REV-1"), token);
        }

        // Act
        var first = RenderAt("/audit?actor=ann&size=25");
        var older = first.Find("[data-testid='audit-older']").GetAttribute("href")!;
        var firstRows = first.FindAll("[data-testid='audit-row']").Count;
        var second = RenderAt(older);

        // Assert
        Assert.Equal(25, firstRows);
        Assert.StartsWith("/audit?actor=ann&size=25&before=", older, StringComparison.Ordinal);
        Assert.Equal(25, second.FindAll("[data-testid='audit-row']").Count);
        Assert.All(second.FindAll("[data-testid='audit-detail']"), e => Assert.StartsWith("match ", e.TextContent, StringComparison.Ordinal));
        Assert.StartsWith("/audit?actor=ann&size=25&after=", second.Find("[data-testid='audit-newer']").GetAttribute("href"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a filter that matches nothing says so, and still offers the Clear link.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_FilterMatchesNothing_ShowsNoMatchStateAndClearLink()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(2);

        // Act
        var cut = RenderAt("/audit?actor=nobody-has-this-name");

        // Assert
        Assert.Equal("No entry matches the filters.", cut.Find("[data-testid='audit-empty']").TextContent);
        Assert.NotNull(cut.Find("[data-testid='audit-filter-clear']"));
    }

    /// <summary>
    ///     Test that hostile query values never break the page: markup is encoded, huge texts are cut, and bad
    ///     dates, sizes and projects fall back to the defaults.
    /// </summary>
    /// <param name="query">The query string.</param>
    [Theory]
    [InlineData("?actor=<marquee>text</marquee>&review='><blink>")]
    [InlineData("?from=garbage&to=2025-99-99&size=-1&project=-3&action=%27%3B%20drop%20table")]
    [InlineData("?size=99999999999999999999&project=99999999999999999999&after=-4")]
    public async Task AuditPage_Render_HostileQueryValues_AreHandledAndEncoded(string query)
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(2);

        // Act
        var cut = RenderAt("/audit" + query);

        // Assert
        Assert.Empty(cut.FindAll("marquee"));
        Assert.Empty(cut.FindAll("blink"));
        Assert.Empty(cut.FindAll("[data-testid='audit-error']"));
        Assert.NotNull(cut.Find("[data-testid='audit-filter-form']"));
    }

    /// <summary>
    ///     Test that a huge actor text is cut to the stored width instead of failing the page.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_HugeActorText_IsCutAndPageRenders()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(1);

        // Act
        var cut = RenderAt("/audit?actor=" + new string('a', 5000));

        // Assert
        Assert.True(cut.Find("[data-testid='audit-filter-actor']").GetAttribute("value")!.Length <= AuditText.MaxActorNameLength);
        Assert.Empty(cut.FindAll("[data-testid='audit-error']"));
    }

    /// <summary>
    ///     Test that a start date after the end date is ignored with a message instead of failing, and the entries show.
    /// </summary>
    [Fact]
    public async Task AuditPage_Render_FromAfterTo_ShowsMessageAndIgnoresTheDates()
    {
        // Arrange
        await SignInAsAdminAsync();
        await RecordAsync(2);

        // Act
        var cut = RenderAt("/audit?from=2030-01-02&to=2030-01-01");

        // Assert
        Assert.Contains("ignored", cut.Find("[data-testid='audit-filter-message']").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, cut.FindAll("[data-testid='audit-row']").Count);
    }
}
