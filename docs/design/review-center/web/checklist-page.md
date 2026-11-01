### ChecklistPage

![Web Structure](WebView.svg)

#### Purpose

`ChecklistPage` is the page at `/projects/{id}/reviews/{reviewId}/checklist`. Its responsibility is to let a
reviewer work through the checklist of one review and always see what is left to do. From the top it shows the
breadcrumb, the title of the review, a status line ("15 checks unanswered."), the overall progress ("37 of 52 checks
answered - 6 sections not applicable"), a "Next unanswered check" button while checks are unanswered (a link back
to the review once the checklist is complete), and the list of sections. Each section is a
heading with a decorative chevron (hidden from screen readers) and its progress ("3 of 8", "Done", or
"Not applicable"); only one section is open at a time,
and the page opens the first section with unanswered checks. A finished section that is open ends with the next
action, so the reviewer does not have to scroll back: the same "Next unanswered check" button, or, when the whole
checklist is complete, a link back to the review. A Fail is shown in a different color than a Pass. It
is a Razor component split into presentation (`ChecklistPage.razor`) and behavior (`ChecklistPage.razor.cs`). It
reads and writes the checklist only through `ChecklistService`, so every checklist rule is applied there. It requires
a signed-in user, and every answer and section decision is saved on behalf of that user, in the ballot of the reviewer
slot that the user holds (see *ChecklistService Design*). What the page shows depends on the viewer: a user who holds a
slot sees and edits the own ballot, drafts included, and a status line says whether it counts yet ("You have not
answered yet.", "Your ballot is a draft: it does not count until you submit it.", or "Your ballot is submitted and
counts in the review."); a **Submit ballot** button hands in a draft that answers every check it requires. In a review
with a single slot an answer or an edit that completes the ballot hands it in at once, so the page then shows the
ballot as submitted and no button; a complete draft that no such answer produced (a follow-up whose checks are all
carried, or a checklist without checks) still shows the button. A user who holds no slot (and is not read-only) sees
the notice `no-slot-notice` and cannot answer; in a review with a single slot or a concluded review the user also sees
the consolidated standing of the submitted ballots, and in a review with several slots that is not concluded the
standing is masked as described below, unless the user is the lead, an administrator or an Auditor. When the review is
concluded (approved or
rejected), and so locked, the page shows a banner (`checklist-locked`): "This review is concluded (approved or
rejected) and locked, so the checklist is read-only." followed by the remark that a project
administrator can reopen the review, and disables the section applicability switch, the reason field and its Save, the
Pass, Fail, and N/A buttons, and the justification field and its Save (the Submit ballot button is not offered). The
answers stay visible. For a member who may only read (the Auditor role) the page shows a `readonly-notice` and treats
the checklist as locked in the same way (`IsLocked` is true when the review is concluded or the viewer is read-only), so
every control is disabled. When the project does not let authors answer the checklist (the default; see
`ProjectService`) and the signed-in user is the author of the review, the page instead shows the notice
`author-answer-blocked` (another project member has to answer; comment threads are still available) and disables the
same controls, unless the review is locked, which shows only the lock banner. Nothing else changes for the author, and
every other member sees no notice.

