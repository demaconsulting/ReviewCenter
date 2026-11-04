package ReviewCenter {
    part def NotificationWorker {
        doc /* The background loop that runs the notification pass of the dispatcher at the polling interval while the server runs: it stops without delay, goes on after a failing pass, logs a pass only with counts and the type name of a failure and never an address or a message text, and refuses missing arguments and a polling interval that is not positive. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter/Messaging/NotificationWorker.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Messaging/NotificationWorkerTests.cs and StepClock.cs */
        comment designRef /* Design: docs/design/review-center/messaging/notification-worker.md */
        comment verificationRef /* Verification: docs/verification/review-center/messaging/notification-worker.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/messaging/notification-worker.yaml */
    }
}
