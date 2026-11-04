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
///     Tests the pure rule that picks the one thing a viewer should do next in a review.
/// </summary>
public class ReviewNextActionTests
{
    /// <summary>
    ///     Builds the facts of a reviewer who holds a slot and has handed in a complete ballot, then applies a change.
    /// </summary>
    /// <param name="change">The change to the facts; may be null.</param>
    /// <returns>The facts.</returns>
    private static NextActionFacts Reviewer(Func<NextActionFacts, NextActionFacts>? change = null)
    {
        var facts = new NextActionFacts(
            IsConcluded: false,
            CanContribute: true,
            IsLead: false,
            SlotCount: 2,
            HasSlot: true,
            OwnBallot: BallotState.Submitted,
            OwnBallotComplete: true,
            OwnSignature: SlotSignatureState.NotSigned,
            CanAssignMe: false,
            CanSubmitAndSign: false,
            DisputesToDiscuss: 0,
            OpenFindings: 0,
            OpenQuestions: 0,
            ConclusionNeeded: false,
            CanSign: true);
        return change is null ? facts : change(facts);
    }

    /// <summary>
    ///     Builds the facts of a contributor who holds no slot, then applies a change.
    /// </summary>
    /// <param name="change">The change to the facts; may be null.</param>
    /// <returns>The facts.</returns>
    private static NextActionFacts Outsider(Func<NextActionFacts, NextActionFacts>? change = null)
    {
        var facts = Reviewer(f => f with { HasSlot = false, CanSign = false });
        return change is null ? facts : change(facts);
    }

    /// <summary>
    ///     Test that a contributor of a review without reviewers is asked to add the reviewers, or themselves.
    /// </summary>
    [Fact]
    public void ReviewNextAction_NoSlot_AssignMe()
    {
        // Arrange: nobody holds a slot; one viewer leads the review and one only could take a slot
        var lead = Outsider(f => f with { SlotCount = 0, IsLead = true });
        var member = Outsider(f => f with { SlotCount = 0, CanAssignMe = true });

        // Act
        var leadAction = ReviewNextAction.Decide(lead);
        var memberAction = ReviewNextAction.Decide(member);

        // Assert
        Assert.Equal(NextActionKind.AssignReviewer, leadAction.Kind);
        Assert.Equal(NextActionTarget.Reviewers, leadAction.Target);
        Assert.Equal(NextActionKind.AssignReviewer, memberAction.Kind);
        Assert.Equal("Add yourself as a reviewer", memberAction.Label);
    }

    /// <summary>
    ///     Test that a reviewer first answers the checklist, then hands in the ballot, alone or with the signature.
    /// </summary>
    [Fact]
    public void ReviewNextAction_DraftBallot_Submit()
    {
        // Arrange
        var incomplete = Reviewer(f => f with { OwnBallot = BallotState.Draft, OwnBallotComplete = false });
        var complete = Reviewer(f => f with { OwnBallot = BallotState.Draft });
        var signable = Reviewer(f => f with { OwnBallot = BallotState.Draft, CanSubmitAndSign = true });

        // Act
        var answer = ReviewNextAction.Decide(incomplete);
        var submit = ReviewNextAction.Decide(complete);
        var submitAndSign = ReviewNextAction.Decide(signable);

        // Assert
        Assert.Equal(NextActionKind.AnswerChecks, answer.Kind);
        Assert.Equal(NextActionKind.SubmitBallot, submit.Kind);
        Assert.Equal(NextActionKind.SubmitAndSign, submitAndSign.Kind);
        Assert.All([answer, submit, submitAndSign], action => Assert.Equal(NextActionTarget.Checklist, action.Target));
    }

    /// <summary>
    ///     Test that a reviewer who handed in is sent to the disagreements before anything else.
    /// </summary>
    [Fact]
    public void ReviewNextAction_SubmittedWithDispute_Discuss()
    {
        // Arrange: a dispute, and also an open question and a conclusion to write
        var facts = Reviewer(f => f with { DisputesToDiscuss = 2, OpenQuestions = 1, ConclusionNeeded = true });

        // Act
        var action = ReviewNextAction.Decide(facts);

        // Assert
        Assert.Equal(NextActionKind.DiscussDisputes, action.Kind);
        Assert.Equal(NextActionTarget.DisputedChecks, action.Target);
    }

