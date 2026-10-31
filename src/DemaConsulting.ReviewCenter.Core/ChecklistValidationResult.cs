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
///     One problem found in a checklist definition: where it is and what is wrong.
/// </summary>
/// <param name="Path">
///     A human-readable path to the offending value, for example <c>sections[2].items[1].id</c>; <c>json</c> for the
///     text as a whole. Indexes are zero-based, as in the JSON text.
/// </param>
/// <param name="Message">What is wrong, for example <c>duplicate "code-03"</c>.</param>
public sealed record ChecklistValidationError(string Path, string Message)
{
    /// <summary>
    ///     Writes the error as one line.
    /// </summary>
    /// <returns>The path, a colon and the message.</returns>
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
///     The outcome of checking a checklist definition: every error found, and the definition when there is none.
/// </summary>
/// <remarks>
///     The result is immutable and safe to share between threads.
/// </remarks>
/// <param name="Errors">All errors found, in the order of the text; empty when the definition is valid.</param>
/// <param name="Definition">The parsed definition; <c>null</c> when <paramref name="Errors"/> is not empty.</param>
public sealed record ChecklistValidationResult(
    IReadOnlyList<ChecklistValidationError> Errors,
    ChecklistDefinition? Definition)
{
    /// <summary>
    ///     The most errors that <see cref="ThrowIfInvalid"/> puts in its message, so a message stays readable (an
    ///     audit entry or an API problem document holds it).
    /// </summary>
    public const int MaxErrorsInMessage = 10;

    /// <summary>
    ///     Gets a value indicating whether the definition has no error.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    ///     Returns the valid definition, or refuses an invalid one.
    /// </summary>
    /// <remarks>
    ///     The refusal is the one place where invalid text is turned into a domain rule, so every caller that
    ///     must not go on with an invalid definition (a replace, a snapshot at review creation) fails the same way.
    /// </remarks>
    /// <returns>The definition.</returns>
    /// <exception cref="DomainRuleException">
    ///     Thrown with <see cref="DomainRule.ChecklistDefinitionInvalid"/> when there is any error; the message names
    ///     the first <see cref="MaxErrorsInMessage"/> errors with their paths and says how many more there are.
    /// </exception>
    public ChecklistDefinition ThrowIfInvalid()
    {
        if (Definition is null || Errors.Count > 0)
        {
            var shown = string.Join("; ", Errors.Take(MaxErrorsInMessage));
            var more = Errors.Count > MaxErrorsInMessage ? $"; and {Errors.Count - MaxErrorsInMessage} more" : string.Empty;
            throw new DomainRuleException(
                DomainRule.ChecklistDefinitionInvalid,
                $"The checklist definition is not valid ({Errors.Count} error{(Errors.Count == 1 ? string.Empty : "s")}): {shown}{more}");
        }

        return Definition;
    }
}
