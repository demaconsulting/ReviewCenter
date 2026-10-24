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
///     The frozen copy of the checklist a review was created with.
/// </summary>
/// <remarks>
///     It is stored once, when the review is created, and never changed, so the checks a reviewer answers
///     stay the same even when the default checklist later changes. A review created before checklists
///     existed has none. The type is not thread-safe.
/// </remarks>
public sealed class ReviewChecklist
{
    /// <summary>
    ///     Gets the identity of the owning project.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    ///     Gets the identifier of the owning review.
    /// </summary>
    public string ReviewId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the version label of the checklist, copied from <see cref="ChecklistDefinition.Version"/>.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the checklist definition as JSON (see <see cref="ChecklistDefinition.ToJson"/>).
    /// </summary>
    public string Definition { get; init; } = string.Empty;

    /// <summary>
    ///     Reads the stored definition.
    /// </summary>
    /// <returns>The definition the review was created with.</returns>
    public ChecklistDefinition Parse()
    {
        return ChecklistDefinition.FromJson(Definition);
    }
}
