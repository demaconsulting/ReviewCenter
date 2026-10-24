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
///     Unit tests for <see cref="ProjectService"/> using an in-memory store.
/// </summary>
public class ProjectServiceTests
{
    /// <summary>
    ///     The instant reported by the service's clock.
    /// </summary>
    private static readonly DateTimeOffset _now = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>
    ///     Creates a service over the given store with a fixed clock.
    /// </summary>
    /// <param name="store">The store to use.</param>
    /// <returns>The service.</returns>
    private static ProjectService CreateService(FakeReviewStore store) => new(store, new FixedTimeProvider(_now));

    /// <summary>
    ///     Test that a valid project is stored with trimmed text and the clock's creation time.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_ValidInput_StoresTrimmedProject()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project with surrounding whitespace
        var project = await service.CreateAsync("  Apollo  ", "  Moon landing  ", TestContext.Current.CancellationToken);

        // Assert: trimmed text, assigned id, and creation time from the clock
        Assert.True(project.Id > 0);
        Assert.Equal("Apollo", project.Name);
        Assert.Equal("Moon landing", project.Description);
        Assert.Equal(_now, project.Created);
    }

    /// <summary>
    ///     Test that a missing description is stored as empty text.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_NullDescription_StoresEmptyDescription()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project without a description
        var project = await service.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Assert: the description is empty rather than null
        Assert.Equal(string.Empty, project.Description);
    }

    /// <summary>
    ///     Test that an empty or blank name is rejected.
    /// </summary>
    /// <param name="name">The invalid name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProjectService_CreateAsync_BlankName_ThrowsProjectNameInvalid(string? name)
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project with the blank name
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(name, string.Empty, TestContext.Current.CancellationToken));

        // Assert: the rule and a readable message
        Assert.Equal(DomainRule.ProjectNameInvalid, exception.Rule);
        Assert.Contains("name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a name over the limit is rejected but a name exactly at the limit is accepted.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_NameLength_LimitIsInclusive()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create projects with a name at the limit and one over it
        var atLimit = await service.CreateAsync(new string('n', ReviewLimits.MaxProjectNameLength), null, TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(new string('n', ReviewLimits.MaxProjectNameLength + 1), null, TestContext.Current.CancellationToken));

        // Assert: the longer one is rejected
        Assert.Equal(ReviewLimits.MaxProjectNameLength, atLimit.Name.Length);
        Assert.Equal(DomainRule.ProjectNameInvalid, exception.Rule);
    }

    /// <summary>
    ///     Test that a description over the limit is rejected.
    /// </summary>
    [Fact]
    public async Task ProjectService_CreateAsync_DescriptionTooLong_ThrowsDescriptionTooLong()
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: create a project with an over-long description
        var exception = await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync("Apollo", new string('d', ReviewLimits.MaxProjectDescriptionLength + 1), TestContext.Current.CancellationToken));

        // Assert: the description rule is reported
        Assert.Equal(DomainRule.ProjectDescriptionTooLong, exception.Rule);
    }

    /// <summary>
    ///     Provides every kind of invalid project input: a blank name, an over-long name and an over-long description.
    /// </summary>
    public static TheoryData<string?, string?> InvalidInputs => new()
    {
        { null, "Description" },
        { "   ", null },
        { new string('n', ReviewLimits.MaxProjectNameLength + 1), null },
        { "Apollo", new string('d', ReviewLimits.MaxProjectDescriptionLength + 1) }
    };

    /// <summary>
    ///     Test that a rejected project is not stored, whichever rule it broke.
    /// </summary>
    /// <param name="name">The invalid name, or a valid one when the description is the invalid part.</param>
    /// <param name="description">The description.</param>
    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task ProjectService_CreateAsync_Rejected_StoresNothing(string? name, string? description)
    {
        // Arrange: a service over an empty store
        var service = CreateService(new FakeReviewStore());

        // Act: attempt an invalid create, then list
        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.CreateAsync(name, description, TestContext.Current.CancellationToken));
        var projects = await service.ListAsync(TestContext.Current.CancellationToken);

        // Assert: nothing was stored
        Assert.Empty(projects);
    }

    /// <summary>
    ///     Test that projects are listed ordered by name, and projects with the same name by identity.
    /// </summary>
    [Fact]
    public async Task ProjectService_ListAsync_SeveralProjects_ReturnsOrderedByNameThenId()
    {
        // Arrange: a service with projects created out of order, two of them sharing a name
        var service = CreateService(new FakeReviewStore());
        var zeta = await service.CreateAsync("Zeta", null, TestContext.Current.CancellationToken);
        var firstAlpha = await service.CreateAsync("Alpha", null, TestContext.Current.CancellationToken);
        var secondAlpha = await service.CreateAsync("Alpha", null, TestContext.Current.CancellationToken);

        // Act: list the projects
        var projects = await service.ListAsync(TestContext.Current.CancellationToken);

        // Assert: by name, and by identity within the same name
        Assert.Equal([firstAlpha.Id, secondAlpha.Id, zeta.Id], projects.Select(project => project.Id));
    }

    /// <summary>
    ///     Test that a created project can be fetched by id, and an unknown id returns nothing.
    /// </summary>
    [Fact]
    public async Task ProjectService_GetAsync_ExistingAndUnknown_ReturnsProjectOrNull()
    {
        // Arrange: a service with one project
        var service = CreateService(new FakeReviewStore());
        var created = await service.CreateAsync("Apollo", null, TestContext.Current.CancellationToken);

        // Act: fetch the project and an unknown id
        var found = await service.GetAsync(created.Id, TestContext.Current.CancellationToken);
        var missing = await service.GetAsync(created.Id + 100, TestContext.Current.CancellationToken);

        // Assert: the known one is returned, the unknown is null
        Assert.Equal("Apollo", found?.Name);
        Assert.Null(missing);
    }
}
