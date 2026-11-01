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
///     What a person may do about the discussion threads of one review, so a page offers only what would be accepted.
/// </summary>
/// <remarks>
///     The facts come from <see cref="ThreadService.GetCapabilitiesAsync"/>; the rules behind them are the ones the
///     start methods enforce, so a page that follows the capabilities never offers an action that is then refused.
///     The record is an immutable value holder.
/// </remarks>
/// <param name="CanStartThread">
///     <c>true</c> when the person may start a thread about a file: they can contribute to the project and the review
///     is not concluded.
/// </param>
/// <param name="CanStartCheckThread">
///     <c>true</c> when the person may start a discussion about a check: the same as
///     <paramref name="CanStartThread"/> and <see cref="CheckThreadRules.CanStart"/> allows them.
/// </param>
public sealed record ThreadCapabilities(bool CanStartThread, bool CanStartCheckThread)
{
    /// <summary>
    ///     Gets the capabilities of a person who may start nothing.
    /// </summary>
    public static ThreadCapabilities None { get; } = new(false, false);
}
