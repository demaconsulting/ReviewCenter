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

using DemaConsulting.ReviewCenter.ContentSources;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace DemaConsulting.ReviewCenter.Tests.ContentSources;

/// <summary>
///     Tests for <see cref="CredentialProtector"/>: encryption round trip, a feature-specific purpose and
///     unreadable text after the key ring is lost.
/// </summary>
public class CredentialProtectorTests
{
    /// <summary>
    ///     Creates a Data Protection provider whose key ring lives in a folder.
    /// </summary>
    /// <param name="keysFolder">The folder of the key ring.</param>
    /// <returns>The provider.</returns>
    private static IDataProtectionProvider Provider(string keysFolder)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("Tests").PersistKeysToFileSystem(new DirectoryInfo(keysFolder));
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    /// <summary>
    ///     Test that a token survives a round trip and that the protected text does not contain it.
    /// </summary>
    [Fact]
    public void CredentialProtector_ProtectUnprotect_RoundTripsAndHidesToken()
    {
        // Arrange: a protector over a persisted key ring
        using var keys = new TempFolder();
        var protector = new CredentialProtector(Provider(keys.Path));

        // Act: protect and unprotect a token
        var protectedText = protector.Protect("ghp_Secret123");
        var plain = protector.Unprotect(protectedText);

        // Assert: the text is not the token and the token comes back
        Assert.DoesNotContain("Secret123", protectedText, StringComparison.Ordinal);
        Assert.Equal("ghp_Secret123", plain);
    }

    /// <summary>
    ///     Test that a persisted key ring lets a new protector (a restart) read earlier text.
    /// </summary>
    [Fact]
    public void CredentialProtector_PersistedKeyRing_SurvivesRestart()
    {
        // Arrange: text protected by one provider over a key ring folder
        using var keys = new TempFolder();
        var protectedText = new CredentialProtector(Provider(keys.Path)).Protect("tok");

        // Act: read it with a second provider over the same folder
        var plain = new CredentialProtector(Provider(keys.Path)).Unprotect(protectedText);

        // Assert: readable
        Assert.Equal("tok", plain);
    }

    /// <summary>
    ///     Test that text protected under another key ring is reported as unreadable without the text in the message.
    /// </summary>
    [Fact]
    public void CredentialProtector_Unprotect_OtherKeyRing_ThrowsCredentialsUnreadable()
    {
        // Arrange: text protected by one key ring and a protector over a different one
        using var first = new TempFolder();
        using var second = new TempFolder();
        var protectedText = new CredentialProtector(Provider(first.Path)).Protect("tok");
        var other = new CredentialProtector(Provider(second.Path));

        // Act: read it with the other key ring, and read garbage
        var lost = Assert.Throws<DomainRuleException>(() => other.Unprotect(protectedText));
        var garbage = Assert.Throws<DomainRuleException>(() => other.Unprotect("not protected"));

        // Assert: the rule is named, no inner exception leaks, and the text is not repeated
        Assert.Equal(DomainRule.CredentialsUnreadable, lost.Rule);
        Assert.Equal(DomainRule.CredentialsUnreadable, garbage.Rule);
        Assert.Null(lost.InnerException);
        Assert.DoesNotContain(protectedText, lost.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the feature has its own purpose: another purpose on the same key ring cannot read the text.
    /// </summary>
    [Fact]
    public void CredentialProtector_Purpose_IsolatesTokensFromOtherUses()
    {
        // Arrange: a provider, text protected by the credential protector and a protector of another purpose
        using var keys = new TempFolder();
        var provider = Provider(keys.Path);
        var protectedText = new CredentialProtector(provider).Protect("tok");
        var foreign = provider.CreateProtector("Some.Other.Purpose");

        // Act and Assert: the other purpose cannot read it, and the purpose names the feature
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => foreign.Unprotect(protectedText));
        Assert.Contains("RepositoryCredentials", CredentialProtector.Purpose, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that null arguments are refused.
    /// </summary>
    [Fact]
    public void CredentialProtector_NullArguments_ThrowArgumentNull()
    {
        // Arrange: a protector
        using var keys = new TempFolder();
        var protector = new CredentialProtector(Provider(keys.Path));

        // Act and Assert: null provider and text are refused
        Assert.Throws<ArgumentNullException>(() => new CredentialProtector(null!));
        Assert.Throws<ArgumentNullException>(() => protector.Protect(null!));
        Assert.Throws<ArgumentNullException>(() => protector.Unprotect(null!));
    }
}
