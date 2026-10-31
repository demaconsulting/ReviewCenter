### KeysCommand

#### Verification Approach

`KeysCommand` is verified with unit tests defined in `KeysCommandTests.cs`. The tests run the real command, which builds
the same services as the server through `ServerHost.AddServices`, over a temporary SQLite database and a temporary key
folder selected through the `configure` hook. A test helper installs a key ring and a database in the temporary folder
with the production protectors and stores repository tokens and second-factor secrets through them; a second ring
produces secrets that the first ring cannot read. The tests read the result from the log file of the context, the exit
code from the context, the key files from the folder, and the stored values from the database, and then start the
production services again over the same folder to prove that the secrets are still readable. The dispatch from
`Program` to the command and the run of the verb in a separate process are **not** covered by a test; the flag
handling of the verb and the help text are verified by the `Cli` subsystem tests.

#### Test Environment

N/A - standard test environment (temporary SQLite databases and key folders are created by the test helper; no machine
key store is used because the key files are not encrypted).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- After a run every secret is still readable by freshly started production services, one new key file exists, every
  stored payload references the new key and none the old one, and the old key is reported as not referenced.
- A second run succeeds, creates another key, and leaves every secret readable.
- An unreadable secret, a ring without a key although secrets are stored, and a missing database each make the command
  refuse with exit code 1, a message without a secret, and no change at all.
- The output holds no secret value and no ciphertext.
- A database without secrets succeeds with zero counts.
- A null context is rejected with an `ArgumentNullException`.
- A failure while re-protecting (the second table cannot be written) makes the command stop with exit code 1 and a
  message that says that a new key was created, how many secrets of the completed table were re-protected, which
  table is unchanged, and that the command can be run again, without a secret.
- Key ring settings that cannot be used make the command refuse with exit code 1, no key and no change.

#### Test Scenarios

**KeysCommand_Run_SecretsOfTheRing_AreStillReadableAfterwards**: Two repository tokens and one second-factor secret
are stored; the command succeeds, reports the counts, and restarted production services decrypt every value. This
scenario is tested by `KeysCommand_Run_SecretsOfTheRing_AreStillReadableAfterwards`.

**KeysCommand_Run_NewKey_IsTheOnlyOneReferencedAndOldKeyIsReported**: After the run the key folder holds a second key
file, the payload headers of the stored values name the new key only, and the output names the new key and lists the
old key as not referenced. This scenario is tested by
`KeysCommand_Run_NewKey_IsTheOnlyOneReferencedAndOldKeyIsReported`.

**KeysCommand_Run_SecondRun_IsIdempotentAndGivesAnotherKey**: The command is run twice; both runs succeed with the same
counts, the folder holds three key files, the new key ids differ, and everything is readable. This scenario is tested
by `KeysCommand_Run_SecondRun_IsIdempotentAndGivesAnotherKey`.

**KeysCommand_Run_UnreadableSecret_RefusesAndChangesNothing**: One token was protected by another ring; the command
refuses with exit code 1 and names the repository id, no key file is added, and no stored value changes. This scenario
is tested by `KeysCommand_Run_UnreadableSecret_RefusesAndChangesNothing`.

**KeysCommand_Run_EmptyRingWithSecrets_RefusesAndCreatesNoKey**: The key folder is empty but the database holds
secrets; the command refuses and creates no key. This scenario is tested by
`KeysCommand_Run_EmptyRingWithSecrets_RefusesAndCreatesNoKey`.

**KeysCommand_Run_SecondTableFails_ReportsNewKeyAndCompletedTable**: A database trigger makes every update of the
second-factor table fail, which is deterministic and needs no timing. The repository table is committed, the command
stops with exit code 1, and the message says that a new key was created, that one repository token was
re-protected, that the second-factor table is unchanged and that the command can be run again; it holds no secret
and not the refusal wording "nothing was changed"; the stored second-factor value is as before, and the token is
readable with the new key. This scenario is tested by `KeysCommand_Run_SecondTableFails_ReportsNewKeyAndCompletedTable`.

**KeysCommand_Run_KeyRingSettingsProblem_RefusesAndCreatesNoKey**: The settings require encrypted keys while the
protection is permissions only, so the key ring plan has a problem; the command refuses with exit code 1, names the
setting, creates no key and changes no stored value. This scenario is tested by
`KeysCommand_Run_KeyRingSettingsProblem_RefusesAndCreatesNoKey`.

**KeysCommand_Run_Report_HoldsNoSecretValue**: The output of a success holds neither the token text, nor the
second-factor secret, nor any stored ciphertext, old or new; the refusal of an unreadable secret holds no plaintext or
ciphertext either (see the scenario above). This scenario is tested by `KeysCommand_Run_Report_HoldsNoSecretValue`.

**KeysCommand_Run_NoSecrets_SucceedsWithZeroCounts**: A database without secrets succeeds and reports zero counts.
This scenario is tested by `KeysCommand_Run_NoSecrets_SucceedsWithZeroCounts`.

**KeysCommand_Run_MissingKeyFolder_IsCreatedWithOwnerOnlyAccess**: The key folder does not exist. The command
succeeds, the folder holds one key file, and the folder has the owner-only access that the server sets at start-up:
mode 700 on Linux and macOS, access rules without inheritance on Windows. This scenario is tested by
`KeysCommand_Run_MissingKeyFolder_IsCreatedWithOwnerOnlyAccess`.

**KeysCommand_Run_MissingDatabase_RefusesAndCreatesNothing**: The database file does not exist; the command refuses
with exit code 1 and creates neither the database nor a key. This scenario is tested by
`KeysCommand_Run_MissingDatabase_RefusesAndCreatesNothing`.

**KeysCommand_ReadKeyId_Payload_GivesKeyIdOrNull**: The key id of a protected token and of a second-factor secret is
read and matches the key of the ring; text that is not a Data Protection payload gives `null`. This scenario is tested
by `KeysCommand_ReadKeyId_Payload_GivesKeyIdOrNull`.

**KeysCommand_Run_NullContext_ThrowsArgumentNullException**: `Run` is called with a null context; an
`ArgumentNullException` is thrown. This scenario is tested by `KeysCommand_Run_NullContext_ThrowsArgumentNullException`.
