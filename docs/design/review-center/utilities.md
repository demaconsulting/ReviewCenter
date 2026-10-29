## Utilities

![Utilities Structure](UtilitiesView.svg)

### Overview

The `Utilities` subsystem provides shared utility functions for the ReviewCenter. It
supplies reusable, independently testable helpers consumed by other subsystems. Its
responsibilities are safe file-path manipulation, protecting callers from path-traversal
vulnerabilities when constructing paths from caller-supplied inputs, and uniquely named
temporary directories that are removed when disposed. The `Utilities` subsystem
contains two units: `PathHelpers` and `TemporaryDirectory`.

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

### Design

The `Utilities` subsystem contains the `PathHelpers` and `TemporaryDirectory` units. It has no dependencies on
other tool units or subsystems; it uses only .NET BCL types (`Path`, `Directory`, `ArgumentNullException`).

`PathHelpers.SafePathCombine` is a pure utility method: it performs no file-system I/O, holds
no state, and throws immediately on invalid input. `TemporaryDirectory` uses it to build the
path of its directory and of every file inside it, so a caller-supplied name can never reach
outside the directory that is deleted on disposal. All uses in the codebase originate from the
`SelfTest` subsystem, whose checks each keep their files in one `TemporaryDirectory` so that
nothing a check creates outlives it.
