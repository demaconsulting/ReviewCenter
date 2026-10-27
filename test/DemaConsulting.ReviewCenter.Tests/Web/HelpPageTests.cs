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

using System.Reflection;
using Bunit;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Tests for the help pages: the index at <c>/help</c>, one page per topic, and the not-found page.
/// </summary>
public sealed class HelpPageTests : PageTestBase
{
    /// <summary>
    ///     Test that the header offers a Help link to everyone, before the sign-in link when nobody is signed in.
    /// </summary>
    [Fact]
    public void MainLayout_Render_NobodySignedIn_ShowsHelpLinkBeforeSignIn()
    {
        // Arrange: nobody is signed in
        SignOut();

        // Act: render the layout
        var layout = Render<MainLayout>();

        // Assert: Help comes first in the right-hand group, ahead of Sign in
        Assert.Equal("/help", layout.Find("[data-testid='help-link']").GetAttribute("href"));
        var rightGroup = layout.FindAll(".app-header-group")[1];
        Assert.Equal(["help-link", "signin-link"], rightGroup.QuerySelectorAll("a").Select(a => a.GetAttribute("data-testid")).ToArray());
    }

    /// <summary>
    ///     Test that the index is headed Help and lists every topic with a link to its own page.
    /// </summary>
    [Fact]
    public void HelpPage_Render_ListsEveryTopicWithItsPageLink()
    {
        // Act: render the index
        var help = Render<HelpPage>();

        // Assert: the heading, and one card and link per topic, in order
        Assert.Equal("Help", help.Find("h1").TextContent);
        Assert.Equal(HelpTopics.All.Count, help.FindAll("[data-testid='help-topics'] > li").Count);
        foreach (var topic in HelpTopics.All)
        {
            Assert.Equal($"/help/{topic.Slug}", help.Find($"[data-testid='help-topic-link-{topic.Slug}']").GetAttribute("href"));
            Assert.Equal(topic.Title, help.Find($"[data-testid='help-topic-link-{topic.Slug}']").TextContent);
        }

        Assert.Equal("/about", help.Find("[data-testid='help-about-link']").GetAttribute("href"));
        AssertNoAnchorLinks(help);
    }

    /// <summary>
    ///     Test that every topic has a page, that the page shows the topic's heading and images with alternative
    ///     text, and that its Previous, All help topics and Next links are valid.
    /// </summary>
    [Fact]
    public void HelpTopicPages_Render_EveryTopicHasAPageWithValidNavigation()
    {
        // Assert: every topic has exactly one page of its own
        var routes = typeof(HelpPage).Assembly.GetTypes()
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .SelectMany(type => type.GetCustomAttributes<RouteAttribute>().Select(route => (route.Template, Type: type)))
            .Where(route => route.Template.StartsWith("/help/", StringComparison.Ordinal) && !route.Template.Contains('{'))
            .ToDictionary(route => route.Template, route => route.Type);
        Assert.Equal(HelpTopics.All.Count, routes.Count);

        for (var index = 0; index < HelpTopics.All.Count; index++)
        {
            var topic = HelpTopics.All[index];
            var page = RenderPage(routes[topic.Href]);

            // Assert: the heading is the topic title and every image has alternative text
            Assert.Equal(topic.Title, page.Find("h1").TextContent);
            foreach (var image in page.FindAll("img"))
            {
                Assert.False(string.IsNullOrWhiteSpace(image.GetAttribute("alt")));
                Assert.StartsWith("/help/", image.GetAttribute("src"), StringComparison.Ordinal);
            }

            // Assert: Previous and Next point at the adjacent topics and are absent at the ends; the index link is always there
            Assert.Equal("/help", page.Find("[data-testid='help-index-link']").GetAttribute("href"));
            var previous = page.FindAll("[data-testid='help-previous-link']");
            var next = page.FindAll("[data-testid='help-next-link']");
            Assert.Equal(index == 0 ? [] : [HelpTopics.All[index - 1].Href], previous.Select(a => a.GetAttribute("href")!).ToArray());
            Assert.Equal(index == HelpTopics.All.Count - 1 ? [] : [HelpTopics.All[index + 1].Href], next.Select(a => a.GetAttribute("href")!).ToArray());
            AssertNoAnchorLinks(page);
        }
    }

    /// <summary>
    ///     Test that the attestation topic explains what the signature proves and does not prove, and the signing key.
    /// </summary>
    [Fact]
    public void HelpAttestationPage_Render_ExplainsWhatTheSignatureProvesAndTheKey()
    {
        // Act: render the topic page
        var page = Render<HelpAttestationPage>();

        // Assert: the button, the limit of the proof, and the key settings are described
        var text = page.Markup;
        Assert.Equal("Signed attestation", page.Find("h1").TextContent);
        Assert.Contains("Download attestation (PDF)", text, StringComparison.Ordinal);
        Assert.Contains("does <strong>not</strong> prove who the reviewers are", text, StringComparison.Ordinal);
        Assert.Contains("SELF-SIGNED KEY - NOT AN ORGANIZATION", text, StringComparison.Ordinal);
        Assert.Contains("Attestation:RequireTrustedKey", text, StringComparison.Ordinal);
        Assert.Contains("Attestation:Provider", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that an unknown help address shows a friendly message and a link back to the index.
    /// </summary>
    [Fact]
    public void HelpNotFoundPage_Render_UnknownTopic_ShowsMessageAndIndexLink()
    {
        // Act: render the catch-all page for an unknown topic
        var page = Render<HelpNotFoundPage>(parameters => parameters.Add(p => p.Slug, "no-such-topic"));

        // Assert: a message and the way back
        Assert.NotEmpty(page.FindAll("[data-testid='help-not-found']"));
        Assert.Equal("/help", page.Find("[data-testid='help-index-link']").GetAttribute("href"));
    }

    /// <summary>
    ///     Renders a page component given by type.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<DynamicComponent> RenderPage(Type type) =>
        Render<DynamicComponent>(parameters => parameters.Add(p => p.Type, type));

    /// <summary>
    ///     Asserts that no link of the page is an in-page anchor; with the base address of the site, such a link would
    ///     lead to the home page.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="page">The rendered page.</param>
    private static void AssertNoAnchorLinks<T>(IRenderedComponent<T> page)
        where T : IComponent
    {
        foreach (var link in page.FindAll("a[href]"))
        {
            Assert.False(link.GetAttribute("href")!.StartsWith('#'));
        }
    }
}
