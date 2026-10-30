package ReviewCenter {
    part def ChecklistPage {
        doc /* Page that shows the checklist of one review: what is left to do (status line, progress, a next-unanswered action), the signed-in reviewer, and the sections with their progress, one open at a time. An open section offers the applies-to-this-review switch (a reason is required when it does not) and one row per check with Pass, Fail, and N/A buttons; Fail and N/A ask for a justification next to the buttons. When the review is approved and locked a notice says so and every control that changes the checklist is disabled. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/ChecklistPage.razor and ChecklistPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/ChecklistPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/checklist-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/checklist-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/checklist-page.yaml */
    }
}
