### SecretSource

#### Verification Approach

`SecretSource` is verified with unit tests defined in `SecretSourceTests.cs`. The tests use real files in a
`TemporaryDirectory` (a real file system, nothing mocked) and the real `SecretSource`; nothing is read from the
environment or from the home directory of the user, so the tests are hermetic and leave nothing behind. Every error
test also checks that the message does not contain the secret.

#### Test Environment

N/A - standard test environment. The unit is platform neutral: a directory stands in for an unreadable file because
a permission cannot be taken away the same way on every platform. The warning for a secret file that other Unix users
can read is deferred (see the design) and is therefore not verified here.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- An inline secret is returned unchanged, with no trimming.
- With neither setting set (null, empty, or white space), the result is null.
- The contents of a file are returned as UTF-8 text with surrounding white space and line breaks removed, also for a
  file of exactly 8192 bytes.
- Both settings set throws `InvalidOperationException` whose message names both settings and does not contain the
  secret.
- A missing file, a directory, an empty file, a file of only white space, a file larger than 8192 bytes, and a file that
  is not UTF-8 each throw `InvalidOperationException` whose message names the file setting and does not contain the
  contents of the file.
- A null setting name throws `ArgumentNullException`.

#### Test Scenarios

**SecretSource_Resolve_InlineValue_ReturnsTheValueUnchanged**: Only the inline value is set, with white space around
it; the same text is returned. This scenario is tested by
`SecretSource_Resolve_InlineValue_ReturnsTheValueUnchanged`.

**SecretSource_Resolve_File_ReturnsTheContents**: Only the file is set; its contents are returned. This scenario is
tested by `SecretSource_Resolve_File_ReturnsTheContents`.

**SecretSource_Resolve_FileWithSurroundingWhiteSpace_ReturnsTrimmedSecret**: Files that end in a line break (Unix or
Windows), start with a byte order mark, or have spaces around the secret return the bare secret. This scenario is
tested by `SecretSource_Resolve_FileWithSurroundingWhiteSpace_ReturnsTrimmedSecret`.

**SecretSource_Resolve_FileAtSizeLimit_ReturnsTheContents**: A file of exactly 8192 bytes is accepted. This scenario is
tested by `SecretSource_Resolve_FileAtSizeLimit_ReturnsTheContents`.

**SecretSource_Resolve_NeitherSet_ReturnsNull**: With null, empty, and white space for both settings, null is
returned. This scenario is tested by `SecretSource_Resolve_NeitherSet_ReturnsNull`.

**SecretSource_Resolve_BothSet_ThrowsNamingBothSettingsWithoutTheSecret**: Both settings are set; an
`InvalidOperationException` names both settings and does not contain the secret. This scenario is tested by
`SecretSource_Resolve_BothSet_ThrowsNamingBothSettingsWithoutTheSecret`.

**SecretSource_Resolve_MissingFile_ThrowsNamingTheSettingAndTheFile**: The file does not exist; the message names the
setting and the file. This scenario is tested by `SecretSource_Resolve_MissingFile_ThrowsNamingTheSettingAndTheFile`.

**SecretSource_Resolve_DirectoryInsteadOfFile_ThrowsNamingTheSetting**: The path is a directory that cannot be read as
a file; the message names the setting. This scenario is tested by
`SecretSource_Resolve_DirectoryInsteadOfFile_ThrowsNamingTheSetting`.

**SecretSource_Resolve_EmptyFile_ThrowsNamingTheSetting**: An empty file and a file of only white space are refused,
naming the setting. This scenario is tested by `SecretSource_Resolve_EmptyFile_ThrowsNamingTheSetting`.

**SecretSource_Resolve_FilePastSizeLimit_ThrowsWithoutTheContents**: A file one byte over 8192 bytes is refused and
the message does not contain its contents. This scenario is tested by
`SecretSource_Resolve_FilePastSizeLimit_ThrowsWithoutTheContents`.

**SecretSource_Resolve_FileNotUtf8_ThrowsWithoutTheContents**: A file with bytes that are not valid UTF-8 is refused
and the message does not contain the bytes. This scenario is tested by
`SecretSource_Resolve_FileNotUtf8_ThrowsWithoutTheContents`.

**SecretSource_Resolve_NullSettingName_ThrowsArgumentNullException**: A null setting name raises
`ArgumentNullException`. This scenario is tested by `SecretSource_Resolve_NullSettingName_ThrowsArgumentNullException`.
