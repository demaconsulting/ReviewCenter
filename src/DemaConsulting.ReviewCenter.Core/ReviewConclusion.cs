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
///     The derived state of a review that is shown to people: what the reviewers have concluded, if anything.
/// </summary>
/// <remarks>
///     It is computed from the consolidated ballots and the signatures and never stored. The values
///     <see cref="Approved"/> and <see cref="Rejected"/> equal those of <see cref="ReviewOutcome"/>.
/// </remarks>
public enum ReviewConclusion
{
    /// <summary>There is no conclusion yet: checks are pending or disputed, or nobody has handed in a ballot.</summary>
    None = 0,

    /// <summary>The reviewers conclude that the review is approved.</summary>
    Approved = 1,

    /// <summary>The reviewers conclude that the review is rejected.</summary>
    Rejected = 2
}
