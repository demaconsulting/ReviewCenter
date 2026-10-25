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
///     A consistent snapshot of everything a signature covers: the content fingerprint, the checklist with its
///     answers and section applicability, and all threads with their comments.
/// </summary>
/// <remarks>
///     The snapshot is loaded by one store call so the digest and the signing blockers are computed from the
///     same data. Reviewer slots and signatures are deliberately not part of it: signing must not change what
///     was signed. The record is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="ContentFingerprint">The content fingerprint of the review's file set.</param>
/// <param name="Checklist">The frozen checklist of the review, or <c>null</c> when it has none.</param>
/// <param name="SectionStates">The stored applicability of the checklist sections.</param>
/// <param name="Answers">The stored checklist answers.</param>
/// <param name="Threads">All threads of the review with their comments.</param>
public sealed record ReviewRecord(
    string ContentFingerprint,
    ReviewChecklist? Checklist,
    IReadOnlyList<ChecklistSectionState> SectionStates,
    IReadOnlyList<ChecklistAnswer> Answers,
    IReadOnlyList<ReviewThread> Threads);
