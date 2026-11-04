### DigestSchedule

![Core Structure](CoreView.svg)

#### Purpose

`DigestSchedule` answers one question: is the slot of the daily summary of a user due now? The slot of a local date
opens at the digest hour of a configured time zone and is used once. The unit makes the rule explicit and testable,
including the days on which the local clock jumps, so the dispatcher needs no date arithmetic of its own.

#### Data Model

The schedule is immutable. It holds the digest hour (0 to 23) and the time zone (`TimeZoneInfo`). It keeps no state:
what was handled is passed in by the caller, as the instant at which the last slot of the user was handled.

#### Key Methods

**IsDue**: Tells whether the slot is due.

- *Parameters*: The current instant in UTC, and the instant in UTC at which the last slot was handled (null when no
  slot was ever handled).
- *Returns*: True when the local hour of the current instant is at least the digest hour and the local date of the last
  handled instant is before the local date of the current instant, or no slot was handled. Otherwise false.
- *Postconditions*: A pass that runs late, for example after a downtime, finds the slot due as soon as it runs on a
  local date that is not handled. A local date that already has a handled slot is never due again, whatever the hour.
  A digest hour that a clock change skips (the clock goes forward over it) is due at the first local time after the gap,
  because the local hour is then past the digest hour. A digest hour that a clock change repeats (the clock goes back)
  is due once: after the slot was handled the repeated hour is on the same local date and is not due again.

**LocalDate**: Gives the local date of an instant.

- *Parameters*: An instant in UTC.
- *Returns*: The date of that instant in the time zone of the schedule. The dispatcher uses it for the rule that a user
  who switches the daily summary on gets the first summary on the next local date.

#### Error Handling

The constructor raises `ArgumentOutOfRangeException` for a digest hour outside the range 0 to 23, and
`ArgumentNullException` for a missing time zone. The methods raise nothing. The unit does not log.

#### Dependencies

- **TimeZoneInfo** — converts an instant to the local time, with the rules of the clock changes of the zone.

#### Callers

- **NotificationDispatcher** — asks whether the slot of a user in the mode Daily is due, and takes the local date.
- **NotificationRegistration** — makes the schedule from the settings `DigestHour` and `TimeZone`.
