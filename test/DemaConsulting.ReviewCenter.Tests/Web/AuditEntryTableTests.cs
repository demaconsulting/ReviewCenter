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

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     Unit tests for the <see cref="AuditEntryTable"/> component, rendered in memory with bUnit: the paging links
///     carry the filter, and an empty page tells a filter that matches nothing from an empty trail.
/// </summary>
public class AuditEntryTableTests : BunitContext
{
    /// <summary>
    ///     Creates an entry for a page.
    /// </summary>
    /// <param name="sequence">The sequence number.</param>
    /// <returns>The entry.</returns>
    private static AuditEntry Entry(long sequence)
    {
        return new AuditEntry
        {
            Sequence = sequence,
            TimestampUtc = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero),
            ActorName = "Ann",
            Action = "review.created",
            Detail = "detail " + sequence
        };
    }

    /// <summary>
    ///     Test that the Older and Newer links carry the filter and the page size, and stay plain without a filter.
    /// </summary>
    [Fact]
    public void AuditEntryTable_Render_WithFilter_LinksCarryTheFilter()
    {
        // Arrange
        var page = new AuditEntryPage([Entry(9), Entry(8)], true, true);
        var filter = AuditFilterState.FromQuery("an&n", "review.", null, "2025-01-02", null, "25", null);

        // Act
        var filtered = Render<AuditEntryTable>(p => p.Add(c => c.Page, page).Add(c => c.BasePath, "/audit").Add(c => c.Filter, filter));
        var plain = Render<AuditEntryTable>(p => p.Add(c => c.Page, page).Add(c => c.BasePath, "/audit"));

        // Assert
        Assert.Equal(
            "/audit?actor=an%26n&action=review.&from=2025-01-02&size=25&before=8",
            filtered.Find("[data-testid='audit-older']").GetAttribute("href"));
        Assert.Equal(
            "/audit?actor=an%26n&action=review.&from=2025-01-02&size=25&after=9",
            filtered.Find("[data-testid='audit-newer']").GetAttribute("href"));
        Assert.Equal("/audit?before=8", plain.Find("[data-testid='audit-older']").GetAttribute("href"));
        Assert.Equal("/audit?after=9", plain.Find("[data-testid='audit-newer']").GetAttribute("href"));
    }

    /// <summary>
    ///     Test that an empty page says that nothing matches when a filter narrows the entries, and that nothing was
    ///     recorded when none does.
    /// </summary>
    [Fact]
    public void AuditEntryTable_Render_EmptyPage_TellsNoMatchFromNothingRecorded()
    {
        // Arrange
        var page = new AuditEntryPage([], false, false);
        var narrowing = AuditFilterState.FromQuery("zed", null, null, null, null, null, null);
        var sizeOnly = AuditFilterState.FromQuery(null, null, null, null, null, "25", null);

        // Act
        var none = Render<AuditEntryTable>(p => p.Add(c => c.Page, page).Add(c => c.BasePath, "/audit").Add(c => c.Filter, narrowing));
        var empty = Render<AuditEntryTable>(p => p.Add(c => c.Page, page).Add(c => c.BasePath, "/audit").Add(c => c.Filter, sizeOnly));

        // Assert
        Assert.Equal("No entry matches the filters.", none.Find("[data-testid='audit-empty']").TextContent);
        Assert.Equal("Nothing has been recorded yet.", empty.Find("[data-testid='audit-empty']").TextContent);
    }
}
