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
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests of the parts of the sign-in, user list and account pages that concern sign-in with an OpenID
///     Connect provider, rendered in memory with bUnit.
/// </summary>
public class OidcPageTests : PageTestBase
{
    /// <summary>
    ///     The provider name the pages show.
    /// </summary>
    private const string Provider = "Acme ID";

    /// <summary>
    ///     Initializes a new instance of the <see cref="OidcPageTests"/> class; the account page asks the browser for
    ///     the stored appearance, which these tests do not script.
    /// </summary>
    public OidcPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    ///     Test that the sign-in page offers the provider button beside the local form, and no message.
    /// </summary>
    [Fact]
    public void LoginPage_Render_ProviderEnabled_ShowsButtonBesideLocalForm()
    {
        // Arrange: the provider is on and nobody is signed in
        UseProvider(true);
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?returnUrl=/projects");

        // Act: render the page
        var cut = Render<LoginPage>();

        // Assert: both forms are there, the button names the provider, the local form still works, no error
        Assert.Equal($"Sign in with {Provider}", cut.Find("[data-testid='oidc-login']").TextContent);
        Assert.Equal("/auth/oidc/login", cut.Find("[data-testid='oidc-form']").GetAttribute("action"));
        Assert.Equal("/projects", cut.Find("[data-testid='oidc-form'] input[name='returnUrl']").GetAttribute("value"));
        Assert.Equal("/auth/login", cut.Find("[data-testid='login-form']").GetAttribute("action"));
        Assert.Empty(cut.FindAll("[data-testid='oidc-error']"));
    }

    /// <summary>
    ///     Test that the sign-in page shows one generic message next to the provider button after a failed attempt,
    ///     and not the message of the local form.
    /// </summary>
    [Fact]
    public void LoginPage_Render_ProviderFailed_ShowsGenericMessageNextToButton()
    {
        // Arrange: the provider is on and the address says the provider attempt failed
        UseProvider(true);
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?error=oidc");

        // Act: render the page
        var cut = Render<LoginPage>();

        // Assert: the provider message is shown, the local one is not, and nothing reveals the cause
        var message = cut.Find("[data-testid='oidc-error']").TextContent;
        Assert.Contains(Provider, message, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='login-error']"));
    }

    /// <summary>
    ///     Test that the sign-in page has no provider button while the provider is off.
    /// </summary>
    [Fact]
    public void LoginPage_Render_ProviderOff_ShowsNoButton()
    {
        // Arrange: the provider is off, even though the address asks for its message
        SignOut();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?error=oidc");

        // Act: render the page
        var cut = Render<LoginPage>();

        // Assert: no provider card, and the local form reports the failure
        Assert.Empty(cut.FindAll("[data-testid='oidc-card']"));
        Assert.Single(cut.FindAll("[data-testid='login-form']"));
    }

    /// <summary>
    ///     Test that the user list marks an account of the provider and says it has no password, and that a local
    ///     account has neither.
    /// </summary>
    [Fact]
    public async Task UsersPage_Render_ExternalUser_ShowsMarkerAndNoPasswordControls()
    {
        // Arrange: one provider account and one local account
        UseProvider(true);
        var external = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-1", "zoe", "Zoe", TestContext.Current.CancellationToken)).User!;
        var local = await Users.CreateAsync(
            "bea", "Bea", UserRole.Reviewer, "correct horse battery", TestContext.Current.CancellationToken);

        // Act: render the page
        var cut = Render<UsersPage>();

        // Assert: the marker and the no-password note are on the provider account only, with no password controls
        Assert.Equal("OIDC", cut.Find($"[data-testid='user-oidc-{external.Id}']").TextContent);
        Assert.Contains(Provider, cut.Find($"[data-testid='user-oidc-{external.Id}']").GetAttribute("title"));
        Assert.Contains("no local password", cut.Find($"[data-testid='user-no-password-{external.Id}']").TextContent);
        Assert.Empty(cut.FindAll($"[data-testid='user-password-input-{external.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-set-password-{external.Id}']"));
        Assert.Empty(cut.FindAll($"[data-testid='user-oidc-{local.Id}']"));
        Assert.NotNull(cut.Find($"[data-testid='user-set-password-{local.Id}']"));
    }

    /// <summary>
    ///     Test that the account page of a provider account offers no password change and names the provider.
    /// </summary>
    [Fact]
    public async Task AccountPage_Render_ExternalUser_ShowsNoPasswordForm()
    {
        // Arrange: the signed-in user is a provider account
        UseProvider(true);
        var user = (await Users.SignInExternalAsync(
            "https://idp.example.com/", "sub-2", "ann", "Ann", TestContext.Current.CancellationToken)).User!;
        SignInAs(user.Username, "Ann", false);

        // Act: render the page
        var cut = Render<AccountPage>();

        // Assert: a sentence replaces the form
        Assert.Contains(Provider, cut.Find("[data-testid='account-external-password']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='current-password-input']"));
        Assert.Empty(cut.FindAll("[data-testid='change-password-button']"));
    }

    /// <summary>
    ///     Test that the help pages of sign-in, signing and tokens explain the identity provider.
    /// </summary>
    /// <param name="slug">The slug of the help topic.</param>
    [Theory]
    [InlineData("getting-started")]
    [InlineData("reviewers-and-signing")]
    [InlineData("api-tokens")]
    public void HelpTopicPage_Render_MentionsSignInWithProvider(string slug)
    {
        // Arrange: find the page of the topic
        var topic = HelpTopics.All.Single(t => t.Slug == slug);
        var type = typeof(HelpPage).Assembly.GetTypes().Single(t => t.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>().Any(route => route.Template == topic.Href));

        // Act: render it
        var page = Render(builder =>
        {
            builder.OpenComponent(0, type);
            builder.CloseComponent();
        });

        // Assert: it says something about the identity provider
        Assert.Contains("identity provider", page.Markup, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Makes the pages see the provider settings.
    /// </summary>
    /// <param name="enabled">Whether sign-in with the provider is on.</param>
    private void UseProvider(bool enabled)
    {
        Services.Configure<OidcOptions>(options =>
        {
            options.Enabled = enabled;
            options.DisplayName = Provider;
        });
    }
}
