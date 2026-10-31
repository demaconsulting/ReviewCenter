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
///     The stored status of a reviewer's ballot, the set of answers one reviewer slot gives to the checklist.
/// </summary>
/// <remarks>
///     Only the two states that can be stored exist here: a ballot that was never started has no row and is
///     shown as <see cref="BallotState.NotStarted"/>. The numeric values are persisted and must never be
///     renumbered.
/// </remarks>
public enum BallotStatus
{
    /// <summary>The reviewer is still answering; the ballot takes no part in the consolidation.</summary>
    Draft = 0,

    /// <summary>The reviewer has handed in the ballot; its answers count in the consolidation.</summary>
    Submitted = 1
}
