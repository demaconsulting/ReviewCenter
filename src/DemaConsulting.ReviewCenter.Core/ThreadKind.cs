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
///     Whether a thread is chat or part of the signed report of a review.
/// </summary>
/// <remarks>
///     The kind is not stored: it is derived from the starting comment of the thread (see
///     <see cref="ReviewThread.Kind"/>), so it can never disagree with the comment that decides it. Only the
///     comments the report includes are covered by the record digest, so chat never makes a signature out of date.
/// </remarks>
public enum ThreadKind
{
    /// <summary>
    ///     Chat: the starting comment is not included in the report. Discussion only; a reply of a question may
    ///     still be included on its own.
    /// </summary>
    Question = 0,

    /// <summary>
    ///     A finding: the starting comment is included in the report, so the signed record binds to it.
    /// </summary>
    Finding = 1
}
