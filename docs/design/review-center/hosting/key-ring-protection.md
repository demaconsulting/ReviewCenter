### KeyRingProtection

![Hosting Structure](HostingView.svg)

#### Purpose

`KeyRingProtection` decides how the key ring of the Data Protection system is protected at rest, wires that
protection into the Data Protection builder, and checks the ring before the server starts. The key ring is the root of
trust of the stored repository tokens (and of the other secrets that `ServerHost` protects), so a copy of the folder
beside a copy of the database must not be enough to read them. Its single responsibility is to turn the
`DataProtection` settings and the operating system into one protection plan (or into a message that names the setting
at fault), to apply that plan, and to refuse to start rather than let Data Protection silently create a new key when
the existing ring cannot be read.

The unit also owns the settings of the `DataProtection` section (`KeyRingSettings`, which replaces the former
`DataProtectionSettings` of `ServerHost`), so that the folder and the protection are read in one place.

#### Data Model

All types are `internal`; the static class has no state and is thread-safe. The decorator `KeyFileRestrictingXmlRepository`
is a small type of the unit that lives in the same file, like the settings and the plan.

**KeyRingSettings** (section `DataProtection`): `KeysPath` — the folder of the ring; `Protection` — `Auto` (the
default), `Dpapi`, `Certificate`, or `PermissionsOnly`, in any case; `DpapiScope` — `CurrentUser` (the default) or
`LocalMachine`; `CertificatePath` and `CertificateKeyPath` — the certificate (PFX, PKCS#12, or PEM) and an optional
separate PEM private key; `CertificatePassword` and `CertificatePasswordFile` — the password of the certificate, in
one form only (resolved with `SecretSource`); `PreviousCertificatePaths` — certificates that may still decrypt the
ring; `RequireEncryptedKeys` — refuse to start with a ring that is not encrypted (false by default).

**KeyRingProtectionMode**: the resolved choice, `Auto`, `Dpapi`, `Certificate`, or `PermissionsOnly` (the resolved
value is never `Auto`).

**KeyProtectionPlatform**: a record with `IsWindows` and `IsUnix`, so that the decision is tested for every operating
system on any machine. `Current` reads the real platform.

**KeyRingPlan**: the outcome of `Prepare`: the mode, the DPAPI scope, `RequireEncryptedKeys`, the loaded certificate
and previous certificates, and `Problem`, which is a message (never null for a rejected plan) when the settings
cannot be used. The plan owns the certificates and is disposable.

#### Key Methods

**ResolveMode**: The pure decision.

- *Parameters*: `KeyRingSettings settings`, `KeyProtectionPlatform platform`.
- *Returns*: `KeyRingProtectionMode` — an explicit choice as given; `Auto` becomes `Certificate` when
  `CertificatePath` is set, otherwise `Dpapi` on Windows and `PermissionsOnly` elsewhere.
- *Errors*: `InvalidOperationException` for an unknown value, for `Dpapi` on a platform that is not Windows, and for
  `Certificate` without `CertificatePath`; each message names the setting.

**Prepare**: Reads the settings, resolves the mode, loads the certificates, and applies `RequireEncryptedKeys` to the
mode.

- *Parameters*: an `IConfiguration` (or `KeyRingSettings`) and the platform.
- *Returns*: `KeyRingPlan`. Every problem is returned as `Problem`, starting "Invalid key ring settings: ", and
  never thrown, so that `ServerHost` can register the services and report the problem at the start.
- *Notes*: A certificate is loaded from a file only (never from a certificate store): a PFX or PKCS#12 file with
  `X509CertificateLoader`, a PEM file with `X509Certificate2.CreateFromPemFile` or, with a password,
  `CreateFromEncryptedPemFile`. The PFX key is held in memory (`EphemeralKeySet`) except on macOS, which does not
  support that and gets the default key set; a platform that cannot load a file (`PlatformNotSupportedException`) is
  reported like any other file that cannot be loaded. The certificate must have an RSA private key. The password
  comes from `SecretSource`
  and is read only when a certificate is needed; it is never put in a message.

**Apply**: Wires the plan into the Data Protection builder.

- *Parameters*: `IDataProtectionBuilder builder`, `KeyRingPlan plan`, and an optional factory of the DPAPI encryptor
  (a test seam, so that a test sees that DPAPI was chosen with the right scope without calling it).
- *Returns*: the builder. `Dpapi` sets the DPAPI encryptor (current user, or local machine); `Certificate` calls
  `ProtectKeysWithCertificate`; previous certificates call `UnprotectKeysWithAnyCertificate`; `PermissionsOnly` and a
  plan with a problem add nothing.

**CheckRing**: The guard that runs before anything can create a key.

- *Parameters*: the plan, the `IKeyManager`, the folder, and a logger.
- *Postconditions*: With `RequireEncryptedKeys`, no key file in the folder holds a plain `<masterKey`. When the ring
  has keys, the newest key can be decrypted and at least one key can be decrypted (read through `GetAllKeys` and
  the descriptor of each key, which only reads). Otherwise an `InvalidOperationException` names
  `DataProtection:CertificatePath` and `DataProtection:PreviousCertificatePaths`. An older key that cannot be read
  is logged as an error and does not stop the start, because the newer keys still protect new data.
- *Notes*: Data Protection answers a key that it cannot decrypt by creating a new one, after which every stored secret
  is unreadable. That was found when the unit was designed, and it is why this check must run before the probe round
  trip of `ServerHost`, which creates a key.

**RestrictKeyFiles**: On Unix, sets the mode of every key file (`*.xml`) in the folder to 0600 (owner read and write).
Nothing on Windows. The platform and the call that sets the mode are parameters of an overload, so that a test can
supply a recording call on any machine. `ServerHost` calls it at start-up for the files that exist then.

**RestrictKeyFolder**: On Windows, for the `PermissionsOnly` protection only, creates the key folder if it is missing
and replaces its access rules: inheritance from the parent is disabled (the inherited rules are not copied), and
full control is allowed for the account of the server and for the system (`LocalSystemSid`), with container and object
inheritance so that the files created later, such as the key that the ring rolls to, inherit the rules. It is the
equivalent of the 0700 mode of the folder on Unix, is idempotent, and does nothing for `Dpapi` and `Certificate` (which
encrypt the key files) or on another operating system. A failure is an `InvalidOperationException` that names the folder
and `DataProtection:KeysPath`.

**KeyFileRestrictingXmlRepository**: An `IXmlRepository` decorator over the `FileSystemXmlRepository` of the key
folder (the repository that `ServerHost.AddServices` configures). `GetAllElements` delegates. `StoreElement` delegates
and then runs the `RestrictKeyFiles` pass over the folder, so that a key file that Data Protection creates while the
server runs (the ring rolls every 90 days) is restricted to 0600 as it is created, not at the next start. The pass
covers the folder because the file name comes from a friendly name that may be empty. The file has the default mode
for the moment between its creation and the change; the folder (0700) keeps others out meanwhile. The platform and the
call that sets the mode are constructor parameters (defaulting to the real ones). It does nothing on Windows, where
`RestrictKeyFolder` is the protection. A failure to set the mode is not swallowed: it surfaces from the call that stored
the key, as the `InvalidOperationException` of `RestrictKeyFiles`.

**LogProtection**: Logs the protection in force at every start: information for DPAPI (with the scope) and for a
certificate (with the thumbprint), and a warning for `PermissionsOnly` that the ring is not encrypted at rest and how
to change that.

**FindPlaintextKeyFiles**: Returns the key files that hold a plain `<masterKey` instead of an `<encryptedSecret`.

#### Error Handling

`Prepare` never throws for a setting: it reports it as `Problem`, so that the one place that stops the start is
`ServerHost.ValidateDataProtection`, which turns the problem into an `InvalidOperationException`. `ResolveMode` throws
`InvalidOperationException` and `Prepare` catches it. `CheckRing` throws `InvalidOperationException`. Messages name the
setting and, for a certificate, the file; they never hold the password. A key that is unreadable, a wrong
certificate, and a wrong password are all reported as "could not be loaded: wrong password or not a certificate with a
private key", because the platform does not tell them apart; a platform that does not support the load at all is
reported the same way.

Known limitations, documented for the administrator: with no secret from the operator there is no facility of the
operating system that binds the keys to a Linux or macOS machine, so `PermissionsOnly` is protection by file
permissions only (on Windows, by the access rules of the folder) and says so at every start; a key that was written in
plain text stays in plain text until Data
Protection rolls to a new key (every 90 days); previous certificates use the password of the current certificate, or
none; and the plan is made when the services are registered, so a `DataProtection` setting that is added afterwards
(by a test, with `ConfigureBuilder`) is not seen by the encryption.

#### Dependencies

- **SecretSource** (Utilities) — resolves the certificate password from the inline setting or the file.
- **ASP.NET Core Data Protection** (OTS) — the key encryptors for DPAPI and for a certificate, `IKeyManager`.
- **.NET BCL** — `X509Certificate2`, `X509CertificateLoader`, file mode APIs, and the access control APIs of the file
  system (`DirectorySecurity`, `FileSystemAclExtensions`), which are part of the shared framework.

#### Callers

- **ServerHost** — `AddServices` calls `Prepare` and `Apply` and wraps the `FileSystemXmlRepository` of the key folder
  in a `KeyFileRestrictingXmlRepository`; `ValidateDataProtection` calls `RestrictKeyFolder`, `CheckRing`,
  `RestrictKeyFiles`, and `LogProtection`.
