### BallotConsolidation

#### Verification Approach

`BallotConsolidation` is verified with tests defined in `BallotConsolidationTests.cs`. It is a pure unit, so the tests
build a small checklist and ballots in memory, call `Consolidate` and `IsComplete` directly, and check the standing of
checks and sections, the counts, and the derived outcome. Separate tests check that only submitted ballots count, that
wording never creates a dispute, that the order of the ballots does not matter, and that null input is refused.
`BallotProjection` is verified with tests defined in `BallotProjectionTests.cs`, in the same way: the tests build a
record with a checklist and ballots in memory and call `Consolidate` and `ForBallot` directly.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All tests pass with zero failures.
- A check is agreed when all submitted ballots give the same outcome, disputed when they differ (a pass against a
  not-applicable answer included), and pending when no ballot is submitted or a submitted ballot does not answer it.
- A justification or a note never creates a dispute.
- Drafts and slots without a ballot never change the result, and the order of the ballots does not matter.
- A section is not applicable when every submitted ballot marks it so and disputed when only some do; its checks are
  then not listed.
- The outcome is absent while anything is pending or disputed, rejected when an agreed check fails, and approved
  otherwise, also for a checklist without checks that has a submitted ballot.
- The counts add up, and the ballots are counted.
- `IsComplete` asks for an answer to every check of every section that the ballot treats as applicable, whatever its
  status.
- A submitted ballot shows the state submitted and any other ballot shows draft.
- Null arguments are refused.
- `BallotProjection.Consolidate` gives no view for a record without a checklist; an agreed check is answered with the
  row of the lowest slot that answered it, and a disputed or pending check has no answer; drafts never contribute,
  except the sole draft of a review with one slot when the number of slots is given; a section is not applicable
  when the submitted ballots agree and disputed otherwise.
- `BallotProjection.ForBallot` shows the rows of the viewer's own ballot, a draft included, marked as the viewer's own,
  and an empty state that has not been started when the slot has no ballot.
- Missing arguments of the projection are refused.

#### Test Scenarios

**BallotConsolidation_Consolidate_NoSubmittedBallots_AllPending_OutcomeNull**: With no submitted ballot every check is
pending and there is no outcome. This scenario is tested by
`BallotConsolidation_Consolidate_NoSubmittedBallots_AllPending_OutcomeNull`.

**BallotConsolidation_Consolidate_OneSubmittedOneDraft_OnlySubmittedCounts**: A draft does not change the standing that
the submitted ballot gives. This scenario is tested by
`BallotConsolidation_Consolidate_OneSubmittedOneDraft_OnlySubmittedCounts`.

**BallotConsolidation_Consolidate_SubmittedEqual_Agreed**: Submitted ballots with the same outcomes agree. This scenario
is tested by `BallotConsolidation_Consolidate_SubmittedEqual_Agreed`.

**BallotConsolidation_Consolidate_DifferentOutcomes_Disputed**: Submitted ballots with different outcomes are in
dispute. This scenario is tested by `BallotConsolidation_Consolidate_DifferentOutcomes_Disputed`.

**BallotConsolidation_Consolidate_NotesDiffer_StillAgreed**: Different justifications and notes do not create a dispute.
This scenario is tested by `BallotConsolidation_Consolidate_NotesDiffer_StillAgreed`.

**BallotConsolidation_Consolidate_DraftAnswers_Ignored**: The answers and section states of a draft are ignored. This
scenario is tested by `BallotConsolidation_Consolidate_DraftAnswers_Ignored`.

**BallotConsolidation_Consolidate_SectionNaVsApplicable_SectionDisputed**: A section that some submitted ballots mark as
not applicable and others do not is disputed. This scenario is tested by
`BallotConsolidation_Consolidate_SectionNaVsApplicable_SectionDisputed`.

**BallotConsolidation_Consolidate_AllSectionsNa_ChecksOmitted**: A section that every submitted ballot marks as not
applicable has no listed checks. This scenario is tested by
`BallotConsolidation_Consolidate_AllSectionsNa_ChecksOmitted`.

**BallotConsolidation_Consolidate_ItemNaVsPass_Disputed**: A not-applicable answer against a pass is a dispute. This
scenario is tested by `BallotConsolidation_Consolidate_ItemNaVsPass_Disputed`.

**BallotConsolidation_Consolidate_AllItemNa_AgreedNotFailed**: A not-applicable answer on which all agree is agreed and
is not a failure. This scenario is tested by `BallotConsolidation_Consolidate_AllItemNa_AgreedNotFailed`.

**BallotConsolidation_Consolidate_AgreedFail_Rejected**: An agreed failure gives the outcome rejected. This scenario is
tested by `BallotConsolidation_Consolidate_AgreedFail_Rejected`.

**BallotConsolidation_Consolidate_AllPass_Approved**: All checks agreed as pass give the outcome approved. This scenario
is tested by `BallotConsolidation_Consolidate_AllPass_Approved`.

