### AuditCommand

#### Verification Approach

`AuditCommand` is verified with unit tests defined in `AuditCommandTests.cs`. The tests run the real command, which
builds the same services as the server through `ServerHost.AddServices`, over a temporary SQLite database selected
through the `configure` hook (created by the test before the command runs, because the command never creates one),
with a real `Context` created from the `audit verify` arguments. The trail is written
through the real stores, and for the broken cases a row is edited or deleted over a second database connection. Everything
the command writes is read back from the log file of the context, and the exit code from the context. The dispatch
from `Program` to the command and the run of the verb in a separate process are **not** covered by a test: `Program`
is verified only for the help text, and no system-level test starts `reviewcenter audit verify`. The flag handling of
the verb is verified by the `Context` unit.

#### Test Environment

N/A - standard test environment (temporary SQLite databases are created by the test helper).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- An intact trail is reported as "Audit chain intact: N entries" with exit code 0, an empty trail as intact with zero
  entries.
- An edited entry is reported by its sequence and a deleted entry as a broken chain, each with exit code 1.
- A database file that does not exist is reported as an error with exit code 1 and nothing is created; a database that
  cannot be read is reported as an error with exit code 1.
- A null context is rejected with an `ArgumentNullException`.

#### Test Scenarios

**AuditCommand_Run_IntactTrail_ReportsEntryCountWithExitCodeZero**: A trail with several entries is verified; the output
says how many entries are intact and the exit code is 0. This scenario is tested by
`AuditCommand_Run_IntactTrail_ReportsEntryCountWithExitCodeZero`.

**AuditCommand_Run_EmptyTrail_ReportsIntactWithZeroEntries**: An existing database with the schema and no audit entries
is verified; the output says zero entries and the exit code is 0. This scenario is tested by
`AuditCommand_Run_EmptyTrail_ReportsIntactWithZeroEntries`.

**AuditCommand_Run_TamperedEntry_ReportsFirstBadEntryWithExitCodeOne**: An entry is edited; the output names that entry
and the exit code is 1. This scenario is tested by
`AuditCommand_Run_TamperedEntry_ReportsFirstBadEntryWithExitCodeOne`.

**AuditCommand_Run_DeletedEntry_ReportsBrokenChainWithExitCodeOne**: An entry in the middle is deleted; the output says
the chain is broken and the exit code is 1. This scenario is tested by
`AuditCommand_Run_DeletedEntry_ReportsBrokenChainWithExitCodeOne`.

**AuditCommand_Run_MissingDatabase_ReportsErrorAndCreatesNothing**: The configured database file does not exist, in a
folder that does not exist either; the output says that the database file does not exist and the exit code is 1.
Neither the file nor the folder is created. This scenario is tested by
`AuditCommand_Run_MissingDatabase_ReportsErrorAndCreatesNothing`.

**AuditCommand_Run_UnreadableDatabase_ReportsErrorWithExitCodeOne**: The database file exists but is not a database (it
holds text); the output says the trail could not be read and the exit code is 1. This scenario is tested by
`AuditCommand_Run_UnreadableDatabase_ReportsErrorWithExitCodeOne`.

**AuditCommand_Run_NullContext_ThrowsArgumentNullException**: `Run` is called with a null context; an
`ArgumentNullException` is thrown. This scenario is tested by `AuditCommand_Run_NullContext_ThrowsArgumentNullException`.
