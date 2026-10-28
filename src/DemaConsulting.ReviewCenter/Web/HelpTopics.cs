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

namespace DemaConsulting.ReviewCenter.Web;

/// <summary>
///     One topic of the help: the part of its address after <c>/help/</c>, the title, and a one-line description.
/// </summary>
/// <param name="Slug">The last part of the topic's address, for example <c>checklist</c>.</param>
/// <param name="Title">The title shown in the index and as the heading of the topic page.</param>
/// <param name="Summary">One line that says what the topic explains.</param>
internal sealed record HelpTopic(string Slug, string Title, string Summary)
{
    /// <summary>
    ///     Gets the address of the topic page.
    /// </summary>
    public string Href => $"/help/{Slug}";
}

/// <summary>
///     The list of help topics, in reading order. It feeds the help index and the Previous and Next links of the
///     topic pages; to add a topic, add a page <c>/help/{slug}</c> and one entry here.
/// </summary>
internal static class HelpTopics
{
    /// <summary>
    ///     Gets all topics in reading order.
    /// </summary>
    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new("getting-started", "Getting started", "Sign in, find your project and open a review."),
        new("creating-a-review", "Creating a review", "Choose the files from a git repository or by uploading them."),
        new("reading-a-review", "Reading a review", "The Diff, Head and Baseline views and the change kinds."),
        new("comments-and-threads", "Comments and threads", "Comment on a line or a file, reply, resolve and share a link."),
        new("checklist", "Checklist", "Answer each check Pass, Fail or N/A."),
        new("reviewers-and-signing", "Reviewers and signing", "Reviewer slots, signing with your password, and approval."),
        new("signed-attestation", "Signed attestation", "Download a digitally signed PDF of a completed review."),
        new("audit-trail", "Audit trail", "Every action is recorded in a tamper-evident chain."),
        new("planning-reviews", "Planning reviews", "Create the reviews of a repository's review-sets in one go."),
        new(
            "project-members",
            "Managing project members",
            "Add people to a project, create an account, change a role or remove a member."),
        new("api-tokens", "Automation and API tokens", "Use a personal access token to read reviews and create drafts from a script."),
    ];

    /// <summary>
    ///     Finds a topic by its slug.
    /// </summary>
    /// <param name="slug">The slug to look for.</param>
    /// <returns>The topic, or <c>null</c> when there is none with that slug.</returns>
    public static HelpTopic? Find(string slug) =>
        All.FirstOrDefault(topic => string.Equals(topic.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    ///     Gets the topic before the given one in reading order.
    /// </summary>
    /// <param name="slug">The slug of the current topic.</param>
    /// <returns>The previous topic, or <c>null</c> for the first topic or an unknown slug.</returns>
    public static HelpTopic? Previous(string slug)
    {
        var index = IndexOf(slug);
        return index > 0 ? All[index - 1] : null;
    }

    /// <summary>
    ///     Gets the topic after the given one in reading order.
    /// </summary>
    /// <param name="slug">The slug of the current topic.</param>
    /// <returns>The next topic, or <c>null</c> for the last topic or an unknown slug.</returns>
    public static HelpTopic? Next(string slug)
    {
        var index = IndexOf(slug);
        return index >= 0 && index < All.Count - 1 ? All[index + 1] : null;
    }

    /// <summary>
    ///     Gets the position of a topic in the list.
    /// </summary>
    /// <param name="slug">The slug to look for.</param>
    /// <returns>The zero-based position, or -1 when there is no such topic.</returns>
    private static int IndexOf(string slug)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (string.Equals(All[index].Slug, slug, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
