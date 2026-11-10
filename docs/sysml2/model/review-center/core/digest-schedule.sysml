package ReviewCenter {
    private import SoftwareItemMetadata::*;

    part def DigestSchedule {
        doc /* The rule of when the summary of the day of a user is due: once for each local date of the time zone of the server from the digest hour on, also after a downtime and on the days of a clock change, with a slot that is used up when it is handled. */

        metadata info : SoftwareInfo {
            description = "The rule of when the summary of the day of a user is due: once for each local date of the time zone of the server from the digest hour on, also after a downtime and on the days of a clock change, with a slot that is used up when it is handled.";
            sourceFiles = ("src/DemaConsulting.ReviewCenter.Core/DigestSchedule.cs");
            testFiles = ("test/DemaConsulting.ReviewCenter.Tests/Core/DigestScheduleTests.cs");
            designFiles = ("docs/design/review-center/core/digest-schedule.md");
            verificationFiles = ("docs/verification/review-center/core/digest-schedule.md");
            requirementFiles = ("docs/reqstream/review-center/core/digest-schedule.yaml");
        }
    }
}
