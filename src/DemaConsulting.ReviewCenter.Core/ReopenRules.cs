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
///     The pure rule that keeps a reopen from leaving a reviewer slot in which nobody may sign again.
/// </summary>
/// <remarks>
///     While a project does not let the person who reopened a review sign it again, a reopen blocks the person
///     reopening, and every earlier reopener stays blocked. A slot whose primary and alternate (if any) are all blocked
///     could never get a fresh signature, so the review could never be concluded again. Every slot counts, because a
///     reopen needs a concluded review, so every slot has a signature and no thread is open, and a slot with a
///     signature can not be changed.
///     The rules work on values only, so they are tested without any store. The class has no state and is safe to call
///     from several threads.
/// </remarks>
internal static class ReopenRules
{
    /// <summary>
    ///     Finds the slots in which nobody could sign again.
    /// </summary>
    /// <param name="slots">The slots of the review, in any order.</param>
    /// <param name="blockedUserIds">The identities of the users who may not sign (the people who reopened).</param>
    /// <returns>
    ///     The numbers of the stranded slots in ascending order. A number is the 1-based position of the slot when the
    ///     slots are ordered by identity, which is the number the review page shows. A slot is stranded when its primary
    ///     is blocked and it has no alternate or the alternate is blocked too.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static IReadOnlyList<int> StrandedSlots(
        IReadOnlyList<ReviewerSlot> slots,
        IReadOnlySet<int> blockedUserIds)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(blockedUserIds);
        var stranded = new List<int>();
        var number = 0;
        foreach (var slot in slots.OrderBy(slot => slot.Id))
        {
            number++;
            if (blockedUserIds.Contains(slot.PrimaryUserId)
                && (slot.AlternateUserId is not { } alternate || blockedUserIds.Contains(alternate)))
            {
                stranded.Add(number);
            }
        }

        return stranded;
    }

    /// <summary>
    ///     Builds the message that refuses a reopen because slots would be stranded.
    /// </summary>
    /// <param name="slotNumbers">The numbers of the stranded slots; must not be null or empty.</param>
    /// <returns>The message, for example <c>Slots 1 and 3 would have no one who may sign; ...</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="slotNumbers"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="slotNumbers"/> is empty.</exception>
    public static string StrandedMessage(IReadOnlyList<int> slotNumbers)
    {
        ArgumentNullException.ThrowIfNull(slotNumbers);
        if (slotNumbers.Count == 0)
        {
            throw new ArgumentException("At least one slot number is required.", nameof(slotNumbers));
        }

        var subject = slotNumbers.Count == 1
            ? $"Slot {slotNumbers[0]} would have"
            : $"Slots {string.Join(", ", slotNumbers.Take(slotNumbers.Count - 1))} and {slotNumbers[^1]} would have";
        return $"{subject} no one who may sign; ask another administrator or switch the rule off.";
    }
}
