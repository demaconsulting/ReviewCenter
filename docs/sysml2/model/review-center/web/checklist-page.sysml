package ReviewCenter {
    part def ChecklistPage {
        doc /* Page that shows the checklist of one review: what is left to do (status line, progress, a next-unanswered action), the signed-in reviewer, and the sections with their progress, one open at a time. An open section offers the applies-to-this-review switch (a reason is required when it does not) and one row per check with Pass, Fail, and N/A buttons; Fail and N/A ask for a justification next to the buttons. When the review is approved and locked a notice says so and every control that changes the checklist is disabled. An answer or a change of the applicability of a section that would make a current signature out of date first shows a warning next to the control that names the signers and is stored only when the reviewer confirms. On a follow-up review it marks every carried answer and section state with a Carried from badge that links to the earlier review, and shows the earlier Fail of a check that is open again, read-only. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/ChecklistPage.razor, ChecklistPage.razor.cs, SignatureWarning.razor and SignatureWarning.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/ChecklistPageTests.cs and ChecklistPageFollowUpTests.cs */
        comment designRef /* Design: docs/design/review-center/web/checklist-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/checklist-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/checklist-page.yaml */
    }
}
