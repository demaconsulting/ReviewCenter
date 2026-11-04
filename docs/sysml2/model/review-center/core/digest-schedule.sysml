package ReviewCenter {
    part def DigestSchedule {
        doc /* The rule of when the summary of the day of a user is due: once for each local date of the time zone of the server from the digest hour on, also after a downtime and on the days of a clock change, with a slot that is used up when it is handled. */

        comment sourceRef /* Source: src/DemaConsulting.ReviewCenter.Core/DigestSchedule.cs */
        comment testRef /* Test: test/DemaConsulting.ReviewCenter.Tests/Core/DigestScheduleTests.cs */
        comment designRef /* Design: docs/design/review-center/core/digest-schedule.md */
        comment verificationRef /* Verification: docs/verification/review-center/core/digest-schedule.md */
        comment reqRef /* Requirements: docs/reqstream/review-center/core/digest-schedule.yaml */
    }
}