**BallotConsolidation_Consolidate_DisputedCheck_OutcomeNull**: A disputed check leaves the outcome absent. This scenario
is tested by `BallotConsolidation_Consolidate_DisputedCheck_OutcomeNull`.

**BallotConsolidation_Consolidate_PendingCheck_OutcomeNull**: A check that a submitted ballot does not answer is pending
and leaves the outcome absent. This scenario is tested by `BallotConsolidation_Consolidate_PendingCheck_OutcomeNull`.

**BallotConsolidation_Consolidate_NoChecklistItems_OneSubmittedBallot_Approved**: A checklist without checks gives the
outcome approved when a ballot is submitted. This scenario is tested by
`BallotConsolidation_Consolidate_NoChecklistItems_OneSubmittedBallot_Approved`.

**BallotConsolidation_Consolidate_NoChecklistItems_NoSubmittedBallot_OutcomeNull**: A checklist without checks gives no
outcome when no ballot is submitted. This scenario is tested by
`BallotConsolidation_Consolidate_NoChecklistItems_NoSubmittedBallot_OutcomeNull`.

**BallotConsolidation_Consolidate_Counts_AreConsistent**: The counts add up to the listed checks plus the disputed
sections. This scenario is tested by `BallotConsolidation_Consolidate_Counts_AreConsistent`.

**BallotConsolidation_Consolidate_BallotCounts**: The submitted and all ballots are counted. This scenario is tested by
`BallotConsolidation_Consolidate_BallotCounts`.

**BallotConsolidation_Consolidate_BallotOrder_DoesNotMatter**: The same ballots in another order give the same result,
and the per-slot outcomes of a check enumerate in ascending slot order.
This scenario is tested by `BallotConsolidation_Consolidate_BallotOrder_DoesNotMatter`.

**BallotConsolidation_NullArguments_Throws**: Null arguments, a null ballot and two ballots of one slot are refused.
This scenario is tested by
`BallotConsolidation_NullArguments_Throws`.

**BallotConsolidation_BallotState_DraftAndSubmitted_ReflectsStatus**: The state of a ballot is submitted or draft
according to its status. This scenario is tested by `BallotConsolidation_BallotState_DraftAndSubmitted_ReflectsStatus`.

**BallotConsolidation_IsComplete_AllAnswered_True**: A ballot with an answer for every check is complete. This scenario
is tested by `BallotConsolidation_IsComplete_AllAnswered_True`.

**BallotConsolidation_IsComplete_MissingAnswer_False**: A ballot with a missing answer is not complete. This scenario is
tested by `BallotConsolidation_IsComplete_MissingAnswer_False`.

**BallotConsolidation_IsComplete_NaSectionSkipsItems_True**: A section that the ballot marks as not applicable needs no
answers. This scenario is tested by `BallotConsolidation_IsComplete_NaSectionSkipsItems_True`.

**BallotProjection_Consolidate_NoChecklist_ReturnsNull**: A record without a checklist has no consolidated view. This
scenario is tested by `BallotProjection_Consolidate_NoChecklist_ReturnsNull`.

**BallotProjection_NullArguments_Throw**: A null record for `Consolidate` and a null definition for `ForBallot` are
refused with an `ArgumentNullException`. This scenario is tested by `BallotProjection_NullArguments_Throw`.

**BallotProjection_Consolidate_RepresentativeRow_IsTheLowestSlot**: Two submitted ballots agree on one check, disagree
on another, and leave a third unanswered; the agreed check is answered with the row of the lowest slot, and the
disputed and the pending check have no answer but their consolidation. This scenario is tested by
`BallotProjection_Consolidate_RepresentativeRow_IsTheLowestSlot`.

**BallotProjection_Consolidate_DraftBallots_AreIgnored**: A draft ballot, however far along, takes no part in the view.
This scenario is tested by `BallotProjection_Consolidate_DraftBallots_AreIgnored`.

**BallotProjection_Consolidate_SingleSlotDraft_CountsOnlyWithTheSlotCount**: The sole draft of a review with one slot
gives answers when the number of slots 1 is given, and none when it is not given or when the review has two slots. This
scenario is tested by `BallotProjection_Consolidate_SingleSlotDraft_CountsOnlyWithTheSlotCount`.

**BallotProjection_Consolidate_Sections_AgreedNotApplicableAndDisputed**: A section that the submitted ballots agree
is not applicable is not applicable and has the row of the lowest slot, a section on which they disagree is disputed,
and the author blocked and no slot flags of the call are carried into the state. This scenario is tested by
`BallotProjection_Consolidate_Sections_AgreedNotApplicableAndDisputed`.

**BallotProjection_ForBallot_ShowsTheOwnDraft**: The state of a draft ballot shows its own answers and section states,
the slot, and the state draft, and is marked as the viewer's own ballot. This scenario is tested by
`BallotProjection_ForBallot_ShowsTheOwnDraft`.

**BallotProjection_ForBallot_NoBallot_IsNotStarted**: A slot without a ballot gets an empty state that is not started
and is marked as the viewer's own. This scenario is tested by `BallotProjection_ForBallot_NoBallot_IsNotStarted`.
