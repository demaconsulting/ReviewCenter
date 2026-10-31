## Utilities

![Utilities Structure](UtilitiesView.svg)

### Overview

The `Utilities` subsystem provides shared utility functions for the ReviewCenter. It
supplies reusable, independently testable helpers consumed by other subsystems. Its
responsibilities are safe file-path manipulation, protecting callers from path-traversal
vulnerabilities when constructing paths from caller-supplied inputs; uniquely named
temporary directories that are removed when disposed; and the resolution of a secret that an
operator gives inline or in a file. The `Utilities` subsystem
contains three units: `PathHelpers`, `TemporaryDirectory`, and `SecretSource`.

### Interfaces

**PathHelpers.SafePathCombine**: Combines a base path and a relative path, rejecting any result
that escapes the base directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts `string basePath` and `string relativePath`. Returns the combined path
  produced by `Path.Combine(basePath, relativePath)` after verifying that the resolved result
  remains within `basePath`. Preserves the caller's relative/absolute style in the return value.
- *Constraints*: Throws `ArgumentNullException` for null inputs; throws `ArgumentException`
  when the combined path escapes the base directory; may propagate `NotSupportedException` or
  `PathTooLongException` from underlying BCL path operations.

**SecretSource.Resolve**: Resolves a secret from an inline setting or from a file setting.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts the inline value and the name of its setting, and the file path and the name of its
  setting. Returns the inline value when only it is set, the trimmed UTF-8 contents of the file when only the
  file is set, and `null` when neither is set. Null, empty, and white space all mean "not set".
- *Constraints*: Throws `InvalidOperationException`, whose message names the setting and never contains the
  secret, when both are set, or when the file is missing, unreadable, larger than 8 KB, not UTF-8, or empty;
  throws `ArgumentNullException` for a null setting name.

### Design

The `Utilities` subsystem contains the `PathHelpers`, `TemporaryDirectory`, and `SecretSource` units. It has no
dependencies on other tool units or subsystems; it uses only .NET BCL types (`Path`, `Directory`, `File`,
`ArgumentNullException`).

`PathHelpers.SafePathCombine` is a pure utility method: it performs no file-system I/O, holds
no state, and throws immediately on invalid input. `TemporaryDirectory` uses it to build the
path of its directory and of every file inside it, so a caller-supplied name can never reach
outside the directory that is deleted on disposal. All uses in the codebase originate from the
`SelfTest` subsystem, whose checks each keep their files in one `TemporaryDirectory` so that
nothing a check creates outlives it.

`SecretSource` is independent of the other two units. It holds no state and reads a file only when the
file setting is used. It lives here, and not with its first consumer, because every subsystem that holds a
secret setting (today `Identity` for the OpenID Connect client secret) needs the same rules and none of them
may depend on another one.
