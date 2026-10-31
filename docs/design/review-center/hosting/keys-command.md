### KeysCommand

![Hosting Structure](HostingView.svg)

#### Purpose

`KeysCommand` is the `keys reprotect` verb. Its single responsibility is to move every stored secret to a new Data
Protection key, so that an operator who has changed the protection of the key ring (a plaintext ring that is to be
protected, a new certificate) can retire the old key files and the old certificate. It is the only verb that writes to
the key ring and the database. It must be run with the server stopped.

#### Data Model

`KeysCommand` is a stateless static class, so it is thread-safe. It has no input other than the configuration, which
is the one the server reads (`appsettings.json` and environment variables): the key ring and the database it works on
are the ones the server uses.

**Output** (through `Context.WriteLine`, exit code 0): the new key id (`New Data Protection key: {guid}`), the counts
(`Re-protected N repository token(s) and M second-factor secret(s) with the new key.`), either that no stored secret
references an older key or, for each other key id that a stored payload still references, how many tokens and secrets
do, the keys of the ring that no stored secret references, the number of key files of the ring that are not
encrypted, and the next steps for the operator. Key ids are GUIDs. A secret, a ciphertext, and a certificate password
are never printed or logged, and the only path that can appear is the key folder.

**Refusal** (through `Context.WriteError`, exit code 1): `Refusing to re-protect, nothing was changed: {reason}`. The
reasons are unusable key ring settings, a ring that cannot be read, secrets that cannot be read (their number and the
ids of the repositories and users, as the scan reports them), and a database file that does not exist.

#### Key Methods

**Run**: Rotates the key and re-protects the secrets as described by the command-line context.

- *Parameters*: `Context context` — receives the results and the errors; `Action<ConfigurationManager>? configure` —
  adjusts the configuration before the services are built (tests use it to select a temporary database and key folder).
- *Returns*: `void`.
- *Preconditions*: `context` is not null.
- *Postconditions*: On success a new key file exists, every repository token and second-factor secret is protected with
  the newest key, and the report is written. On a refusal nothing at all has changed.

The command builds its configuration and services the same way the server does (`ServerHost.AddServices`), with the
host logging switched off, and with `KeyManagementOptions.AutoGenerateKeys` switched off, so that Data Protection can
not create a key as a side effect of a refusal. The sequence is:

1. **Settings.** Unusable key ring settings (`KeyRingPlan.Problem`) and a database file that does not exist are
   refused.
2. **Scan.** `KeyRingProtection.CheckRing` checks the ring (a relaxed copy of the plan, in which unencrypted key files
   do not refuse, because retiring them is what this command is for), then `SecretScan.ScanAsync` decrypts every
   secret with the production protectors. When any secret cannot be read, or the ring has no usable key, the command
   refuses: re-protecting cannot rescue what is unreadable.
3. **New key.** `IKeyManager.CreateNewKey` creates a key that is active now. The key manager applies the protection
   that the configuration chose (DPAPI, a certificate, or the permissions of the folder), so the new key file is
   protected as configured.
4. **Services again.** A Data Protection key ring that was read before the new key existed is refreshed in the
   background and may protect with the old key for a moment. The command therefore builds a second set of the
   production services, which reads the ring from the folder and uses the newest key at once.
5. **Re-protect.** For each table (`Repositories.EncryptedToken`, `UserTotps.ProtectedSecret`) one transaction reads
   the rows, decrypts each value with the production protector, protects it again, and replaces it with an update that
   is guarded by the old value (`WHERE Id = ... AND value = old`). A row that no longer matches (the server was not
   stopped) fails the table, which is rolled back. The database is opened read-write, never creating, without pooling.
6. **Report.** The payload headers of the stored values are read (a magic number `0x09F0C9F0` and the 16-byte key id;
   diagnostic only) to say which keys are still referenced, and the keys of the ring that nothing references are
   listed.

**ReadKeyId**: Reads the key id from the header of a stored payload (Base64Url for repository tokens, Base64 for
second-factor secrets), or returns `null` when the text is no Data Protection payload. It decides nothing about a
secret.

#### Error Handling

A refusal and a failure are reported with `Context.WriteError`, exit code 1, with sanitized messages: a message holds
counts, ids, and the key folder, never a secret, a ciphertext, or a certificate password. A cryptographic failure is
reported with a fixed message that points at the protection settings. Once the new key exists, every failure of the
re-protecting (a secret that cannot be read, a changed row, a locked database, a full disk, a file or cryptographic
error) is reported in one message: it says that a new key was created and its id, the counts of the tables that were
completed before, which table was being changed and is unchanged (a table is one transaction, so it is rolled
back), and that the command can be run again: a second run is fine, it creates another key and protects the secrets
with it again. The text of a database or file error is part of the message; the text of a cryptographic failure never
is. A null context is rejected with `ArgumentNullException`.

#### Dependencies

- **Context** — receives the results and errors.
- **ServerHost** — `AddServices` registers the key ring protection, the protectors, and the database settings;
  `ResolveKeysPath` gives the key folder.
- **KeyRingProtection** — `CheckRing` checks the ring; `InspectKeyFiles` counts the unencrypted key files.
- **SecretScan** — decides whether every secret can be read.
- **DatabaseSetup** — tells whether the database file is missing and gives the connection string.
- **CredentialProtector** and **TotpSecretProtector** (through their interfaces) — decrypt and protect the secrets.
- **ASP.NET Core Data Protection** — `IKeyManager` creates the key.
- **Microsoft.Data.Sqlite** — the transaction per table.

#### Callers

- **Program** — calls `Run` when the `keys reprotect` verb is present.
- **Tests** — call `Run` with a temporary database and key folder.
