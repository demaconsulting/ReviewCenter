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

using Bunit;
using DemaConsulting.ReviewCenter.Core;
using DemaConsulting.ReviewCenter.Web;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the "Assign me as reviewer" shortcut of the <see cref="ChecklistPage"/>: it always adds a new
///     reviewer slot with the viewer as primary reviewer and never takes over an existing one. Rendered in memory
///     with bUnit.
/// </summary>
public class ChecklistPageAssignMeTests : BallotPageTestBase
{
    /// <summary>
    ///     Test that a member without a slot gets one of their own, as primary reviewer without an alternate, and that
    ///     the existing slots stay as they were.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_AssignMe_AddsNewSlotAndKeepsExistingOnes()
    {
        // Arrange: bob holds a slot with an alternate; dee is a member without a slot
        var projectId = await CreateReviewAsync();
        await SeatAsync(projectId, ReviewKey, "bob", "cyd");
        await AddMemberAsync(projectId, "dee");
        SignInAs("dee", "Dee", false);
        var cut = RenderPage(projectId);

        // Act
        await cut.Find("[data-testid='assign-me']").ClickAsync();

        // Assert
        var slots = await Store.ListSlotsAsync(projectId, ReviewKey, TestContext.Current.CancellationToken);
        Assert.Equal(2, slots.Count);
        var dee = await UserStore.GetByUsernameAsync("dee", TestContext.Current.CancellationToken);
        var added = Assert.Single(slots, slot => slot.PrimaryUserId == dee!.Id);
        Assert.Null(added.AlternateUserId);
        var bob = await UserStore.GetByUsernameAsync("bob", TestContext.Current.CancellationToken);
        var cyd = await UserStore.GetByUsernameAsync("cyd", TestContext.Current.CancellationToken);
        var existing = Assert.Single(slots, slot => slot.PrimaryUserId == bob!.Id);
        Assert.Equal(cyd!.Id, existing.AlternateUserId);
        Assert.Empty(cut.FindAll("[data-testid='assign-me']"));
        Assert.Empty(cut.FindAll("[data-testid='assign-error']"));
        Assert.Empty(cut.FindAll("[data-testid='no-slot-notice']"));
    }

    /// <summary>
    ///     Test that a project administrator who is not the author of the review is offered the shortcut too.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_ProjectAdministratorWithoutSlot_OffersAssignMe()
    {
        // Arrange
        var projectId = await CreateReviewAsync("bob");
        await AddMemberAsync(projectId, "pam");
        await ChangeMemberRoleAsync(projectId, "pam", ProjectRole.ProjectAdmin);
        SignInAs("pam", "Pam", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.NotNull(cut.Find("[data-testid='assign-me']"));
    }

    /// <summary>
    ///     Test that a viewer who already holds a slot is not offered the shortcut.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_ViewerWithSlot_OffersNoAssignMe()
    {
        // Arrange
        var projectId = await CreateReviewAsync("ann", "bob");
        SignInAs("bob", "Bob", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='assign-me']"));
        Assert.Empty(cut.FindAll("[data-testid='no-slot-notice']"));
    }

    /// <summary>
    ///     Test that a person who is not a member of the project is offered no shortcut.
    /// </summary>
    [Fact]
    public async Task ChecklistPage_NonMember_OffersNoAssignMe()
    {
        // Arrange: the user exists but is not a member of the project
        var projectId = await CreateReviewAsync("bob");
        await Users.CreateAsync("eve", "Eve", false, "correct horse battery", TestContext.Current.CancellationToken);
        SignInAs("eve", "Eve", false);

        // Act
        var cut = RenderPage(projectId);

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='assign-me']"));
    }
}
