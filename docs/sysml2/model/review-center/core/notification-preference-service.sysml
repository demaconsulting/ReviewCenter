package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def NotificationPreferenceService {
        doc /* The rules of the optional e-mail address for notifications: an address is confirmed only by typing back a six digit code that was mailed to it, with a lifetime of the code, a pause before a new code, a limit on wrong codes, limits on the mails for each user, each address, and the whole server, mail only to a confirmed address, refusal for actors with an API token, a feature that is off, disabled or unknown users, and users with a temporary password, only a salted hash of the code stored, the code in the confirmation mail only, and every change in the audit trail without the address and the code, and the choice of a notification mode (none by default, a daily summary, or every change) that needs a confirmed address, is audited without the address, and starts after a baseline. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/NotificationPreferenceService.cs", "src/DemaConsulting.ReviewCenter.Core/NotificationSettings.cs", "src/DemaConsulting.ReviewCenter.Core/NotificationPreference.cs", "src/DemaConsulting.ReviewCenter.Core/NotificationSendLogEntry.cs", "src/DemaConsulting.ReviewCenter.Core/INotificationStore.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/NotificationPreferenceServiceTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/FakeNotificationStore.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/NotificationPreferenceServiceModeTests.cs", "test/DemaConsulting.ReviewCenter.Tests/Core/FakeNotificationSender.cs");
            designFiles = ("docs/design/review-center/core/notification-preference-service.md");
            verificationFiles = ("docs/verification/review-center/core/notification-preference-service.md");
            requirementFiles = ("docs/reqstream/review-center/core/notification-preference-service.yaml");
        }
    }
}
