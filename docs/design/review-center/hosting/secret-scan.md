### SecretScan

![Hosting Structure](HostingView.svg)

#### Purpose

`SecretScan` tells, without changing anything, whether the secrets that the database stores can still be decrypted
with the key ring that is configured. A database that is restored next to a missing or wrong key ring looks healthy
until somebody uses a repository token or signs in with the second factor, and worse, Data Protection answers a ring
without a key by silently creating a new one, after which every stored secret is lost for good. Its single
responsibility is to count the key files of the ring and to decrypt every stored repository token and second-factor
secret with the production protectors, so that the server (before it starts) and the self-validation (on request) can
say what is wrong, and never to change anything while doing so.

#### Data Model

`SecretScan` is an `internal static` class without state, so it is thread-safe. Its result is the immutable record
`SecretScanResult`:

- `KeyFiles` and `PlaintextKeyFiles` — the number of `key-*.xml` files in the key folder and how many of them hold a
  plain `<masterKey`.
- `RepositoryTokens` and `UnreadableRepositoryTokens` — the number of stored repository tokens
  (`Repositories.EncryptedToken`) and how many cannot be decrypted.
- `TotpSecrets` and `UnreadableTotpSecrets` — the same for the second-factor secrets (`UserTotps.ProtectedSecret`).
- `UnreadableRepositoryIds` and `UnreadableUserIds` — the ids of the rows that cannot be decrypted, ascending.
- `ProtectedSecrets`, `UnreadableSecrets`, and `RingIsEmpty` — derived: the sums of both kinds, and whether the
  folder holds no key file.

The record holds counts and row ids only. It never holds a secret, a ciphertext, or a path.

#### Key Methods

**ScanAsync**: Scans the key ring and the stored secrets.

- *Parameters*: the key folder; the `DatabaseOptions`; the production `ICredentialProtector` and
  `ITotpSecretProtector`; a cancellation token.
- *Returns*: `SecretScanResult`.
- *Preconditions*: No argument is null.
- *Postconditions*: Nothing in the database or in the key folder was created, written, or changed, and neither
  protector's `Protect` was called, so no key was created.

Steps: (1) describe the ring with `KeyRingProtection.InspectKeyFiles`; (2) when the database file exists, open it
read-only and unpooled (`DatabaseSetup.ResolveReadOnlyConnectionString`, the same pattern as the check of the
configured database) and read the id and the protected text of every row that has a value, for each of the two tables;
a missing table or column means no secrets of that kind, and a missing file means no secrets at all; (3) close the
database; (4) decrypt each value with the protector of its kind and drop the plaintext at once; a value that raises a
`DomainRuleException`, `CryptographicException`, or `FormatException` is counted as unreadable and its row id is kept.

When the folder holds no key file, step 4 does not ask the protectors: Data Protection would create the first key
for the question, so every secret is counted as unreadable instead. A caller that must not create keys under any
circumstance (the self-validation) also switches automatic key generation off for its services.

**FormatIds**: Formats the ids of unreadable rows for a message: the first ten, followed by "and N more" when there
are more.

#### Error Handling

An unreadable secret is the finding, not an error, so it is counted and the message of the exception is not used. An
argument that is null raises `ArgumentNullException`; a database configuration that cannot be opened read-only (an
in-memory database) raises `InvalidOperationException`; a database file that exists but cannot be read raises the
`SqliteException`, which the callers report without the connection string. Nothing is logged by the unit, and no
plaintext, ciphertext, or key ever leaves it.

Known limitation: SQLite may leave empty `-wal` and `-shm` side files beside a database in write-ahead log mode after
a read-only connection is closed. The database file itself and the key folder are unchanged; the check of the
configured database behaves the same.

#### Dependencies

- **KeyRingProtection** — `InspectKeyFiles` describes the ring.
- **DatabaseSetup** (Storage) — says whether the file exists and gives the read-only connection string.
- **ICredentialProtector** and **ITotpSecretProtector** (Core) — decrypt the values; their production
  implementations (`CredentialProtector`, `TotpSecretProtector`) are passed in by the callers.
- **Microsoft.Data.Sqlite** — the read-only connection.

#### Callers

- **ServerHost** — `ValidateDataProtection` (the lost-ring guard) calls `ScanAsync` after `CheckRing` and before the
  probe round trip, for the `serve` verb only.
- **ConfiguredKeysCheck** (SelfTest) — calls `ScanAsync` and `FormatIds` for the check `ReviewCenter_ConfiguredKeys`.
