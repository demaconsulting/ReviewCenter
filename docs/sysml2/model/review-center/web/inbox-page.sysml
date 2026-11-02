package ReviewCenter {
    part def InboxPage {
        doc /* Page at /inbox, for a signed-in user, that lists what waits for the user in seven groups (To answer, Disputed, Ready to sign, Replies waiting, Failed checks, Waiting for others, and Updates), each row with the project, the review, a chip, an optional due or overdue mark, a New marker for what the user has not seen, and a link to the review, with a summary of what needs action and what is an update, an empty state, and a Mark all as read button that stores the read state of every listed review. A visitor without a session sees the session-ended message. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Web/InboxPage.razor and InboxPage.razor.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Web/InboxPageTests.cs */
        comment designRef /* Design: docs/design/review-center/web/inbox-page.md */
        comment verificationRef /* Verification: docs/verification/review-center/web/inbox-page.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/web/inbox-page.yaml */
    }
}
