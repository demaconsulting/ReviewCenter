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
///     Which version of a file the line numbers of a review thread refer to.
/// </summary>
/// <remarks>
///     A repository review freezes both the head and the baseline of every changed file, so a reviewer can
///     comment on a line that was removed (it exists only in the baseline) as well as on a line of the head.
///     The side is part of the thread anchor and of the record digest. The enumeration is stored by name, so
///     sides added later do not change the meaning of stored values.
/// </remarks>
public enum ThreadSide
{
    /// <summary>
    ///     The line numbers refer to the head content; the side of every whole-file thread and of every thread
    ///     of an upload review.
    /// </summary>
    Head,

    /// <summary>
    ///     The line numbers refer to the frozen baseline content (the file as it was before the change).
    /// </summary>
    Baseline
}
