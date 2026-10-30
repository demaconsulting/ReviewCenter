### SecretSource

![Utilities Structure](UtilitiesView.svg)

#### Purpose

`SecretSource` gives every setting that holds a secret the same two forms and the same rules: the inline value, or
the path of a file that holds the secret. A secret in a configuration file that is checked in, or in the environment
of a process, is easy to leak, while a file that the operating system or a secret store provides (a systemd
credential, a mounted secret) is the safer way to hand a secret to a service. Its single responsibility is to turn
the two settings into one secret, or into a message that tells the operator which setting is wrong, without ever
putting the secret in the message.

A secret from an environment variable needs no form of its own: the configuration system already maps an
environment variable to the inline setting (for example `Identity__Oidc__ClientSecret`).

#### Data Model

`SecretSource` is an `internal static` class without instance state, so it is thread-safe.

**Constants**: `MaxFileBytes`, 8192, the largest accepted file. A secret is a short text, so a larger file is the
wrong file (a binary, a log). The file is never read past one byte beyond this size, so an endless file cannot
exhaust memory.

#### Key Methods

**Resolve**: Resolves the secret from the inline setting or from the file setting.

- *Parameters*: `string? value` and `string valueSetting` — the inline secret and the full name of its setting;
  `string? file` and `string fileSetting` — the path of the file and the full name of its setting. The names are
  used only in messages and must not be null.
- *Returns*: `string?` — the inline value as it is (not trimmed) when only it is set; the contents of the file,
  decoded as UTF-8 and trimmed of surrounding white space (and of a byte order mark), when only the file is set;
  `null` when neither is set, so that the caller decides whether a missing secret is an error.
- *Preconditions*: Both setting names are non-null.
- *Postconditions*: A non-null result is never empty. Null, empty, and white space all mean "not set" for both
  parameters.

Steps: (1) reject a null setting name; (2) when both forms are set, throw an error that names both settings;
(3) return the inline value when it is set; (4) otherwise read the file when it is set; (5) otherwise return
`null`. Reading the file: open it for reading with sharing allowed (so a secret store that replaces the file in
place does not make the read fail), read at most `MaxFileBytes + 1` bytes, refuse the file when it is larger than
`MaxFileBytes`, decode it strictly as UTF-8, trim it, and refuse it when nothing is left.

#### Error Handling

Every problem is an `InvalidOperationException`, because the callers are start-up checks that turn it into the
message of a failed start. A null setting name raises `ArgumentNullException`. The messages are:

- both set: `{valueSetting} and {fileSetting} are both set; set only one of them.`
- missing: `{fileSetting} refers to the file '{path}', which does not exist.`
- unreadable (a directory, no permission, an I/O error): `... which cannot be read.`
- too large: `... which is larger than 8192 bytes.`
- not UTF-8: `... which is not valid UTF-8 text.`
- empty after trimming: `... which is empty.`

A message names the setting and the path of the file, which is operator configuration and not a secret; it never
holds the secret or a part of it. The exception of the strict decoder quotes the offending bytes, so it is not
passed on as the inner exception; the inner exception of a missing or unreadable file (an I/O error) holds the path
only. Nothing is logged by the unit.

**Deferred**: a warning (or, with a later setting that requires encrypted keys, a refusal) when the file can be read
by the group or by other users on Unix. It depends on the platform and cannot be tested without a Unix file system,
so it is left to a later change together with the settings that need it.

#### Dependencies

- **.NET BCL** — `FileStream`, `UTF8Encoding`, and related types are the only dependencies. No other tool units or
  subsystems are used.

#### Callers

- **OidcSignIn** (Identity) — `OidcOptions.ResolveClientSecret` resolves the client secret from
  `Identity:Oidc:ClientSecret` or `Identity:Oidc:ClientSecretFile`, for the start-up check
  (`OidcOptionsValidator`) and for the registration of the provider.