In a review with several slots the page keeps the answers of the other reviewers from a viewer who may not see them
(see *BallotVisibility*): a reviewer who has not handed in the own ballot, and a member who holds no slot. Such a viewer
sees the line `ballots-handed-in` ("2 of 3 ballots handed in.") and the notice `ballots-hidden-notice` (the answers of
the others stay hidden until the own ballot is handed in), and no standing, no count of disputed checks, and no
different-answers badge; the lead, a project or system administrator, and an Auditor see everything, and so does
everybody once the review is concluded. A review with a single slot shows none of this. A viewer who has handed in the
own ballot sees, under every check that the submitted ballots answer differently, the line `ballot-others-{id}` ("Ann:
Pass / Bob: N/A", in slot order, with the justifications) and a "Different answers" badge, and a **Discuss** button
that opens a small form (`discuss-form-{id}`); starting it creates a discussion thread about that check through
`ThreadService` and shows a link to the review page where the thread is read and answered. A reviewer whose own ballot
is a complete draft in a review with several slots, where nothing but the hand-in blocks the signature, sees **Submit
and sign** as the main action (`submit-and-sign`, with the password field `submit-sign-password`, and the code field
`submit-sign-totp` when the account has a second factor) and **Submit ballot** as the secondary one. The first hands in
the ballot and then signs it for the viewer's slot; when the signature is refused the ballot stays handed in and the
page says so and links to the review page. An account of an identity provider has no password to give, so its button
hands in the ballot and then offers the confirmation at the provider, which leads back to the page. A member who holds
no slot, and who may be a reviewer, sees **Assign me as reviewer** (`assign-me`), which adds a new slot with the user as
primary reviewer and no alternate, through `SigningService.AddSlotAsync`, and never takes the place of
a reviewer who is already there.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the route parameters.

**IsLocked**: whether the review is locked (concluded, or the viewer is read-only); **IsConcluded**: whether the review
is concluded as approved or rejected. It is read from `SigningService.GetApprovalAsync` (`IsConcluded`) when the
page loads, only for a review that has a checklist, and is also set when a save is refused with `ReviewLocked`, so a
page that was loaded before the last signature stops offering the controls at its first refused save. **LockedMarker**:
`true` while locked, written to the `data-locked` attribute of the disabled applicability switch and outcome buttons.

**ControlsDisabled**: `true` while the review is locked, `State.AuthorBlocked` is set, or the displayed state is not the
own ballot (`State.IsOwnBallot` is false: the viewer holds no slot). `State` comes from
`ChecklistService.GetAsync(projectId, reviewId, viewerUsername: ...)` with the user name of the signed-in user, every
time the page reads the checklist; for a slot owner it is the own ballot (`IsOwnBallot`, `BallotState`, `SlotId`), for
every other viewer the consolidated standing of the submitted ballots, or, when the viewer may not see the others yet
(a review with several slots that is not concluded), a state without a standing (`IsMasked`; `NoSlot` is true for a
viewer without a slot). The page never reads the checklist without the viewer: a read without a viewer is the unmasked
system-caller path of `ChecklistService` (`GetForSystemAsync`), which no page uses. `AuthorBlocked` is
computed in the same call; a page loaded before the project switched the rule on shows the refusal message of
the rule (`AuthorCannotAnswer`) in the error line of the check or section, stores nothing, and shows the notice after
the next read. A page loaded before the user lost the slot shows the refusal `NoReviewerSlot` in the same way.

**Loaded**, **Review**, **ProjectName**: whether loading finished, the review (`null` when it does not exist or its
project is not visible to the user), and
the name of the owning project shown in the breadcrumb.

**State**: the `ChecklistState` of the review (`null` when the review has no checklist); it is read again after each
change. **FollowedState**: for a follow-up review, the `ChecklistState` of the review it follows (read with
`ChecklistService.GetAsync` and the viewer, once when the page loads, so it is masked for the viewer in the same way as
the review itself), so a check that failed there can be shown;
`null` otherwise.

**OpenSectionId**: the section whose checks are shown; at most one.

**PendingOutcomes**, **JustificationTexts**, **ItemErrors**: per check, the Fail or N/A choice not yet saved, the
justification typed, and the message of a violated rule. **PendingNotApplicable**, **ReasonTexts**,
**SectionErrors**: the same per section for a "does not apply" decision. All of these are cleared when the route
parameters change. The page also keeps the test id to scroll to after the next render, and the interactive marker.

**Warning**: the pending `PendingSignatureWarning` (`null` when there is none): which control it belongs to, the
affected slots, and the call to repeat when the person confirms. Only one exists at a time. It is cleared when the route
parameters change.

Test identifiers: `checklist-page` (root), `breadcrumb-projects-link`, `project-link`, `review-link`,
`checklist-loading`, `checklist-not-found`, `checklist-missing`, `checklist-review-title`, `checklist-status`,
`checklist-progress`, `next-unanswered-button`, `back-to-review-link`, `checklist-locked`, `author-answer-blocked`,
`no-slot-notice` (a viewer without a slot), `ballot-status` (the state of the own ballot), `submit-ballot` and
`ballot-error` (the hand-in and its refusal), and,
per section,
`section-{id}`, `section-{id}-toggle`, `section-{id}-progress`, `section-{id}-applicable`, `section-{id}-reason`,
`section-{id}-save`, `section-{id}-error`, `section-{id}-unsaved-hint` (shown while the switch is off and the reason is
not saved), `section-{id}-next`, and `section-{id}-back`, and, per check, `check-{item}`,
`check-pass-{item}`, `check-fail-{item}`, `check-na-{item}`, `check-justification-{item}`, `check-save-{item}`,
`check-error-{item}`, and `check-answered-by-{item}`; when a refusal is pending, `signature-warning`,
`signature-warning-slots`, `signature-warning-confirm`, and `signature-warning-cancel` (one banner at a time); for a
follow-up review also `section-{id}-carried` and
`section-{id}-carried-link` (a not-applicable section carried over), `check-carried-{item}` and
`check-carried-link-{item}` (an answer carried over), and `check-earlier-fail-{item}` and
`check-earlier-fail-link-{item}` (a check that failed in the followed review and is not answered here). Section and
check identifiers are written in lower case (`section-req-toggle`,
`check-pass-req-01`). The justification box, its Save button, the reason box and its Save button exist only while
they are needed.

**Ballots**: the `BallotView` of the viewer, read from `ChecklistService.GetBallotViewAsync` whenever the checklist is
read (`IsMasked`, the differing checks with the answers of every slot, the counts of handed-in ballots,
`CanSubmitAndSign`, and `CanAssignMe`). **Capabilities**: the `ThreadCapabilities` of the viewer, which decide whether
Discuss is offered.
**CurrentIsExternal**, **CurrentHasTotp**: the kind of account of the viewer, which decide the entries of Submit and
sign. **DiscussOpen**, **DiscussTexts**, **DiscussErrors**, **DiscussStarted**: the state of the Discuss forms, by check.
**SignNotice**, **SignError**, **AssignError**, **ReauthPending**: the messages and the pending confirmation at the
identity provider of the actions above.

#### Key Methods

**OnParametersSetAsync**: Clears the displayed review, checklist, and everything typed, and sets `Loaded` to false
before the first await, then loads the review, the project name, and the checklist, and opens the first section
with unanswered checks. The router reuses the component when only the route changes, so following a link to
another review never shows the previous one.

**ToggleSection**: Opens a section and closes the open one; choosing the open section closes it.

**GoToNextUnanswered**: Opens the section of the first unanswered check and scrolls to that check; sections just
switched off and not yet saved are skipped, unless they are all that is left, in which case the first of them is
opened and the page scrolls to its reason field. It is used by the button at the top and by the button at the end of
a finished section.

**ChooseAsync**: Handles a click on Pass, Fail, or N/A. Pass is stored at once and needs no justification. Fail and
N/A show a justification box marked as required and wait for Save, so the reviewer is never asked for a reason
before choosing. Choosing the stored outcome again leaves nothing unsaved.

**SaveAsync**: Stores the outcome and justification shown for a check through `ChecklistService.SetAnswerAsync`, on
behalf of the signed-in user, who comes from the authentication state (`SignIn.GetActorAsync`); there is no field in
which a name could be typed. The answer goes into the ballot of the slot of that user, as a draft.

**SubmitBallotAsync**: Handles **Submit ballot**, which is offered only while `State.IsOwnBallot`, the ballot is a
`Draft`, `State.IsComplete` is true, and the controls are not disabled (`CanSubmitBallot`); in a single-slot review an
answer or an edit that completes the ballot has already handed it in, so the button shows there only for a complete
draft that no such answer produced (carried answers only, or no checks). It calls `ChecklistService.SubmitBallotAsync` on
behalf of the signed-in user, with the same warning, retry, and error pattern as an answer (a refusal is shown in
`ballot-error`), and reads the state again, so the status line then says that the ballot is submitted.

**Signature warning**: What the page stores does not always change what the signatures cover. An answer or section
decision saved into a draft ballot is not part of the record yet, so no warning is shown for it. An edit of a ballot
that was submitted returns it to a draft, which takes it out of the consolidation; that changes the digest only when
the ballot made a difference (for example, a dissent that the others did not share, or a section it marked not
applicable). The warning appears
when the call would change the digest while a signature of another slot is current: at **Submit ballot**, and at an
answer or section decision that changes the standing in this way (for example, passing a check
that the reviewer had failed in a submitted ballot, or switching a not-applicable section back to "applies"). The own
slot's signature never needs an acknowledgement. `SaveAsync`, `SubmitBallotAsync` and the applicability saves call the
service without an acknowledgement. When it
refuses with `SignaturesWouldBecomeOutOfDate`, the page keeps the request and shows the `SignatureWarning` component
(`signature-warning`) next to the control that was used: inside the check (after `check-error`) for an answer, under the
ballot status line (after `ballot-error`) for a hand-in, and
inside
the section (after the section error) for a reason saved to make a section not applicable or a switch back to
"applies". The banner names every affected slot and its signer (`signature-warning-slots`, for example "Slot 1 (Rita)").
Nothing is stored; an unsaved Fail, N/A, or reason stays as typed, and a section just switched to "applies" stays shown
as switched until the person decides. **Confirm change** (`signature-warning-confirm`) repeats the call with the
identities of the affected slots as the acknowledgement. When another signature appeared meanwhile, the service
refuses again and the page shows the banner again with the slots of both refusals merged. **Cancel**
(`signature-warning-cancel`) only removes the banner; the switch of a section to "applies" returns to its stored
state, and the typed reason and chosen outcome stay (a ballot that was being handed in stays a draft). The page holds
one banner at a time, and Confirm runs the
session check of the page again. A refusal for any other rule is shown as before.

**SetApplicableAsync** and **SaveNotApplicableAsync**: Switching a section to "applies" is stored at once; switching
it off shows a required reason field, and the section is stored as not applicable when the reason is saved. Until then
the section is still stored as applying, so its progress label reads "Reason needed" (`SectionProgressLabel`), its
badge has the `pending` style (`SectionBadgeClass`), and a hint "Not saved until you press Save reason." is shown
(`section-{id}-unsaved-hint`). A
section that does not apply shows its reason and no checks.

**Carried marks**: In a follow-up review (`Review.FollowsReviewId` is set) an answer that was carried over shows the
badge "Carried from" followed by a link to the review X (`check-carried-{item}`), and a section that was carried over as
not applicable shows "Not applicable, carried from review X" with a link to X (`section-{id}-carried`)
(`ChecklistState.CarriedFromFor`). A check that is not answered here and that failed in the followed review, with the
same wording, shows the earlier Fail and its justification as read-only text, "Failed in review X: justification"
(`EarlierFailFor`), so the reviewer has to decide it again. Nothing is stored for these marks, the earlier answer is
not copied, and carrying never makes the creator the answerer: "Answered by" still names the one who answered.

A violated rule is shown in an alert next to the check (`check-error-{item}`), the section
(`section-{id}-error`) or the ballot (`ballot-error`) and everything typed stays. An answered check
shows "Answered by {display name}, {UTC time}", the display name saved at that time; in the consolidated view (a viewer
without a slot) that is the row of the lowest slot whose submitted ballot gave the agreed answer, and a disputed or
open check shows no answer. The status line and the numbers
come from `ChecklistState` (the own ballot for a slot owner, the consolidated standing otherwise), so the page holds no
counting rules of its own. The outcome buttons are a group of toggle buttons (`aria-pressed`), named by the wording of
the check (`aria-labelledby`), at least 44 pixels
high on a phone, and an unanswered check has a visible marker, so state does not depend on color alone.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection and, when a scroll
target is set, calls the browser function `reviewCenter.scrollToTestId` (defined in `App`) once and clears the
target; a connection that is already gone is ignored.

The checklist is read when the page loads and after each change made on the page. An answer saved in another browser
appears when this page is next loaded or changed; nothing is pushed.

**SubmitAndSignAsync**: Hands in the own ballot with the same checks as the Submit ballot button (including the
confirmation of signatures that would no longer match what was signed after the hand-in; after such a confirmation only
the hand-in is made and the page points to the review page for the signature), and then signs for the viewer's slot with
the password and the code that `SignConfirm` handed over, through `SigningService.SignWithCredentialsAsync`. Before the
hand-in a viewer in a review with several slots has not seen the answers of the others, so the page never signs a record
the viewer has not been shown: after the hand-in it reads the digest of the record for the viewer
(`SigningService.GetApprovalAsync`, which is no longer masked now that the ballot is in), then reloads the page, so the
view the viewer is shown is the record of that digest, and signs that digest only when the reloaded view shows no check
that the submitted ballots answer differently (`Ballots.Differing` is empty). When it shows a difference, or the digest
could not be read unmasked, nothing is signed: the page shows `sign-error` ("Your ballot is in, but you have not
signed: something changed while it was handed in, and the answers of the other reviewers are shown now. Review them,
then sign from the review page."), shows the answers side by side, and the viewer signs from the review page after
reading them, which is the second confirmation. When the record changes between the read of the digest and the
signature, the store refuses the digest (`RecordChanged`), so a changed record is never signed by this step. A refusal
of the signature leaves the ballot handed in and shows `sign-error` with a link to the review page; success shows
`sign-notice`. For an
account of an identity provider the ballot is handed in first and the page then runs the same check on the reloaded
view: when it shows a difference (or is masked) the page shows the same `sign-error` and offers no confirmation at the
provider, because the viewer has just learned of a record they should read before signing; otherwise the page shows
`submit-sign-reauth-hint` with the button that starts the confirmation at the provider. The kind of account is read
before the hand-in, because the reload that follows it no longer offers the combined step.

**AssignMeAsync**: Adds a new reviewer slot with the viewer as primary reviewer through `SigningService.AddSlotAsync`
and reloads; a refusal of the rules is shown as `assign-error`.

**StartDiscussionAsync**: Starts a discussion thread about a check with the typed text through `ThreadService`; an empty
text is refused with `discuss-error-{id}`, and the same signature warning applies as for any other change after which a
signature would no longer match what was signed.

#### Error Handling

A review that does not exist or whose project the user may not see (the same message), and a review without a
checklist, are shown as messages on the page. Checklist rule
violations, and the refusal of an action whose sign-in state has already ended ("You are no longer signed in, so nothing
was saved. Reload the page to sign in again."), are shown as messages next to the control that caused them and never end
the connection. The page raises
no other errors of its own.

**Access re-check**: Every handler that reads or stores something (answer, save, and the not-applicable switch) starts
with `GuardAsync`, which asks `ProjectViewGuard.GuardViewAsync` whether the signed-in user may still see the project;
opening a section and going to the next unanswered check are wrapped at their binding by `GuardedAsync`. When access is
lost, `ShowNoAccess` resets the component state exactly as a change of address does, shows the same message as for a
review that does not exist, and the handler returns without calling a service, so no answer is stored and nothing of the
checklist stays in the page. An ended sign-in state counts as no access, so this message replaces the "no longer signed
in" message on this page.

#### Dependencies

- **SessionGate** (App) — wraps the whole page content, so the page is replaced by a "Session ended" message when the
  sign-in state of the open page ends.
- **IProjectAccess** (Core) — through `ProjectViewGuard`, asked before every handler whether the user may still see the
  project.
- **ReviewService** — reads the review.
- **SignatureWarning** — draws the signature warning banner and holds the pending request (`PendingSignatureWarning`);
  it is a part of this unit.
- **SignConfirm** — draws the password field (and the code field when the account has a second factor) of Submit and
  sign, hands the entries to the page once when the button is pressed and clears the password field; for an account of
  an identity provider it draws no field. It is a part of this unit and is used only by this page (the review page has
  its own signing form).
- **SigningService** — tells whether the review is concluded (`GetApprovalAsync`), which is what locks the page, tells
  whether Submit and sign is offered (`CanSignAfter(record, slotCount, hidden)`, used for `CanSubmitAndSign` of the
  ballot view), signs for Submit and sign (`SignWithCredentialsAsync`), and adds the slot of Assign me
  (`AddSlotAsync`).
- **ThreadService** — starts the discussion about a check and tells which discussions the viewer may start
  (`GetCapabilitiesAsync`).
- **UserService** and **TotpService** — find the account of the viewer and tell whether it signs in at an identity
  provider or has a second factor, so that Submit and sign asks for the right entries.
- **ChecklistService** — reads the checklist (the own ballot for a slot owner, the consolidated standing for a viewer
  who may see it, and a state without a standing for one who may not; and tells, for the signed-in user, whether the
  author rule blocks them,
  `AuthorBlocked`) and records answers, section decisions, and the hand-in of the ballot; it refuses a change of a
  locked review with `ReviewLocked`, the change of the author with `AuthorCannotAnswer`, and an answer or hand-in of a
  user without a slot with `NoReviewerSlot`, and an incomplete hand-in with `BallotIncomplete`; its ballot view
  (`GetBallotViewAsync`) says whether the answers of the others are hidden from the viewer, which checks differ, how
  many ballots are handed in, and whether Assign me and Submit and sign are offered.
- **ProjectService** — reads the project for the signed-in user (nothing is loaded when the user may not see it) and its
  name for the breadcrumb.
- **SignIn** — `GetActorAsync` supplies the signed-in user from the authentication state.
- **ASP.NET Core Blazor components** — the component base class, events, JavaScript interop, and the interactive
  server render mode.
- **App** — defines the scroll function that the page calls.

#### Callers

- **App** — the router renders the page for the checklist route.
- **ReviewPage** — links to the page.
