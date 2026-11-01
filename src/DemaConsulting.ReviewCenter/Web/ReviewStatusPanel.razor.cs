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

using System.Globalization;
using DemaConsulting.ReviewCenter.Core;
using Microsoft.AspNetCore.Components;

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     Code-behind for the "Where are we?" panel (<c>ReviewStatusPanel.razor</c>): the words and the target address of
///     what the panel shows.
/// </summary>
/// <remarks>
///     The component only shows the status its parent gives it and holds no state, so it needs no synchronization. It
///     never decides what is visible to the viewer: the status has already left out what the viewer may not see.
/// </remarks>
public partial class ReviewStatusPanel : ComponentBase
{
    /// <summary>
    ///     Gets or sets the status to show; nothing is shown while it is <c>null</c>.
    /// </summary>
    [Parameter]
    public ReviewStatus? Status { get; set; }

    /// <summary>
    ///     Gets or sets the identity of the project, used to build the address of the checklist page.
    /// </summary>
    [Parameter]
    public int ProjectId { get; set; }

    /// <summary>
    ///     Gets or sets the identity of the review, used to build the address of the checklist page.
    /// </summary>
    [Parameter]
    public string ReviewId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the viewer may only read, so no Discuss button is offered.
    /// </summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>
    ///     Gets or sets the callback that runs with the check identifier when the viewer chooses Discuss.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnDiscuss { get; set; }

    /// <summary>
    ///     Gets or sets the content the page shows under a disputed check, for example the form that starts the
    ///     discussion; <c>null</c> for none.
    /// </summary>
    [Parameter]
    public RenderFragment<DisputedCheck>? DiscussForm { get; set; }

    /// <summary>
    ///     Gets or sets the navigation manager, used to build the part links of this page from its current address.
    /// </summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>
    ///     Gets the address the next-action button leads to: the checklist page, or a part of the review page.
    /// </summary>
    private string NextActionHref => Status!.NextAction.Target switch
    {
        NextActionTarget.Checklist => $"/projects/{ProjectId}/reviews/{ReviewId}/checklist",
        NextActionTarget.Reviewers => PageAnchor("reviewers-card"),
        NextActionTarget.DisputedChecks => PageAnchor("disputed-checks"),
        NextActionTarget.Threads => PageAnchor("thread-index"),
        NextActionTarget.Conclusion => PageAnchor("review-conclusion"),
        _ => PageAnchor("status-panel")
    };

    /// <summary>
    ///     Builds the link to a part of this page. The page has a base address of the site root, so a bare
    ///     <c>#part</c> would leave the review page; the link keeps the current address (without an old fragment).
    /// </summary>
    /// <param name="id">The identifier of the part.</param>
    /// <returns>The current address with the fragment of the part.</returns>
    private string PageAnchor(string id)
    {
        var current = Navigation.Uri;
        var hash = current.IndexOf('#', StringComparison.Ordinal);
        return (hash < 0 ? current : current[..hash]) + "#" + id;
    }

    /// <summary>
    ///     Words the ballot of a reviewer.
    /// </summary>
    /// <param name="reviewer">The reviewer.</param>
    /// <returns>Not started, Draft or Handed in.</returns>
    private static string BallotText(ReviewerStatus reviewer)
    {
        return reviewer.Ballot switch
        {
            BallotState.Submitted => "Handed in",
            BallotState.Draft => "Draft",
            _ => "Not started"
        };
    }

    /// <summary>
    ///     Gets the semantic class of the ballot of a reviewer.
    /// </summary>
    /// <param name="reviewer">The reviewer.</param>
    /// <returns>The class that colors the chip.</returns>
    private static string BallotClass(ReviewerStatus reviewer)
    {
        return reviewer.Ballot switch
        {
            BallotState.Submitted => "done",
            BallotState.Draft => "partial",
            _ => "waiting"
        };
    }

    /// <summary>
    ///     Words the signature of a reviewer.
    /// </summary>
    /// <param name="reviewer">The reviewer.</param>
    /// <returns>Signed, Not signed, or that the reviewer signed before the review changed.</returns>
    private static string SignatureText(ReviewerStatus reviewer)
    {
        return reviewer.Signature switch
        {
            SlotSignatureState.Signed => "Signed",
            SlotSignatureState.OutOfDate => "Signed, then the review changed",
            _ => "Not signed"
        };
    }

    /// <summary>
    ///     Gets the semantic class of the signature of a reviewer.
    /// </summary>
    /// <param name="reviewer">The reviewer.</param>
    /// <returns>The class that colors the chip.</returns>
    private static string SignatureClass(ReviewerStatus reviewer)
    {
        return reviewer.Signature switch
        {
            SlotSignatureState.Signed => "done",
            SlotSignatureState.OutOfDate => "changed",
            _ => "waiting"
        };
    }

    /// <summary>
    ///     Makes a check identifier usable in a test identifier.
    /// </summary>
    /// <param name="id">The check identifier.</param>
    /// <returns>The lower-case identifier.</returns>
    private static string TestId(string id)
    {
        return id.ToLowerInvariant();
    }

    /// <summary>
    ///     Formats a time for display.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The UTC time as "yyyy-MM-dd HH:mm UTC".</returns>
    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }
}
