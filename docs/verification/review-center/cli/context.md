### Context

#### Verification Approach

`Context` is verified with unit tests defined in `ContextTests.cs`. Because `Context` depends
only on .NET BCL types (`Console`, `StreamWriter`), no mocking or test doubles are required.
Tests call `Context.Create` with controlled argument arrays, inspect the resulting properties
and exit codes, and verify output written to captured streams.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- All flag properties are correctly set for each recognized argument.
- `ArgumentException` is thrown for all unknown or malformed arguments.
- `ExitCode` is 1 after `WriteError` is called and 0 otherwise.
- Silent mode suppresses console output but does not affect the log file.
- `Serve` is true only when the `serve` verb is present, and `Urls` defaults to the default
  listen address or holds the validated `--urls` value.
- `--urls` without `serve`, without a value, or with a value that is not an absolute http or
  https URL is rejected with an `ArgumentException`.
- `UserAdd` is true only for the `user add` verb, which records `--username`, `--display-name`, and `--role`
  (not case sensitive, `Reviewer` when omitted).
- `user add` without the add action, without both names, or with a role other than `Admin` or `Reviewer`, and the
  options without the verb, are rejected with an `ArgumentException`; there is no password option.

#### Test Scenarios

**Context_Create_NoArguments_ReturnsDefaultContext**: `Context.Create` is called with an empty
argument array; all boolean flags are false, `ResultsFile` is null, `HeadingDepth` is 1, and
exit code is 0. This scenario is tested by `Context_Create_NoArguments_ReturnsDefaultContext`.

**Context_Create_VersionFlag_SetsVersionTrue**: `Context.Create` is called with `["--version"]`;
the `Version` property is true. This scenario is tested by
`Context_Create_VersionFlag_SetsVersionTrue`.

**Context_Create_ShortVersionFlag_SetsVersionTrue**: `Context.Create` is called with `["-v"]`;
the `Version` property is true. This scenario is tested by
`Context_Create_ShortVersionFlag_SetsVersionTrue`.

**Context_Create_HelpFlag_SetsHelpTrue**: `Context.Create` is called with `["--help"]`; the
`Help` property is true. This scenario is tested by `Context_Create_HelpFlag_SetsHelpTrue`.

**Context_Create_ShortHelpFlag_H_SetsHelpTrue**: `Context.Create` is called with `["-h"]`; the
`Help` property is true. This scenario is tested by
`Context_Create_ShortHelpFlag_H_SetsHelpTrue`.

**Context_Create_ShortHelpFlag_Question_SetsHelpTrue**: `Context.Create` is called with `["-?"]`;
the `Help` property is true. This scenario is tested by
`Context_Create_ShortHelpFlag_Question_SetsHelpTrue`.

**Context_Create_SilentFlag_SetsSilentTrue**: `Context.Create` is called with `["--silent"]`;
the `Silent` property is true. This scenario is tested by
`Context_Create_SilentFlag_SetsSilentTrue`.

**Context_Create_ValidateFlag_SetsValidateTrue**: `Context.Create` is called with
`["--validate"]`; the `Validate` property is true. This scenario is tested by
`Context_Create_ValidateFlag_SetsValidateTrue`.

**Context_Create_ResultsFlag_SetsResultsFile**: `Context.Create` is called with
`["--results", "output.trx"]`; `ResultsFile` equals `"output.trx"`. This scenario is tested by
`Context_Create_ResultsFlag_SetsResultsFile`.

**Context_Create_LogFlag_OpensLogFile**: `Context.Create` is called with
`["--log", "<tmp>.log"]` and then `WriteLine` is called with a test message; the log file is
created and contains the test message. This scenario is tested by
`Context_Create_LogFlag_OpensLogFile`.

**Context_Create_UnknownArgument_ThrowsArgumentException**: `Context.Create` is called with
`["--unknown"]`; an `ArgumentException` containing "Unsupported argument" is thrown. This
scenario is tested by `Context_Create_UnknownArgument_ThrowsArgumentException`.

