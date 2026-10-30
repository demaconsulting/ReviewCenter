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
///     The checklist a project gives to its new reviews: the definition, where it comes from, and the hash a change
///     must be based on.
/// </summary>
/// <remarks>
///     The record is immutable and safe to share between threads.
/// </remarks>
/// <param name="Definition">The definition: the project's own, or the built-in default.</param>
/// <param name="IsDefault"><c>true</c> when the project has no definition of its own and the built-in default applies.</param>
/// <param name="Json">
///     The JSON text of the definition (the stored text, or the text of the built-in default); the text an export
///     offers.
/// </param>
/// <param name="Hash">
///     The hash of <paramref name="Json"/> (see <see cref="ProjectChecklistService.HashOf"/>); a change is based on it
///     and is refused when the stored definition no longer has it.
/// </param>
public sealed record ProjectChecklist(ChecklistDefinition Definition, bool IsDefault, string Json, string Hash)
{
    /// <summary>
    ///     Gets the version label of the definition.
    /// </summary>
    public string Version => Definition.Version;

    /// <summary>
    ///     Gets the number of sections of the definition.
    /// </summary>
    public int SectionCount => Definition.Sections.Count;

    /// <summary>
    ///     Gets the number of checks of the definition, over all sections.
    /// </summary>
    public int ItemCount => Definition.Sections.Sum(section => section.Items.Count);
}
