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

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="NotificationAddress"/>.
/// </summary>
public class NotificationAddressTests
{
    /// <summary>
    ///     Test that an ordinary address is accepted.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_PlainAddress_Succeeds()
    {
        // Arrange and Act: normalize an ordinary address
        var accepted = NotificationAddress.TryNormalize("ann@example.com", out var address);

        // Assert: accepted unchanged
        Assert.True(accepted);
        Assert.Equal("ann@example.com", address);
    }

    /// <summary>
    ///     Test that an address is trimmed and put in lower case.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_MixedCaseWithSpaces_ReturnsTrimmedLowerCase()
    {
        // Arrange and Act: normalize an address with a trailing line break and capitals
        var accepted = NotificationAddress.TryNormalize("  Ann.Smith@Example.COM\r\n", out var address);

        // Assert: trimmed and lower case
        Assert.True(accepted);
        Assert.Equal("ann.smith@example.com", address);
    }

    /// <summary>
    ///     Test that the usual punctuation of a local part is accepted.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_PlusTagInLocalPart_Succeeds()
    {
        // Arrange and Act: a plus tag and a hyphenated sub-domain
        var accepted = NotificationAddress.IsValid("ann+reviews@mail.my-company.example");

        // Assert: accepted
        Assert.True(accepted);
    }

    /// <summary>
    ///     Test that an address without an at sign is refused.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_NoAtSign_Fails()
    {
        // Arrange and Act: normalize text without an at sign
        var accepted = NotificationAddress.TryNormalize("ann.example.com", out var address);

        // Assert: refused and the output is empty
        Assert.False(accepted);
        Assert.Equal(string.Empty, address);
    }

    /// <summary>
    ///     Test that more than one at sign is refused.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_TwoAtSigns_Fails()
    {
        // Arrange and Act: two at signs
        var accepted = NotificationAddress.IsValid("ann@bob@example.com");

        // Assert: refused
        Assert.False(accepted);
    }

    /// <summary>
    ///     Test that a list of addresses is refused.
    /// </summary>
    [Theory]
    [InlineData("ann@example.com,bob@example.com")]
    [InlineData("ann@example.com;bob@example.com")]
    [InlineData("ann@example.com bob@example.com")]
    public void NotificationAddress_TryNormalize_MoreThanOneAddress_Fails(string text)
    {
        // Arrange and Act: validate the list
        var accepted = NotificationAddress.IsValid(text);

        // Assert: refused
        Assert.False(accepted);
    }

    /// <summary>
    ///     Test that a display name, a comment or a quoted local part is refused.
    /// </summary>
    [Theory]
    [InlineData("Ann <ann@example.com>")]
    [InlineData("ann@example.com (Ann)")]
    [InlineData("\"ann smith\"@example.com")]
    [InlineData("ann@[127.0.0.1]")]
    public void NotificationAddress_TryNormalize_NotAPlainAddress_Fails(string text)
    {
        // Arrange and Act: normalize
        var accepted = NotificationAddress.TryNormalize(text, out var address);

        // Assert: refused and nothing returned
        Assert.False(accepted);
        Assert.Equal(string.Empty, address);
    }

    /// <summary>
    ///     Test that a control character is refused, so no header line can be smuggled in.
    /// </summary>
    [Theory]
    [InlineData("ann@example.com\r\nBcc: eve@example.org")]
    [InlineData("ann\n@example.com")]
    [InlineData("ann@example.com\u0007")]
    [InlineData("ann@example.com\0")]
    public void NotificationAddress_TryNormalize_ControlCharacter_Fails(string text)
    {
        // Arrange and Act: validate and normalize
        var valid = NotificationAddress.IsValid(text);
        var normalized = NotificationAddress.TryNormalize(text, out var address);

        // Assert: refused by both and nothing returned
        Assert.False(valid);
        Assert.False(normalized);
        Assert.Equal(string.Empty, address);
    }

    /// <summary>
    ///     Test that an address with a character outside ASCII is refused.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_NonAscii_Fails()
    {
        // Arrange and Act: an umlaut in the local part
        var accepted = NotificationAddress.IsValid("j\u00fcrgen@example.com");

        // Assert: refused
        Assert.False(accepted);
    }

    /// <summary>
    ///     Test that a domain without a dot is refused.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_DomainWithoutDot_Fails()
    {
        // Arrange and Act: a bare host name
        var accepted = NotificationAddress.IsValid("ann@localhost");

        // Assert: refused
        Assert.False(accepted);
    }

    /// <summary>
    ///     Test that a malformed dot placement is refused.
    /// </summary>
    [Theory]
    [InlineData(".ann@example.com")]
    [InlineData("ann.@example.com")]
    [InlineData("a..nn@example.com")]
    [InlineData("ann@example..com")]
    [InlineData("ann@-example.com")]
    [InlineData("ann@example-.com")]
    [InlineData("ann@.com")]
    [InlineData("@example.com")]
    [InlineData("ann@")]
    public void NotificationAddress_TryNormalize_MalformedDots_Fails(string text)
    {
        // Arrange and Act: validate, and check that a trailing blank does not rescue it
        var accepted = NotificationAddress.IsValid(text);
        var paddedAccepted = NotificationAddress.IsValid($"  {text}  ");

        // Assert: refused both ways
        Assert.False(accepted);
        Assert.False(paddedAccepted);
    }

    /// <summary>
    ///     Test that empty and missing text is refused.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotificationAddress_TryNormalize_EmptyOrNull_Fails(string? text)
    {
        // Arrange and Act: validate
        var accepted = NotificationAddress.TryNormalize(text, out var address);

        // Assert: refused
        Assert.False(accepted);
        Assert.Equal(string.Empty, address);
    }

    /// <summary>
    ///     Test that an address of exactly the maximum length is accepted and one character more is refused.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_LengthBoundary_AcceptsMaximumAndRefusesMore()
    {
        // Arrange: an address of exactly 254 characters made of valid labels (the local part stays short)
        var label = new string('a', 63);
        var atMax = $"x@{label}.{label}.{label}.{new string('d', 254 - 2 - (3 * 64))}";

        // Act and Assert: the limit is inclusive
        Assert.Equal(NotificationAddress.MaxLength, atMax.Length);
        Assert.True(NotificationAddress.IsValid(atMax));
        Assert.False(NotificationAddress.IsValid(atMax + "d"));
    }

    /// <summary>
    ///     Test that a local part of more than 64 characters is refused.
    /// </summary>
    [Fact]
    public void NotificationAddress_TryNormalize_LongLocalPart_Fails()
    {
        // Arrange and Act: 65 characters before the at sign
        var accepted = NotificationAddress.IsValid($"{new string('a', 65)}@example.com");

        // Assert: refused; 64 is accepted
        Assert.False(accepted);
        Assert.True(NotificationAddress.IsValid($"{new string('a', 64)}@example.com"));
    }
}
