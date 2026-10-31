### SecretScan

#### Verification Approach

`SecretScan` is verified with unit tests defined in `SecretScanTests.cs`. Each test builds a small installation in a
temporary folder: a real SQLite database created with the schema of the build, a real Data Protection key ring in a
temporary folder, and the production `CredentialProtector` and `TotpSecretProtector` over that ring, so that the values
are protected and read back exactly as the server does. A second installation stands in for the ring that was lost or
swapped in. Secrets are written with `SecretSeed`, which inserts rows directly. Nothing uses the certificate store,
DPAPI, the home directory, or a real key ring, so the tests are hermetic and leave nothing behind.

#### Test Environment

N/A - standard test environment. The tests run on every platform.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The scan counts the repository tokens and the second-factor secrets of the database, and counts none as unreadable
  when the ring that protected them is the ring that is scanned.
- With the ring of another installation, every secret is unreadable and the ids of the repositories and users are
  reported, ascending; with a mix of secrets only the foreign ones are unreadable.
- A key folder with no key file counts every secret as unreadable and no key file is created.
- Key files are counted by their content: plaintext (`<masterKey`) and in total.
- A database that does not exist is not created and holds no secrets; a database without the tables, or with the
  tables and no rows, holds no secrets and is no error.
- The database and the key folder are unchanged by the scan (content and write time of every file, no new key file).
- The result, as text, holds no secret, no ciphertext, and no path.
- Many ids are shortened in a message, and a null argument is refused.

#### Test Scenarios

**SecretScan_ScanAsync_SecretsOfTheRing_AreCountedReadable**: Two repository tokens and one second-factor secret are
protected by the ring and scanned with it; the counts are two and one, nothing is unreadable, and the ring has one key
file with a plain key. This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_OtherRing_CountsEverySecretUnreadableWithIds**: The database of one installation is scanned with
the ring and protectors of another that has a key of its own; every secret is unreadable and the repository id and user
id are reported. This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_MixedSecrets_CountsOnlyTheUnreadableOnes**: A token of the ring and a token of another ring are
scanned; only the second is unreadable. This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_EmptyRingWithSecrets_CountsThemUnreadableAndCreatesNoKey**: The database is scanned with the
protectors of an installation whose ring was never used; the ring is empty, every secret is unreadable, and no key file
exists afterwards (the scan did not ask the ring). This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_PlaintextKeyFile_IsCountedAndMissingDatabaseIsNotCreated**: A folder with one plain and one
encrypted key file is described as two files of which one is plain, and a database file that does not exist is not
created and has no secrets. This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_NoTablesOrNoRows_HoldsNoSecrets**: A database without the two tables, and one with the tables and
no rows, give zero secrets and no error. This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_AnyRing_LeavesDatabaseAndRingUnchanged**: The database and ring files are hashed and their write
times recorded; scans with the ring of the installation and with an empty ring change none of them and create no key
file. The snapshot leaves out `-wal` and `-shm` side files, which SQLite may leave empty after a read-only connection
to a database in write-ahead log mode; the database file itself is what must not change. This scenario is tested by
the test of the same name.

**SecretScan_ScanAsync_Result_HoldsNoSecretCiphertextOrPath**: The text of the result holds neither the token, its
ciphertext, nor the key folder. This scenario is tested by the test of the same name.

**SecretScan_FormatIds_ManyIds_ShowsTenAndTheCountOfTheOthers**: Two ids are listed; twelve are shortened to ten and
"and 2 more". This scenario is tested by the test of the same name.

**SecretScan_ScanAsync_NullArgument_ThrowsArgumentNullException**: Each argument that is null is refused. This scenario
is tested by the test of the same name.
