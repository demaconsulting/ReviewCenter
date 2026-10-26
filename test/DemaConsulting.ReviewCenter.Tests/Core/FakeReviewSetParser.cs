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
///     An <see cref="IReviewSetParser"/> that returns scripted review-sets (or throws a scripted rule violation),
///     so the Core and Web tests do not depend on YAML.
/// </summary>
internal sealed class FakeReviewSetParser : IReviewSetParser
{
    /// <summary>
    ///     Gets or sets the review-sets <see cref="Parse"/> returns.
    /// </summary>
    public List<ReviewSetDefinition> Sets { get; set; } = [];

    /// <summary>
    ///     Gets or sets the exception <see cref="Parse"/> throws, or <c>null</c> to succeed.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    ///     Gets the content passed to the last <see cref="Parse"/> call.
    /// </summary>
    public byte[]? LastContent { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<ReviewSetDefinition> Parse(byte[] content)
    {
        LastContent = content;
        return Failure is null ? Sets : throw Failure;
    }
}