**Context_Create_LogFlag_WithoutValue_ThrowsArgumentException**: `Context.Create` is called
with `["--log"]` (value missing); an `ArgumentException` is thrown. This scenario is tested by
`Context_Create_LogFlag_WithoutValue_ThrowsArgumentException`.

**Context_Create_ResultsFlag_WithoutValue_ThrowsArgumentException**: `Context.Create` is called
with `["--results"]` (value missing); an `ArgumentException` is thrown. This scenario is tested
by `Context_Create_ResultsFlag_WithoutValue_ThrowsArgumentException`.

**Context_Create_ResultAliasFlag_SetsResultsFile**: `Context.Create` is called with
`["--result", "output.trx"]` (legacy alias); `ResultsFile` equals `"output.trx"`, identical to
the `--results` flag behavior. This scenario is tested by
`Context_Create_ResultAliasFlag_SetsResultsFile`.

**Context_Create_ResultAliasFlag_WithoutValue_ThrowsArgumentException**: `Context.Create` is
called with `["--result"]` (value missing); an `ArgumentException` is thrown. This scenario is
tested by `Context_Create_ResultAliasFlag_WithoutValue_ThrowsArgumentException`.

**Context_Create_DepthFlag_SetsHeadingDepth**: `Context.Create` is called with
`["--depth", "3"]`; `HeadingDepth` equals 3. This scenario is tested by
`Context_Create_DepthFlag_SetsHeadingDepth`.

**Context_Create_NoDepthFlag_ReturnsDefaultHeadingDepth**: `Context.Create` is called with an
empty argument array; `HeadingDepth` equals 1 (the default; valid range is 1–6). This scenario
is tested by `Context_Create_NoDepthFlag_ReturnsDefaultHeadingDepth`.

**Context_Create_DepthFlag_WithoutValue_ThrowsArgumentException**: `Context.Create` is called
with `["--depth"]` (value missing); an `ArgumentException` is thrown. This scenario is tested
by `Context_Create_DepthFlag_WithoutValue_ThrowsArgumentException`.

**Context_Create_DepthFlag_NonIntegerValue_ThrowsArgumentException**: `Context.Create` is
called with `["--depth", "abc"]`; an `ArgumentException` is thrown. This scenario is tested by
`Context_Create_DepthFlag_NonIntegerValue_ThrowsArgumentException`.

**Context_Create_DepthFlag_ZeroValue_ThrowsArgumentException**: `Context.Create` is called with
`["--depth", "0"]` (below the minimum of 1); an `ArgumentException` is thrown. This scenario
is tested by `Context_Create_DepthFlag_ZeroValue_ThrowsArgumentException`.

**Context_Create_DepthFlag_ExceedsMaxValue_ThrowsArgumentException**: `Context.Create` is
called with `["--depth", "7"]` (above the maximum of 6); an `ArgumentException` is thrown.
This scenario is tested by `Context_Create_DepthFlag_ExceedsMaxValue_ThrowsArgumentException`.

**Context_WriteLine_NotSilent_WritesToConsole**: A non-silent `Context` calls `WriteLine` with
a test message; the message appears on standard output. This scenario is tested by
`Context_WriteLine_NotSilent_WritesToConsole`.

**Context_WriteLine_Silent_DoesNotWriteToConsole**: A silent `Context` (created with
`["--silent"]`) calls `WriteLine`; standard output receives nothing, confirming `--silent`
suppresses normal output. This scenario is tested by
`Context_WriteLine_Silent_DoesNotWriteToConsole`.

**Context_WriteError_Silent_DoesNotWriteToConsole**: A silent `Context` calls `WriteError`;
standard error receives nothing, confirming `--silent` also suppresses error output. This
scenario is tested by `Context_WriteError_Silent_DoesNotWriteToConsole`.

**Context_WriteError_SetsErrorExitCode**: A `Context` calls `WriteError`; `ExitCode` is 1
after the call. This scenario is tested by `Context_WriteError_SetsErrorExitCode`.

