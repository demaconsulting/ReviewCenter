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
///     What a viewer should do next in a review.
/// </summary>
public enum NextActionKind
{
    /// <summary>There is nothing the viewer can do here.</summary>
    None,

    /// <summary>The review has no reviewer yet, or the viewer can add themselves as one.</summary>
    AssignReviewer,

    /// <summary>The viewer's ballot still has checks without an answer.</summary>
    AnswerChecks,

    /// <summary>The viewer's ballot is complete and waits to be handed in.</summary>
    SubmitBallot,

    /// <summary>The viewer's ballot is complete and can be handed in and signed in one step.</summary>
    SubmitAndSign,

    /// <summary>Reviewers answered a check differently and nobody has started a discussion about it.</summary>
    DiscussDisputes,

    /// <summary>Findings are open and must be resolved.</summary>
    ResolveFindings,

    /// <summary>Questions are open and wait for an answer.</summary>
    AnswerQuestions,

    /// <summary>A rejected review needs its conclusion written.</summary>
    WriteConclusion,

    /// <summary>The viewer can sign the review.</summary>
    Sign,

    /// <summary>The viewer has done their part; the others have not.</summary>
    WaitForOthers,

    /// <summary>The review is concluded; nothing is left to do.</summary>
    Done
}

/// <summary>
///     Where on the review page, or in the review, the next action is done.
/// </summary>
public enum NextActionTarget
{
    /// <summary>There is nothing to go to.</summary>
    None,

    /// <summary>The reviewers and signing card of the review page.</summary>
    Reviewers,

    /// <summary>The checklist page of the review.</summary>
    Checklist,

    /// <summary>The disputed checks of the status panel.</summary>
    DisputedChecks,

    /// <summary>The threads (questions and findings) of the review page.</summary>
    Threads,

    /// <summary>The review conclusion card of the review page.</summary>
    Conclusion
}

/// <summary>
///     The facts about a viewer and a review that decide the next action.
/// </summary>
/// <remarks>
///     Every fact is already reduced to what the viewer may see, so the rule cannot reveal anything: for a viewer who
///     may not see the other reviewers' answers yet, no check is disputed and no check thread is counted.
/// </remarks>
/// <param name="IsConcluded">Whether the review is concluded (and locked).</param>
/// <param name="CanContribute">Whether the viewer may take part in the review (not read-only, not an outsider).</param>
/// <param name="IsLead">Whether the viewer leads the review (its creator, or an administrator).</param>
/// <param name="SlotCount">How many reviewer slots the review has.</param>
/// <param name="HasSlot">Whether the viewer holds a reviewer slot.</param>
/// <param name="OwnBallot">The state of the viewer's ballot; only meaningful when <paramref name="HasSlot"/>.</param>
/// <param name="OwnBallotComplete">Whether the viewer's ballot has an answer for every check that applies.</param>
/// <param name="OwnSignature">The signature state of the viewer's slot; only meaningful when <paramref name="HasSlot"/>.</param>
/// <param name="CanAssignMe">Whether the viewer could add themselves as a reviewer.</param>
/// <param name="CanSubmitAndSign">Whether handing in and signing can be done in one step.</param>
/// <param name="DisputesToDiscuss">How many disputed checks the viewer can start a discussion about.</param>
/// <param name="OpenFindings">How many findings the viewer sees open.</param>
/// <param name="OpenQuestions">How many questions the viewer sees open.</param>
/// <param name="ConclusionNeeded">Whether the viewer sees that the review needs a conclusion and may write it.</param>
/// <param name="CanSign">Whether the viewer may sign right now.</param>
public sealed record NextActionFacts(
    bool IsConcluded,
    bool CanContribute,
    bool IsLead,
    int SlotCount,
    bool HasSlot,
    BallotState OwnBallot,
    bool OwnBallotComplete,
    SlotSignatureState OwnSignature,
    bool CanAssignMe,
    bool CanSubmitAndSign,
    int DisputesToDiscuss,
    int OpenFindings,
    int OpenQuestions,
    bool ConclusionNeeded,
    bool CanSign);

/// <summary>
///     The one thing a viewer should do next in a review, in plain words.
/// </summary>
/// <remarks>
///     <see cref="Decide"/> is a pure rule: the same facts always give the same action, and every viewer gets exactly
///     one. The record is an immutable value holder and is safe to share between threads.
/// </remarks>
/// <param name="Kind">What to do.</param>
/// <param name="Label">The words for the button or the sentence.</param>
/// <param name="Target">Where it is done.</param>
public sealed record ReviewNextAction(NextActionKind Kind, string Label, NextActionTarget Target)
{
    /// <summary>
    ///     Gets a value indicating whether there is something to go to (a button), as opposed to a sentence that only
    ///     says what is going on.
    /// </summary>
    public bool IsActionable => Target != NextActionTarget.None;

