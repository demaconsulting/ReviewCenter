package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def NotificationWorker {
        doc /* The background loop that runs the notification pass of the dispatcher at the polling interval while the server runs: it stops without delay, goes on after a failing pass, logs a pass only with counts and the type name of a failure and never an address or a message text, and refuses missing arguments and a polling interval that is not positive. */

        metadata info : SoftwareInfo {
            description = "The background loop that runs the notification pass of the dispatcher at the polling interval while the server runs: it stops without delay, goes on after a failing pass, logs a pass only with counts and the type name of a failure and never an address or a message text, and refuses missing arguments and a polling interval that is not positive.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter/Messaging/NotificationWorker.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Messaging/NotificationWorkerTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Messaging/StepClock.cs");
            designFiles = ("docs/design/review-center/messaging/notification-worker.md");
            verificationFiles = ("docs/verification/review-center/messaging/notification-worker.md");
            requirementFiles = ("docs/reqstream/review-center/messaging/notification-worker.yaml");
        }
    }
}
