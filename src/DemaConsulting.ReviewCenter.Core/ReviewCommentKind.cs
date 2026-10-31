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
///     What a comment of a review thread is for.
/// </summary>
/// <remarks>
///     The distinction exists so that the note written when a thread is resolved can be told apart from the
///     discussion: it is shown and digested as a resolution, and it follows the kind of its thread. The enumeration
///     is stored by name, so kinds added later do not change the meaning of stored values.
/// </remarks>
public enum ReviewCommentKind
{
    /// <summary>
    ///     An ordinary comment: the starting comment of a thread or a reply; the kind of every comment until a
    ///     thread is resolved with a note.
    /// </summary>
    Comment = 0,

    /// <summary>
    ///     A note written when a thread was resolved. Whether it is part of the signed report follows the thread:
    ///     it is included exactly when the thread is a <see cref="ThreadKind.Finding"/>.
    /// </summary>
    Resolution = 1
}