    /// <summary>
    ///     Decides the next action of a viewer.
    /// </summary>
    /// <param name="facts">The facts about the viewer and the review. Must not be null.</param>
    /// <returns>Exactly one action.</returns>
    /// <remarks>
    ///     The order is: a concluded review is done; a viewer who cannot take part has nothing to do; a reviewer first
    ///     answers the checklist, hands in the ballot, deals with the disagreements, the findings and the questions,
    ///     writes the conclusion when one is needed, and signs; somebody who holds no slot adds the reviewers or
    ///     themselves, or helps with the discussions; everybody else waits.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="facts"/> is null.</exception>
    public static ReviewNextAction Decide(NextActionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.IsConcluded)
        {
            return new ReviewNextAction(NextActionKind.Done, "The review is finished. Nothing is left to do.", NextActionTarget.None);
        }

        if (!facts.CanContribute)
        {
            return new ReviewNextAction(NextActionKind.None, "You are reading this review. There is nothing to do.", NextActionTarget.None);
        }

        return facts.HasSlot ? ForReviewer(facts) : ForOthers(facts);
    }

    /// <summary>
    ///     Decides the next action of a viewer who holds a reviewer slot.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The action.</returns>
    private static ReviewNextAction ForReviewer(NextActionFacts facts)
    {
        // The ballot comes first: nothing else can be judged before the viewer's own answers are in
        if (facts.OwnBallot != BallotState.Submitted)
        {
            if (!facts.OwnBallotComplete)
            {
                return new ReviewNextAction(NextActionKind.AnswerChecks, "Answer the checklist", NextActionTarget.Checklist);
            }

            return facts.CanSubmitAndSign
                ? new ReviewNextAction(NextActionKind.SubmitAndSign, "Hand in your ballot and sign", NextActionTarget.Checklist)
                : new ReviewNextAction(NextActionKind.SubmitBallot, "Hand in your ballot", NextActionTarget.Checklist);
        }

        // A reviewer who has signed has done their part, except for the discussions that are still open
        var signed = facts.OwnSignature == SlotSignatureState.Signed;
        if (!signed && facts.DisputesToDiscuss > 0)
        {
            return new ReviewNextAction(NextActionKind.DiscussDisputes, "Discuss the checks you answered differently", NextActionTarget.DisputedChecks);
        }

        var threads = OpenThreadAction(facts);
        if (threads is not null)
        {
            return threads;
        }

        if (signed)
        {
            return Wait();
        }

        if (facts.ConclusionNeeded)
        {
            return new ReviewNextAction(NextActionKind.WriteConclusion, "Write the review conclusion", NextActionTarget.Conclusion);
        }

        if (facts.CanSign)
        {
            var label = facts.OwnSignature == SlotSignatureState.OutOfDate
                ? "Look at the changes and sign again"
                : "Sign the review";
            return new ReviewNextAction(NextActionKind.Sign, label, NextActionTarget.Reviewers);
        }

        return Wait();
    }

    /// <summary>
    ///     Decides the next action of a viewer who holds no reviewer slot but may take part.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The action.</returns>
    private static ReviewNextAction ForOthers(NextActionFacts facts)
    {
        if (facts.SlotCount == 0 && (facts.IsLead || facts.CanAssignMe))
        {
            var label = facts.IsLead ? "Add the reviewers" : "Add yourself as a reviewer";
            return new ReviewNextAction(NextActionKind.AssignReviewer, label, NextActionTarget.Reviewers);
        }

        if (facts.DisputesToDiscuss > 0)
        {
            return new ReviewNextAction(NextActionKind.DiscussDisputes, "Discuss the checks the reviewers answered differently", NextActionTarget.DisputedChecks);
        }

        var threads = OpenThreadAction(facts);
        if (threads is not null)
        {
            return threads;
        }

        if (facts.ConclusionNeeded)
        {
            return new ReviewNextAction(NextActionKind.WriteConclusion, "Write the review conclusion", NextActionTarget.Conclusion);
        }

        if (facts.CanAssignMe)
        {
            return new ReviewNextAction(NextActionKind.AssignReviewer, "Add yourself as a reviewer", NextActionTarget.Reviewers);
        }

        return facts.SlotCount == 0
            ? new ReviewNextAction(NextActionKind.None, "No reviewer has been added yet.", NextActionTarget.None)
            : Wait();
    }

    /// <summary>
    ///     Decides the action for open findings and questions.
    /// </summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The action, or <c>null</c> when no thread is open.</returns>
    private static ReviewNextAction? OpenThreadAction(NextActionFacts facts)
    {
        if (facts.OpenFindings > 0)
        {
            return new ReviewNextAction(NextActionKind.ResolveFindings, "Resolve the open findings", NextActionTarget.Threads);
        }

        return facts.OpenQuestions > 0
            ? new ReviewNextAction(NextActionKind.AnswerQuestions, "Answer the open questions", NextActionTarget.Threads)
            : null;
    }

    /// <summary>
    ///     Gets the action of a person who has done their part.
    /// </summary>
    /// <returns>The action.</returns>
    private static ReviewNextAction Wait()
    {
        return new ReviewNextAction(NextActionKind.WaitForOthers, "Wait for the other reviewers.", NextActionTarget.None);
    }
}
