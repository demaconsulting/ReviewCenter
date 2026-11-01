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
///     How many threads of a review are open or resolved, split by kind (Question or Finding), so the thread index,
///     the summary strip, the progress and the inbox count and word threads the same way.
/// </summary>
/// <remarks>
///     A pure value computed from a list of threads; nothing is stored. The wording helpers put a Finding before a
///     Question because a Finding is part of the signed report. An immutable value holder that is safe to share
///     between threads.
/// </remarks>
/// <param name="OpenQuestions">The number of open threads that are Questions (chat).</param>
/// <param name="OpenFindings">The number of open threads that are Findings (part of the report).</param>
/// <param name="ResolvedQuestions">The number of resolved threads that are Questions.</param>
/// <param name="ResolvedFindings">The number of resolved threads that are Findings.</param>
public readonly record struct ThreadCounts(
    int OpenQuestions,
    int OpenFindings,
    int ResolvedQuestions,
    int ResolvedFindings)
{
    /// <summary>
    ///     Gets the number of open threads, of either kind.
    /// </summary>
    public int Open => OpenQuestions + OpenFindings;

    /// <summary>
    ///     Gets the number of resolved threads, of either kind.
    /// </summary>
    public int Resolved => ResolvedQuestions + ResolvedFindings;

    /// <summary>
    ///     Gets the number of Questions, open or resolved.
    /// </summary>
    public int Questions => OpenQuestions + ResolvedQuestions;

    /// <summary>
    ///     Gets the number of Findings, open or resolved.
    /// </summary>
    public int Findings => OpenFindings + ResolvedFindings;

    /// <summary>
    ///     Gets the number of threads, whatever their kind and state.
    /// </summary>
    public int Total => Open + Resolved;

    /// <summary>
    ///     Gets the open threads in words, for example "2 open findings, 1 open question".
    /// </summary>
    public string OpenText => OpenPhrase(OpenQuestions, OpenFindings);

    /// <summary>
    ///     Counts threads by kind and state.
    /// </summary>
    /// <param name="threads">The threads to count. Must not be null.</param>
    /// <param name="include">
    ///     Decides which threads take part, or <c>null</c> for all of them; a viewer that must not know of the threads
    ///     about a check leaves them out here.
    /// </param>
    /// <returns>The counts.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="threads"/> is null.</exception>
    public static ThreadCounts From(IEnumerable<ReviewThread> threads, Func<ReviewThread, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(threads);
        int openQuestions = 0, openFindings = 0, resolvedQuestions = 0, resolvedFindings = 0;
        foreach (var thread in threads)
        {
            if (include is not null && !include(thread))
            {
                continue;
            }

            var finding = thread.Kind == ThreadKind.Finding;
            if (thread.State == ReviewThreadState.Open)
            {
                openFindings += finding ? 1 : 0;
                openQuestions += finding ? 0 : 1;
            }
            else
            {
                resolvedFindings += finding ? 1 : 0;
                resolvedQuestions += finding ? 0 : 1;
            }
        }

        return new ThreadCounts(openQuestions, openFindings, resolvedQuestions, resolvedFindings);
    }

    /// <summary>
    ///     Words a number of open Questions and open Findings.
    /// </summary>
    /// <param name="openQuestions">The number of open Questions.</param>
    /// <param name="openFindings">The number of open Findings.</param>
    /// <returns>
    ///     For example "2 open findings, 1 open question" (Findings first); "no open threads" when both are zero.
    /// </returns>
    public static string OpenPhrase(int openQuestions, int openFindings)
    {
        var parts = new List<string>();
        if (openFindings > 0)
        {
            parts.Add(OpenFindingsPhrase(openFindings));
        }

        if (openQuestions > 0)
        {
            parts.Add(OpenQuestionsPhrase(openQuestions));
        }

        return parts.Count == 0 ? "no open threads" : string.Join(", ", parts);
    }

    /// <summary>
    ///     Words a number of open Questions, also when it is zero.
    /// </summary>
    /// <param name="count">The number of open Questions.</param>
    /// <returns>For example "1 open question" or "0 open questions".</returns>
    public static string OpenQuestionsPhrase(int count)
    {
        return count == 1 ? "1 open question" : $"{count} open questions";
    }

    /// <summary>
    ///     Words a number of open Findings, also when it is zero.
    /// </summary>
    /// <param name="count">The number of open Findings.</param>
    /// <returns>For example "1 open finding" or "0 open findings".</returns>
    public static string OpenFindingsPhrase(int count)
    {
        return count == 1 ? "1 open finding" : $"{count} open findings";
    }
}
