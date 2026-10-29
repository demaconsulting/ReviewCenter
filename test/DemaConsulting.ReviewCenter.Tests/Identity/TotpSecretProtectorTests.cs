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

using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Identity;
using DemaConsulting.ReviewCenter.Tests.Core;
using Microsoft.AspNetCore.DataProtection;

namespace DemaConsulting.ReviewCenter.Tests.Identity;

/// <summary>
///     Tests of <see cref="TotpSecretProtector"/> over the Data Protection system.
/// </summary>
public class TotpSecretProtectorTests
{
    /// <summary>
    ///     Test that a secret survives a protect and unprotect round trip and is not stored in the clear.
    /// </summary>
    [Fact]
    public void TotpSecretProtector_ProtectAndUnprotect_RoundTripsAndHidesTheSecret()
    {
        // Arrange: a protector over an in-memory key ring and a secret
        var protector = new TotpSecretProtector(new EphemeralDataProtectionProvider());
        var secret = TotpOracle.VectorSecret;

        // Act: protect and unprotect
        var stored = protector.Protect(7, secret);
        var back = protector.Unprotect(7, stored);

        // Assert: same bytes, and neither the Base32 text nor the raw ASCII is visible in the stored value
        Assert.Equal(secret, back);
        Assert.DoesNotContain("GEZDGNBV", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "12345678901234567890", System.Text.Encoding.Latin1.GetString(Convert.FromBase64String(stored)), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a stored value copied to another user is unreadable.
    /// </summary>
    [Fact]
    public void TotpSecretProtector_Unprotect_ValueOfAnotherUser_IsUnreadable()
    {
        // Arrange: a value protected for user 1
        var protector = new TotpSecretProtector(new EphemeralDataProtectionProvider());
        var stored = protector.Protect(1, TotpOracle.VectorSecret);

        // Act: read it as user 2
        var exception = Assert.Throws<DomainRuleException>(() => protector.Unprotect(2, stored));

        // Assert: the rule says unreadable and nothing of the value is in the message
        Assert.Equal(DomainRule.TotpSecretUnreadable, exception.Rule);
        Assert.DoesNotContain(stored, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a value from another key ring and damaged text are unreadable.
    /// </summary>
    /// <param name="kind">The kind of damage.</param>
    [Theory]
    [InlineData("other-ring")]
    [InlineData("not-base64")]
    [InlineData("truncated")]
    public void TotpSecretProtector_Unprotect_DamagedOrForeignValue_IsUnreadable(string kind)
    {
        // Arrange: a value, and a protector over another key ring
        var first = new TotpSecretProtector(new EphemeralDataProtectionProvider());
        var second = new TotpSecretProtector(new EphemeralDataProtectionProvider());
        var stored = first.Protect(1, TotpOracle.VectorSecret);
        var (reader, text) = kind switch
        {
            "other-ring" => (second, stored),
            "not-base64" => (first, "###"),
            _ => (first, stored[..(stored.Length / 2)])
        };

        // Act: read it
        var exception = Assert.Throws<DomainRuleException>(() => reader.Unprotect(1, text));

        // Assert: unreadable
        Assert.Equal(DomainRule.TotpSecretUnreadable, exception.Rule);
    }

    /// <summary>
    ///     Test that null arguments are refused.
    /// </summary>
    [Fact]
    public void TotpSecretProtector_NullArguments_Throw()
    {
        // Arrange: a protector
        var protector = new TotpSecretProtector(new EphemeralDataProtectionProvider());

        // Act and Assert: each misuse is refused
        Assert.Throws<ArgumentNullException>(() => new TotpSecretProtector(null!));
        Assert.Throws<ArgumentNullException>(() => protector.Protect(1, null!));
        Assert.Throws<ArgumentNullException>(() => protector.Unprotect(1, null!));
    }
}
