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
///     Unit tests for <see cref="RevisionValidator"/>: accepted names and the inputs that could be taken for a
///     git option or revision expression.
/// </summary>
public class RevisionValidatorTests
{
    /// <summary>
    ///     Test that ordinary branch, tag and commit names are accepted and returned unchanged.
    /// </summary>
    /// <param name="revision">The valid revision.</param>
    [Theory]
    [InlineData("main")]
    [InlineData("release/1.2")]
    [InlineData("v1.0.0")]
    [InlineData("feature/x_y+z")]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("a")]
    [InlineData("user@example")]
    [InlineData("issue#12")]
    public void RevisionValidator_Validate_ValidRevision_ReturnsIt(string revision)
    {
        // Arrange: nothing beyond the revision

        // Act: validate it
        var result = RevisionValidator.Validate(revision);

        // Assert: returned unchanged, and reported valid
        Assert.Equal(revision, result);
        Assert.True(RevisionValidator.IsValid(revision));
    }

    /// <summary>
    ///     Test that option-like and expression-like revisions are refused with the revision rule.
    /// </summary>
    /// <param name="revision">The invalid revision.</param>
    [Theory]
    [InlineData("--upload-pack=touch x")]
    [InlineData("--upload-pack=calc")]
    [InlineData("-x")]
    [InlineData("--help")]
    [InlineData("a b")]
    [InlineData("a..b")]
    [InlineData("a...b")]
    [InlineData("x@{1}")]
    [InlineData("@")]
    [InlineData("@{u}")]
    [InlineData("HEAD~1")]
    [InlineData("HEAD^")]
    [InlineData("v^")]
    [InlineData(":/x")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData("a//b")]
    [InlineData("/main")]
    [InlineData("main/")]
    [InlineData("main.")]
    [InlineData("main.lock")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a[b")]
    [InlineData("a\"b")]
    [InlineData("a'b")]
    [InlineData("a;b")]
    [InlineData("a$b")]
    [InlineData("\u00e9")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RevisionValidator_Validate_InvalidRevision_ThrowsRevisionInvalid(string? revision)
    {
        // Arrange: nothing beyond the revision

        // Act: validate it
        var exception = Assert.Throws<DomainRuleException>(() => RevisionValidator.Validate(revision));

        // Assert: the rule is named and IsValid agrees
        Assert.Equal(DomainRule.RevisionInvalid, exception.Rule);
        Assert.False(RevisionValidator.IsValid(revision));
    }

    /// <summary>
    ///     Test that the length limit is inclusive.
    /// </summary>
    [Fact]
    public void RevisionValidator_Validate_LengthLimit_IsInclusive()
    {
        // Arrange: names at and beyond the limit
        var atLimit = new string('a', RevisionValidator.MaxLength);
        var beyond = new string('a', RevisionValidator.MaxLength + 1);

        // Act: validate them
        var accepted = RevisionValidator.Validate(atLimit);
        var exception = Assert.Throws<DomainRuleException>(() => RevisionValidator.Validate(beyond));

        // Assert: the longest allowed name passes and the next one fails
        Assert.Equal(atLimit, accepted);
        Assert.Equal(DomainRule.RevisionInvalid, exception.Rule);
    }
}
