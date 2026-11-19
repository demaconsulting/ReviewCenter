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

using BenchmarkDotNet.Attributes;
using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Benchmarks;

/// <summary>
///     Measures how the time to decide a path against a pattern of many double-star segments grows with the number of
///     those segments: the design of <see cref="SelectionMatcher"/> memoizes each pair of positions, so the time grows
///     with the product of the segment counts and not exponentially.
/// </summary>
public class SelectionMatcherBenchmarks
{
    /// <summary>
    ///     The patterns: the given number of <c>**</c> segments and a last segment that the path does not have.
    /// </summary>
    private IReadOnlyList<SelectionPattern> _patterns = [];

    /// <summary>
    ///     A path of 60 segments that the patterns do not match.
    /// </summary>
    private readonly string _path = string.Join("/", Enumerable.Repeat("d", 60)) + "/x.cs";

    /// <summary>
    ///     Gets or sets the number of <c>**</c> segments of the pattern.
    /// </summary>
    [Params(10, 20, 40)]
    public int DoubleStars { get; set; }

    /// <summary>
    ///     Builds the pattern of the current size.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _patterns = SelectionMatcher.Parse(string.Join("/", Enumerable.Repeat("**", DoubleStars)) + "/never.cs");
    }

    /// <summary>
    ///     Decides the path against the pattern.
    /// </summary>
    /// <returns>Whether the path matches (it never does).</returns>
    [Benchmark]
    public bool IsMatch()
    {
        return SelectionMatcher.IsMatch(_path, _patterns);
    }
}
