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

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="SignConfirm"/> component: the fields for the password and the second-factor code,
///     the credentials handed to the page once and then cleared, and the variant for a person who signs in with an
///     identity provider. Rendered in memory with bUnit.
/// </summary>
public class SignConfirmTests : BunitContext
{
    /// <summary>
    ///     Renders the component and records what the form hands over.
    /// </summary>
    /// <param name="needsPassword">Whether the person confirms with a password.</param>
    /// <param name="needsCode">Whether the person has a second factor.</param>
    /// <param name="received">The credentials handed over, one per sent form.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<SignConfirm> RenderForm(bool needsPassword, bool needsCode, List<SignerCredentials> received)
    {
        return Render<SignConfirm>(parameters => parameters
            .Add(component => component.NeedsPassword, needsPassword)
            .Add(component => component.NeedsCode, needsCode)
            .Add(component => component.OnSubmitAndSign, credentials => received.Add(credentials)));
    }

    /// <summary>
    ///     Test that a password user gets a password field only, and the typed password is handed over once and cleared.
    /// </summary>
    [Fact]
    public async Task SignConfirm_PasswordUser_HandsPasswordOverOnceAndClearsField()
    {
        // Arrange
        var received = new List<SignerCredentials>();
        var cut = RenderForm(true, false, received);
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-totp']"));
        await cut.Find("[data-testid='submit-sign-password']").InputAsync("secret words");

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert
        var credentials = Assert.Single(received);
        Assert.Equal("secret words", credentials.Password);
        Assert.Null(credentials.TotpCode);
        Assert.Equal(string.Empty, cut.Find("[data-testid='submit-sign-password']").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>
    ///     Test that a user with a second factor also gets a code field, and both values are handed over.
    /// </summary>
    [Fact]
    public async Task SignConfirm_UserWithSecondFactor_HandsPasswordAndCodeOver()
    {
        // Arrange
        var received = new List<SignerCredentials>();
        var cut = RenderForm(true, true, received);
        await cut.Find("[data-testid='submit-sign-password']").InputAsync("secret words");
        await cut.Find("[data-testid='submit-sign-totp']").InputAsync("123456");

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert
        var credentials = Assert.Single(received);
        Assert.Equal("secret words", credentials.Password);
        Assert.Equal("123456", credentials.TotpCode);
    }

    /// <summary>
    ///     Test that a person who signs in with an identity provider gets no fields and an empty set of credentials.
    /// </summary>
    [Fact]
    public async Task SignConfirm_ProviderUser_ShowsNoFieldsAndHandsOverNothing()
    {
        // Arrange
        var received = new List<SignerCredentials>();
        var cut = RenderForm(false, true, received);

        // Act
        await cut.Find("[data-testid='submit-sign-form']").SubmitAsync();

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-password']"));
        Assert.Empty(cut.FindAll("[data-testid='submit-sign-totp']"));
        Assert.NotNull(cut.Find("[data-testid='submit-sign-oidc-hint']"));
        var credentials = Assert.Single(received);
        Assert.Null(credentials.Password);
        Assert.Null(credentials.TotpCode);
    }
}
