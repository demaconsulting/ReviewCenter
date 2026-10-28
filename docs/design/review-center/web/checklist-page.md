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
a signed-in user, and every answer and section decision is saved on behalf of that user.

#### Data Model

The page instance is created per browser connection, so its state is per user.

**ProjectId**, **ReviewId**: the route parameters.

**Loaded**, **Review**, **ProjectName**: whether loading finished, the review (`null` when it does not exist or its
project is not visible to the user), and
the name of the owning project shown in the breadcrumb.

**State**: the `ChecklistState` of the review (`null` when the review has no checklist); it is read again after each
change.

**OpenSectionId**: the section whose checks are shown; at most one.

**PendingOutcomes**, **JustificationTexts**, **ItemErrors**: per check, the Fail or N/A choice not yet saved, the
justification typed, and the message of a violated rule. **PendingNotApplicable**, **ReasonTexts**,
**SectionErrors**: the same per section for a "does not apply" decision. All of these are cleared when the route
parameters change. The page also keeps the test id to scroll to after the next render, and the interactive marker.

Test identifiers: `checklist-page` (root), `breadcrumb-projects-link`, `project-link`, `review-link`,
`checklist-loading`, `checklist-not-found`, `checklist-missing`, `checklist-review-title`, `checklist-status`,
`checklist-progress`, `next-unanswered-button`, `back-to-review-link`, and, per section,
`section-{id}`, `section-{id}-toggle`, `section-{id}-progress`, `section-{id}-applicable`, `section-{id}-reason`,
`section-{id}-save`, `section-{id}-error`, `section-{id}-next`, and `section-{id}-back`, and, per check, `check-{item}`,
`check-pass-{item}`, `check-fail-{item}`, `check-na-{item}`, `check-justification-{item}`, `check-save-{item}`,
`check-error-{item}`, and `check-answered-by-{item}`. Section and check identifiers are written in lower case (`section-req-toggle`,
`check-pass-req-01`). The justification box, its Save button, the reason box and its Save button exist only while
they are needed.

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
which a name could be typed.

**SetApplicableAsync** and **SaveNotApplicableAsync**: Switching a section to "applies" is stored at once; switching
it off shows a required reason field, and the section is stored as not applicable when the reason is saved. A
section that does not apply shows its reason and no checks.

A violated rule is shown in an alert next to the check (`check-error-{item}`) or the section
(`section-{id}-error`) and everything typed stays. An answered check
shows "Answered by {display name}, {UTC time}", the display name saved at that time. The status line and the numbers
come from `ChecklistState`, so the page holds no
counting rules of its own. The outcome buttons are a group of toggle buttons (`aria-pressed`), named by the wording of
the check (`aria-labelledby`), at least 44 pixels
high on a phone, and an unanswered check has a visible marker, so state does not depend on color alone.

**OnAfterRenderAsync**: Marks the page interactive after the first render on a live connection and, when a scroll
target is set, calls the browser function `reviewCenter.scrollToTestId` (defined in `App`) once and clears the
target; a connection that is already gone is ignored.

The checklist is read when the page loads and after each change made on the page. An answer saved in another browser
appears when this page is next loaded or changed; nothing is pushed.

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
- **ChecklistService** — reads the checklist and records answers and section decisions.
- **ProjectService** — reads the project for the signed-in user (nothing is loaded when the user may not see it) and its
  name for the breadcrumb.
- **SignIn** — `GetActorAsync` supplies the signed-in user from the authentication state.
- **ASP.NET Core Blazor components** — the component base class, events, JavaScript interop, and the interactive
  server render mode.
- **App** — defines the scroll function that the page calls.

#### Callers

- **App** — the router renders the page for the checklist route.
- **ReviewPage** — links to the page.
