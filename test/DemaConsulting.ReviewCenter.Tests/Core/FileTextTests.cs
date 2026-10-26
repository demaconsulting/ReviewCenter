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

using System.Text;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="FileText"/>: the shared definition of text and lines.
/// </summary>
public class FileTextTests
{
    /// <summary>
    ///     Test that text bytes are decoded and split with the shared line rules.
    /// </summary>
    /// <param name="text">The file text.</param>
    /// <param name="expected">The lines, joined with a vertical bar for the assertion.</param>
    [Theory]
    [InlineData("a\nb\n", "a|b")]
    [InlineData("a\r\nb\r\n", "a|b")]
    [InlineData("a\nb", "a|b")]
    [InlineData("a\n\nb\n", "a||b")]
    [InlineData("\n", "")]
    [InlineData("a\rb", "a\rb")]
    [InlineData("", null)]
    public void FileText_TryReadLines_Text_SplitsLikeSplitLines(string text, string? expected)
    {
        // Arrange: the bytes of the text
        var bytes = Encoding.UTF8.GetBytes(text);

        // Act: read the lines
        var lines = FileText.TryReadLines(bytes);

        // Assert: the same lines as SplitLines gives; empty text has none
        Assert.NotNull(lines);
        Assert.Equal(FileText.SplitLines(text), lines);
        Assert.Equal(expected is null ? 0 : expected.Split('|').Length, lines.Count);
        if (expected is not null)
        {
            Assert.Equal(expected.Split('|'), lines);
        }
    }

    /// <summary>
    ///     Test that binary content (a NUL byte or invalid UTF-8) gives no lines.
    /// </summary>
    [Fact]
    public void FileText_TryReadLines_Binary_ReturnsNull()
    {
        // Arrange, Act, Assert: a NUL byte, and a byte sequence that is not UTF-8
        Assert.Null(FileText.TryReadLines([0x61, 0x00, 0x62]));
        Assert.Null(FileText.TryReadLines([0xC3, 0x28]));
    }

    /// <summary>
    ///     Test that a null argument is rejected.
    /// </summary>
    [Fact]
    public void FileText_TryReadLines_Null_ThrowsArgumentNullException()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => FileText.TryReadLines(null!));
    }
}