**Context_WriteError_NotSilent_WritesToConsole**: A non-silent `Context` calls `WriteError`
with a test message; the message appears on standard error. This scenario is tested by
`Context_WriteError_NotSilent_WritesToConsole`.

**Context_WriteError_WritesToLogFile**: A `Context` created with
`["--silent", "--log", "<tmp>.log"]` calls `WriteError` with a test message; the message appears
in the log file, confirming errors are always written to the log regardless of `--silent`. This
scenario is tested by `Context_WriteError_WritesToLogFile`.

**Context_Create_LogFlag_InvalidPath_ThrowsInvalidOperationException**:
`Context.Create` is called with `["--log", "/invalid/\x00path.log"]`; an
`InvalidOperationException` is thrown because the log file cannot be opened. This
scenario is tested by `Context_Create_LogFlag_InvalidPath_ThrowsInvalidOperationException`.

**Context_Create_ServeVerb_SetsServe**: `Context.Create` is called with `["serve"]`; the `Serve`
property is true and `Urls` equals the default listen address. This scenario is tested by
`Context_Create_ServeVerb_SetsServe`.

**Context_Create_NoServeVerb_SetsServeFalse**: `Context.Create` is called with an empty argument
array; the `Serve` property is false. This scenario is tested by
`Context_Create_NoServeVerb_SetsServeFalse`.

**Context_Create_UrlsFlag_SetsUrls**: `Context.Create` is called with
`["serve", "--urls", "http://127.0.0.1:6001"]`; `Urls` equals the supplied value. This scenario
is tested by `Context_Create_UrlsFlag_SetsUrls`.

**Context_Create_UrlsWithoutServe_ThrowsArgumentException**: `Context.Create` is called with
`["--urls", "http://127.0.0.1:6001"]`; an `ArgumentException` whose message mentions `serve` is
thrown. This scenario is tested by `Context_Create_UrlsWithoutServe_ThrowsArgumentException`.

**Context_Create_UrlsFlag_WithoutValue_ThrowsArgumentException**: `Context.Create` is called
with `["serve", "--urls"]` (value missing); an `ArgumentException` mentioning `--urls` is thrown.
This scenario is tested by `Context_Create_UrlsFlag_WithoutValue_ThrowsArgumentException`.

**Context_Create_InvalidUrls_ThrowsArgumentException**: `Context.Create` is called with
`["serve", "--urls", value]` for values that are not absolute http or https URLs (`not-a-url`
and `ftp://localhost:21`); an `ArgumentException` mentioning `--urls` is thrown for each. This
scenario is tested by `Context_Create_InvalidUrls_ThrowsArgumentException`.

**Context_Create_UserAddVerb_SetsUserAddAndOptions**: `Context.Create` is called with `["user", "add",
"--username", "ann", "--display-name", "Ann Example", "--role", "admin"]`; `UserAdd` is true, the user name and display
name are recorded, and the role is `Admin` although it was given in lower case. This scenario is tested by
`Context_Create_UserAddVerb_SetsUserAddAndOptions`.

**Context_Create_UserAddWithoutRole_DefaultsToReviewer**: `Context.Create` is called with `user add` and both names
but no role; the role is `Reviewer`. A context created without arguments has `UserAdd` false. This scenario is
tested by `Context_Create_UserAddWithoutRole_DefaultsToReviewer`.

**Context_Create_UserAddMisuse_ThrowsArgumentException**: A theory over incorrect use: `user` and `user remove`
(no add action), `user add` with only one of the two names, a role that is not `Admin` or `Reviewer`, `--role`
without a value, and `--username` or `--role` without the verb; an `ArgumentException` whose message says what is
wrong is thrown for each. This scenario is tested by `Context_Create_UserAddMisuse_ThrowsArgumentException`.

**Context_Create_PasswordOption_IsNotSupported**: `Context.Create` is called with `user add` and a `--password`
option; an `ArgumentException` containing "Unsupported argument" is thrown, so a password cannot be given on the
command line. This scenario is tested by `Context_Create_PasswordOption_IsNotSupported`.