    /// <summary>
    ///     Test that open findings come before open questions, and both before signing.
    /// </summary>
    [Fact]
    public void ReviewNextAction_OpenFindings_Resolve()
    {
        // Arrange
        var findings = Reviewer(f => f with { OpenFindings = 1, OpenQuestions = 3 });
        var questions = Reviewer(f => f with { OpenQuestions = 3 });

        // Act
        var resolve = ReviewNextAction.Decide(findings);
        var answer = ReviewNextAction.Decide(questions);

        // Assert
        Assert.Equal(NextActionKind.ResolveFindings, resolve.Kind);
        Assert.Equal(NextActionKind.AnswerQuestions, answer.Kind);
        Assert.Equal(NextActionTarget.Threads, resolve.Target);
        Assert.Equal(NextActionTarget.Threads, answer.Target);
    }

    /// <summary>
    ///     Test that a reviewer with nothing open writes a needed conclusion, then signs, and signs again after a change.
    /// </summary>
    [Fact]
    public void ReviewNextAction_ReadyToSign_Sign()
    {
        // Arrange
        var conclusion = Reviewer(f => f with { ConclusionNeeded = true });
        var sign = Reviewer();
        var again = Reviewer(f => f with { OwnSignature = SlotSignatureState.OutOfDate });

        // Act
        var write = ReviewNextAction.Decide(conclusion);
        var first = ReviewNextAction.Decide(sign);
        var second = ReviewNextAction.Decide(again);

        // Assert
        Assert.Equal(NextActionKind.WriteConclusion, write.Kind);
        Assert.Equal(NextActionTarget.Conclusion, write.Target);
        Assert.Equal(NextActionKind.Sign, first.Kind);
        Assert.Equal("Sign the review", first.Label);
        Assert.Equal(NextActionKind.Sign, second.Kind);
        Assert.Equal("Look at the changes and sign again", second.Label);
        Assert.DoesNotContain("out of date", second.Label, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a reviewer who signed, and a reviewer who cannot sign yet, wait for the others without a button.
    /// </summary>
    [Fact]
    public void ReviewNextAction_SignedWaiting_WaitForOthers()
    {
        // Arrange
        var signed = Reviewer(f => f with { OwnSignature = SlotSignatureState.Signed });
        var blocked = Reviewer(f => f with { CanSign = false });

        // Act
        var afterSigning = ReviewNextAction.Decide(signed);
        var whileBlocked = ReviewNextAction.Decide(blocked);

        // Assert
        Assert.Equal(NextActionKind.WaitForOthers, afterSigning.Kind);
        Assert.False(afterSigning.IsActionable);
        Assert.Equal(NextActionKind.WaitForOthers, whileBlocked.Kind);
    }

    /// <summary>
    ///     Test that a concluded review is done for everyone, and that a reader has nothing to do.
    /// </summary>
    [Fact]
    public void ReviewNextAction_Concluded_Done()
    {
        // Arrange: even a reviewer with a draft ballot has nothing to do once the review is concluded
        var concluded = Reviewer(f => f with { IsConcluded = true, OwnBallot = BallotState.Draft });
        var reader = Outsider(f => f with { CanContribute = false, DisputesToDiscuss = 1 });

        // Act
        var done = ReviewNextAction.Decide(concluded);
        var reading = ReviewNextAction.Decide(reader);

        // Assert
        Assert.Equal(NextActionKind.Done, done.Kind);
        Assert.False(done.IsActionable);
        Assert.Equal(NextActionKind.None, reading.Kind);
        Assert.False(reading.IsActionable);
    }

    /// <summary>
    ///     Test that the person who asked every open question waits for the answer, that the other party is still told to
    ///     answer, that findings still come first, and that unknown facts keep the older rule.
    /// </summary>
    [Fact]
    public void ReviewNextAction_OwnQuestion_WaitsForTheOtherParty()
    {
        // Arrange: three open questions, all asked by the viewer; the same questions seen by the other reviewer
        var asker = Reviewer(f => f with { OpenQuestions = 1, OpenQuestionsAwaitingYou = 0, OtherReviewers = ["Bob"] });
        var many = Reviewer(f => f with { OpenQuestions = 1, OpenQuestionsAwaitingYou = 0, OtherReviewers = ["Bob", "Cyd", "Dee"] });
        var other = Reviewer(f => f with { OpenQuestions = 1, OpenQuestionsAwaitingYou = 1, OtherReviewers = ["Ann"] });
        var unknown = Reviewer(f => f with { OpenQuestions = 1 });
        var findings = Reviewer(f => f with { OpenFindings = 1, OpenQuestions = 1, OpenQuestionsAwaitingYou = 0 });

        // Act
        var waiting = ReviewNextAction.Decide(asker);
        var crowd = ReviewNextAction.Decide(many);
        var answer = ReviewNextAction.Decide(other);
        var older = ReviewNextAction.Decide(unknown);
        var resolve = ReviewNextAction.Decide(findings);

        // Assert: the asker has a sentence without a button; everybody else is as before
        Assert.Equal(NextActionKind.WaitForOthers, waiting.Kind);
        Assert.False(waiting.IsActionable);
        Assert.Equal("Waiting for Bob to answer your question.", waiting.Label);
        Assert.Equal("Waiting for an answer to your question.", crowd.Label);
        Assert.Equal(NextActionKind.AnswerQuestions, answer.Kind);
        Assert.Equal("Answer the open questions", answer.Label);
        Assert.Equal(NextActionKind.AnswerQuestions, older.Kind);
        Assert.Equal(NextActionKind.ResolveFindings, resolve.Kind);
    }

    /// <summary>
    ///     Test that a reviewer who signed is told whom the review waits for, and that a reviewer whose signature no
    ///     longer counts, and who cannot sign again yet, is told so.
    /// </summary>
    [Fact]
    public void ReviewNextAction_AfterSigning_NamesWhoIsMissing()
    {
        // Arrange
        var named = Reviewer(f => f with { OwnSignature = SlotSignatureState.Signed, OthersToSign = ["Bob"] });
        var unnamed = Reviewer(f => f with { OwnSignature = SlotSignatureState.Signed });
        var lost = Reviewer(f => f with { OwnSignature = SlotSignatureState.OutOfDate, CanSign = false });

        // Act
        var afterSigning = ReviewNextAction.Decide(named);
        var fallback = ReviewNextAction.Decide(unnamed);
        var lostAction = ReviewNextAction.Decide(lost);

        // Assert
        Assert.Equal("You have signed. Waiting for Bob to sign.", afterSigning.Label);
        Assert.False(afterSigning.IsActionable);
        Assert.Equal("You have signed. Wait for the other reviewers.", fallback.Label);
        Assert.Equal(NextActionKind.WaitForOthers, lostAction.Kind);
        Assert.False(lostAction.IsActionable);
        Assert.Contains("sign again", lostAction.Label, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every combination of the facts gives exactly one action with words, and that an action with a
    ///     target always has a button label.
    /// </summary>
    [Fact]
    public void ReviewNextAction_OneActionPerViewer()
    {
        // Arrange: all combinations of the yes/no facts and the states, with and without open work
        var bools = new[] { false, true };
        var combinations =
            (from concluded in bools
             from contribute in bools
             from hasSlot in bools
             from ballot in Enum.GetValues<BallotState>()
             from signature in Enum.GetValues<SlotSignatureState>()
             from slots in new[] { 0, 1, 3 }
             from open in new[] { 0, 2 }
             from flag in bools
             select new NextActionFacts(
                 concluded, contribute, flag, slots, hasSlot, ballot, flag, signature, flag, !flag, open, open, open, flag, !flag))
             .ToList();

        // Act
        var actions = combinations.Select(ReviewNextAction.Decide).ToList();

        // Assert
        Assert.Equal(combinations.Count, actions.Count);
        Assert.All(actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Label));
            Assert.True(Enum.IsDefined(action.Kind));
            Assert.Equal(action.Target != NextActionTarget.None, action.IsActionable);
            Assert.DoesNotContain("out of date", action.Label, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    ///     Test that a viewer who may take part but holds no slot is asked to discuss the disputed checks.
    /// </summary>
    [Fact]
    public void ReviewNextAction_NoSlotViewer_Disputes_Discuss()
    {
        // Arrange: the review has slots, and a check is disputed
        var facts = Outsider(f => f with { DisputesToDiscuss = 1 });

        // Act
        var action = ReviewNextAction.Decide(facts);

        // Assert
        Assert.Equal(NextActionKind.DiscussDisputes, action.Kind);
        Assert.Equal(NextActionTarget.DisputedChecks, action.Target);
    }

    /// <summary>
    ///     Test that a viewer who may take part but holds no slot is asked to resolve the open findings.
    /// </summary>
    [Fact]
    public void ReviewNextAction_NoSlotViewer_OpenFindings_Resolve()
    {
        // Arrange: the review has slots and an open finding, and no dispute
        var facts = Outsider(f => f with { OpenFindings = 1 });

        // Act
        var action = ReviewNextAction.Decide(facts);

        // Assert
        Assert.Equal(NextActionKind.ResolveFindings, action.Kind);
        Assert.Equal(NextActionTarget.Threads, action.Target);
    }

    /// <summary>
    ///     Test that a viewer who may take part but holds no slot is asked to answer the open questions.
    /// </summary>
    [Fact]
    public void ReviewNextAction_NoSlotViewer_OpenQuestions_Answer()
    {
        // Arrange: the review has slots and an open question, and nothing else is open
        var facts = Outsider(f => f with { OpenQuestions = 1 });

        // Act
        var action = ReviewNextAction.Decide(facts);

        // Assert
        Assert.Equal(NextActionKind.AnswerQuestions, action.Kind);
        Assert.Equal(NextActionTarget.Threads, action.Target);
    }

    /// <summary>
    ///     Test that a viewer who may take part but holds no slot is asked to write a needed conclusion.
    /// </summary>
    [Fact]
    public void ReviewNextAction_NoSlotViewer_ConclusionNeeded_WriteConclusion()
    {
        // Arrange: the review has slots and needs a conclusion, and nothing else is open
        var facts = Outsider(f => f with { ConclusionNeeded = true });

        // Act
        var action = ReviewNextAction.Decide(facts);

        // Assert
        Assert.Equal(NextActionKind.WriteConclusion, action.Kind);
        Assert.Equal(NextActionTarget.Conclusion, action.Target);
    }

    /// <summary>
    ///     Test that a reviewer who asked the open question waits for two other reviewers by name.
    /// </summary>
    [Fact]
    public void ReviewNextAction_OwnQuestion_TwoOtherReviewers_AreNamedTogether()
    {
        // Arrange: the viewer asked the only open question, and two other reviewers hold slots
        var facts = Reviewer(f => f with { OpenQuestions = 1, OpenQuestionsAwaitingYou = 0, OtherReviewers = ["Bob", "Cyd"] });

        // Act
        var action = ReviewNextAction.Decide(facts);

        // Assert
        Assert.Equal(NextActionKind.WaitForOthers, action.Kind);
        Assert.Equal("Waiting for Bob and Cyd to answer your question.", action.Label);
    }

    /// <summary>
    ///     Test that a missing set of facts is refused.
    /// </summary>
    [Fact]
    public void ReviewNextAction_Decide_NullFacts_Throws()
    {
        // Arrange / Act / Assert
        var exception = Assert.Throws<ArgumentNullException>(() => ReviewNextAction.Decide(null!));
        Assert.Equal("facts", exception.ParamName);
    }
}
