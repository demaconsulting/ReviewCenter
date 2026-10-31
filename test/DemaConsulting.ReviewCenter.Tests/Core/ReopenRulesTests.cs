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

using DemaConsulting.ReviewCenter.Core;

namespace DemaConsulting.ReviewCenter.Tests.Core;

/// <summary>
///     Unit tests for <see cref="ReopenRules"/>, the pure rule behind the stranded-slot guard of a reopen.
/// </summary>
public class ReopenRulesTests
{
    /// <summary>
    ///     Builds a slot.
    /// </summary>
    /// <param name="id">The slot identity.</param>
    /// <param name="primary">The primary user.</param>
    /// <param name="alternate">The alternate user, or <c>null</c>.</param>
    /// <returns>The slot.</returns>
    private static ReviewerSlot Slot(int id, int primary, int? alternate = null)
    {
        return new ReviewerSlot { Id = id, PrimaryUserId = primary, AlternateUserId = alternate };
    }

    /// <summary>
    ///     Test that a slot is stranded only when everyone who may sign it is blocked: a blocked primary without an
    ///     alternate, or with a blocked alternate, strands it; a free alternate or a free primary does not.
    /// </summary>
    [Fact]
    public void ReopenRules_StrandedSlots_PrimaryAndAlternate_StrandsOnlyWhenNobodyIsFree()
    {
        // Arrange: slot 1 has a blocked primary only, slot 2 a free alternate, slot 3 two blocked users,
        // slot 4 a free primary and a blocked alternate
        var slots = new[] { Slot(1, 10), Slot(2, 10, 20), Slot(3, 10, 11), Slot(4, 20, 10) };
        var blocked = new HashSet<int> { 10, 11 };

        // Act
        var stranded = ReopenRules.StrandedSlots(slots, blocked);

        // Assert
        Assert.Equal([1, 3], stranded);
    }

    /// <summary>
    ///     Test that the numbers follow the identity order of the slots, whatever order they are given in, and that
    ///     nobody blocked or no slots give an empty result.
    /// </summary>
    [Fact]
    public void ReopenRules_StrandedSlots_UnorderedSlotsAndNoBlockedUsers_NumbersByIdentity()
    {
        // Arrange: slots given out of order; the one with the highest identity is the third
        var slots = new[] { Slot(30, 10), Slot(10, 20), Slot(20, 20) };

        // Act, Assert
        Assert.Equal([3], ReopenRules.StrandedSlots(slots, new HashSet<int> { 10 }));
        Assert.Empty(ReopenRules.StrandedSlots(slots, new HashSet<int>()));
        Assert.Empty(ReopenRules.StrandedSlots([], new HashSet<int> { 10 }));
    }

    /// <summary>
    ///     Test that a missing argument is refused.
    /// </summary>
    [Fact]
    public void ReopenRules_StrandedSlots_NullArguments_Throw()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => ReopenRules.StrandedSlots(null!, new HashSet<int>()));
        Assert.Throws<ArgumentNullException>(() => ReopenRules.StrandedSlots([], null!));
    }

    /// <summary>
    ///     Test the wording for one, two and three stranded slots.
    /// </summary>
    /// <param name="numbers">The slot numbers.</param>
    /// <param name="subject">The expected start of the message.</param>
    [Theory]
    [InlineData(new[] { 1 }, "Slot 1 would have")]
    [InlineData(new[] { 1, 3 }, "Slots 1 and 3 would have")]
    [InlineData(new[] { 1, 2, 3 }, "Slots 1, 2 and 3 would have")]
    public void ReopenRules_StrandedMessage_OneOrMoreSlots_NamesThem(int[] numbers, string subject)
    {
        // Arrange, Act
        var message = ReopenRules.StrandedMessage(numbers);

        // Assert
        Assert.Equal(subject + " no one who may sign; ask another administrator or switch the rule off.", message);
    }

    /// <summary>
    ///     Test that a missing or empty list of slot numbers is refused.
    /// </summary>
    [Fact]
    public void ReopenRules_StrandedMessage_NullOrEmpty_Throws()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => ReopenRules.StrandedMessage(null!));
        Assert.Throws<ArgumentException>(() => ReopenRules.StrandedMessage([]));
    }
}
