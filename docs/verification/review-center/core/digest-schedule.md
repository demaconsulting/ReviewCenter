### DigestSchedule

#### Verification Approach

`DigestSchedule` is verified with unit tests defined in `DigestScheduleTests.cs`. The unit is a pure function of the
instants and the time zone, so the tests call it directly with fixed instants. Three kinds of zone are used: UTC, a zone
that is three hours ahead of UTC, and a zone with a daylight saving rule that is built in the test (so the result does
not depend on the zones of the machine). The days on which the clock changes are tested with the instants just before
and just after the change. No stand-in and no database are needed.

#### Test Environment

N/A - standard test environment (no special fixtures). The zones are made in the test with
`TimeZoneInfo.CreateCustomTimeZone`.

#### Acceptance Criteria

- All tests pass with zero failures.
- The slot is not due before the digest hour and is due at the hour and after it, when the local date is not handled.
- The slot is not due on a local date that is handled, and is due on a later local date and when no slot was handled.
- The local dates of the zone decide, not the dates in UTC.
- The digest hour 0 is accepted.
- On a day on which the clock skips the digest hour the slot is due after the gap, and on a day on which the clock
  repeats the hour the slot is due once.
- The local date of an instant is the date in the zone.
- A digest hour of 24 and a missing zone are refused.

#### Test Scenarios

**Before the hour**: The slot is not due while the local hour is before the digest hour. This scenario is tested by
`DigestSchedule_IsDue_BeforeHour_False`.

**At and after the hour**: The slot is due at the digest hour and after it. These scenarios are tested by
`DigestSchedule_IsDue_AtHour_True` and `DigestSchedule_IsDue_AfterHour_True`.

**Handled slots**: The slot is not due when it was handled on the same local date, is due when it was handled on the
day before, when it was never handled, and when it was handled several days ago. These scenarios are tested by
`DigestSchedule_IsDue_HandledToday_False`, `DigestSchedule_IsDue_HandledYesterday_True`,
`DigestSchedule_IsDue_NeverHandled_True`, and `DigestSchedule_IsDue_HandledDaysAgo_True`.

**Local date**: In a zone that is ahead of UTC the local date decides, and the local date of an instant is converted to
the zone. These scenarios are tested by `DigestSchedule_IsDue_ZoneAheadOfUtc_UsesLocalDate` and
`DigestSchedule_LocalDate_Instant_ConvertsToZone`.

**Hour zero**: The digest hour 0 is accepted, and with it the slot is due just after local midnight. These scenarios are
tested by `DigestSchedule_Constructor_Hour0_Accepted` and `DigestSchedule_IsDue_HourZero_DueAfterMidnight`.

**Clock changes**: When the clock goes forward over the digest hour the slot is not due before the gap and is due at the
first instant after it. When the clock goes back and repeats the digest hour the slot is due in the first pass and is
not due in the repeated hour after it was handled. These scenarios are tested by
`DigestSchedule_IsDue_SpringForwardGap_DueAfterGap` and `DigestSchedule_IsDue_FallBackRepeat_OnceAfterHandled`.

**Refused arguments**: A digest hour of 24 raises `ArgumentOutOfRangeException`, and a missing zone raises
`ArgumentNullException`. These scenarios are tested by `DigestSchedule_Constructor_Hour24_Throws` and
`DigestSchedule_Constructor_NullZone_Throws`.
