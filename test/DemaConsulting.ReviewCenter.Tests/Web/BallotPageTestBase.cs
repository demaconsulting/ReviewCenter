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
using DemaConsulting.ReviewCenter.Tests.Core;
using DemaConsulting.ReviewCenter.Web;

using TestContext = Xunit.TestContext;

namespace DemaConsulting.ReviewCenter.Tests.Web;

/// <summary>
///     The arrangement shared by the tests of the ballot experience of the <see cref="ChecklistPage"/>: a review with
///     several reviewers whose answers are hidden from a viewer who has not handed in a ballot.
/// </summary>
/// <remarks>
///     The user <c>ann</c> is the ambient system administrator of the page tests; the other users are plain members.
///     To see the page as a member, call <c>SignInAs(name, display, false)</c>.
/// </remarks>
public abstract class BallotPageTestBase : PageTestBase
{
    /// <summary>
    ///     The review identifier.
    /// </summary>
    protected const string ReviewKey = "REV-1";

    /// <summary>
    ///     The number of projects created so far, which keeps the project names apart.
    /// </summary>
    private int _projectCount;

    /// <summary>
    ///     Gets the identifier of the first check of the default checklist.
    /// </summary>
    protected static string FirstItemId => DefaultChecklist.Current.Sections[0].Items[0].Id;

    /// <summary>
    ///     Gets the part of the test identifiers that names the first check.
    /// </summary>
    protected static string FirstItemTestId => FirstItemId.ToLowerInvariant();

    /// <summary>
    ///     Creates a project with the review <c>REV-1</c> and one reviewer slot for each of the given users.
    /// </summary>
    /// <param name="seated">The user names that hold a slot each.</param>
    /// <returns>The project id.</returns>
    protected async Task<int> CreateReviewAsync(params string[] seated)
    {
        var project = await Projects.CreateAsync($"Apollo {++_projectCount}", null, TestContext.Current.CancellationToken);
        await Reviews.CreateDraftAsync(
            project.Id, ReviewKey, "First review", [new("a.txt", TestContent.A)], TestContext.Current.CancellationToken);
        foreach (var name in seated)
        {
            await SeatAsync(project.Id, ReviewKey, name);
        }

        return project.Id;
    }

    /// <summary>
    ///     Makes a user a plain member of a project without a reviewer slot.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name; the display name is the capitalized user name.</param>
    /// <returns>A task that completes when the user is a member.</returns>
    protected async Task AddMemberAsync(int projectId, string username)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = await UserStore.GetByUsernameAsync(username, cancellationToken)
                   ?? await Users.CreateAsync(
                       username,
                       char.ToUpperInvariant(username[0]) + username[1..],
                       false,
                       "correct horse battery",
                       cancellationToken);
        await Store.AddMembershipAsync(user.Id, projectId, cancellationToken);
    }

    /// <summary>
    ///     Lets a reviewer answer every check, the first one with the given outcome and the others Pass, and hands the
    ///     ballot in when asked to.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <param name="username">The user name of the reviewer, who holds a slot.</param>
    /// <param name="first">The answer to the first check.</param>
    /// <param name="submit">Whether to hand the ballot in.</param>
    /// <returns>A task that completes when the answers are stored.</returns>
    protected async Task AnswerAsync(int projectId, string username, ChecklistOutcome first = ChecklistOutcome.Pass, bool submit = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actor = new Actor(username, char.ToUpperInvariant(username[0]) + username[1..]);
        foreach (var item in DefaultChecklist.Current.Sections.SelectMany(section => section.Items))
        {
            var outcome = item.Id == FirstItemId ? first : ChecklistOutcome.Pass;
            await Checklists.SetAnswerAsync(
                projectId, ReviewKey, item.Id, outcome, outcome == ChecklistOutcome.Pass ? null : "A reason", actor, cancellationToken);
        }

        if (submit)
        {
            await Checklists.SubmitBallotAsync(projectId, ReviewKey, actor, cancellationToken);
        }
    }

    /// <summary>
    ///     Renders the page for the review <c>REV-1</c>.
    /// </summary>
    /// <param name="projectId">The project identity.</param>
    /// <returns>The rendered component.</returns>
    protected IRenderedComponent<ChecklistPage> RenderPage(int projectId)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<ChecklistPage>(parameters => parameters
            .Add(page => page.ProjectId, projectId)
            .Add(page => page.ReviewId, ReviewKey));
    }
}
