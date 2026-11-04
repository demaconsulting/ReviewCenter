package ReviewCenter {
    part def NotificationComposer {
        doc /* The composition of one notification mail from the inbox items that are new for a user: the choice of the new items by their state, the plain-text and HTML body with the group, title, review, and state of each item and a link to the review through the configured address of the server, a fixed subject for an update and for the daily summary, a limit on the number of listed items and on the size of the mail, and no detail text, outcome, or text of a comment. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/NotificationComposer.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/NotificationComposerTests.cs */
        comment designRef /* Design: docs/design/review-center/core/notification-composer.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/notification-composer.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/notification-composer.yaml */
    }
}
