package ReviewCenter {
    part def NotificationPreferenceService {
        doc /* The rules of the optional e-mail address for notifications: an address is confirmed only by typing back a six digit code that was mailed to it, with a lifetime of the code, a pause before a new code, a limit on wrong codes, limits on the mails for each user, each address, and the whole server, mail only to a confirmed address, refusal for actors with an API token, a feature that is off, disabled or unknown users, and users with a temporary password, only a salted hash of the code stored, the code in the confirmation mail only, and every change in the audit trail without the address and the code. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/NotificationPreferenceService.cs, NotificationSettings.cs, NotificationPreference.cs, NotificationSendLogEntry.cs and INotificationStore.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/NotificationPreferenceServiceTests.cs with the support classes FakeNotificationSender.cs and FakeNotificationStore.cs */
        comment designRef /* Design: docs/design/review-center/core/notification-preference-service.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/notification-preference-service.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/notification-preference-service.yaml */
    }
}
