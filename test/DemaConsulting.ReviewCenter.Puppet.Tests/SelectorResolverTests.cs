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
    ///     Test that test ids (prefixed or plain) become attribute selectors and everything else passes through.
    /// </summary>
    [Fact]
    public void SelectorResolver_Resolve_AllForms_ReturnsPlaywrightSelector()
    {
        // Arrange: every supported form
        // Act: resolve them
        // Assert: test ids become data-testid selectors (with quotes escaped); the rest is unchanged
        Assert.Equal("[data-testid=\"hello-button\"]", SelectorResolver.Resolve("hello-button"));
        Assert.Equal("[data-testid=\"hello-button\"]", SelectorResolver.Resolve("testid:hello-button"));
        Assert.Equal("[data-testid=\"a\\\"b\"]", SelectorResolver.Resolve("testid:a\"b"));
        Assert.Equal("css=.button", SelectorResolver.Resolve("css=.button"));
        Assert.Equal("text=Count: 2", SelectorResolver.Resolve("text=Count: 2"));
        Assert.Equal("role=button[name=\"Save\"]", SelectorResolver.Resolve("role=button[name=\"Save\"]"));
        Assert.Equal("#main", SelectorResolver.Resolve("#main"));
    }

    /// <summary>
    ///     Test that a blank selector is rejected.
    /// </summary>
    [Fact]
    public void SelectorResolver_Resolve_Blank_ThrowsArgumentException()
    {
        // Arrange: blank selectors
        // Act and assert: both are rejected
        Assert.Throws<ArgumentException>(() => SelectorResolver.Resolve("  "));
        Assert.Throws<ArgumentException>(() => SelectorResolver.Resolve(string.Empty));
        Assert.Throws<ArgumentException>(() => SelectorResolver.Resolve("testid:  "));
    }
}
