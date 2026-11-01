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
///     What a signature of a review attests: that the review is approved or that it is rejected.
/// </summary>
/// <remarks>
///     The numeric values are persisted with signatures and must never be renumbered. A review that has no
///     outcome yet has no value of this type (<c>null</c>); see <see cref="ReviewConclusion"/> for the derived
///     display state that also has a value for that case. Every <see cref="ReviewSignature"/> records the outcome it
///     attests (<see cref="ReviewSignature.Outcome"/>).
/// </remarks>
public enum ReviewOutcome
{
    /// <summary>Every agreed check passes or does not apply.</summary>
    Approved = 1,

    /// <summary>At least one agreed check fails.</summary>
    Rejected = 2
}
