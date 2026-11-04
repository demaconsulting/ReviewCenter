package ReviewCenter {
    part def NotificationDispatcher {
        doc /* The pass that decides, for each user with a notification mode and a confirmed address, which inbox items are new and mails them once: the inbox is read as the recipient, the backlog of a newly chosen mode is recorded without a mail, the mode Every holds items back for a batch window, the mode Daily sends one summary for each local date, a failed send is retried five times with growing waits and then given up, mails beyond the limits of the server and of the user are deferred, each mail is reserved in the shared send log before it is sent and released when the send fails, the inbox is read only when something may have changed, and the failure of one user does not stop the others. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/NotificationDispatcher.cs, NotificationRecipient.cs, NotificationDispatchState.cs, NotificationCommit.cs, NotifiedItem.cs and NotificationMode.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/NotificationDispatcher*Tests.cs and NotificationDispatcherWorld.cs */
        comment designRef /* Design: docs/design/review-center/core/notification-dispatcher.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/notification-dispatcher.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/notification-dispatcher.yaml */
    }
}
