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

namespace DemaConsulting.ReviewCenter.Puppet.Tests;

/// <summary>
///     Unit tests for the <see cref="SelectorResolver"/>.
/// </summary>
public class SelectorResolverTests
{
    /// <summary>
    ///     Test that a plain name is treated as a data-testid.
    /// </summary>
    [Fact]
    public void SelectorResolver_Resolve_PlainName_UsesDataTestId()
    {
        // Arrange: a plain name
        const string selector = "hello-button";

        // Act: resolve it
        var resolved = SelectorResolver.Resolve(selector);

        // Assert: it becomes a data-testid attribute selector
        Assert.Equal("[data-testid=\"hello-button\"]", resolved);
    }

    /// <summary>
    ///     Test that selectors with special characters are passed through unchanged.
    /// </summary>
    /// <param name="selector">A Playwright selector.</param>
    [Theory]
    [InlineData("#main")]
    [InlineData(".button")]
    [InlineData("text=Hello")]
    [InlineData("button >> nth=0")]
    [InlineData("[data-testid='x']")]
    public void SelectorResolver_Resolve_SpecialCharacters_PassesThroughUnchanged(string selector)
    {
        // Arrange: a selector that is not a plain name
        // Act: resolve it
        var resolved = SelectorResolver.Resolve(selector);

        // Assert: it is unchanged
        Assert.Equal(selector, resolved);
    }

    /// <summary>
    ///     Test that a blank selector is rejected.
    /// </summary>
    /// <param name="selector">A blank selector.</param>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SelectorResolver_Resolve_Blank_ThrowsArgumentException(string selector)
    {
        // Arrange: a blank selector (the test parameter)
        // Act: resolve it
        var exception = Record.Exception(() => SelectorResolver.Resolve(selector));

        // Assert: blank selectors fail
        Assert.IsType<ArgumentException>(exception);
    }
}
