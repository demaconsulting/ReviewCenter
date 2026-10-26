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

namespace DemaConsulting.ReviewCenter.Core;

/// <summary>
///     Reads the review-sets out of the bytes of a repository's <c>.reviewmark.yaml</c>.
/// </summary>
/// <remarks>
///     The contract keeps every YAML type out of Core: the content sources subsystem implements it with a YAML
///     library, and tests give the planner a fake. Implementations must be thread-safe.
/// </remarks>
public interface IReviewSetParser
{
    /// <summary>
    ///     Parses the content of a <c>.reviewmark.yaml</c> file.
    /// </summary>
    /// <param name="content">The raw bytes of the file. Must not be null.</param>
    /// <returns>The review-sets in file order; never empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is null.</exception>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ReviewSetConfigInvalid"/> when the file is too large, is not valid YAML,
    ///     defines no review-sets, or a review-set is incomplete, repeated or too large. The message is written for
    ///     the operator and never contains a stack trace.
    /// </exception>
    IReadOnlyList<ReviewSetDefinition> Parse(byte[] content);
}
